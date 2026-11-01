using System.Globalization;

namespace TradingAgents;

public sealed class PriceBar
{
    public DateOnly Date { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Volume { get; set; }
}

public sealed class MarketSnapshot
{
    public string Ticker { get; set; } = "";
    public string TradeDate { get; set; } = "";
    public string Source { get; set; } = "";
    public List<PriceBar> Bars { get; set; } = new List<PriceBar>();
    public string News { get; set; } = "";
    public string Fundamentals { get; set; } = "";
    public string Sentiment { get; set; } = "";

    public string IndicatorBlock()
    {
        if (Bars.Count == 0)
            return "No prices are available as of " + TradeDate + ".";
        double[] closes = Bars.Select(bar => bar.Close).ToArray();
        PriceBar last = Bars[Bars.Count - 1];
        double? rsi = Indicators.Rsi(closes);
        MacdReading macd = Indicators.Macd(closes);
        double? sma20 = Indicators.Sma(closes, 20);
        return string.Join("\n", new[]
        {
            "Data source: " + Source,
            "As of: " + TradeDate,
            "Last close: " + last.Close.ToString("0.####", CultureInfo.InvariantCulture),
            "SMA(20): " + Format(sma20),
            "RSI(14): " + Format(rsi),
            "MACD(12,26,9): line " + Format(macd.Line) + ", signal " + Format(macd.Signal) + ", histogram " + Format(macd.Histogram)
        });
    }

    static string Format(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : "n/a";
    }
}

public interface IMarketData
{
    MarketSnapshot Load(string ticker, string tradeDate);
}

// Deterministic sample book. The numbers are not a live quote and must not be traded.
public sealed class OfflineMarketData : IMarketData
{
    public MarketSnapshot Load(string ticker, string tradeDate)
    {
        string symbol = PortfolioContext.NormalizeSymbol(ticker);
        if (!DateOnly.TryParseExact(tradeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            throw new ArgumentException("Trade date must be yyyy-MM-dd.");
        var bars = new List<PriceBar>();
        for (int i = 0; i < 60; i++)
        {
            double close = 100 + i * 0.4;
            bars.Add(new PriceBar
            {
                Date = date.AddDays(i - 59),
                Open = close - 0.2,
                High = close + 0.3,
                Low = close - 0.4,
                Close = close,
                Volume = 1_000_000 + i * 1000
            });
        }
        return new MarketSnapshot
        {
            Ticker = symbol,
            TradeDate = tradeDate,
            Source = "offline sample, not a live market",
            Bars = bars,
            News = "Sample headline for " + symbol + " as of " + tradeDate + ". This is not a live news feed.",
            Fundamentals = "Offline sample for " + symbol + ". This is not an SEC filing and has no reported revenue.",
            Sentiment = "Offline sample for " + symbol + ". No social posts were read."
        };
    }
}
