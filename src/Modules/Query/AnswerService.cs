using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Query;

namespace Query;

internal sealed record AnswerResult(Guid ConversationId, string Answer, List<SearchHit> Sources);

internal sealed partial class AnswerService(
    SearchService search,
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

    private const string RewritePrompt = """
        Skriv om brukerens siste spørsmål til et selvstendig spørsmål som kan forstås uten samtalen.

        Regler:
        - Ta med det samtalen allerede har avklart, f.eks. tema, lov og opplysninger brukeren har gitt.
        - Ikke svar på spørsmålet.
        - Er spørsmålet allerede selvstendig, gjengi det uendret.
        - Svar bare med det omskrevne spørsmålet, uten forklaring.
        """;

    private static readonly Dictionary<string, int> Ordinals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["første"] = 1,
        ["andre"] = 2,
        ["annet"] = 2,
        ["tredje"] = 3,
        ["fjerde"] = 4,
        ["femte"] = 5,
        ["sjette"] = 6,
        ["sjuende"] = 7,
        ["syvende"] = 7,
        ["åttende"] = 8,
        ["niende"] = 9,
        ["tiende"] = 10,
        ["ellevte"] = 11,
        ["tolvte"] = 12,
        ["trettende"] = 13,
        ["fjortende"] = 14,
        ["femtende"] = 15,
    };

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
        var options = new ChatOptions { Temperature = settings.Value.Temperature };

        string? rewritten = null;

        if (history.Count > 0)
        {
            string transcript = string.Join("\n\n", history.Select(h => $"Bruker: {h.Question}\nAssistent: {h.Answer}"));

            List<ChatMessage> rewriteMessages =
            [
                new(ChatRole.System, RewritePrompt),
                new(ChatRole.User, $"Samtale:\n\n{transcript}\n\nSiste spørsmål: {question}")
            ];

            rewritten = (await chat.GetResponseAsync(rewriteMessages, options, ct)).Text.Trim();
            LogRewritten(logger, question, rewritten);
        }

        string searchQuestion = rewritten ?? question;
        var hits = await search.SearchAsync(searchQuestion, ct);

        LogQuestion(logger, searchQuestion, hits.Count);

        foreach (var hit in hits)
        {
            LogHit(logger, hit.Id, hit.Score);
        }

        var context = string.Join("\n\n---\n\n", hits.Select(h =>
        {
            string header = h.Title.Length == 0
                ? $"[{h.Law} § {h.Number}]"
                : $"[{h.Law} § {h.Number} {h.Title}]";

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

        var response = await chat.GetResponseAsync(messages, options, ct);

        foreach (var citation in FindInvalidCitations(response.Text, hits))
        {
            LogInvalidCitation(logger, citation);
        }

        await repository.SaveHistoryAsync(id, question, rewritten, response.Text, ct);

        return new AnswerResult(id, response.Text, hits);
    }

    internal static List<string> FindInvalidCitations(string answer, IReadOnlyList<SearchHit> hits)
    {
        var invalid = new List<string>();

        foreach (Match parentheses in Parentheses().Matches(answer))
        {
            foreach (Match match in Citation().Matches(parentheses.Groups[1].Value))
            {
                string law = match.Groups["law"].Value;
                string number = Normalize(match.Groups["number"].Value);
                var subsections = ParseSubsections(match.Groups["subsections"].Value);

                bool valid = hits.Any(h =>
                    Normalize(h.Number) == number &&
                    (law.Length == 0 || string.Equals(h.Law, law, StringComparison.OrdinalIgnoreCase)) &&
                    subsections.All(n => h.Subsections.Any(s => s.Number == n)));

                if (!valid && !invalid.Contains(match.Value))
                {
                    invalid.Add(match.Value);
                }
            }
        }

        return invalid;

        static string Normalize(string number) =>
            string.Concat(number.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

        static List<int> ParseSubsections(string text)
        {
            var numbers = new List<int>();
            bool range = false;

            foreach (var word in text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                if (word == "og")
                {
                    continue;
                }

                if (word == "til")
                {
                    range = true;
                    continue;
                }

                if (!Ordinals.TryGetValue(word, out int n))
                {
                    return []; // Unknown word, e.g. "siste": skip the subsection check
                }

                if (range && numbers.Count > 0)
                {
                    for (int i = numbers[^1] + 1; i < n; i++)
                    {
                        numbers.Add(i);
                    }
                }

                numbers.Add(n);
                range = false;
            }

            return numbers;
        }
    }

    [GeneratedRegex(@"\(([^()]*)\)")]
    private static partial Regex Parentheses();

    [GeneratedRegex(@"(?:(?<law>\p{L}+loven)\s+)?§\s*(?<number>\d+(?:\s?[A-Z](?!\p{L}))?(?:-\d+)?(?:\s?[a-z](?!\p{L}))?)(?:\s+(?<subsections>\p{Ll}+(?:(?:\s*,\s*|\s+og\s+|\s+til\s+)\p{Ll}+)*)\s+ledd)?")]
    private static partial Regex Citation();

    [LoggerMessage(Level = LogLevel.Information, Message = "Rewrote '{Question}' as '{Rewritten}'")]
    private static partial void LogRewritten(ILogger logger, string question, string rewritten);

    [LoggerMessage(Level = LogLevel.Information, Message = "Answering '{Question}' with '{Count}' hits")]
    private static partial void LogQuestion(ILogger logger, string question, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hit {Id} {Score:F4}")]
    private static partial void LogHit(ILogger logger, string id, double score);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid citation '{Citation}'")]
    private static partial void LogInvalidCitation(ILogger logger, string citation);
}