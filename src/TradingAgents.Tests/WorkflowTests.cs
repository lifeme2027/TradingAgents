using TradingAgents;
using TradingAgents.Cli;
using Xunit;

namespace TradingAgents.Tests;

public class RatingTests
{
    [Theory]
    [InlineData("Rating: Buy", "Buy")]
    [InlineData("Rating: **Overweight**", "Overweight")]
    [InlineData("- **Rating**: Hold", "Hold")]
    [InlineData("Rating: BUY", "Buy")]
    [InlineData("Consensus rating: Buy", "Buy")]
    [InlineData("Rating：Overweight", "Overweight")]
    [InlineData("Rating: Buy\nRating: Sell", "Buy")]
    public void ReadsASingleRating(string text, string expected)
    {
        Assert.Equal(expected, Rating.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a Sell")]
    [InlineData("Operating margin: Sell-side")]
    [InlineData("Rating scale: Buy, Overweight, Hold, Underweight, Sell")]
    [InlineData("The call is unclear.\nRating: Buy\nRating: Sell")]
    [InlineData("- **Rating**: Buy\nRating: Sell")]
    public void UnreadableTextStaysReview(string text)
    {
        Assert.Equal(Rating.Review, Rating.Parse(text));
        Assert.True(Rating.IsReview(Rating.Parse(text)));
    }
}

public class IndicatorTests
{
    [Fact]
    public void RisingSeriesRsiIs100AndFlatSeriesIs50()
    {
        var rising = Enumerable.Range(0, 20).Select(i => (double)i).ToArray();
        Assert.Equal(100d, Indicators.Rsi(rising).GetValueOrDefault());
        var flat = Enumerable.Repeat(10d, 20).ToArray();
        Assert.Equal(50d, Indicators.Rsi(flat).GetValueOrDefault());
    }

    [Fact]
    public void OfflineSnapshotIsLabeledAndHasIndicators()
    {
        MarketSnapshot snapshot = new OfflineMarketData().Load("msft", "2026-01-15");
        string block = snapshot.IndicatorBlock();
        Assert.Contains("offline sample, not a live market", block);
        Assert.Contains("As of: 2026-01-15", block);
        Assert.Contains("Last close: 123.6", block);
        Assert.Contains("RSI(14): 100", block);
        Assert.DoesNotContain("n/a", block);
        Assert.Equal(60, snapshot.Bars.Count);
        Assert.Contains("not a live news feed", snapshot.News);
    }
}

public class PortfolioTests
{
    [Fact]
    public void PositionFlatAndOmittedStayDistinct()
    {
        var held = new PortfolioContext
        {
            Cash = 1000,
            Currency = "USD",
            Positions = new List<Position>
            {
                new Position { Ticker = "aapl", Quantity = 2, AveragePrice = 10 },
                new Position { Ticker = "MSFT", Quantity = -1 }
            }
        };
        string rendered = held.Render("AAPL");
        Assert.Contains("Current position in AAPL: 2 units, average price 10.00", rendered);
        Assert.Contains("Cash available: 1,000.00 USD", rendered);
        Assert.Contains("MSFT -1", rendered);

        string flat = new PortfolioContext().Render("AAPL");
        Assert.Contains("No current position in AAPL", flat);
        Assert.DoesNotContain("Cash available", flat);
        Assert.Equal("omitted", PortfolioContext.FingerprintOf(null));
        Assert.NotEqual(PortfolioContext.FingerprintOf(new PortfolioContext()), PortfolioContext.FingerprintOf(held));
    }

    [Fact]
    public void BadPortfolioFileIsRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{");
        try
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() => PortfolioContext.Load(path));
            Assert.Contains("is not usable", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class RoutingTests
{
    [Fact]
    public void DebateAlternatesThenStops()
    {
        var state = new InvestDebateState();
        Assert.Equal("Bull Researcher", ConditionalLogic.NextDebate(state, 1));
        state.Count = 1;
        state.CurrentResponse = "Bull Analyst: up";
        Assert.Equal("Bear Researcher", ConditionalLogic.NextDebate(state, 1));
        state.Count = 2;
        state.CurrentResponse = "Bear Analyst: down";
        Assert.Equal("Research Manager", ConditionalLogic.NextDebate(state, 1));
        Assert.Equal("Bull Researcher", ConditionalLogic.NextDebate(state, 2));
        state.Count = 4;
        Assert.Equal("Research Manager", ConditionalLogic.NextDebate(state, 2));
    }

    [Fact]
    public void RiskWalksThreeSeatsThenStops()
    {
        var state = new RiskDebateState();
        Assert.Equal("Aggressive Analyst", ConditionalLogic.NextRisk(state, 1));
        state.Count = 1;
        state.LatestSpeaker = "Aggressive";
        Assert.Equal("Conservative Analyst", ConditionalLogic.NextRisk(state, 1));
        state.Count = 2;
        state.LatestSpeaker = "Conservative";
        Assert.Equal("Neutral Analyst", ConditionalLogic.NextRisk(state, 1));
        state.Count = 3;
        state.LatestSpeaker = "Neutral";
        Assert.Equal("Portfolio Manager", ConditionalLogic.NextRisk(state, 1));
        state.Count = 3;
        Assert.Equal("Aggressive Analyst", ConditionalLogic.NextRisk(state, 2));
    }
}

public class GraphTests
{
    [Fact]
    public void OfflineRunFollowsTheRoleOrderAndRatesOverweight()
    {
        var model = new ScriptedLanguageModel();
        var graph = new TradingGraph(model, new OfflineMarketData(), new TradingConfig { OutputLanguage = "Chinese" });
        TradingResult result = graph.Propagate("nvda", "2026-01-15");
        string[] roles = model.Calls.Select(call => call.Role).ToArray();
        Assert.Equal(new[]
        {
            "Market Analyst", "Sentiment Analyst", "News Analyst", "Fundamentals Analyst",
            "Bull Researcher", "Bear Researcher", "Research Manager", "Trader",
            "Aggressive Analyst", "Conservative Analyst", "Neutral Analyst", "Portfolio Manager"
        }, roles);
        Assert.Equal("Overweight", result.Rating);
        Assert.Contains("offline sample, not a live market", result.State.MarketReport);
        Assert.Contains("Write the report in Chinese.", model.Calls[0].Prompt);
        Assert.Contains("Write the argument in English.", model.Calls.Single(call => call.Role == "Bull Researcher").Prompt);
        Assert.DoesNotContain("Write the report in Chinese.", model.Calls.Single(call => call.Role == "Bull Researcher").Prompt);
        Assert.Contains("Bull Analyst:", result.State.InvestmentDebate.History);
        Assert.StartsWith("Bear Analyst:", result.State.InvestmentDebate.CurrentResponse, StringComparison.Ordinal);
        Assert.Equal(2, result.State.InvestmentDebate.Count);
        Assert.Equal(3, result.State.RiskDebate.Count);
        Assert.Equal("", result.State.PortfolioContext);
    }

    [Fact]
    public void TwoDebateRoundsCallEachSideTwice()
    {
        var model = new ScriptedLanguageModel();
        var config = new TradingConfig { MaxDebateRounds = 2, Analysts = new List<string> { "market" } };
        var graph = new TradingGraph(model, new OfflineMarketData(), config);
        graph.Propagate("AMD", "2026-02-02");
        Assert.Equal(2, model.Calls.Count(call => call.Role == "Bull Researcher"));
        Assert.Equal(2, model.Calls.Count(call => call.Role == "Bear Researcher"));
        Assert.DoesNotContain(model.Calls, call => call.Role == "News Analyst");
    }

    [Fact]
    public void FlatBookIsShownAndOmittedBookIsNot()
    {
        var flatModel = new ScriptedLanguageModel();
        var graph = new TradingGraph(flatModel, new OfflineMarketData(), new TradingConfig());
        graph.Propagate("AAPL", "2026-01-15", new PortfolioContext());
        Assert.Contains(flatModel.Calls, call => call.Prompt.Contains("No current position in AAPL"));

        var omitted = new ScriptedLanguageModel();
        new TradingGraph(omitted, new OfflineMarketData(), new TradingConfig()).Propagate("AAPL", "2026-01-16");
        Assert.DoesNotContain(omitted.Calls, call => call.Prompt.Contains("Portfolio at the analysis date"));
    }

    [Fact]
    public void CheckpointSkipsAnalystsThatAlreadyRan()
    {
        string directory = TestPaths.TempDir();
        try
        {
            var config = new TradingConfig();
            string threadId = TradingGraph.ThreadId("NVDA", "2026-01-15", config.Analysts, 1, 1, "omitted");
            var seeded = new AgentState
            {
                CompanyOfInterest = "NVDA",
                TradeDate = "2026-01-15",
                MarketReport = "cached",
                CompletedStages = new List<string>
                {
                    "past-context", "analyst:market", "analyst:social", "analyst:news", "analyst:fundamentals"
                }
            };
            var store = new JsonCheckpointStore(directory);
            store.Save(threadId, seeded);
            var model = new ScriptedLanguageModel();
            var graph = new TradingGraph(model, new OfflineMarketData(), config, checkpoints: store);
            TradingResult result = graph.Propagate("NVDA", "2026-01-15");
            Assert.Equal("cached", result.State.MarketReport);
            Assert.DoesNotContain(model.Calls, call => call.Role is "Market Analyst" or "Sentiment Analyst" or "News Analyst" or "Fundamentals Analyst");
            Assert.Contains(model.Calls, call => call.Role == "Portfolio Manager");
            Assert.Equal("Overweight", result.Rating);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

public class MemoryTests
{
    [Fact]
    public void PendingIsHiddenUntilSettledAndARepeatDoesNotDuplicate()
    {
        string path = Path.Combine(TestPaths.TempDir(), "trading_memory.md");
        try
        {
            var log = new TradingMemoryLog(path, maxResolvedEntries: 1);
            log.StoreDecision("nvda", "2026-01-15", "Rating: Buy", "Buy");
            log.StoreDecision("NVDA", "2026-01-15", "Rating: Buy", "Buy");
            Assert.Equal(1, File.ReadAllText(path).Split(new[] { "[2026-01-15 | NVDA |" }, StringSplitOptions.None).Length - 1);
            Assert.Equal("", log.GetPastContext("NVDA", "2026-01-20"));

            Assert.True(log.Settle("NVDA", "2026-01-15", 0.012, 0.004, 5, "The sample rose.", "2026-01-22"));
            Assert.Contains("| +1.2% | +0.4% | 5d | resolved:2026-01-22]", File.ReadAllText(path));
            Assert.Equal("", log.GetPastContext("NVDA", "2026-01-21"));
            string visible = log.GetPastContext("NVDA", "2026-01-22");
            Assert.Contains("REFLECTION:", visible);
            Assert.Contains("The sample rose.", visible);

            log.StoreDecision("MSFT", "2026-01-02", "Rating: Sell", "Sell");
            log.Settle("MSFT", "2026-01-02", -0.01, 0, 5, "Older lesson.", "2026-01-09");
            log.StoreDecision("AMD", "2026-01-10", "Rating: Hold", "Hold");
            string text = File.ReadAllText(path);
            Assert.DoesNotContain("[2026-01-02 | MSFT |", text);
            Assert.Contains("[2026-01-10 | AMD |", text);
            Assert.Contains("pending]", text);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}

public class ClientTests
{
    [Fact]
    public void EndpointsAndResponseTextMatchTheDocumentedShapes()
    {
        Assert.Equal("https://api.x.ai/v1/responses", OpenAiCompatibleClient.BuildEndpoint("https://api.x.ai/v1", "responses").AbsoluteUri);
        Assert.Equal("https://api.openai.com/v1/chat/completions", OpenAiCompatibleClient.BuildEndpoint("https://api.openai.com/v1/", "chat").AbsoluteUri);
        string responses = "{\"output\":[{\"type\":\"reasoning\"},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"Rating: Hold\"}]}]}";
        Assert.Equal("Rating: Hold", OpenAiCompatibleClient.ReadOutputText(responses));
        string chat = "{\"choices\":[{\"message\":{\"content\":\"Rating: Sell\"}}]}";
        Assert.Equal("Rating: Sell", OpenAiCompatibleClient.ReadOutputText(chat));
    }

    [Fact]
    public void EnvironmentSelectsTheKeyAndRejectsABadBoolean()
    {
        var env = new Dictionary<string, string>
        {
            ["XAI_API_KEY"] = "xai-test",
            ["OPENAI_API_KEY"] = "openai-test",
            ["TRADINGAGENTS_QUICK_THINK_LLM"] = "grok-4.3",
            ["TRADINGAGENTS_CHECKPOINT_ENABLED"] = "yes"
        };
        TradingConfig config = TradingConfig.FromEnvironment(env);
        Assert.Equal("xai-test", config.ApiKey);
        Assert.Equal("grok-4.3", config.QuickModel);
        Assert.Equal("grok-4.7", config.DeepModel);
        Assert.True(config.CheckpointEnabled);
        env["TRADINGAGENTS_LLM_API_KEY"] = "explicit";
        Assert.Equal("explicit", TradingConfig.FromEnvironment(env).ApiKey);
        env["TRADINGAGENTS_CHECKPOINT_ENABLED"] = "treu";
        Assert.Throws<ArgumentException>(() => TradingConfig.FromEnvironment(env));
    }
}

public class CliTests
{
    static readonly Dictionary<string, string> Empty = new Dictionary<string, string>();

    [Fact]
    public void HelpExitsZeroAndMissingArgumentsExit64()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Assert.Equal(0, Program.Execute(new[] { "--help" }, stdout, stderr, Empty));
        Assert.Contains("--offline", stdout.ToString());
        Assert.Contains("does not send orders", stdout.ToString());

        stdout = new StringWriter();
        stderr = new StringWriter();
        Assert.Equal(64, Program.Execute(Array.Empty<string>(), stdout, stderr, Empty));
        Assert.Contains("Ticker and date are required.", stderr.ToString());

        stderr = new StringWriter();
        Assert.Equal(64, Program.Execute(new[] { "--ticker", "AAPL", "--date", "2026-01-15", "--analysts", "weather", "--offline" }, new StringWriter(), stderr, Empty));
        Assert.Contains("Unknown analyst", stderr.ToString());
    }

    [Fact]
    public void MissingKeyFailsBeforeARequestAndOfflineRunWritesAReport()
    {
        var stderr = new StringWriter();
        int missing = Program.Execute(new[] { "--ticker", "AAPL", "--date", "2026-01-15" }, new StringWriter(), stderr, Empty);
        Assert.Equal(1, missing);
        Assert.Contains("No API key", stderr.ToString());

        string root = TestPaths.TempDir();
        try
        {
            string memory = Path.Combine(root, "memory.md");
            string output = Path.Combine(root, "out");
            var stdout = new StringWriter();
            int code = Program.Execute(new[]
            {
                "--ticker", "nvda", "--date", "2026-01-15", "--offline",
                "--memory", memory, "--output", output, "--analysts", "market,social"
            }, stdout, new StringWriter(), Empty);
            Assert.Equal(0, code);
            Assert.Contains("Rating: Overweight", stdout.ToString());
            Assert.Contains("No order was sent", stdout.ToString());
            Assert.True(File.Exists(Path.Combine(output, "NVDA_2026-01-15.md")));
            string log = File.ReadAllText(memory);
            Assert.Contains("[2026-01-15 | NVDA | Overweight | pending]", log);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

static class TestPaths
{
    public static string TempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "tradingagents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
