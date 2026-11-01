using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TradingAgents;

public sealed class MemoryEntry
{
    public string Date { get; set; } = "";
    public string Ticker { get; set; } = "";
    public string Rating { get; set; } = "";
    public bool Pending { get; set; }
    public string? Raw { get; set; }
    public string? Alpha { get; set; }
    public string? Holding { get; set; }
    public string? Resolved { get; set; }
    public string Decision { get; set; } = "";
    public string Reflection { get; set; } = "";
}

public sealed class TradingMemoryLog
{
    public const string Separator = "\n\n<!-- ENTRY_END -->\n\n";
    static readonly Regex DecisionPattern = new Regex("DECISION:\n(.*?)(?=\nREFLECTION:|\\Z)", RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex ReflectionPattern = new Regex("REFLECTION:\n(.*)\\Z", RegexOptions.Singleline | RegexOptions.Compiled);

    readonly string? _path;
    readonly int? _maxResolvedEntries;

    public TradingMemoryLog(string? path, int? maxResolvedEntries = null)
    {
        _maxResolvedEntries = maxResolvedEntries;
        if (string.IsNullOrWhiteSpace(path))
            return;
        _path = path;
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
    }

    public void StoreDecision(string ticker, string tradeDate, string finalTradeDecision, string? rating = null)
    {
        if (_path == null)
            return;
        string symbol = PortfolioContext.NormalizeSymbol(ticker);
        string resolvedRating = string.IsNullOrWhiteSpace(rating) ? Rating.Parse(finalTradeDecision) : rating;
        string tag = "[" + tradeDate + " | " + symbol + " | " + resolvedRating + " | pending]";
        string entry = tag + "\n\nDECISION:\n" + finalTradeDecision + Separator;
        WithLock(() =>
        {
            string existing = File.Exists(_path) ? File.ReadAllText(_path, Encoding.UTF8) : "";
            if (HasEntry(existing, tradeDate, symbol))
                return;
            File.AppendAllText(_path, entry, new UTF8Encoding(false));
        });
    }

    public IReadOnlyList<MemoryEntry> LoadEntries()
    {
        if (_path == null || !File.Exists(_path))
            return Array.Empty<MemoryEntry>();
        string text = File.ReadAllText(_path, Encoding.UTF8);
        var entries = new List<MemoryEntry>();
        foreach (string raw in text.Split(new[] { Separator }, StringSplitOptions.None))
        {
            MemoryEntry? parsed = ParseEntry(raw);
            if (parsed != null)
                entries.Add(parsed);
        }
        return entries;
    }

    public string GetPastContext(string ticker, string? asOf, int nSame = 5, int nCross = 3)
    {
        string symbol = PortfolioContext.NormalizeSymbol(ticker);
        List<MemoryEntry> entries = LoadEntries().Where(entry => !entry.Pending).ToList();
        if (asOf != null)
            entries = entries.Where(entry => !string.IsNullOrEmpty(entry.Resolved) && string.CompareOrdinal(entry.Resolved, asOf) <= 0).ToList();
        if (entries.Count == 0)
            return "";
        var order = Enumerable.Range(0, entries.Count)
            .OrderByDescending(index => entries[index].Date, StringComparer.Ordinal)
            .ThenByDescending(index => index)
            .ToList();
        var same = new List<MemoryEntry>();
        var cross = new List<MemoryEntry>();
        foreach (int index in order)
        {
            if (same.Count >= nSame && cross.Count >= nCross)
                break;
            MemoryEntry entry = entries[index];
            if (entry.Ticker == symbol && same.Count < nSame)
                same.Add(entry);
            else if (entry.Ticker != symbol && cross.Count < nCross)
                cross.Add(entry);
        }
        if (same.Count == 0 && cross.Count == 0)
            return "";
        var parts = new List<string>();
        if (same.Count > 0)
        {
            parts.Add("Past analyses of " + symbol + " (most recent first):");
            parts.AddRange(same.Select(FormatFull));
        }
        if (cross.Count > 0)
        {
            parts.Add("Recent cross-ticker lessons:");
            parts.AddRange(cross.Select(FormatReflectionOnly));
        }
        return string.Join("\n\n", parts);
    }

    public bool Settle(string ticker, string tradeDate, double rawReturn, double alphaReturn, int holdingDays, string reflection, string? resolutionDate = null)
    {
        if (_path == null || !File.Exists(_path))
            return false;
        string symbol = PortfolioContext.NormalizeSymbol(ticker);
        bool updated = false;
        WithLock(() =>
        {
            string text = File.ReadAllText(_path, Encoding.UTF8);
            string[] blocks = text.Split(new[] { Separator }, StringSplitOptions.None);
            string prefix = "[" + tradeDate + " | " + symbol + " |";
            var rebuilt = new List<string>();
            foreach (string block in blocks)
            {
                string stripped = block.Trim();
                if (stripped.Length == 0)
                {
                    rebuilt.Add(block);
                    continue;
                }
                string[] lines = stripped.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                string tagLine = lines[0].Trim();
                if (!updated && tagLine.StartsWith(prefix, StringComparison.Ordinal) && tagLine.EndsWith("| pending]", StringComparison.Ordinal))
                {
                    string[] fields = tagLine.Substring(1, tagLine.Length - 2).Split('|').Select(field => field.Trim()).ToArray();
                    string rating = fields.Length > 2 ? fields[2] : Rating.Review;
                    string rest = string.Join("\n", lines.Skip(1)).TrimStart();
                    string replacement = ResolvedTag(tradeDate, symbol, rating, rawReturn, alphaReturn, holdingDays, resolutionDate)
                        + "\n\n" + rest + "\n\nREFLECTION:\n" + reflection;
                    rebuilt.Add(replacement);
                    updated = true;
                }
                else
                {
                    rebuilt.Add(block);
                }
            }
            if (!updated)
                return;
            List<string> rotated = Rotate(rebuilt);
            string combined = string.Join(Separator, rotated);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, combined, new UTF8Encoding(false));
            File.Move(temporary, _path, true);
        });
        return updated;
    }

