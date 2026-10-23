using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ingestion.Chunking;
using Ingestion.Parsing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Embeddings;
using SharedKernel.Ingestion;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Ingestion.Pipeline;

public sealed record IngestLaw(string FileName);

public sealed partial class IngestLawHandler(
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    IngestionRepository repository,
    IOptions<EmbeddingSettings> embeddingSettings,
    IOptions<IngestionSettings> ingestionSettings,
    ILogger<IngestLawHandler> logger)
{

    public static void Configure(HandlerChain chain)
    {
        chain.ExecutionTimeoutInSeconds = 7200; // 2h

        chain.OnException<HttpRequestException>()
            .OrInner<TimeoutException>()
            .Or<NpgsqlException>(ex => ex.IsTransient)
            .RetryWithCooldown(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))
            .Then.ScheduleRetry(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    }

    public async Task HandleAsync(IngestLaw message, CancellationToken ct)
    {
        string file = Path.Combine(ingestionSettings.Value.DocumentsFolder, message.FileName);
        string hash = await FileHash.ComputeAsync(file, ct);

        if (await repository.GetFileHashAsync(message.FileName, ct) == hash)
        {
            LogSkipped(logger, message.FileName);
            return;
        }

        var law = await LawParser.ParseAsync(file, ct);
        var chunks = Chunker.CreateChunks(law);

        var embedded = new List<EmbeddedChunk>();
        int batchSize = embeddingSettings.Value.BatchSize;
        foreach (var batch in chunks.Chunk(batchSize))
        {
            var embeddings = await embedder.GenerateAsync(batch.Select(c => c.EmbeddingText), cancellationToken: ct);

            if (embeddings.Count != batch.Length)
            {
                throw new InvalidOperationException($"Expected {batch.Length} embeddings, got {embeddings.Count}.");
            }

            embedded.AddRange(batch.Zip(embeddings, (chunk, e) => new EmbeddedChunk(chunk, e.Vector)));
        }

        await repository.SaveLawAsync(law.DocumentId, law.Name, message.FileName, hash, embedded, ct);

        LogIngested(logger, message.FileName, law.DocumentId, chunks.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped {File}, already ingested")]
    private static partial void LogSkipped(ILogger logger, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingested {File} as {DocumentId} with {Chunks} chunks")]
    private static partial void LogIngested(ILogger logger, string file, string documentId, int chunks);
}