using System.Text;

namespace TradingAgents;

public static class ReportWriter
{
    public static string Write(TradingResult result, string directory)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Output directory is required.");
        Directory.CreateDirectory(directory);
        AgentState state = result.State;
        string path = Path.Combine(directory, state.CompanyOfInterest + "_" + state.TradeDate + ".md");
        var text = new StringBuilder();
        text.AppendLine("# " + state.CompanyOfInterest + " " + state.TradeDate);
        text.AppendLine();
        text.AppendLine("Rating: " + result.Rating);
        text.AppendLine();
        text.AppendLine("Research note only. No order was sent.");
        text.AppendLine();
        text.AppendLine("## Decision");
        text.AppendLine();
        text.AppendLine(state.FinalTradeDecision);
        text.AppendLine();
        text.AppendLine("## Market");
        text.AppendLine();
        text.AppendLine(state.MarketReport);
        text.AppendLine();
        text.AppendLine("## Sentiment");
        text.AppendLine();
        text.AppendLine(state.SentimentReport);
        text.AppendLine();
        text.AppendLine("## News");
        text.AppendLine();
        text.AppendLine(state.NewsReport);
        text.AppendLine();
        text.AppendLine("## Fundamentals");
        text.AppendLine();
        text.AppendLine(state.FundamentalsReport);
        text.AppendLine();
        text.AppendLine("## Debate");
        text.AppendLine();
        text.AppendLine(state.InvestmentDebate.History);
        text.AppendLine();
        text.AppendLine("## Trader");
        text.AppendLine();
        text.AppendLine(state.TraderInvestmentPlan);
        text.AppendLine();
        text.AppendLine("## Risk");
        text.AppendLine();
        text.AppendLine(state.RiskDebate.History);
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        return path;
    }
}
