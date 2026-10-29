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

internal sealed record Paragraph(
    string Id,
    string Law,
    string Chapter,
    string Number,
    string Title,
    IReadOnlyList<Subsection> Subsections,
    double Score);

internal sealed record Subsection(int Number, string Text);

// Finds the paragraphs that best match the query.
// First the database finds candidates (vector search, keyword search and paragraph numbers
// written in the query). Then the reranker scores each subsection, and the paragraphs are
// sorted by their best subsection. Paragraphs named in the query always come first.
internal sealed partial class SearchService(
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    QueryRepository repository,
    RerankClient reranker,
    IOptions<QuerySettings> settings,
    ILogger<SearchService> logger)
{
    public async Task<IReadOnlyList<Paragraph>> SearchAsync(string query, CancellationToken ct)
    {
        var references = Citations.FindReferences(query);

        long started = Stopwatch.GetTimestamp();
        var vector = await embedder.GenerateVectorAsync(query, cancellationToken: ct);
        double embeddingMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        started = Stopwatch.GetTimestamp();
        var candidates = await repository.SearchAsync(vector, query, references, ct);
        double sqlMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        LogSearched(logger, query, embeddingMs, sqlMs, candidates.Count);

        if (candidates.Count == 0)
        {
            return [];
        }

        var texts = new List<string>();
        var origins = new List<(int Candidate, int Subsection)>();

        for (int i = 0; i < candidates.Count; i++)
        {
            var paragraph = candidates[i].Paragraph;

            foreach (var subsection in paragraph.Subsections)
            {
                texts.Add($"{ParagraphHeader.Format(paragraph.Law, paragraph.Number, paragraph.Title)}\n\n{subsection.Text}");
                origins.Add((i, subsection.Number));
            }
        }

        var scores = await reranker.ScoreAsync(query, texts, ct);

        if (scores is null)
        {
            return candidates.Take(settings.Value.TopK).Select(c => c.Paragraph).ToList();
        }

        var bestScore = new double[candidates.Count];
        var bestSubsection = new int[candidates.Count];

        for (int t = 0; t < scores.Length; t++)
        {
            var (candidate, subsection) = origins[t];

            if (scores[t] > bestScore[candidate])
            {
                bestScore[candidate] = scores[t];
                bestSubsection[candidate] = subsection;
            }
        }

        var reranked = candidates
            .Select((c, i) => (Paragraph: c.Paragraph with { Score = bestScore[i] }, c.Exact, RankBeforeRerank: i + 1, Subsection: bestSubsection[i]))
            .OrderByDescending(x => x.Exact)
            .ThenByDescending(x => x.Paragraph.Score)
            .ToList();

        for (int i = 0; i < reranked.Count; i++)
        {
            var r = reranked[i];
            string source = r.Exact ? "lookup" : "search";
            LogRank(logger, r.Paragraph.Id, r.RankBeforeRerank, i + 1, r.Paragraph.Score, r.Subsection, source);
        }

        return reranked.Take(settings.Value.TopK).Select(x => x.Paragraph).ToList();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Searched {Candidates} candidates for '{Query}' in {EmbeddingMs:F0} ms embedding + {SqlMs:F0} ms SQL")]
    private static partial void LogSearched(ILogger logger, string query, double embeddingMs, double sqlMs, int candidates);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ranked {Id} as {Rank} ({RankBeforeRerank} before rerank) with score {Score:F4}, subsection {Subsection}, found by {Source}")]
    private static partial void LogRank(ILogger logger, string id, int rankBeforeRerank, int rank, double score, int subsection, string source);
}