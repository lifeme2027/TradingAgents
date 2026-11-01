namespace TradingAgents;

public sealed class TradingResult
{
    public TradingResult(AgentState state, string decision, string rating)
    {
        State = state;
        Decision = decision;
        Rating = rating;
    }

    public AgentState State { get; }
    public string Decision { get; }
    public string Rating { get; }
}

public sealed class TradingGraph
{
    readonly ILanguageModel _model;
    readonly IMarketData _data;
    readonly TradingConfig _config;
    readonly TradingMemoryLog? _memory;
    readonly JsonCheckpointStore? _checkpoints;

    public TradingGraph(ILanguageModel model, IMarketData data, TradingConfig config, TradingMemoryLog? memory = null, JsonCheckpointStore? checkpoints = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _memory = memory;
        _checkpoints = checkpoints;
        if (_config.Analysts == null || _config.Analysts.Count == 0)
            throw new ArgumentException("Select at least one analyst.");
        if (_config.MaxDebateRounds < 1)
            throw new ArgumentException("Debate rounds must be at least 1.");
        if (_config.MaxRiskDiscussRounds < 1)
            throw new ArgumentException("Risk rounds must be at least 1.");
        foreach (string analyst in _config.Analysts)
        {
            if (analyst != "market" && analyst != "social" && analyst != "news" && analyst != "fundamentals")
                throw new ArgumentException("Unknown analyst: " + analyst);
        }
    }

    public static string ThreadId(string ticker, string tradeDate, IReadOnlyList<string> analysts, int debateRounds, int riskRounds, string portfolioFingerprint)
    {
        string signature = ticker + ":" + tradeDate + ":" + string.Join(",", analysts) + ":" + debateRounds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + riskRounds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + portfolioFingerprint;
        return Sha256Hex.Compute(signature).Substring(0, 16);
    }

