namespace MultiPlanerSharedUI.Services;

public sealed record AdvancedSearchFilter(DateOnly? From, DateOnly? To, string Priority, string Time);