using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ingestion.Chunking;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace Ingestion;

internal sealed record EmbeddedChunk(Chunk Chunk, ReadOnlyMemory<float> Vector);
internal sealed record StoredLaw(string DocumentId, string FileName, string FileHash);

public sealed class IngestionRepository(NpgsqlDataSource db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal async Task<Dictionary<string, StoredLaw>> GetStoredLawsAsync(CancellationToken ct)
    {
        var laws = new Dictionary<string, StoredLaw>();

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT id, file_name, file_hash FROM documents", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var law = new StoredLaw(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            laws[law.FileName] = law;
        }

        return laws;
    }

    internal async Task SaveLawAsync(
        string documentId,
        string lawName,
        string fileName,
        string fileHash,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var upsert = new NpgsqlCommand("""
            INSERT INTO documents (id, title, file_name, file_hash)
            VALUES (@id, @title, @fileName, @hash)
            ON CONFLICT (id) DO UPDATE SET
                title = EXCLUDED.title,
                file_name = EXCLUDED.file_name,
                file_hash = EXCLUDED.file_hash,
                updated_at = now()
            """, conn, tx))
        {
            upsert.Parameters.AddWithValue("id", documentId);
            upsert.Parameters.AddWithValue("title", lawName);
            upsert.Parameters.AddWithValue("fileName", fileName);
            upsert.Parameters.AddWithValue("hash", fileHash);
            await upsert.ExecuteNonQueryAsync(ct);
        }

        await using (var delete = new NpgsqlCommand("DELETE FROM chunks WHERE document_id = @id", conn, tx))
        {
            delete.Parameters.AddWithValue("id", documentId);
            await delete.ExecuteNonQueryAsync(ct);
        }

        foreach (var (chunk, vector) in chunks)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO chunks (id, document_id, paragraph_id, chapter, number, title, subsections, content, embedding)
                VALUES (@id, @documentId, @paragraphId, @chapter, @number, @title, @subsections, @content, @embedding)
            """, conn, tx);

            insert.Parameters.AddWithValue("id", chunk.Id);
            insert.Parameters.AddWithValue("documentId", chunk.DocumentId);
            insert.Parameters.AddWithValue("paragraphId", chunk.ParagraphId);
            insert.Parameters.AddWithValue("chapter", chunk.Chapter);
            insert.Parameters.AddWithValue("number", chunk.Number);
            insert.Parameters.AddWithValue("title", chunk.Title);
            insert.Parameters.AddWithValue("subsections", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(chunk.Subsections, JsonOptions));
            insert.Parameters.AddWithValue("content", chunk.Content);
            insert.Parameters.AddWithValue("embedding", new Vector(vector));
            await insert.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    internal async Task RemoveLawAsync(string documentId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM documents WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", documentId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<string?> GetFileHashAsync(string fileName, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT file_hash FROM documents WHERE file_name = @fileName", conn);
        cmd.Parameters.AddWithValue("fileName", fileName);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }
}