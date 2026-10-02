using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CriterionArt.Provider;

internal record SearchCandidate(string Title, string? Director, int? Year, string ImageUrl, string Url);

internal static class CriterionSearchParser
{
    public static List<SearchCandidate> ParseSnapshot(string snapshotJson)
    {
        var candidates = new List<SearchCandidate>();

        using var snapshotDoc = JsonDocument.Parse(snapshotJson);
        var products = snapshotDoc.RootElement.GetProperty("data").GetProperty("products");
        var items = products[0];

        foreach (var pair in items.EnumerateArray())
        {
            var product = pair[0];

            var title = product.GetProperty("title").GetString();
            if (string.IsNullOrWhiteSpace(title)) continue;

            var slug = product.GetProperty("slug").GetString();
            var imageUrl = product.GetProperty("image_url").GetString();
            var director = product.TryGetProperty("directors", out var d) ? d.GetString() : null;

            int? year = null;
            if (product.TryGetProperty("year", out var y) && int.TryParse(y.GetString(), out var parsedYear))
                year = parsedYear;

            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(imageUrl)) continue;

            candidates.Add(new SearchCandidate(title, director, year, imageUrl, $"https://www.criterion.com/{slug}"));
        }

        return candidates;
    }

    public static SearchCandidate? FindBestMatch(
        IEnumerable<SearchCandidate> candidates, string targetTitle, int? targetYear)
    {
        SearchCandidate? best = null;
        double bestScore = 0;

        foreach (var c in candidates)
        {
            double score = TitleSimilarity(c.Title, targetTitle);

            if (targetYear.HasValue && c.Year.HasValue)
            {
                if (c.Year == targetYear) score += 0.5;
                else if (Math.Abs(c.Year.Value - targetYear.Value) <= 1) score += 0.2;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        return bestScore >= 0.65 ? best : null;
    }

    private static double TitleSimilarity(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);

        if (na == nb) return 1.0;
        if (na.Contains(nb) || nb.Contains(na)) return 0.85;

        var setA = na.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var setB = nb.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (setA.Count == 0 || setB.Count == 0) return 0;

        return (double)setA.Intersect(setB).Count() / Math.Max(setA.Count, setB.Count);
    }

    private static string Normalize(string s) =>
        Regex.Replace(s.ToLowerInvariant(), @"[^\w\s]", "").Trim();
}