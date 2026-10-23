using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Ingestion.Parsing;

internal sealed partial class LawParser
{
    public static async Task<Law> ParseAsync(string path, CancellationToken ct)
    {
        var doc = await GetHtmlDocumentAsync(path, ct);

        string documentId = GetDocumentId(doc, path);
        string lawName = GetLawName(doc, path);

        StripAnnotations(doc);
        AddListLineBreaks(doc);

        var paragraphs = doc.QuerySelectorAll("article.legalArticle:not([data-repealeddate])")
            .Where(a => a.QuerySelector(".legalArticleTitle")?.TextContent.Trim() != "(Opphevet)")
            .Select(ToParagraph)
            .Where(p => p.Subsections.Count > 0 && !OmittedMarker().IsMatch(p.Subsections[^1].Text))
            .ToList();

        return new Law(documentId, lawName, paragraphs);
    }

    private static async Task<IHtmlDocument> GetHtmlDocumentAsync(string path, CancellationToken ct)
    {
        string html = await File.ReadAllTextAsync(path, ct);
        return await new HtmlParser().ParseDocumentAsync(html, ct);
    }

    private static string GetDocumentId(IHtmlDocument doc, string path)
    {
        string? id = doc.QuerySelector("dd.dokid")?.TextContent.Trim();

        return string.IsNullOrEmpty(id)
            ? Path.GetFileNameWithoutExtension(path)
            : id;
    }

    private static string GetLawName(IHtmlDocument doc, string path)
    {
        string? lawName = doc.QuerySelector("dd.titleShort")?.TextContent
            ?? doc.QuerySelector("dd.title")?.TextContent;

        if (string.IsNullOrWhiteSpace(lawName))
        {
            throw new InvalidDataException($"Law name not found in '{path}'.");
        }

        return TitleSeparator().Split(lawName)[0].Trim();
    }

    private static void StripAnnotations(IHtmlDocument doc)
    {
        var selection = doc
            .QuerySelectorAll("sup.footnotereference, footer.footnotes, article.changesToParent")
            .ToList();

        foreach (var el in selection)
        {
            el.Remove();
        }
    }

    private static void AddListLineBreaks(IHtmlDocument doc)
    {
        foreach (var li in doc.QuerySelectorAll("ol.defaultList > li"))
        {
            li.Prepend(doc.CreateTextNode($"\n{li.GetAttribute("data-name")} "));
        }

        foreach (var ol in doc.QuerySelectorAll("ol.defaultList"))
        {
            ol.After(doc.CreateTextNode("\n"));
        }
    }

    private static Paragraph ToParagraph(IElement article)
    {
        string paragraphNumber = GetParagraphNumber(article);

        return new Paragraph(
            Id: article.GetAttribute("data-lovdata-url") ?? paragraphNumber,
            Chapter: GetChapterPath(article),
            Number: paragraphNumber,
            Title: article.QuerySelector(".legalArticleTitle")?.TextContent.Trim() ?? "",
            Subsections: GetSubsections(article));
    }

    private static string GetParagraphNumber(IElement article)
    {
        string number = article.QuerySelector(".legalArticleValue")?.TextContent
            ?? article.GetAttribute("data-name")
            ?? "";

        return Whitespace().Replace(number, " ").Trim().TrimStart('§').Trim();
    }

    private static List<Subsection> GetSubsections(IElement article)
    {
        return article.Children
            .Where(c => c.ClassList.Contains("legalP") || c.ClassList.Contains("numberedLegalP"))
            .Select((c, i) =>
            {
                var match = SubsectionId().Match(c.Id ?? "");

                int number =
                    int.TryParse(c.GetAttribute("data-numerator"), NumberStyles.None, CultureInfo.InvariantCulture, out var numerator) ? numerator
                    : match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                    : i + 1;

                string text = string.Join("\n", c.TextContent
                    .Split('\n')
                    .Select(l => Whitespace().Replace(l, " ").Trim())
                    .Where(l => l.Length > 0));

                return new Subsection(number, text);
            })
            .Where(s => s.Text.Length > 0)
            .ToList();
    }

    private static string GetChapterPath(IElement article)
    {
        var headings = new List<string>();

        for (var parent = article.ParentElement; parent != null; parent = parent.ParentElement)
        {
            if (parent.LocalName == "section" && parent.ClassList.Contains("section"))
            {
                var heading = parent.FirstElementChild?.TextContent.Trim();

                if (!string.IsNullOrEmpty(heading))
                {
                    headings.Add(heading);
                }
            }
        }

        headings.Reverse();
        return string.Join(" > ", headings);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s[\-\u2013\u2014]\s")] // " – ", " - " (em dash etc)
    private static partial Regex TitleSeparator();

    [GeneratedRegex(@"(?:^|\s)(?:[\-\u2013\u2014]\s*){3}$")]
    private static partial Regex OmittedMarker();

    [GeneratedRegex(@"-ledd-(\d+)$")]
    private static partial Regex SubsectionId();
}