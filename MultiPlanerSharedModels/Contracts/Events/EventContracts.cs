using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MultiPlanerSharedModels.Contracts.Events;

[JsonConverter(typeof(EventCategoryConverter))]
public enum EventCategory { Meeting, Training, Social, BusinessTrip, Leave, Shift }

public sealed class EventCategoryConverter() : JsonStringEnumConverter<EventCategory>(allowIntegerValues: false);

public sealed class EventTimeRequest
{
    public bool IsAllDay { get; init; }
    public DateTimeOffset? StartsAtUtc { get; init; }
    public DateTimeOffset? EndsAtUtc { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDateExclusive { get; init; }
}

public sealed class CreateEventRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;
    [StringLength(4000)] public string? Description { get; init; }
    [Required] public EventTimeRequest Time { get; init; } = null!;
    [EnumDataType(typeof(EventCategory))] public EventCategory Category { get; init; } = EventCategory.Meeting;
    [RegularExpression("^[0-9A-Fa-f]{6}$")] public string? Color { get; init; }
    public bool IsHighPriority { get; init; }
    public bool IsPinned { get; init; }
    [MaxLength(1000)] public int[]? ParticipantMemberIds { get; init; }
}

public sealed class UpdateEventRequest
{
    [StringLength(128, MinimumLength = 1)] public string? Title { get; init; }
    // Null leaves optional values unchanged; empty strings clear description/color.
    [StringLength(4000)] public string? Description { get; init; }
    public EventTimeRequest? Time { get; init; }
    [EnumDataType(typeof(EventCategory))] public EventCategory? Category { get; init; }
    [RegularExpression("^([0-9A-Fa-f]{6})?$")] public string? Color { get; init; }
    public bool? IsHighPriority { get; init; }
    public bool? IsPinned { get; init; }
    [MaxLength(1000)] public int[]? ParticipantMemberIds { get; init; }
    [Required, MinLength(8), MaxLength(8)] public byte[] RowVersion { get; init; } = [];
}

public sealed class EventVersionRequest
{
    [Required, MinLength(8), MaxLength(8)] public byte[] RowVersion { get; init; } = [];
}

public sealed class PinEventRequest
{
    public bool IsPinned { get; init; }
    [Required, MinLength(8), MaxLength(8)] public byte[] RowVersion { get; init; } = [];
}

public sealed class EventListRequest : IValidatableObject
{
    [Required] public DateTimeOffset? From { get; init; }
    [Required] public DateTimeOffset? To { get; init; }
    [EnumDataType(typeof(EventCategory))] public EventCategory? Category { get; init; }
    [Range(1, int.MaxValue)] public int? AuthorMemberId { get; init; }
    [Range(1, int.MaxValue)] public int? RoomId { get; init; }
    public bool? IsPinned { get; init; }
    public bool? IsHighPriority { get; init; }
    [RegularExpression("^(active|archived|all)$")] public string Status { get; init; } = "active";
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 50;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is { } from && To is { } to && to <= from)
            yield return new ValidationResult("To must be later than From.", [nameof(To)]);
    }
}

public sealed record EventAuthorResponse(int MemberId, string DisplayName, bool IsGuest, bool IsOwner, bool HasLeft);
public sealed record EventPermissions(bool CanEdit, bool CanDelete, bool CanPin);
public sealed record EventResponse(
    int Id, int RoomId, string RoomName, string RoomTimeZoneId, bool RoomIsArchived,
    string Title, string? Description, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
    bool IsAllDay, DateOnly? StartDate, DateOnly? EndDateExclusive, string? TimeZoneId,
    EventCategory Category, string Color, string? CustomColor, bool IsHighPriority, bool IsPinned,
    EventAuthorResponse Author, IReadOnlyList<int> ParticipantMemberIds, byte[] RowVersion,
    EventPermissions Permissions);
