using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Query;

public sealed class QuerySettings
{
    public const string SectionName = "Query";

    [Range(1, 20)]
    public required int TopK { get; init; }

    [Range(1, 200)]
    public required int VectorCandidates { get; init; }

    [Range(1, 200)]
    public required int KeywordCandidates { get; init; }

    [Range(1, 1000)]
    public required int RrfK { get; init; }

    [Range(0.0, 10.0)]
    public required double VectorWeight { get; init; }

    [Range(0.0, 10.0)]
    public required double KeywordWeight { get; init; }

    [Range(0.0, 2.0)]
    public required float Temperature { get; init; }

    [Range(1, 100)]
    public required int RerankCandidates { get; init; }

    [Range(1, 20)]
    public required int HistoryLength { get; init; }
}