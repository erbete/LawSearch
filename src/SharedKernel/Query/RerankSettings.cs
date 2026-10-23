using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Query;

public sealed class RerankSettings
{
    public const string SectionName = "Rerank";

    [Required, Url]
    public required string Url { get; init; }

    [Range(1, 300)]
    public required int TimeoutSeconds { get; init; }

    [Range(1, 32)]
    public required int BatchSize { get; init; }
}