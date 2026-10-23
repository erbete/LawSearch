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
}