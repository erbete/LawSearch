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

// Writes the answer the user gets back.
// Search has found the paragraphs; this class sends them to the language model together with
// the question and the conversation so far. The system prompt tells the model to answer only
// from those paragraphs and to cite law, paragraph and subsection for every claim.
// Afterwards, citations that do not match the paragraphs are logged as warnings.
internal sealed partial class AnswerGenerator(
    IChatClient chat,
    IOptions<QuerySettings> settings,
    ILogger<AnswerGenerator> logger)
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

    public async Task<string> GenerateAsync(
        string question,
        IReadOnlyList<HistoryEntry> history,
        IReadOnlyList<SearchHit> paragraphs,
        CancellationToken ct)
    {
        // The paragraphs as one text, separated by "---":
        //   [Husleieloven § 3-5 Depositum]
        //   (1) First subsection ...
        //
        //   (2) Second subsection ...
        var context = string.Join("\n\n---\n\n", paragraphs.Select(paragraph =>
        {
            string header = $"[{ParagraphHeader.Format(paragraph.Law, paragraph.Number, paragraph.Title)}]";
            string body = string.Join("\n\n", paragraph.Subsections.Select(subsection => $"({subsection.Number}) {subsection.Text}"));
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
        string answer = (await chat.GetResponseAsync(messages, options, ct)).Text;
        double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogAnswered(logger, elapsedMs);

        foreach (var citation in Citations.FindInvalid(answer, paragraphs))
        {
            LogInvalidCitation(logger, citation);
        }

        return answer;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered in {ElapsedMs:F0} ms")]
    private static partial void LogAnswered(ILogger logger, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Found invalid citation '{Citation}'")]
    private static partial void LogInvalidCitation(ILogger logger, string citation);
}