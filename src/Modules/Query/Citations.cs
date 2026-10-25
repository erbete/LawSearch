using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Query;

// Finds paragraph citations like "§ 9-6" in a text.
// Search uses it to fetch the paragraphs the user names in the question.
// The answer is checked with it, in case the language model cites a paragraph it was not given.
internal static partial class Citations
{
    private static readonly Dictionary<string, int> Ordinals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["første"] = 1,
        ["andre"] = 2,
        ["annet"] = 2,
        ["tredje"] = 3,
        ["fjerde"] = 4,
        ["femte"] = 5,
        ["sjette"] = 6,
        ["sjuende"] = 7,
        ["syvende"] = 7,
        ["åttende"] = 8,
        ["niende"] = 9,
        ["tiende"] = 10,
        ["ellevte"] = 11,
        ["tolvte"] = 12,
        ["trettende"] = 13,
        ["fjortende"] = 14,
        ["femtende"] = 15,
    };

    // Finds "§ 28 a" or "arbeidsmiljøloven § 15-3" in the question.
    // Returns the law (empty if not written) and the number without spaces, e.g. "28A".
    // Returns an empty list if the question has none.
    public static List<(string Law, string Number)> FindReferences(string query)
    {
        var references = new List<(string Law, string Number)>();

        foreach (Match match in Citation().Matches(query))
        {
            string law = match.Groups["law"].Value;
            string number = Normalize(match.Groups["number"].Value);

            if (!references.Contains((law, number)))
            {
                references.Add((law, number));
            }
        }

        return references;
    }

    // Finds citations in parentheses in the answer, e.g. "(Husleieloven § 9-6 andre ledd)",
    // and returns the ones that do not match any of the paragraphs the language model got.
    public static List<string> FindInvalid(string answer, IReadOnlyList<SearchHit> paragraphs)
    {
        var invalid = new List<string>();

        foreach (Match parentheses in Parentheses().Matches(answer))
        {
            foreach (Match match in Citation().Matches(parentheses.Groups[1].Value))
            {
                string law = match.Groups["law"].Value;
                string number = Normalize(match.Groups["number"].Value);
                var subsections = ParseSubsections(match.Groups["subsections"].Value);

                bool valid = paragraphs.Any(p =>
                    Normalize(p.Number) == number &&
                    (law.Length == 0 || string.Equals(p.Law, law, StringComparison.OrdinalIgnoreCase)) &&
                    subsections.All(n => p.Subsections.Any(s => s.Number == n)));

                if (!valid && !invalid.Contains(match.Value))
                {
                    invalid.Add(match.Value);
                }
            }
        }

        return invalid;

        // "første og andre" -> 1, 2. "første til tredje" -> 1, 2, 3.
        static List<int> ParseSubsections(string text)
        {
            var numbers = new List<int>();
            bool range = false;

            foreach (var word in text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                if (word == "og")
                {
                    continue;
                }

                if (word == "til")
                {
                    range = true;
                    continue;
                }

                if (!Ordinals.TryGetValue(word, out int n))
                {
                    return [];
                }

                if (range && numbers.Count > 0)
                {
                    for (int i = numbers[^1] + 1; i < n; i++)
                    {
                        numbers.Add(i);
                    }
                }

                numbers.Add(n);
                range = false;
            }

            return numbers;
        }
    }

    private static string Normalize(string number) =>
        string.Concat(number.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    [GeneratedRegex(@"\(([^()]*)\)")]
    private static partial Regex Parentheses();

    [GeneratedRegex(@"(?:(?<law>\p{L}+loven)\s+)?§\s*(?<number>\d+(?:\s?[A-Z](?!\p{L}))?(?:-\d+)?(?:\s?[a-z](?!\p{L}))?)(?:\s+(?<subsections>\p{Ll}+(?:(?:\s*,\s*|\s+og\s+|\s+til\s+)\p{Ll}+)*)\s+ledd)?")]
    private static partial Regex Citation();
}