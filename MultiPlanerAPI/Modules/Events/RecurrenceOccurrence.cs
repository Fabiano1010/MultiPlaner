namespace MultiPlanerAPI.Modules.Events;

/// <summary>
/// Represents a single materialized occurrence of a recurring calendar event.
/// </summary>
internal readonly record struct RecurrenceOccurrence(
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateOnly Date,
    DateOnly? StartDate,
    DateOnly? EndDateExclusive);