    public TradingResult Propagate(string ticker, string tradeDate, PortfolioContext? portfolio = null)
    {
        string symbol = PortfolioContext.NormalizeSymbol(ticker);
        if (!DateOnly.TryParseExact(tradeDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("Trade date must be yyyy-MM-dd.");
        string fingerprint = PortfolioContext.FingerprintOf(portfolio);
        string threadId = ThreadId(symbol, tradeDate, _config.Analysts, _config.MaxDebateRounds, _config.MaxRiskDiscussRounds, fingerprint);
        AgentState state = _checkpoints?.Load(threadId) ?? new AgentState();
        state.Normalize();
        state.CompanyOfInterest = symbol;
        state.TradeDate = tradeDate;
        state.PortfolioContext = portfolio == null ? "" : portfolio.Render(symbol);
        MarketSnapshot snapshot = _data.Load(symbol, tradeDate);

        if (!state.HasStage("past-context"))
        {
            state.PastContext = _memory == null ? "" : _memory.GetPastContext(symbol, tradeDate);
            state.MarkStage("past-context");
            Persist(threadId, state);
        }

        foreach (string analyst in _config.Analysts)
        {
            string stage = "analyst:" + analyst;
            if (state.HasStage(stage))
                continue;
            string role = RoleName(analyst);
            string dataBlock = DataBlock(snapshot, analyst);
            string prompt = Context(state)
                + "Ticker: " + symbol + "\nTrade date: " + tradeDate + "\n\n"
                + dataBlock + "\n\n"
                + "Write a research comment from the " + role + " seat. Use only the supplied sample. "
                + LanguageLine();
            string commentary = _model.Complete(role, prompt).Trim();
            AssignReport(state, analyst, dataBlock + "\n\n" + commentary);
            state.MarkStage(stage);
            Persist(threadId, state);
        }

        while (!state.HasStage("debate"))
        {
            string next = ConditionalLogic.NextDebate(state.InvestmentDebate, _config.MaxDebateRounds);
            if (next == "Research Manager")
            {
                state.MarkStage("debate");
                Persist(threadId, state);
                break;
            }
            if (next == "Bull Researcher")
                SpeakBull(state, symbol, tradeDate);
            else
                SpeakBear(state, symbol, tradeDate);
            Persist(threadId, state);
        }

        if (!state.HasStage("research-manager"))
        {
            string prompt = Context(state)
                + Reports(state)
                + "\n\nDebate:\n" + state.InvestmentDebate.History
                + "\n\nYou are the Research Manager. The first line must be a non-list line of the form 'Rating: Buy', 'Rating: Overweight', 'Rating: Hold', 'Rating: Underweight', or 'Rating: Sell'. "
                + "Conflict by itself is not a Hold. Then give the investment thesis. " + LanguageLine();
            state.InvestmentPlan = _model.Complete("Research Manager", prompt).Trim();
            state.MarkStage("research-manager");
            Persist(threadId, state);
        }

        if (!state.HasStage("trader"))
        {
            string prompt = Context(state)
                + "Investment plan:\n" + state.InvestmentPlan
                + "\n\nYou are the Trader. State Action: Buy, Action: Hold, or Action: Sell. Overweight is a Buy and Underweight is a Sell. "
                + "Give entry and stop as absolute prices from the supplied data, not as percentages. Do not place an order. " + LanguageLine();
            state.TraderInvestmentPlan = _model.Complete("Trader", prompt).Trim();
            state.MarkStage("trader");
            Persist(threadId, state);
        }

        while (!state.HasStage("risk"))
        {
            string next = ConditionalLogic.NextRisk(state.RiskDebate, _config.MaxRiskDiscussRounds);
            if (next == "Portfolio Manager")
            {
                state.MarkStage("risk");
                Persist(threadId, state);
                break;
            }
            if (next == "Aggressive Analyst")
                SpeakRisk(state, "Aggressive", "Aggressive Analyst");
            else if (next == "Conservative Analyst")
                SpeakRisk(state, "Conservative", "Conservative Analyst");
            else
                SpeakRisk(state, "Neutral", "Neutral Analyst");
            Persist(threadId, state);
        }

        if (!state.HasStage("portfolio-manager"))
        {
            string prompt = Context(state)
                + "Trader plan:\n" + state.TraderInvestmentPlan
                + "\n\nRisk debate:\n" + state.RiskDebate.History
                + "\n\nYou are the Portfolio Manager. The first line must be '- **Rating**: ' followed by one of Buy, Overweight, Hold, Underweight, Sell, "
                + "and the next paragraph must also contain one plain line 'Rating: ' with that same word so the rating is unambiguous. "
                + "Then write an Executive Summary and an Investment Thesis. This is a research decision, not an order. " + LanguageLine();
            state.FinalTradeDecision = _model.Complete("Portfolio Manager", prompt).Trim();
            state.FinalRating = Rating.Parse(state.FinalTradeDecision);
            state.MarkStage("portfolio-manager");
            Persist(threadId, state);
        }
        else if (string.IsNullOrEmpty(state.FinalRating))
        {
            state.FinalRating = Rating.Parse(state.FinalTradeDecision);
        }

        if (!state.HasStage("memory-store"))
        {
            _memory?.StoreDecision(symbol, tradeDate, state.FinalTradeDecision, state.FinalRating);
            state.MarkStage("memory-store");
            Persist(threadId, state);
        }

        return new TradingResult(state, state.FinalTradeDecision, state.FinalRating);
    }

    void SpeakBull(AgentState state, string symbol, string tradeDate)
    {
        string prompt = "Ticker: " + symbol + "\nTrade date: " + tradeDate + "\n\n"
            + Reports(state)
            + "\n\nDebate so far:\n" + state.InvestmentDebate.History
            + "\n\nYou are the Bull Researcher. Argue the constructive case in one short paragraph. " + EnglishDebateLine();
        string argument = "Bull Analyst: " + _model.Complete("Bull Researcher", prompt).Trim();
        AppendDebate(state, argument, bull: true);
    }

    void SpeakBear(AgentState state, string symbol, string tradeDate)
    {
        string prompt = "Ticker: " + symbol + "\nTrade date: " + tradeDate + "\n\n"
            + Reports(state)
            + "\n\nDebate so far:\n" + state.InvestmentDebate.History
            + "\n\nYou are the Bear Researcher. Argue the cautious case in one short paragraph. " + EnglishDebateLine();
        string argument = "Bear Analyst: " + _model.Complete("Bear Researcher", prompt).Trim();
        AppendDebate(state, argument, bull: false);
    }

    static void AppendDebate(AgentState state, string argument, bool bull)
    {
        InvestDebateState debate = state.InvestmentDebate;
        debate.History = Join(debate.History, argument);
        if (bull)
            debate.BullHistory = Join(debate.BullHistory, argument);
        else
            debate.BearHistory = Join(debate.BearHistory, argument);
        debate.CurrentResponse = argument;
        debate.Count += 1;
    }

    void SpeakRisk(AgentState state, string speaker, string role)
    {
        string prompt = "Trader plan:\n" + state.TraderInvestmentPlan
            + "\n\nRisk debate so far:\n" + state.RiskDebate.History
            + "\n\nYou are the " + role + ". Respond in one short paragraph. " + EnglishDebateLine();
        string argument = role + ": " + _model.Complete(role, prompt).Trim();
        RiskDebateState risk = state.RiskDebate;
        risk.History = Join(risk.History, argument);
        risk.LatestSpeaker = speaker;
        risk.Count += 1;
        if (speaker == "Aggressive")
        {
            risk.AggressiveHistory = Join(risk.AggressiveHistory, argument);
            risk.CurrentAggressiveResponse = argument;
        }
        else if (speaker == "Conservative")
        {
            risk.ConservativeHistory = Join(risk.ConservativeHistory, argument);
            risk.CurrentConservativeResponse = argument;
        }
        else
        {
            risk.NeutralHistory = Join(risk.NeutralHistory, argument);
            risk.CurrentNeutralResponse = argument;
        }
    }

    void Persist(string threadId, AgentState state)
    {
        _checkpoints?.Save(threadId, state);
    }

    string LanguageLine()
    {
        return "Write the report in " + _config.OutputLanguage + ". Keep ticker symbols unchanged.";
    }

    static string EnglishDebateLine()
    {
        return "Write the argument in English.";
    }

    static string Context(AgentState state)
    {
        var blocks = new List<string>();
        if (!string.IsNullOrWhiteSpace(state.PortfolioContext))
            blocks.Add(state.PortfolioContext);
        if (!string.IsNullOrWhiteSpace(state.PastContext))
            blocks.Add(state.PastContext);
        return blocks.Count == 0 ? "" : string.Join("\n\n", blocks) + "\n\n";
    }

    static string Reports(AgentState state)
    {
        return "Market report:\n" + state.MarketReport
            + "\n\nSentiment report:\n" + state.SentimentReport
            + "\n\nNews report:\n" + state.NewsReport
            + "\n\nFundamentals report:\n" + state.FundamentalsReport;
    }

    static string DataBlock(MarketSnapshot snapshot, string analyst)
    {
        switch (analyst)
        {
            case "market":
                return snapshot.IndicatorBlock();
            case "social":
                return snapshot.Sentiment;
            case "news":
                return snapshot.News;
            default:
                return snapshot.Fundamentals;
        }
    }

    static string RoleName(string analyst)
    {
        switch (analyst)
        {
            case "market":
                return "Market Analyst";
            case "social":
                return "Sentiment Analyst";
            case "news":
                return "News Analyst";
            default:
                return "Fundamentals Analyst";
        }
    }

    static void AssignReport(AgentState state, string analyst, string report)
    {
        switch (analyst)
        {
            case "market":
                state.MarketReport = report;
                break;
            case "social":
                state.SentimentReport = report;
                break;
            case "news":
                state.NewsReport = report;
                break;
            default:
                state.FundamentalsReport = report;
                break;
        }
    }

    static string Join(string existing, string addition)
    {
        return string.IsNullOrEmpty(existing) ? addition : existing + "\n" + addition;
    }
}
