using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Common;

public sealed class PageRequest
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 50;
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
