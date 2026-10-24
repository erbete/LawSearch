using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace Query.Tests;

// Asks the same questions as .http, checks that the expected paragraphs are among the sources,
// and saves question, time, sources and answer to Evaluation/<case>.json.
// Compare the files with git diff before and after a change.
//
// Requires: docker compose up -d, laws ingested, and the Host running (dotnet run --project src/Host).
// Run: dotnet test --project tests/Modules/Query.Tests --explicit only
public sealed class Evaluation
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("http://localhost:5000"),
        Timeout = TimeSpan.FromMinutes(3)
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    [Fact(Explicit = true)]
    public async Task NoticePeriod()
    {
        var turn = await AskAsync("Hvor lang oppsigelsestid har jeg?", null);
        await WriteAsync("notice-period", [turn]);

        Assert.True(HasSource(turn, "Arbeidsmiljøloven", "15-3"), "Arbeidsmiljøloven § 15-3 missing");
        Assert.True(HasSource(turn, "Husleieloven", "9-6"), "Husleieloven § 9-6 missing");
        Assert.False(HasSource(turn, "Arbeidsmiljøloven", "14 A-2"), "Arbeidsmiljøloven § 14 A-2 present");
    }

    [Fact(Explicit = true)]
    public async Task Layoff()
    {
        var turn = await AskAsync("Gjelder oppsigelsestiden hvis jeg er permittert?", null);
        await WriteAsync("layoff", [turn]);

        Assert.True(HasSource(turn, "Arbeidsmiljøloven", "15-3"), "Arbeidsmiljøloven § 15-3 missing");
    }

    [Fact(Explicit = true)]
    public async Task ExactParagraph()
    {
        var turn = await AskAsync("Hva sier § 28 a?", null);
        await WriteAsync("exact-paragraph", [turn]);

        Assert.NotEmpty(turn.Result.Sources);
        Assert.Equal("28a", turn.Result.Sources[0].Number.Replace(" ", "", StringComparison.Ordinal), ignoreCase: true);
    }

    [Fact(Explicit = true)]
    public async Task FishingLicense()
    {
        var turn = await AskAsync("Hvor mye koster et fiskekort i Tana?", null);
        await WriteAsync("fishing-license", [turn]);
    }

    [Fact(Explicit = true)]
    public async Task Conversation()
    {
        var first = await AskAsync("Hvor lang oppsigelsestid har jeg?", null);
        var followUp = await AskAsync("Hva hvis jeg har jobbet der i 12 år?", first.Result.ConversationId);
        await WriteAsync("conversation", [first, followUp]);

        Assert.True(HasSource(followUp, "Arbeidsmiljøloven", "15-3"), "Arbeidsmiljøloven § 15-3 missing in follow-up");
    }

    // Expected: "Law § number", several separated by | when any of them is enough.
    [Theory(Explicit = true)]
    [InlineData("deposit", "Hvor mye depositum kan utleier kreve?", "Husleieloven § 3-5")]
    [InlineData("rent-increase", "Kan utleier øke husleia hvert år?", "Husleieloven § 4-2")]
    [InlineData("pets", "Kan jeg ha katt selv om kontrakten forbyr dyr?", "Husleieloven § 5-2")]
    [InlineData("sublet-room", "Kan jeg leie ut et rom i leiligheten jeg leier?", "Husleieloven § 7-3")]
    [InlineData("fixed-term", "Hvor kort kan en tidsbestemt leiekontrakt være?", "Husleieloven § 9-3")]
    [InlineData("overtime-pay", "Hvor mye ekstra skal jeg ha betalt for overtid?", "Arbeidsmiljøloven § 10-6")]
    [InlineData("weekly-hours", "Hvor mange timer kan jeg jobbe i uka?", "Arbeidsmiljøloven § 10-4")]
    [InlineData("breaks", "Har jeg krav på pause i arbeidsdagen?", "Arbeidsmiljøloven § 10-9")]
    [InlineData("whistleblowing", "Kan sjefen straffe meg for å varsle om noe kritikkverdig?", "Arbeidsmiljøloven § 2 A-4")]
    [InlineData("temporary", "Hvor lenge kan jeg være midlertidig ansatt?", "Arbeidsmiljøloven § 14-9")]
    [InlineData("cloudberries", "Kan jeg plukke multer i Finnmark?", "Finnmarksloven § 23|Finnmarksloven § 25")]
    public async Task Question(string name, string question, string expected)
    {
        var turn = await AskAsync(question, null);
        await WriteAsync(name, [turn]);

        bool found = expected.Split('|').Any(e =>
        {
            var parts = e.Split(" § ");
            return HasSource(turn, parts[0], parts[1]);
        });

        Assert.True(found, $"None of '{expected}' in sources");
    }

    private static async Task<Turn> AskAsync(string question, Guid? conversationId)
    {
        string url = $"/ask?q={Uri.EscapeDataString(question)}";

        if (conversationId is { } c)
        {
            url += $"&c={c}";
        }

        long started = Stopwatch.GetTimestamp();
        var result = await Http.GetFromJsonAsync<AnswerResult>(url, JsonOptions, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException($"Empty response for '{question}'.");

        return new Turn(question, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, result);
    }

    private static async Task WriteAsync(string name, IReadOnlyList<Turn> turns)
    {
        string projectDirectory = typeof(Evaluation).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "ProjectDirectory").Value!;
        string folder = Path.Combine(projectDirectory, "Evaluation");
        Directory.CreateDirectory(folder);

        var output = turns.Select(t => new
        {
            t.Question,
            t.ElapsedMs,
            Sources = t.Result.Sources.Select(s => new { s.Law, s.Number, Score = Math.Round(s.Score, 4) }),
            t.Result.Answer
        });

        await File.WriteAllTextAsync(
            Path.Combine(folder, $"{name}.json"),
            JsonSerializer.Serialize(output, JsonOptions),
            TestContext.Current.CancellationToken);
    }

    private static bool HasSource(Turn turn, string law, string number) =>
        turn.Result.Sources.Any(s =>
            string.Equals(s.Law, law, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                s.Number.Replace(" ", "", StringComparison.Ordinal),
                number.Replace(" ", "", StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase));

    private sealed record Turn(string Question, long ElapsedMs, AnswerResult Result);
}