    List<string> Rotate(List<string> blocks)
    {
        if (_maxResolvedEntries == null || _maxResolvedEntries <= 0)
            return blocks;
        var described = new List<(string Block, bool Resolved, string Date)>();
        foreach (string block in blocks)
        {
            string stripped = block.Trim();
            if (stripped.Length == 0)
            {
                described.Add((block, false, ""));
                continue;
            }
            string tagLine = stripped.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0].Trim();
            bool resolved = tagLine.StartsWith("[", StringComparison.Ordinal)
                && tagLine.EndsWith("]", StringComparison.Ordinal)
                && !tagLine.EndsWith("| pending]", StringComparison.Ordinal);
            string date = "";
            if (tagLine.StartsWith("[", StringComparison.Ordinal))
                date = tagLine.Substring(1).Split('|')[0].Trim();
            described.Add((block, resolved, date));
        }
        int resolvedCount = described.Count(item => item.Resolved);
        if (resolvedCount <= _maxResolvedEntries.Value)
            return blocks;
        var resolvedIndexes = Enumerable.Range(0, described.Count).Where(index => described[index].Resolved).ToList();
        resolvedIndexes.Sort((left, right) =>
        {
            int dateOrder = string.CompareOrdinal(described[left].Date, described[right].Date);
            return dateOrder != 0 ? dateOrder : left.CompareTo(right);
        });
        var dropped = new HashSet<int>(resolvedIndexes.Take(resolvedCount - _maxResolvedEntries.Value));
        return described.Where((item, index) => !dropped.Contains(index)).Select(item => item.Block).ToList();
    }

    static string ResolvedTag(string tradeDate, string ticker, string rating, double rawReturn, double alphaReturn, int holdingDays, string? resolutionDate)
    {
        string tag = "[" + tradeDate + " | " + ticker + " | " + rating + " | " + FormatPercent(rawReturn) + " | " + FormatPercent(alphaReturn) + " | " + holdingDays.ToString(CultureInfo.InvariantCulture) + "d";
        if (!string.IsNullOrWhiteSpace(resolutionDate))
            tag += " | resolved:" + resolutionDate;
        return tag + "]";
    }

    public static string FormatPercent(double value)
    {
        return value.ToString("+0.0%;-0.0%;+0.0%", CultureInfo.InvariantCulture);
    }

    static bool HasEntry(string text, string tradeDate, string ticker)
    {
        string prefix = "[" + tradeDate + " | " + ticker + " |";
        foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    static MemoryEntry? ParseEntry(string raw)
    {
        string stripped = raw.Trim();
        if (stripped.Length == 0)
            return null;
        string[] lines = stripped.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string tagLine = lines[0].Trim();
        if (!tagLine.StartsWith("[", StringComparison.Ordinal) || !tagLine.EndsWith("]", StringComparison.Ordinal))
            return null;
        string[] fields = tagLine.Substring(1, tagLine.Length - 2).Split('|').Select(field => field.Trim()).ToArray();
        if (fields.Length < 4)
            return null;
        string? resolved = null;
        for (int i = 6; i < fields.Length; i++)
        {
            if (fields[i].StartsWith("resolved:", StringComparison.Ordinal))
                resolved = fields[i].Substring("resolved:".Length).Trim();
        }
        string body = string.Join("\n", lines.Skip(1)).Trim();
        Match decision = DecisionPattern.Match(body);
        Match reflection = ReflectionPattern.Match(body);
        return new MemoryEntry
        {
            Date = fields[0],
            Ticker = fields[1],
            Rating = fields[2],
            Pending = fields[3] == "pending",
            Raw = fields[3] == "pending" ? null : fields[3],
            Alpha = fields.Length > 4 ? fields[4] : null,
            Holding = fields.Length > 5 ? fields[5] : null,
            Resolved = resolved,
            Decision = decision.Success ? decision.Groups[1].Value.Trim() : "",
            Reflection = reflection.Success ? reflection.Groups[1].Value.Trim() : ""
        };
    }

    static string FormatFull(MemoryEntry entry)
    {
        string tag = "[" + entry.Date + " | " + entry.Ticker + " | " + entry.Rating + " | " + (entry.Raw ?? "n/a") + " | " + (entry.Alpha ?? "n/a") + " | " + (entry.Holding ?? "n/a") + "]";
        var parts = new List<string> { tag, "DECISION:\n" + entry.Decision };
        if (!string.IsNullOrEmpty(entry.Reflection))
            parts.Add("REFLECTION:\n" + entry.Reflection);
        return string.Join("\n\n", parts);
    }

    static string FormatReflectionOnly(MemoryEntry entry)
    {
        string tag = "[" + entry.Date + " | " + entry.Ticker + " | " + entry.Rating + " | " + (entry.Raw ?? "n/a") + "]";
        if (!string.IsNullOrEmpty(entry.Reflection))
            return tag + "\n" + entry.Reflection;
        string text = entry.Decision.Length > 300 ? entry.Decision.Substring(0, 300) : entry.Decision;
        string suffix = entry.Decision.Length > 300 ? "..." : "";
        return tag + "\n" + text + suffix;
    }

    void WithLock(Action action)
    {
        if (_path == null)
            return;
        string lockPath = _path + ".lock";
        using var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        action();
    }
}
