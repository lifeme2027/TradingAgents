namespace TradingAgents;

public readonly struct MacdReading
{
    public MacdReading(double? line, double? signal, double? histogram)
    {
        Line = line;
        Signal = signal;
        Histogram = histogram;
    }

    public double? Line { get; }
    public double? Signal { get; }
    public double? Histogram { get; }
}

public static class Indicators
{
    public static double? Sma(IReadOnlyList<double> values, int period)
    {
        if (period <= 0 || values.Count < period)
            return null;
        double sum = 0;
        for (int i = values.Count - period; i < values.Count; i++)
            sum += values[i];
        return sum / period;
    }

    public static double? Rsi(IReadOnlyList<double> closes, int period = 14)
    {
        if (period <= 0 || closes.Count <= period)
            return null;
        double gain = 0;
        double loss = 0;
        for (int i = 1; i <= period; i++)
        {
            double change = closes[i] - closes[i - 1];
            if (change >= 0)
                gain += change;
            else
                loss -= change;
        }
        gain /= period;
        loss /= period;
        for (int i = period + 1; i < closes.Count; i++)
        {
            double change = closes[i] - closes[i - 1];
            double up = change > 0 ? change : 0;
            double down = change < 0 ? -change : 0;
            gain = (gain * (period - 1) + up) / period;
            loss = (loss * (period - 1) + down) / period;
        }
        if (loss == 0)
            return gain == 0 ? 50 : 100;
        double relative = gain / loss;
        return 100 - (100 / (1 + relative));
    }

    public static MacdReading Macd(IReadOnlyList<double> closes, int fast = 12, int slow = 26, int signalPeriod = 9)
    {
        List<double> fastEma = EmaSeries(closes, fast);
        List<double> slowEma = EmaSeries(closes, slow);
        if (fastEma.Count == 0 || slowEma.Count == 0)
            return new MacdReading(null, null, null);
        int start = slow - 1;
        var macd = new List<double>();
        for (int i = start; i < closes.Count; i++)
            macd.Add(fastEma[i] - slowEma[i]);
        List<double> signal = EmaSeries(macd, signalPeriod);
        if (signal.Count == 0)
            return new MacdReading(macd[macd.Count - 1], null, null);
        double line = macd[macd.Count - 1];
        double signalValue = signal[signal.Count - 1];
        return new MacdReading(line, signalValue, line - signalValue);
    }

    static List<double> EmaSeries(IReadOnlyList<double> values, int period)
    {
        var series = new List<double>();
        if (period <= 0 || values.Count < period)
            return series;
        double seed = 0;
        for (int i = 0; i < period; i++)
            seed += values[i];
        double ema = seed / period;
        for (int i = 0; i < period - 1; i++)
            series.Add(double.NaN);
        series.Add(ema);
        double k = 2.0 / (period + 1);
        for (int i = period; i < values.Count; i++)
        {
            ema = values[i] * k + ema * (1 - k);
            series.Add(ema);
        }
        return series;
    }
}
