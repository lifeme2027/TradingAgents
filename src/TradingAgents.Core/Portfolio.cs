using System.Globalization;
using System.Text.Json;

namespace TradingAgents;

public sealed class Position
{
    public string Ticker { get; set; } = "";
    public double Quantity { get; set; }
    public double? AveragePrice { get; set; }
}

public sealed class PortfolioContext
{
    public double? Cash { get; set; }
    public string? Currency { get; set; }
    public List<Position> Positions { get; set; } = new List<Position>();

    public static string NormalizeSymbol(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("Ticker is required.");
        string symbol = ticker.Trim().ToUpperInvariant();
        if (symbol.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || symbol.Contains("..") || symbol.Contains('/') || symbol.Contains('\\'))
            throw new ArgumentException("Ticker is not a symbol: " + ticker);
        return symbol;
    }

    public List<Position> PositionsIn(string ticker)
    {
        string symbol = NormalizeSymbol(ticker);
        return Positions.Where(position => NormalizeSymbol(position.Ticker) == symbol).ToList();
    }

    public string Render(string ticker)
    {
        string symbol = NormalizeSymbol(ticker);
        List<Position> held = PositionsIn(symbol);
        var lines = new List<string>();
        foreach (Position position in held)
        {
            string price = position.AveragePrice.HasValue
                ? ", average price " + position.AveragePrice.Value.ToString("N2", CultureInfo.InvariantCulture)
                : "";
            string spelled = position.Ticker.Trim().ToUpperInvariant();
            string listed = spelled == symbol ? "" : " (listed as " + position.Ticker + ")";
            lines.Add("- Current position in " + symbol + ": " + position.Quantity.ToString("0.####", CultureInfo.InvariantCulture) + " units" + price + listed);
        }
        if (held.Count == 0)
            lines.Add("- No current position in " + symbol);
        if (Cash.HasValue)
            lines.Add("- Cash available: " + Cash.Value.ToString("N2", CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(Currency) ? "" : " " + Currency));
        List<Position> others = Positions.Where(position => held.All(item => !ReferenceEquals(item, position))).ToList();
        if (others.Count > 0)
            lines.Add("- Other positions: " + string.Join(", ", others.Select(position => position.Ticker.ToUpperInvariant() + " " + position.Quantity.ToString("0.####", CultureInfo.InvariantCulture))));
        return "Portfolio at the analysis date:\n" + string.Join("\n", lines);
    }

    public string Fingerprint()
    {
        var payload = new
        {
            cash = Cash,
            currency = Currency ?? "",
            positions = Positions.Select(position => new
            {
                ticker = NormalizeSymbol(position.Ticker),
                quantity = position.Quantity,
                average_price = position.AveragePrice
            }).ToList()
        };
        string json = JsonSerializer.Serialize(payload);
        return Sha256Hex.Compute(json).Substring(0, 12);
    }

    public static string FingerprintOf(PortfolioContext? portfolio)
    {
        return portfolio == null ? "omitted" : portfolio.Fingerprint();
    }

    public static PortfolioContext Load(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            return Read(document.RootElement);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException or NotSupportedException)
        {
            throw new ArgumentException("portfolio file " + path + " is not usable: " + ex.Message, ex);
        }
    }

    public static PortfolioContext Read(JsonElement element)
    {
        var portfolio = new PortfolioContext();
        if (element.TryGetProperty("cash", out JsonElement cash) && cash.ValueKind != JsonValueKind.Null)
            portfolio.Cash = ReadFinite(cash, "cash");
        if (element.TryGetProperty("currency", out JsonElement currency) && currency.ValueKind == JsonValueKind.String)
            portfolio.Currency = currency.GetString();
        if (element.TryGetProperty("positions", out JsonElement positions))
        {
            foreach (JsonElement item in positions.EnumerateArray())
            {
                var position = new Position
                {
                    Ticker = item.GetProperty("ticker").GetString() ?? "",
                    Quantity = ReadFinite(item.GetProperty("quantity"), "quantity")
                };
                if (item.TryGetProperty("average_price", out JsonElement average) && average.ValueKind != JsonValueKind.Null)
                    position.AveragePrice = ReadFinite(average, "average_price");
                NormalizeSymbol(position.Ticker);
                portfolio.Positions.Add(position);
            }
        }
        return portfolio;
    }

    static double ReadFinite(JsonElement element, string name)
    {
        double value = element.GetDouble();
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentException(name + " must be a finite number.");
        return value;
    }
}
