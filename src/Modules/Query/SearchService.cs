using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;
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
    IOptions<QuerySettings> settings,
    ILogger<SearchService> logger)
{
    public async Task<List<SearchHit>> SearchAsync(string query, CancellationToken ct)
    {
        int take = settings.Value.TopK;
        var references = Citations.FindReferences(query);

        long started = Stopwatch.GetTimestamp();
        var vector = await embedder.GenerateVectorAsync(query, cancellationToken: ct);
        double embeddingMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        started = Stopwatch.GetTimestamp();
        var candidates = await repository.SearchAsync(vector, query, references, ct);
        double sqlMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        LogSearched(logger, embeddingMs, sqlMs, candidates.Count);

        if (candidates.Count == 0)
        {
            return [];
        }

        var texts = new List<string>();
        var owners = new List<(int Candidate, int Subsection)>();

        for (int i = 0; i < candidates.Count; i++)
        {
            var h = candidates[i].Hit;

            foreach (var s in h.Subsections)
            {
                texts.Add($"{ParagraphHeader.Format(h.Law, h.Number, h.Title)}\n\n{s.Text}");
                owners.Add((i, s.Number));
            }
        }

        var scores = await reranker.ScoreAsync(query, texts, ct);

        if (scores is null)
        {
            return candidates.Take(take).Select(c => c.Hit).ToList();
        }

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

        // Paragraphs named in the question stay first, whatever the reranker scores them.
        var reranked = candidates
            .Select((c, i) => (Hit: c.Hit with { Score = best[i] }, c.Exact, HybridRank: i + 1, Subsection: bestSubsection[i]))
            .OrderByDescending(x => x.Exact)
            .ThenByDescending(x => x.Hit.Score)
            .ToList();

        for (int i = 0; i < reranked.Count; i++)
        {
            var r = reranked[i];
            string source = r.Exact ? "lookup" : "search";
            LogRank(logger, r.Hit.Id, r.HybridRank, i + 1, r.Hit.Score, r.Subsection, source);
        }

        return reranked.Take(take).Select(x => x.Hit).ToList();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Searched {Candidates} candidates in {EmbeddingMs:F0} ms embedding + {SqlMs:F0} ms SQL")]
    private static partial void LogSearched(ILogger logger, double embeddingMs, double sqlMs, int candidates);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ranked {Id} as {RerankRank} ({HybridRank} before rerank) with score {Score:F4}, subsection {Subsection}, found by {Source}")]
    private static partial void LogRank(ILogger logger, string id, int hybridRank, int rerankRank, double score, int subsection, string source);
}