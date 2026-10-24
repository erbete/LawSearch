using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Query;

namespace Query;

internal sealed record RerankRequest(string Query, IReadOnlyList<string> Texts);
internal sealed record RerankResult(int Index, double Score);

internal sealed partial class RerankClient(
    IHttpClientFactory httpClientFactory,
    IOptions<RerankSettings> settings,
    ILogger<RerankClient> logger)
{
    public const string HttpClientName = "Rerank";

    // Returns null when TEI fails or does not answer within TimeoutSeconds.
    public async Task<double[]?> ScoreAsync(string query, IReadOnlyList<string> texts, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        int batchSize = settings.Value.BatchSize;
        var scores = new double[texts.Count];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Value.TimeoutSeconds));

        long started = Stopwatch.GetTimestamp();

        try
        {
            for (int offset = 0; offset < texts.Count; offset += batchSize)
            {
                var batch = texts.Skip(offset).Take(batchSize).ToList();

                using var response = await http.PostAsJsonAsync("rerank", new RerankRequest(query, batch), timeout.Token);
                response.EnsureSuccessStatusCode();

                var results = await response.Content.ReadFromJsonAsync<List<RerankResult>>(timeout.Token) ?? [];

                foreach (var result in results)
                {
                    scores[offset + result.Index] = result.Score;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            double failedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LogFailed(logger, texts.Count, failedMs, ex);
            return null;
        }

        double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogScored(logger, texts.Count, elapsedMs);

        return scores;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reranked {Texts} texts in {ElapsedMs:F0} ms")]
    private static partial void LogScored(ILogger logger, int texts, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to rerank {Texts} texts after {ElapsedMs:F0} ms")]
    private static partial void LogFailed(ILogger logger, int texts, double elapsedMs, Exception exception);
}