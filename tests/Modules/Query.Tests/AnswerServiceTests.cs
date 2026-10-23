using System.Collections.Generic;
using System.Linq;

namespace Query.Tests;

public sealed class AnswerServiceTests
{
    private static readonly List<SearchHit> Hits =
    [
        Hit("Arbeidsmiljøloven", "15-3", 10),
        Hit("Arbeidsmiljøloven", "14 A-2", 5),
        Hit("Husleieloven", "9-6", 4),
        Hit("Finnmarksloven", "27", 7),
    ];

    [Theory]
    [InlineData("(Arbeidsmiljøloven § 15-3 første ledd)")]
    [InlineData("(§ 15-3 niende ledd)")]
    [InlineData("(Husleieloven § 9-6)")]
    [InlineData("(§ 14A-2 andre ledd)")]
    [InlineData("(Arbeidsmiljøloven § 15-3 første til tredje ledd)")]
    [InlineData("(Husleieloven § 9-6 første og andre ledd)")]
    [InlineData("(§ 9-6 siste ledd)")]
    [InlineData("Det kan ikke kreves avgift etter § 24 (Finnmarksloven § 27 tredje ledd).")]
    public void FindInvalidCitations_ValidCitation_ReturnsEmpty(string answer)
    {
        // Act
        var actual = AnswerService.FindInvalidCitations(answer, Hits);

        // Assert
        Assert.Empty(actual);
    }

    [Theory]
    [InlineData("(Arbeidsmiljøloven § 15-11)", "Arbeidsmiljøloven § 15-11")]
    [InlineData("(Husleieloven § 9-6 femte ledd)", "Husleieloven § 9-6 femte ledd")]
    [InlineData("(Husleieloven § 15-3)", "Husleieloven § 15-3")]
    [InlineData("(Husleieloven § 9-6 tredje til sjette ledd)", "Husleieloven § 9-6 tredje til sjette ledd")]
    [InlineData("(§ 15-3 første ledd og § 15-11)", "§ 15-11")]
    public void FindInvalidCitations_InvalidCitation_ReturnsCitation(string answer, string expected)
    {
        // Act
        var actual = AnswerService.FindInvalidCitations(answer, Hits);

        // Assert
        Assert.Equal([expected], actual);
    }

    private static SearchHit Hit(string law, string number, int subsections) =>
        new("", law, "", number, "",
            Enumerable.Range(1, subsections).Select(i => new Subsection(i, "")).ToList(), 0);
}