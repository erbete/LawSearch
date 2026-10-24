using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;
using SharedKernel.Query;

namespace Query;

internal sealed record HistoryEntry(string Question, string Answer);

internal sealed class QueryRepository(NpgsqlDataSource db, IOptions<QuerySettings> settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<(SearchHit Hit, bool Exact)>> SearchAsync(
    ReadOnlyMemory<float> vector,
    string query,
    IReadOnlyList<(string Law, string Number)> references,
    CancellationToken ct)
    {
        var s = settings.Value;

        await using var conn = await db.OpenConnectionAsync(ct);

        await using var cmd = new NpgsqlCommand("""
        WITH vector AS (
            SELECT id, ROW_NUMBER() OVER (ORDER BY distance) AS rank
            FROM (
                SELECT id, embedding <=> @embedding AS distance
                FROM chunks
                ORDER BY embedding <=> @embedding
                LIMIT @vectorCandidates
            ) nearest
        ),
        keyword AS (
            SELECT id, ROW_NUMBER() OVER (ORDER BY ts_rank(tsv, query) DESC) AS rank
            FROM chunks,
                to_tsquery('norwegian', replace(plainto_tsquery('norwegian', @query)::text, '&', '|')) AS query
            WHERE tsv @@ query
            ORDER BY ts_rank(tsv, query) DESC
            LIMIT @keywordCandidates
        ),
        fused AS (
            SELECT id, SUM(weight / (@rrfK + rank))::float8 AS score
            FROM (
                SELECT id, rank, @vectorWeight AS weight FROM vector
                UNION ALL
                SELECT id, rank, @keywordWeight AS weight FROM keyword
            ) ranked
            GROUP BY id
        ),
        exact AS (
            SELECT DISTINCT c.paragraph_id
            FROM chunks c
            JOIN documents d ON d.id = c.document_id
            JOIN unnest(@laws::text[], @numbers::text[]) AS r(law, number)
                ON upper(replace(c.number, ' ', '')) = r.number
               AND (r.law = '' OR lower(d.title) = lower(r.law))
        ),
        paragraphs AS (
            SELECT paragraph_id, MAX(score) AS score, bool_or(exact) AS exact
            FROM (
                SELECT c.paragraph_id, f.score, false AS exact
                FROM fused f
                JOIN chunks c ON c.id = f.id
                UNION ALL
                SELECT paragraph_id, 1.0::float8, true
                FROM exact
            ) candidates
            GROUP BY paragraph_id
            ORDER BY exact DESC, score DESC
            LIMIT @limit
        ),
        subsections AS (
            SELECT c.paragraph_id,
                   (s.elem ->> 'number')::int AS number,
                   string_agg(s.elem ->> 'text', ' ' ORDER BY c.id, s.ord) AS text
            FROM paragraphs p
            JOIN chunks c ON c.paragraph_id = p.paragraph_id
            CROSS JOIN LATERAL jsonb_array_elements(c.subsections) WITH ORDINALITY AS s(elem, ord)
            GROUP BY c.paragraph_id, (s.elem ->> 'number')::int
        )
        SELECT p.paragraph_id, d.title, c.chapter, c.number, c.title,
               (SELECT jsonb_agg(jsonb_build_object('number', ss.number, 'text', ss.text) ORDER BY ss.number)
                FROM subsections ss
                WHERE ss.paragraph_id = p.paragraph_id),
               p.score,
               p.exact
        FROM paragraphs p
        JOIN LATERAL (
            SELECT chapter, number, title, document_id
            FROM chunks
            WHERE paragraph_id = p.paragraph_id
            ORDER BY id
            LIMIT 1
        ) c ON true
        JOIN documents d ON d.id = c.document_id
        ORDER BY p.exact DESC, p.score DESC
        """, conn);

        cmd.Parameters.AddWithValue("embedding", new Vector(vector));
        cmd.Parameters.AddWithValue("query", query);
        cmd.Parameters.AddWithValue("laws", references.Select(r => r.Law).ToArray());
        cmd.Parameters.AddWithValue("numbers", references.Select(r => r.Number).ToArray());
        cmd.Parameters.AddWithValue("vectorCandidates", s.VectorCandidates);
        cmd.Parameters.AddWithValue("keywordCandidates", s.KeywordCandidates);
        cmd.Parameters.AddWithValue("rrfK", s.RrfK);
        cmd.Parameters.AddWithValue("vectorWeight", s.VectorWeight);
        cmd.Parameters.AddWithValue("keywordWeight", s.KeywordWeight);
        cmd.Parameters.AddWithValue("limit", s.RerankCandidates);

        var hits = new List<(SearchHit Hit, bool Exact)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var subsections = (JsonSerializer.Deserialize<List<Subsection>>(reader.GetString(5), JsonOptions) ?? [])
                .Select(s =>
                {
                    string prefix = $"({s.Number}) ";
                    return s.Text.StartsWith(prefix, StringComparison.Ordinal) ? s with { Text = s.Text[prefix.Length..] } : s;
                })
                .ToList();

            var hit = new SearchHit(
                Id: reader.GetString(0),
                Law: reader.GetString(1),
                Chapter: reader.GetString(2),
                Number: reader.GetString(3),
                Title: reader.GetString(4),
                Subsections: subsections,
                Score: reader.GetDouble(6));

            hits.Add((hit, reader.GetBoolean(7)));
        }

        return hits;
    }

    public async Task<List<HistoryEntry>> GetHistoryAsync(Guid conversationId, int length, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT question, answer
            FROM (
                SELECT id, question, answer
                FROM conversation_history
                WHERE conversation_id = @conversationId
                ORDER BY id DESC
                LIMIT @length
            ) latest
            ORDER BY id
        """, conn);

        cmd.Parameters.AddWithValue("conversationId", conversationId);
        cmd.Parameters.AddWithValue("length", length);

        var history = new List<HistoryEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            history.Add(new HistoryEntry(reader.GetString(0), reader.GetString(1)));
        }

        return history;
    }

    public async Task SaveHistoryAsync(
        Guid conversationId,
        string question,
        string? rewrittenQuestion,
        string answer,
        CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO conversation_history (conversation_id, question, rewritten_question, answer)
            VALUES (@conversationId, @question, @rewrittenQuestion, @answer)
        """, conn);

        cmd.Parameters.AddWithValue("conversationId", conversationId);
        cmd.Parameters.AddWithValue("question", question);
        cmd.Parameters.AddWithValue("rewrittenQuestion", (object?)rewrittenQuestion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("answer", answer);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}