using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ingestion.Chunking;
using Ingestion.Parsing;

namespace Ingestion.Tests;

public sealed class ChunkerTests
{
    [Theory]
    [InlineData("nl-20050617-062.xml")] // Arbeidsmiljøloven
    [InlineData("nl-19990326-017.xml")] // Husleieloven
    [InlineData("nl-20050617-085.xml")] // Finnmarksloven
    public async Task CreateChunks_MatchesExpected(string fileName)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        string expectedPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Expected", "Chunker", Path.ChangeExtension(fileName, ".json"));
        var expected = JsonSerializer.Deserialize<List<Chunk>>(await File.ReadAllTextAsync(expectedPath, ct), GoldenFiles.JsonOptions)!;
        var law = await LawParser.ParseAsync(path, ct);

        // Act
        var actual = Chunker.CreateChunks(law);

        // Assert
        Assert.Equal(expected.Count, actual.Count);

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(
                JsonSerializer.Serialize(expected[i], GoldenFiles.JsonOptions),
                JsonSerializer.Serialize(actual[i], GoldenFiles.JsonOptions));
        }
    }

    // Golden files accept whatever they are given when rewritten, so they cannot catch lost text.
    // For each paragraph: the subsections from all its chunks, joined by number, must hold the same
    // text as the subsections from the parser. Whitespace is ignored, because splitting a long
    // subsection into sentences turns line breaks into spaces.
    [Theory]
    [InlineData("nl-20050617-062.xml")] // Arbeidsmiljøloven
    [InlineData("nl-19990326-017.xml")] // Husleieloven
    [InlineData("nl-20050617-085.xml")] // Finnmarksloven
    public async Task CreateChunks_KeepsAllText(string fileName)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        var law = await LawParser.ParseAsync(path, ct);

        // Act
        var chunks = Chunker.CreateChunks(law);

        // Assert
        foreach (var paragraph in law.Paragraphs)
        {
            var expected = paragraph.Subsections
                .Select(s => (s.Number, Normalize(s.Text)));

            var actual = chunks
                .Where(c => c.ParagraphId == paragraph.Id)
                .SelectMany(c => c.Subsections)
                .GroupBy(s => s.Number)
                .Select(g => (g.Key, Normalize(string.Join(" ", g.Select(s => s.Text)))));

            Assert.Equal(expected, actual);
        }

        static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
    }
}