using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Ingestion.Parsing;

namespace Ingestion.Tests;

public sealed class LawParserTests
{
    [Theory]
    [InlineData("nl-20050617-062.xml")] // Arbeidsmiljøloven
    [InlineData("nl-19990326-017.xml")] // Husleieloven
    [InlineData("nl-20050617-085.xml")] // Finnmarksloven
    public async Task Parse_IdsAreUnique(string fileName)
    {
        // Arrange
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

        // Act
        var paragraphs = (await LawParser.ParseAsync(path, TestContext.Current.CancellationToken)).Paragraphs;

        // Assert
        var ids = paragraphs.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [InlineData("nl-20050617-062.xml")] // Arbeidsmiljøloven
    [InlineData("nl-19990326-017.xml")] // Husleieloven
    [InlineData("nl-20050617-085.xml")] // Finnmarksloven
    public async Task Parse_MatchesExpected(string fileName)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        string expectedPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Expected", "LawParser", Path.ChangeExtension(fileName, ".json"));
        var expected = JsonSerializer.Deserialize<Law>(await File.ReadAllTextAsync(expectedPath, ct), GoldenFiles.JsonOptions)!;

        // Act
        var actual = await LawParser.ParseAsync(path, ct);

        // Assert
        Assert.Equal(expected.DocumentId, actual.DocumentId);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Paragraphs.Count, actual.Paragraphs.Count);

        for (var i = 0; i < expected.Paragraphs.Count; i++)
        {
            Assert.Equal(
                JsonSerializer.Serialize(expected.Paragraphs[i], GoldenFiles.JsonOptions),
                JsonSerializer.Serialize(actual.Paragraphs[i], GoldenFiles.JsonOptions));
        }
    }
}