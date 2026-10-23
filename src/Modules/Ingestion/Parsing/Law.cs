using System.Collections.Generic;

namespace Ingestion.Parsing;

internal sealed record Law(string DocumentId, string Name, IReadOnlyList<Paragraph> Paragraphs);

internal sealed record Paragraph(
    string Id,
    string Chapter,
    string Number,
    string Title,
    IReadOnlyList<Subsection> Subsections);

internal sealed record Subsection(int Number, string Text);