using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Query;

internal sealed record RerankRequest(string Query, IReadOnlyList<string> Texts);
internal sealed record RerankResult(int Index, double Score);

// Sends the query and the texts to the reranker (TEI) and returns one score per text, in the same order.
// Higher score means the text answers the query better.
// All texts go in one request; TEI splits the work itself. TEI accepts at most --max-client-batch-size texts per request (docker-compose.yml).
// Returns null if TEI fails or does not answer within TimeoutSeconds (set on the HttpClient in QueryModule), so search can continue without rerank.
internal sealed partial class RerankClient(
    IHttpClientFactory httpClientFactory,
    ILogger<RerankClient> logger)
{
    public const string HttpClientName = "Rerank";

    public async Task<double[]?> ScoreAsync(string query, IReadOnlyList<string> texts, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        var scores = new double[texts.Count];

        long started = Stopwatch.GetTimestamp();

        try
        {
            using var response = await http.PostAsJsonAsync("rerank", new RerankRequest(query, texts), ct);
            response.EnsureSuccessStatusCode();

            var results = await response.Content.ReadFromJsonAsync<List<RerankResult>>(ct) ?? [];

            foreach (var result in results)
            {
                scores[result.Index] = result.Score;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            double failedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LogFailed(logger, texts.Count, failedMs, ex);
            return null;
        }

        double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogScored(logger, texts.Count, elapsedMs);

        return scores;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reranked {Count} texts in {ElapsedMs:F0} ms")]
    private static partial void LogScored(ILogger logger, int count, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to rerank {Count} texts after {ElapsedMs:F0} ms")]
    private static partial void LogFailed(ILogger logger, int count, double elapsedMs, Exception exception);
}