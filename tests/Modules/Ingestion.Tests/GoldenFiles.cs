using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Ingestion.Chunking;
using Ingestion.Parsing;

namespace Ingestion.Tests;

public sealed class GoldenFiles
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    [Theory(Explicit = true)]
    [InlineData("nl-20050617-062.xml")] // Arbeidsmiljøloven
    [InlineData("nl-19990326-017.xml")] // Husleieloven
    [InlineData("nl-20050617-085.xml")] // Finnmarksloven
    public async Task Write(string fileName)
    {
        var ct = TestContext.Current.CancellationToken;
        string projectDirectory = typeof(GoldenFiles).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "ProjectDirectory").Value!;
        string testData = Path.Combine(projectDirectory, "TestData");
        string jsonFile = Path.ChangeExtension(fileName, ".json");

        var law = await LawParser.ParseAsync(Path.Combine(testData, fileName), ct);
        var chunks = Chunker.CreateChunks(law);

        await File.WriteAllTextAsync(Path.Combine(testData, "Expected", "LawParser", jsonFile), JsonSerializer.Serialize(law, JsonOptions), ct);
        await File.WriteAllTextAsync(Path.Combine(testData, "Expected", "Chunker", jsonFile), JsonSerializer.Serialize(chunks, JsonOptions), ct);
    }
}