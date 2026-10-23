using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SharedKernel.Query;

namespace Query;

internal sealed record RerankRequest(string Query, IReadOnlyList<string> Texts);
internal sealed record RerankResult(int Index, double Score);

internal sealed class RerankClient(IHttpClientFactory httpClientFactory, IOptions<RerankSettings> settings)
{
    public const string HttpClientName = "Rerank";

    public async Task<double[]> ScoreAsync(string query, IReadOnlyList<string> texts, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        int batchSize = settings.Value.BatchSize;
        var scores = new double[texts.Count];

        for (int offset = 0; offset < texts.Count; offset += batchSize)
        {
            var batch = texts.Skip(offset).Take(batchSize).ToList();

            using var response = await http.PostAsJsonAsync("rerank", new RerankRequest(query, batch), ct);
            response.EnsureSuccessStatusCode();

            var results = await response.Content.ReadFromJsonAsync<List<RerankResult>>(ct) ?? [];

            foreach (var result in results)
            {
                scores[offset + result.Index] = result.Score;
            }
        }

        return scores;
    }
}