using System;
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

internal sealed record AnswerResult(Guid ConversationId, string Answer, List<SearchHit> Sources);

internal sealed partial class AnswerService(
    SearchService search,
    QuestionRewriter rewriter,
    QueryRepository repository,
    IChatClient chat,
    IOptions<QuerySettings> settings,
    ILogger<AnswerService> logger)
{
    private const string SystemPrompt = """
        Du er en assistent som svarer på spørsmål om norsk lov.

        Konteksten består av paragrafer merket [Lov § nummer tittel]. Leddene i hver paragraf er nummerert (1), (2) osv.

        Regler:
        - Svar KUN basert på lovtekstene i konteksten. Ikke bruk egen kunnskap.
        - Henvis til lov, paragraf og ledd for hver påstand. Skriv leddet med ordenstall, f.eks. (Loven § 1-2 andre ledd).
        - Står ikke svaret i lovtekstene, si at du ikke finner det i de tilgjengelige lovtekstene.
        - Avhenger svaret av opplysninger om brukerens situasjon, si hvilke opplysninger som trengs.
        - Svar kort og presist, og med samme språk som brukeren.
        - Kan spørsmålet gjelde flere lover, og konteksten inneholder flere av dem,
          svar for hver tolkning hver for seg, f.eks. "Gjelder det jobb: … Gjelder det leie: …".
        - Henvis bare til paragrafer i konteksten til det siste spørsmålet, ikke til paragrafer fra tidligere svar.
        """;

    public async Task<AnswerResult?> AnswerAsync(string question, Guid? conversationId, CancellationToken ct)
    {
        List<HistoryEntry> history = [];

        if (conversationId is { } existing)
        {
            history = await repository.GetHistoryAsync(existing, settings.Value.HistoryLength, ct);

            if (history.Count == 0)
            {
                return null;
            }
        }

        Guid id = conversationId ?? Guid.CreateVersion7();

        string? rewritten = history.Count > 0
            ? await rewriter.RewriteAsync(question, history, ct)
            : null;

        string searchQuestion = rewritten ?? question;
        var hits = await search.SearchAsync(searchQuestion, ct);

        LogQuestion(logger, searchQuestion, hits.Count);

        var context = string.Join("\n\n---\n\n", hits.Select(h =>
        {
            string header = $"[{ParagraphHeader.Format(h.Law, h.Number, h.Title)}]";
            string body = string.Join("\n\n", h.Subsections.Select(s => $"({s.Number}) {s.Text}"));
            return $"{header}\n{body}";
        }));

        List<ChatMessage> messages = [new(ChatRole.System, SystemPrompt)];

        foreach (var entry in history)
        {
            messages.Add(new(ChatRole.User, entry.Question));
            messages.Add(new(ChatRole.Assistant, entry.Answer));
        }

        messages.Add(new(ChatRole.User, $"Kontekst:\n\n{context}\n\nSpørsmål: {question}"));

        var options = new ChatOptions { Temperature = settings.Value.Temperature };

        long started = Stopwatch.GetTimestamp();
        var response = await chat.GetResponseAsync(messages, options, ct);
        double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogAnswered(logger, elapsedMs);

        foreach (var citation in Citations.FindInvalid(response.Text, hits))
        {
            LogInvalidCitation(logger, citation);
        }

        await repository.SaveHistoryAsync(id, question, rewritten, response.Text, ct);

        return new AnswerResult(id, response.Text, hits);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered in {ElapsedMs:F0} ms")]
    private static partial void LogAnswered(ILogger logger, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hit {Id} {Score:F4}")]
    private static partial void LogHit(ILogger logger, string id, double score);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} hits for '{Question}'")]
    private static partial void LogQuestion(ILogger logger, string question, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Found invalid citation '{Citation}'")]
    private static partial void LogInvalidCitation(ILogger logger, string citation);
}