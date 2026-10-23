using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Embeddings;

public sealed class EmbeddingSettings
{
    public const string SectionName = "Embedding";

    [Required, Url]
    public required string Url { get; init; }

    [Required]
    public required string Model { get; init; }

    [Range(1, 2000)]
    public required int Dimensions { get; init; }

    [Range(1, 256)]
    public required int BatchSize { get; init; }
}