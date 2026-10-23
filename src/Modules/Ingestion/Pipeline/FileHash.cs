using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Ingestion.Pipeline;

internal static class FileHash
{
    public static async Task<string> ComputeAsync(string file, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
}