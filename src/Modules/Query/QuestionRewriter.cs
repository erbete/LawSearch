using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Query;

namespace Query;

internal sealed partial class QuestionRewriter(
    IChatClient chat,
    IOptions<QuerySettings> settings,
    ILogger<QuestionRewriter> logger)
{
    private const string Prompt = """
        Skriv om brukerens siste spørsmål til et selvstendig spørsmål som kan forstås uten de tidligere spørsmålene.

        Regler:
        - Ta med det de tidligere spørsmålene allerede har avklart, f.eks. tema, lov og opplysninger brukeren har gitt.
        - Ikke legg til paragrafnummer som brukeren ikke selv har skrevet.
        - Ikke svar på spørsmålet.
        - Er spørsmålet allerede selvstendig, gjengi det uendret.
        - Svar bare med det omskrevne spørsmålet, uten forklaring.
        """;

    public async Task<string> RewriteAsync(string question, IReadOnlyList<HistoryEntry> history, CancellationToken ct)
    {
        string earlier = string.Join("\n", history.Select(h => $"- {h.Question}"));

        List<ChatMessage> messages =
        [
            new(ChatRole.System, Prompt),
            new(ChatRole.User, $"Tidligere spørsmål:\n{earlier}\n\nSiste spørsmål: {question}")
        ];

        var options = new ChatOptions { Temperature = settings.Value.Temperature };

        long started = Stopwatch.GetTimestamp();
        string rewritten = (await chat.GetResponseAsync(messages, options, ct)).Text.Trim();
        double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogRewritten(logger, question, rewritten, elapsedMs);

        return rewritten;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rewrote '{Question}' as '{Rewritten}' in {ElapsedMs:F0} ms")]
    private static partial void LogRewritten(ILogger logger, string question, string rewritten, double elapsedMs);
}