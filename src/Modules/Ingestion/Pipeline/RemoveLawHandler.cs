using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Ingestion.Pipeline;

public sealed record RemoveLaw(string DocumentId);

public sealed partial class RemoveLawHandler(IngestionRepository repository, ILogger<RemoveLawHandler> logger)
{
    public static void Configure(HandlerChain chain)
    {
        chain.OnException<NpgsqlException>(ex => ex.IsTransient)
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
    }

    public async Task HandleAsync(RemoveLaw message, CancellationToken ct)
    {
        await repository.RemoveLawAsync(message.DocumentId, ct);
        LogRemoved(logger, message.DocumentId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {DocumentId}")]
    private static partial void LogRemoved(ILogger logger, string documentId);
}