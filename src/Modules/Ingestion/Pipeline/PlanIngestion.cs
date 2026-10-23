using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SharedKernel.Ingestion;

namespace Ingestion.Pipeline;

internal sealed record IngestionPlan(IReadOnlyList<string> FilesToIngest, IReadOnlyList<StoredLaw> ToRemove);

internal sealed class PlanIngestion(IngestionRepository repository, IOptions<IngestionSettings> settings)
{
    public async Task<IngestionPlan> HandleAsync(CancellationToken ct)
    {
        var stored = await repository.GetStoredLawsAsync(ct);

        var filesToIngest = new List<string>();
        var found = new HashSet<string>();

        foreach (var file in Directory.GetFiles(settings.Value.DocumentsFolder, "*.xml"))
        {
            string fileName = Path.GetFileName(file);
            string hash = await FileHash.ComputeAsync(file, ct);
            found.Add(fileName);

            if (!stored.TryGetValue(fileName, out var law) || law.FileHash != hash)
            {
                filesToIngest.Add(fileName);
            }
        }

        var toRemove = stored.Values.Where(law => !found.Contains(law.FileName)).ToList();

        return new IngestionPlan(filesToIngest, toRemove);
    }
}