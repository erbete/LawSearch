using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Ingestion;

public sealed class IngestionSettings
{
    public const string SectionName = "Ingestion";

    [Required]
    public required string DocumentsFolder { get; set; }
}