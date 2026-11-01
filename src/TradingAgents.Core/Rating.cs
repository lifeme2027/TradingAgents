using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TradingAgents;

public static class Rating
{
    public static readonly string[] Scale = { "Buy", "Overweight", "Hold", "Underweight", "Sell" };
    public const string Review = "REVIEW";

    static readonly Regex Label = new Regex(
        "(?<![a-z])rating\\b[^:\\-\u2010-\u2015]*[:\\-\u2010-\u2015][\\s*]*(\\w+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex Line = new Regex(
        "^\\s*(?<item>(?:[-+*\u2022]|\\d+[.)])\\s+)?[\\s*_#]*(?:(?:final|our|overall|my|recommended|updated|revised|new|current)\\s+)*rating[^\\w:\\-\u2010-\u2015]*[:\\-\u2010-\u2015][\\s*]*(?<value>\\w+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex ScaleLine = new Regex(
        "rating\\s*(scale|options|legend)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? Extract(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        string norm = text.Normalize(NormalizationForm.FormKC);
        var lines = new List<string>();
        foreach (string raw in norm.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
        {
            if (string.IsNullOrWhiteSpace(raw) || ScaleLine.IsMatch(raw))
                continue;
            lines.Add(raw);
        }
        string first = "";
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("#", StringComparison.Ordinal) || Line.IsMatch(line))
            {
                first = line;
                break;
            }
        }
        Match openingMatch = Line.Match(first);
        bool openingOk = openingMatch.Success && IsScale(openingMatch.Groups["value"].Value);
        var own = new List<string>();
        if (openingOk && string.IsNullOrEmpty(openingMatch.Groups["item"].Value))
            return Canonical(openingMatch.Groups["value"].Value);
        if (openingOk)
            own.Add(Canonical(openingMatch.Groups["value"].Value));

        var labels = new List<string>();
        foreach (string line in lines)
        {
            Match lineMatch = Line.Match(line);
            if (lineMatch.Success && string.IsNullOrEmpty(lineMatch.Groups["item"].Value) && IsScale(lineMatch.Groups["value"].Value))
                own.Add(Canonical(lineMatch.Groups["value"].Value));
            foreach (Match label in Label.Matches(line))
            {
                if (IsScale(label.Groups[1].Value))
                    labels.Add(Canonical(label.Groups[1].Value));
            }
        }
        List<string> found = own.Count > 0 ? own : labels;
        if (found.Count == 0)
            return null;
        string firstRating = found[0];
        for (int i = 1; i < found.Count; i++)
        {
            if (!string.Equals(found[i], firstRating, StringComparison.Ordinal))
                return null;
        }
        return firstRating;
    }

    public static string Parse(string? text)
    {
        return Extract(text) ?? Review;
    }

    public static bool IsReview(string signal)
    {
        return signal == Review;
    }

    static bool IsScale(string value)
    {
        foreach (string rating in Scale)
        {
            if (string.Equals(rating, value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static string Canonical(string value)
    {
        string lower = value.ToLower(CultureInfo.InvariantCulture);
        return char.ToUpper(lower[0], CultureInfo.InvariantCulture) + lower.Substring(1);
    }
}
