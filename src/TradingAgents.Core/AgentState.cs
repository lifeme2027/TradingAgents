namespace TradingAgents;

public sealed class InvestDebateState
{
    public string BullHistory { get; set; } = "";
    public string BearHistory { get; set; } = "";
    public string History { get; set; } = "";
    public string CurrentResponse { get; set; } = "";
    public int Count { get; set; }
}

public sealed class RiskDebateState
{
    public string AggressiveHistory { get; set; } = "";
    public string ConservativeHistory { get; set; } = "";
    public string NeutralHistory { get; set; } = "";
    public string History { get; set; } = "";
    public string LatestSpeaker { get; set; } = "";
    public string CurrentAggressiveResponse { get; set; } = "";
    public string CurrentConservativeResponse { get; set; } = "";
    public string CurrentNeutralResponse { get; set; } = "";
    public int Count { get; set; }
}

public sealed class AgentState
{
    public string CompanyOfInterest { get; set; } = "";
    public string AssetType { get; set; } = "stock";
    public string InstrumentContext { get; set; } = "";
    public string TradeDate { get; set; } = "";
    public string MarketReport { get; set; } = "";
    public string SentimentReport { get; set; } = "";
    public string NewsReport { get; set; } = "";
    public string FundamentalsReport { get; set; } = "";
    public InvestDebateState InvestmentDebate { get; set; } = new InvestDebateState();
    public string InvestmentPlan { get; set; } = "";
    public string TraderInvestmentPlan { get; set; } = "";
    public RiskDebateState RiskDebate { get; set; } = new RiskDebateState();
    public string FinalTradeDecision { get; set; } = "";
    public string FinalRating { get; set; } = "";
    public string PastContext { get; set; } = "";
    public string PortfolioContext { get; set; } = "";
    public List<string> CompletedStages { get; set; } = new List<string>();

    public bool HasStage(string stage)
    {
        return CompletedStages.Contains(stage);
    }

    public void MarkStage(string stage)
    {
        if (!CompletedStages.Contains(stage))
            CompletedStages.Add(stage);
    }

    public void Normalize()
    {
        CompanyOfInterest ??= "";
        AssetType ??= "stock";
        InstrumentContext ??= "";
        TradeDate ??= "";
        MarketReport ??= "";
        SentimentReport ??= "";
        NewsReport ??= "";
        FundamentalsReport ??= "";
        InvestmentPlan ??= "";
        TraderInvestmentPlan ??= "";
        FinalTradeDecision ??= "";
        FinalRating ??= "";
        PastContext ??= "";
        PortfolioContext ??= "";
        InvestmentDebate ??= new InvestDebateState();
        InvestmentDebate.BullHistory ??= "";
        InvestmentDebate.BearHistory ??= "";
        InvestmentDebate.History ??= "";
        InvestmentDebate.CurrentResponse ??= "";
        RiskDebate ??= new RiskDebateState();
        RiskDebate.AggressiveHistory ??= "";
        RiskDebate.ConservativeHistory ??= "";
        RiskDebate.NeutralHistory ??= "";
        RiskDebate.History ??= "";
        RiskDebate.LatestSpeaker ??= "";
        RiskDebate.CurrentAggressiveResponse ??= "";
        RiskDebate.CurrentConservativeResponse ??= "";
        RiskDebate.CurrentNeutralResponse ??= "";
        CompletedStages ??= new List<string>();
    }
}

public static class ConditionalLogic
{
    public static string NextDebate(InvestDebateState state, int maxDebateRounds)
    {
        if (state.Count >= 2 * maxDebateRounds)
            return "Research Manager";
        if (state.CurrentResponse.StartsWith("Bull", StringComparison.Ordinal))
            return "Bear Researcher";
        return "Bull Researcher";
    }

    public static string NextRisk(RiskDebateState state, int maxRiskRounds)
    {
        if (state.Count >= 3 * maxRiskRounds)
            return "Portfolio Manager";
        if (state.LatestSpeaker.StartsWith("Aggressive", StringComparison.Ordinal))
            return "Conservative Analyst";
        if (state.LatestSpeaker.StartsWith("Conservative", StringComparison.Ordinal))
            return "Neutral Analyst";
        return "Aggressive Analyst";
    }
}
