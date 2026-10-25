using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Query.Tests;

// Asks questions through /ask and checks that the expected paragraphs are among the sources.
// The test starts the Host itself. Docker must be running, with the laws ingested.
//
// Each question is saved to Evaluation/<name>.json: question, time, sources, answer and invalid citations.
// The files are checked in. After a change, run the tests and compare with git diff.
public sealed class Evaluation : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient http;

    public Evaluation(WebApplicationFactory<Program> factory)
    {
        http = factory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(3);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    // Expected: "Law § number".
    //   ";" between paragraphs that must all be among the sources.
    //   "|" between paragraphs where one is enough.
    //   No law ("§ 28 a") accepts the number in any law.
    //   Empty: nothing is checked; read the answer in the file.
    [Theory]
    [InlineData("notice-period", "Hvor lang oppsigelsestid har jeg?", "Arbeidsmiljøloven § 15-3; Husleieloven § 9-6")]
    [InlineData("layoff", "Gjelder oppsigelsestiden hvis jeg er permittert?", "Arbeidsmiljøloven § 15-3")]
    [InlineData("exact-paragraph", "Hva sier § 28 a?", "§ 28 a")]
    [InlineData("fishing-license", "Hvor mye koster et fiskekort i Tana?", "")]
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
        AssertSources(turn, expected);
    }

    [Fact]
    public async Task Conversation()
    {
        var first = await AskAsync("Hvor lang oppsigelsestid har jeg?", null);
        var followUp = await AskAsync("Hva hvis jeg har jobbet der i 12 år?", first.Result.ConversationId);
        await WriteAsync("conversation", [first, followUp]);

        AssertSources(followUp, "Arbeidsmiljøloven § 15-3");
    }

    [Fact]
    public async Task UnknownConversation()
    {
        var response = await http.GetAsync($"/ask?q=Hei&c={Guid.NewGuid()}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Turn> AskAsync(string question, Guid? conversationId)
    {
        string url = $"/ask?q={Uri.EscapeDataString(question)}";

        if (conversationId is { } c)
        {
            url += $"&c={c}";
        }

        long started = Stopwatch.GetTimestamp();
        var result = await http.GetFromJsonAsync<AnswerResult>(url, JsonOptions, TestContext.Current.CancellationToken)
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
            t.Result.Answer,
            InvalidCitations = Citations.FindInvalid(t.Result.Answer, t.Result.Sources)
        });

        await File.WriteAllTextAsync(
            Path.Combine(folder, $"{name}.json"),
            JsonSerializer.Serialize(output, JsonOptions),
            TestContext.Current.CancellationToken);
    }

    private static void AssertSources(Turn turn, string expected)
    {
        var options = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

        foreach (string required in expected.Split(';', options))
        {
            bool found = required.Split('|', options).Any(alternative =>
            {
                var parts = alternative.Split('§', StringSplitOptions.TrimEntries);
                string law = parts[0];
                string number = parts[1].Replace(" ", "", StringComparison.Ordinal);

                return turn.Result.Sources.Any(s =>
                    (law.Length == 0 || string.Equals(s.Law, law, StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(s.Number.Replace(" ", "", StringComparison.Ordinal), number, StringComparison.OrdinalIgnoreCase));
            });

            Assert.True(found, $"'{required}' missing from sources");
        }
    }

    private sealed record Turn(string Question, long ElapsedMs, AnswerResult Result);
}