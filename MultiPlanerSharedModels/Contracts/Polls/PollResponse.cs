using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Polls;

public sealed class PollOptionRequest {
    [Required, StringLength(128), MinLength(1)]
    public string Text { get; set; } = string.Empty;
    
    [RegularExpression("^[0-9A-Fa-f]{6}$")]
    public string? Color { get; init; }
}

public sealed class CreatePollRequest {
    [Required, StringLength(256, MinimumLength = 2)]
    public string Question { get; init; } = string.Empty;

    [Required, MinLength(2), MaxLength(10)]
    public List<PollOptionRequest> Options { get; init; } = [];

    public bool AllowMultiple { get; init; }
    [Required] public DateTimeOffset? ClosesAtUtc { get; init; }
}
public sealed class UpdatePollRequest
{
    [StringLength(256, MinimumLength = 2)] public string? Question { get; init; }
    public DateTimeOffset? ClosesAtUtc { get; init; }

    // Options and selection mode can only be set before the first vote; null = no changes, the list replaces the entire set.
    [MinLength(2), MaxLength(10)] public List<PollOptionRequest>? Options { get; init; }
    public bool? AllowMultiple { get; init; }

    [Required, MinLength(8), MaxLength(8)]
    public byte[] RowVersion { get; init; } = [];
}

public sealed class VotePollRequest {
    // empty array = unvote, set of options
    [Required, MaxLength(10)]
    public int[] OptionIds { get; init; } = [];
}

public sealed record PollOptionResponse(int Id, string Text, string Color, int VoteCount);

public sealed record PollAuthorResponse(int MemberId, string DisplayName, bool HasLeft);

public sealed record PollPermissions(bool CanVote, bool CanEdit, bool CanClose, bool CanDelete);

public sealed record PollResponse(
    int Id,
    int RoomId,
    string Question,
    bool AllowMultiple,
    DateTimeOffset ClosesAtUtc,
    DateTimeOffset? ClosedAtUtc,
    bool IsClosed,
    PollAuthorResponse Author,
    IReadOnlyList<PollOptionResponse> Options,
    int TotalVoters,
    IReadOnlyList<int> MyOptionIds,
    byte[] RowVersion,
    PollPermissions Permissions);