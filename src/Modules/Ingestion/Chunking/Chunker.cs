using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ingestion.Parsing;
using SharedKernel;

namespace Ingestion.Chunking;

internal sealed record Chunk(
    string Id,
    string DocumentId,
    string ParagraphId,
    string Chapter,
    string Number,
    string Title,
    IReadOnlyList<Subsection> Subsections,
    string EmbeddingText)
{
    public string Content => string.Join("\n\n", Subsections.Select(s => s.Text));
}

internal sealed partial class Chunker
{
    private const int MaxWords = 500;

    public static List<Chunk> CreateChunks(Law law) =>
        law.Paragraphs.SelectMany(p => ChunkParagraph(law, p)).ToList();

    private static List<Chunk> ChunkParagraph(Law law, Paragraph paragraph)
    {
        var segments = new List<Segment>();

        foreach (var subsection in paragraph.Subsections)
        {
            int words = CountWords(subsection.Text);

            if (words <= MaxWords)
            {
                segments.Add(new Segment(subsection.Number, subsection.Text, words));
                continue;
            }

            foreach (var sentence in SentenceEnd().Split(subsection.Text))
            {
                segments.Add(new Segment(subsection.Number, sentence, CountWords(sentence)));
            }
        }

        var groups = new List<List<Segment>>();
        var current = new List<Segment>();
        int currentWords = 0;

        foreach (var segment in segments)
        {
            if (current.Count > 0 && currentWords + segment.Words > MaxWords)
            {
                groups.Add(current);
                current = [];
                currentWords = 0;
            }

            current.Add(segment);
            currentWords += segment.Words;
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return groups
            .Select((group, i) =>
            {
                var subsections = new List<Subsection>();

                foreach (var segment in group)
                {
                    if (subsections.Count > 0 && subsections[^1].Number == segment.Subsection)
                    {
                        subsections[^1] = subsections[^1] with { Text = $"{subsections[^1].Text} {segment.Text}" };
                    }
                    else
                    {
                        subsections.Add(new Subsection(segment.Subsection, segment.Text));
                    }
                }

                string id = groups.Count == 1 ? paragraph.Id : $"{paragraph.Id}#{i + 1}";
                string content = string.Join("\n\n", subsections.Select(s => s.Text));
                string header = ParagraphHeader.Format(law.Name, paragraph.Number, paragraph.Title);
                if (paragraph.Chapter.Length > 0) header += $"\n{paragraph.Chapter}";
                string embeddingText = $"{header}\n\n{content}";

                return new Chunk(id,
                    law.DocumentId,
                    paragraph.Id,
                    paragraph.Chapter,
                    paragraph.Number,
                    paragraph.Title,
                    subsections,
                    embeddingText);
            })
            .ToList();

        static int CountWords(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private readonly record struct Segment(int Subsection, string Text, int Words);

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-ZÆØÅ])")]
    private static partial Regex SentenceEnd();
}