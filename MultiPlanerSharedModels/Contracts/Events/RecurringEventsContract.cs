using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MultiPlanerSharedModels.Contracts.Events;

[JsonConverter(typeof(RecurrenceFrequencyConverter))]
public enum RecurrenceFrequency {
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly
}

public sealed class RecurrenceFrequencyConverter()
    : JsonStringEnumConverter<RecurrenceFrequency>(allowIntegerValues: false);

public sealed class RecurrenceRequest {
    public RecurrenceFrequency Frequency { get; init; }
    [Range(1, 12)] public int Interval { get; init; } = 1;
    public DateOnly? Until { get; init; }
}