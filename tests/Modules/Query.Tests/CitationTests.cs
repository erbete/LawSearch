using System.Collections.Generic;
using System.Linq;

namespace Query.Tests;

public sealed class CitationTests
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
    public void FindInvalid_ValidCitation_ReturnsEmpty(string answer)
    {
        // Act
        var actual = Citations.FindInvalid(answer, Hits);

        // Assert
        Assert.Empty(actual);
    }

    [Theory]
    [InlineData("(Arbeidsmiljøloven § 15-11)", "Arbeidsmiljøloven § 15-11")]
    [InlineData("(Husleieloven § 9-6 femte ledd)", "Husleieloven § 9-6 femte ledd")]
    [InlineData("(Husleieloven § 15-3)", "Husleieloven § 15-3")]
    [InlineData("(Husleieloven § 9-6 tredje til sjette ledd)", "Husleieloven § 9-6 tredje til sjette ledd")]
    [InlineData("(§ 15-3 første ledd og § 15-11)", "§ 15-11")]
    public void FindInvalid_InvalidCitation_ReturnsCitation(string answer, string expected)
    {
        // Act
        var actual = Citations.FindInvalid(answer, Hits);

        // Assert
        Assert.Equal([expected], actual);
    }

    [Theory]
    [InlineData("Hva sier § 28 a?", "", "28A")]
    [InlineData("Hva sier arbeidsmiljøloven § 15-3?", "arbeidsmiljøloven", "15-3")]
    [InlineData("Gjelder § 14 A-2 meg?", "", "14A-2")]
    public void FindReferences_Reference_ReturnsLawAndNumber(string question, string law, string number)
    {
        // Act
        var actual = Citations.FindReferences(question);

        // Assert
        Assert.Equal((law, number), Assert.Single(actual));
    }

    [Fact]
    public void FindReferences_NoReference_ReturnsEmpty()
    {
        // Act
        var actual = Citations.FindReferences("Hvor lang oppsigelsestid har jeg?");

        // Assert
        Assert.Empty(actual);
    }

    private static SearchHit Hit(string law, string number, int subsections) =>
        new("", law, "", number, "",
            Enumerable.Range(1, subsections).Select(i => new Subsection(i, "")).ToList(), 0);
}