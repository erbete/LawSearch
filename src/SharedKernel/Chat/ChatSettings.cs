using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Chat;

public sealed class ChatSettings
{
    public const string SectionName = "Chat";

    [Required, Url]
    public required string Endpoint { get; init; }

    [Required]
    public required string Model { get; init; }

    [Required]
    public required string ApiKey { get; init; }

    [Required, AllowedValues("low", "high", "max")]
    public required string ReasoningEffort { get; init; }
}