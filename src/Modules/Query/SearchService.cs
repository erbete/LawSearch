using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Query;

namespace Query;

internal sealed record SearchHit(
    string Id,
    string Law,
    string Chapter,
    string Number,
    string Title,
    IReadOnlyList<Subsection> Subsections,
    double Score);

internal sealed record Subsection(int Number, string Text);

internal sealed partial class SearchService(
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    QueryRepository repository,
    RerankClient reranker,
    IOptions<QuerySettings> querySettings,
    IOptions<RerankSettings> rerankSettings,
    ILogger<SearchService> logger)
{
    public async Task<List<SearchHit>> SearchAsync(string query, int? limit, CancellationToken ct)
    {
        var settings = querySettings.Value;
        int take = limit ?? settings.TopK;

        var vector = await embedder.GenerateVectorAsync(query, cancellationToken: ct);
        var candidates = await repository.SearchAsync(vector, query, settings.RerankCandidates, ct);

        if (candidates.Count == 0)
        {
            return candidates;
        }

        var texts = new List<string>();
        var owners = new List<(int Candidate, int Subsection)>();

        for (int i = 0; i < candidates.Count; i++)
        {
            var h = candidates[i];

            foreach (var s in h.Subsections)
            {
                texts.Add($"{h.Law} § {h.Number} {h.Title}\n\n{s.Text}");
                owners.Add((i, s.Number));
            }
        }

        double[] scores;
        long started = Stopwatch.GetTimestamp();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(rerankSettings.Value.TimeoutSeconds));
            scores = await reranker.ScoreAsync(query, texts, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            LogRerankFailed(logger, ex);
            return candidates.Take(take).ToList();
        }

        double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogReranked(logger, candidates.Count, texts.Count, elapsedMs);

        var best = new double[candidates.Count];
        var bestSubsection = new int[candidates.Count];

        for (int t = 0; t < scores.Length; t++)
        {
            var (candidate, subsection) = owners[t];

            if (scores[t] > best[candidate])
            {
                best[candidate] = scores[t];
                bestSubsection[candidate] = subsection;
            }
        }

        var reranked = candidates
            .Select((hit, i) => (Hit: hit with { Score = best[i] }, HybridRank: i + 1, Subsection: bestSubsection[i]))
            .OrderByDescending(x => x.Hit.Score)
            .ToList();

        for (int i = 0; i < reranked.Count; i++)
        {
            var r = reranked[i];
            LogRank(logger, r.Hit.Id, r.HybridRank, i + 1, r.Hit.Score, r.Subsection);
        }

        return reranked.Take(take).Select(x => x.Hit).ToList();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reranked {Count} candidates ({Texts} subsections) in {ElapsedMs:F0} ms")]
    private static partial void LogReranked(ILogger logger, int count, int texts, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rank {Id} hybrid {HybridRank} -> rerank {RerankRank} {Score:F4} (subsection {Subsection})")]
    private static partial void LogRank(ILogger logger, string id, int hybridRank, int rerankRank, double score, int subsection);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rerank failed, using hybrid order")]
    private static partial void LogRerankFailed(ILogger logger, Exception exception);
}