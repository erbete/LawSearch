using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
}