using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Database;

public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    [Required(ErrorMessage = "Database connection string is required")]
    public required string DefaultConnection { get; init; }
}