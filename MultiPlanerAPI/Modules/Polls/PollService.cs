using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Common;
using MultiPlanerSharedModels.Contracts.Polls;

namespace MultiPlanerAPI.Modules.Polls;

public sealed class PollService(
    AppDbContext db,
    DbContextOptions<AppDbContext> dbOptions,
    CurrentActor actor,
    TimeProvider clock,
    RoomAccessService access)
{
    private const int MaxOptions = 10;

    private static readonly string[] DefaultColors =
        ["4F8EF7", "F76B6B", "5CC98C", "F7B84F", "A178F0", "4FC7D9", "F27AB0", "8D9AA6", "B5D94F", "F78E4F"];

    public async Task<PollResponse> CreateAsync(int roomId, CreatePollRequest request, CancellationToken ct)
    {
        var (room, member) = await access.RequireMemberAsync(roomId, ct);
        access.RequireActive(room);
        var now = clock.GetUtcNow();

        var errors = new Dictionary<string, string[]>();
        var question = NormalizeQuestion(request.Question, errors);
        var options = NormalizeOptions(request.Options, errors);
        ValidateClose(request.ClosesAtUtc, now, errors);
        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }

        var poll = new Poll
        {
            RoomId = roomId,
            AuthorMemberId = member.Id,
            Question = question,
            AllowMultiple = request.AllowMultiple,
            ClosesAtUtc = request.ClosesAtUtc!.Value,
            Options = options
        };
        db.Polls.Add(poll);
        await db.SaveChangesAsync(ct);   // Poll and options in one transaction
        return (await BuildAsync(db, [poll], room, member, ct))[0];
    }

    public async Task<PagedResponse<PollResponse>> ListAsync(
        int roomId, PageRequest page, string status, CancellationToken ct)
    {
        if (status is not ("open" or "closed" or "all"))
        {
            throw ApiException.Validation(new Dictionary<string, string[]>
                { ["status"] = ["Use open, closed or all."] });
        }

        var (room, viewer) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        var now = clock.GetUtcNow();

        var query = db.Polls.AsNoTracking().Where(p => p.RoomId == roomId);
        query = status switch
        {
            "open" => query.Where(p => p.ClosedAtUtc == null && p.ClosesAtUtc > now),
            "closed" => query.Where(p => p.ClosedAtUtc != null || p.ClosesAtUtc <= now),
            _ => query
        };

        var count = await query.CountAsync(ct);
        var offset = (long)(page.Page - 1) * page.PageSize;
        if (offset > int.MaxValue)
        {
            return new PagedResponse<PollResponse>([], count, page.Page, page.PageSize);
        }

        var polls = await WithOptions(query.OrderByDescending(p => p.Id))
            .Skip((int)offset).Take(page.PageSize).ToListAsync(ct);
        return new PagedResponse<PollResponse>(
            await BuildAsync(db, polls, room, viewer, ct), count, page.Page, page.PageSize);
    }

    public async Task<PollResponse> GetAsync(int roomId, int pollId, CancellationToken ct)
    {
        var (room, viewer) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        var poll = await WithOptions(db.Polls.AsNoTracking())
            .SingleOrDefaultAsync(p => p.Id == pollId && p.RoomId == roomId, ct) ?? throw NotFound();
        return (await BuildAsync(db, [poll], room, viewer, ct))[0];
    }

    public Task<PollResponse> UpdateAsync(int roomId, int pollId, UpdatePollRequest request, CancellationToken ct)
    {
        return new RoomMutationService(dbOptions, clock).ExecuteAsync(roomId, async (context, room) =>
        {
            var (member, poll) = await LoadForManageAsync(context, room, roomId, pollId, ct);
            var now = clock.GetUtcNow();
            if (poll.GetClosedAtUtc(now) is not null)
            {
                throw new ApiException(409, "poll_closed", "A closed poll cannot be edited.");
            }

            if (!poll.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
            {
                throw new ApiException(409, "concurrency_conflict", "Poll changed. Reload and retry.");
            }

            var errors = new Dictionary<string, string[]>();
            if (request.Question is not null)
            {
                poll.Question = NormalizeQuestion(request.Question, errors);
            }

            if (request.ClosesAtUtc is { } closes)
            {
                ValidateClose(closes, now, errors);
                poll.ClosesAtUtc = closes;
            }

            var replacement = request.Options is null ? null : NormalizeOptions(request.Options, errors);
            if (errors.Count != 0)
            {
                throw ApiException.Validation(errors);
            }

            if (replacement is not null || request.AllowMultiple is not null)
            {
                if (await context.PollVotes.AnyAsync(v => v.PollId == pollId, ct))
                {
                    throw new ApiException(409, "poll_has_votes",
                        "Options and selection mode cannot change after the first vote.");
                }

                if (request.AllowMultiple is { } multiple)
                {
                    poll.AllowMultiple = multiple;
                }

                if (replacement is not null)
                {
                    context.PollOptions.RemoveRange(poll.Options);
                    poll.Options = replacement;
                }
            }

            poll.UpdatedAtUtc = now;   // option change, changes RowVersion
            await context.SaveChangesAsync(ct);
            return (await BuildAsync(context, [poll], room, member, ct))[0];
        }, ct);
    }

    public Task<PollResponse> CloseAsync(int roomId, int pollId, CancellationToken ct)
    {
        return new RoomMutationService(dbOptions, clock).ExecuteAsync(roomId, async (context, room) =>
        {
            var (member, poll) = await LoadForManageAsync(context, room, roomId, pollId, ct);
            var now = clock.GetUtcNow();
            if (poll.GetClosedAtUtc(now) is null)
            {
                poll.ClosedAtUtc = now;
                await context.SaveChangesAsync(ct);
            }

            return (await BuildAsync(context, [poll], room, member, ct))[0];
        }, ct);
    }

    public Task DeleteAsync(int roomId, int pollId, CancellationToken ct)
    {
        return new RoomMutationService(dbOptions, clock).ExecuteAsync(roomId, async (context, room) =>
        {
            var (_, poll) = await LoadForManageAsync(context, room, roomId, pollId, ct);
            var votes = await context.PollVotes.Where(v => v.PollId == pollId).ToListAsync(ct);
            context.PollVotes.RemoveRange(votes);
            context.PollOptions.RemoveRange(poll.Options);
            context.Polls.Remove(poll);
            await context.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public Task<PollResponse> VoteAsync(int roomId, int pollId, VotePollRequest request, CancellationToken ct)
    {
        return new RoomMutationService(dbOptions, clock).ExecuteAsync(roomId, async (context, room) =>
        {
            var roomAccess = new RoomAccessService(context, actor, clock);
            var (_, member) = await roomAccess.RequireMemberAsync(roomId, ct);
            roomAccess.RequireActive(room);

            var poll = await FindTrackedAsync(context, roomId, pollId, ct);
            var now = clock.GetUtcNow();
            if (poll.GetClosedAtUtc(now) is not null)
            {
                throw new ApiException(409, "poll_closed", "Voting has ended.");
            }

            var optionIds = request.OptionIds.Distinct().ToArray();
            var valid = poll.Options.Select(o => o.Id).ToHashSet();
            if (optionIds.Any(id => !valid.Contains(id)))
            {
                throw ApiException.Validation(new Dictionary<string, string[]>
                    { ["optionIds"] = ["Choose options that belong to this poll."] });
            }

            if (!poll.AllowMultiple && optionIds.Length > 1)
            {
                throw ApiException.Validation(new Dictionary<string, string[]>
                    { ["optionIds"] = ["This poll allows a single choice."] });
            }

            var existing = await context.PollVotes
                .Where(v => v.PollId == pollId && v.MemberId == member.Id).ToListAsync(ct);

            if (poll.AllowMultiple)
            {
                context.PollVotes.RemoveRange(existing.Where(v => !optionIds.Contains(v.OptionId)));
                foreach (var id in optionIds.Except(existing.Select(v => v.OptionId)))
                {
                    context.PollVotes.Add(new PollVote
                        { PollId = pollId, MemberId = member.Id, OptionId = id, Slot = id, CastAtUtc = now });
                }
            }
            else if (optionIds.Length == 0)
            {
                context.PollVotes.RemoveRange(existing);
            }
            else if (existing.Count == 0)
            {
                context.PollVotes.Add(new PollVote
                    { PollId = pollId, MemberId = member.Id, OptionId = optionIds[0], Slot = 0, CastAtUtc = now });
            }
            else
            {
                existing[0].OptionId = optionIds[0];
                existing[0].CastAtUtc = now;
            }

            await context.SaveChangesAsync(ct);
            return (await BuildAsync(context, [poll], room, member, ct))[0];
        }, ct);
    }

    

    private static ApiException NotFound() => new(404, "poll_not_found", "Poll not found.");

    private static IQueryable<Poll> WithOptions(IQueryable<Poll> query)
    {
        return query.Include(p => p.Options.OrderBy(o => o.Position));
    }

    private static async Task<Poll> FindTrackedAsync(AppDbContext context, int roomId, int pollId,
        CancellationToken ct)
    {
        return await WithOptions(context.Polls)
            .SingleOrDefaultAsync(p => p.Id == pollId && p.RoomId == roomId, ct) ?? throw NotFound();
    }

    private static bool CanManage(Room room, RoomMember viewer, Poll poll)
    {
        return poll.AuthorMemberId == viewer.Id || room.OwnerUserId == viewer.UserId || viewer.IsAdmin;
    }

    private async Task<(RoomMember Member, Poll Poll)> LoadForManageAsync(
        AppDbContext context, Room room, int roomId, int pollId, CancellationToken ct)
    {
        var roomAccess = new RoomAccessService(context, actor, clock);
        var (_, member) = await roomAccess.RequireMemberAsync(roomId, ct);
        roomAccess.RequireActive(room);
        var poll = await FindTrackedAsync(context, roomId, pollId, ct);
        if (!CanManage(room, member, poll))
        {
            throw new ApiException(403, "poll_manage_required",
                "Only the author, the owner or an administrator may change this poll.");
        }

        return (member, poll);
    }

    private static string NormalizeQuestion(string? question, Dictionary<string, string[]> errors)
    {
        var text = question?.Trim() ?? "";
        if (text.Length is < 2 or > 256)
        {
            errors["question"] = ["The question must contain 2-256 characters."];
        }

        return text;
    }

    private static void ValidateClose(DateTimeOffset? closes, DateTimeOffset now,
        Dictionary<string, string[]> errors)
    {
        if (closes is not { } value || value.Offset != TimeSpan.Zero || value <= now || value > now.AddDays(365))
        {
            errors["closesAtUtc"] = ["The closing time must be UTC, in the future and within one year."];
        }
    }

    private static List<PollOption> NormalizeOptions(IReadOnlyList<PollOptionRequest>? input,
        Dictionary<string, string[]> errors)
    {
        var result = new List<PollOption>();
        if (input is null || input.Count is < 2 or > MaxOptions)
        {
            errors["options"] = [$"A poll needs between 2 and {MaxOptions} options."];
            return result;
        }

        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        for (var i = 0; i < input.Count; i++)
        {
            var text = input[i].Text?.Trim() ?? "";
            if (text.Length is < 1 or > 128)
            {
                errors["options"] = ["Each option must contain 1-128 characters."];
                return result;
            }

            if (!seen.Add(text))
            {
                errors["options"] = ["Options must be unique."];
                return result;
            }

            var color = string.IsNullOrWhiteSpace(input[i].Color)
                ? DefaultColors[i % DefaultColors.Length]
                : input[i].Color!.Trim().ToUpperInvariant();
            if (color.Length != 6 || !color.All(Uri.IsHexDigit))
            {
                errors["options"] = ["Option colour must be a 6-digit hex value."];
                return result;
            }

            result.Add(new PollOption { Position = i, Text = text, Color = color });
        }

        return result;
    }

    private async Task<List<PollResponse>> BuildAsync(AppDbContext context, IReadOnlyList<Poll> polls,
        Room room, RoomMember viewer, CancellationToken ct)
    {
        if (polls.Count == 0)
        {
            return [];
        }

        var now = clock.GetUtcNow();
        var pollIds = polls.Select(p => p.Id).ToArray();
        var authorIds = polls.Select(p => p.AuthorMemberId).Distinct().ToArray();

        var optionCounts = await context.PollVotes.AsNoTracking()
            .Where(v => pollIds.Contains(v.PollId))
            .GroupBy(v => v.OptionId)
            .Select(g => new { OptionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OptionId, x => x.Count, ct);
        var voters = await context.PollVotes.AsNoTracking()
            .Where(v => pollIds.Contains(v.PollId))
            .GroupBy(v => v.PollId)
            .Select(g => new { PollId = g.Key, Count = g.Select(v => v.MemberId).Distinct().Count() })
            .ToDictionaryAsync(x => x.PollId, x => x.Count, ct);
        var mine = (await context.PollVotes.AsNoTracking()
                .Where(v => pollIds.Contains(v.PollId) && v.MemberId == viewer.Id)
                .Select(v => new { v.PollId, v.OptionId }).ToListAsync(ct))
            .ToLookup(v => v.PollId, v => v.OptionId);
        var authors = await context.RoomMembers.AsNoTracking()
            .Where(m => authorIds.Contains(m.Id))
            .Select(m => new { m.Id, m.DisplayName, HasLeft = m.LeftAtUtc != null })
            .ToDictionaryAsync(m => m.Id, ct);

        var roomActive = room.ArchivedAtUtc == null && room.ExpiresAtUtc > now;
        return polls.Select(poll =>
        {
            var closedAt = poll.GetClosedAtUtc(now);
            var closed = closedAt is not null;
            var manage = CanManage(room, viewer, poll);
            var author = authors.TryGetValue(poll.AuthorMemberId, out var a) ? a : null;
            return new PollResponse(
                poll.Id, poll.RoomId, poll.Question, poll.AllowMultiple, poll.ClosesAtUtc, closedAt, closed,
                new PollAuthorResponse(poll.AuthorMemberId, author?.DisplayName ?? "", author?.HasLeft ?? true),
                poll.Options.OrderBy(o => o.Position)
                    .Select(o => new PollOptionResponse(o.Id, o.Text, o.Color,
                        optionCounts.GetValueOrDefault(o.Id)))
                    .ToList(),
                voters.GetValueOrDefault(poll.Id),
                mine[poll.Id].ToList(),
                poll.RowVersion,
                new PollPermissions(
                    roomActive && !closed,
                    roomActive && !closed && manage,
                    roomActive && !closed && manage,
                    roomActive && manage));
        }).ToList();
    }
}