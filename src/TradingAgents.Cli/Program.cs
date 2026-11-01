using System.Globalization;
using TradingAgents;

namespace TradingAgents.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        return Execute(args, Console.Out, Console.Error, null);
    }

    public static int Execute(string[] args, TextWriter stdout, TextWriter stderr, IReadOnlyDictionary<string, string>? environment)
    {
        try
        {
            return Run(args, stdout, stderr, environment);
        }
        catch (Exception ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
    }

    static int Run(string[] args, TextWriter stdout, TextWriter stderr, IReadOnlyDictionary<string, string>? environment)
    {
        string? ticker = null;
        string? date = null;
        string? analysts = null;
        string? portfolioPath = null;
        string? output = null;
        string? memory = null;
        string? checkpointDir = null;
        string? language = null;
        int? debateRounds = null;
        int? riskRounds = null;
        bool offline = false;
        bool checkpoint = false;
        bool help = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h" or "/?")
            {
                help = true;
                continue;
            }
            if (arg == "--offline")
            {
                offline = true;
                continue;
            }
            if (arg == "--checkpoint")
            {
                checkpoint = true;
                continue;
            }
            if (!TakesValue(arg))
            {
                stderr.WriteLine("Unknown option: " + arg);
                WriteUsage(stderr);
                return 64;
            }
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                stderr.WriteLine("Missing value for " + arg);
                return 64;
            }
            string value = args[++i];
            switch (arg)
            {
                case "--ticker":
                    ticker = value;
                    break;
                case "--date":
                    date = value;
                    break;
                case "--analysts":
                    analysts = value;
                    break;
                case "--debate-rounds":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int debate) || debate < 1)
                    {
                        stderr.WriteLine("Debate rounds must be an integer of at least 1.");
                        return 64;
                    }
                    debateRounds = debate;
                    break;
                case "--risk-rounds":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int risk) || risk < 1)
                    {
                        stderr.WriteLine("Risk rounds must be an integer of at least 1.");
                        return 64;
                    }
                    riskRounds = risk;
                    break;
                case "--language":
                    language = value;
                    break;
                case "--portfolio":
                    portfolioPath = value;
                    break;
                case "--output":
                    output = value;
                    break;
                case "--memory":
                    memory = value;
                    break;
                case "--checkpoint-dir":
                    checkpointDir = value;
                    break;
                default:
                    stderr.WriteLine("Unknown option: " + arg);
                    WriteUsage(stderr);
                    return 64;
            }
        }

        if (help)
        {
            WriteUsage(stdout);
            return 0;
        }
        if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(date))
        {
            stderr.WriteLine("Ticker and date are required.");
            WriteUsage(stderr);
            return 64;
        }
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            stderr.WriteLine("Date must be yyyy-MM-dd.");
            return 64;
        }

        TradingConfig config;
        try
        {
            config = TradingConfig.FromEnvironment(environment);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine(ex.Message);
            return 64;
        }
        if (debateRounds.HasValue)
            config.MaxDebateRounds = debateRounds.Value;
        if (riskRounds.HasValue)
            config.MaxRiskDiscussRounds = riskRounds.Value;
        if (!string.IsNullOrWhiteSpace(language))
            config.OutputLanguage = language;
        try
        {
            config.Analysts = TradingConfig.ParseAnalysts(analysts).ToList();
            PortfolioContext.NormalizeSymbol(ticker);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine(ex.Message);
            return 64;
        }

        PortfolioContext? portfolio = null;
        if (!string.IsNullOrWhiteSpace(portfolioPath))
        {
            try
            {
                portfolio = PortfolioContext.Load(portfolioPath);
            }
            catch (ArgumentException ex)
            {
                stderr.WriteLine(ex.Message);
                return 64;
            }
        }

        OpenAiCompatibleClient? client = null;
        try
        {
            ILanguageModel model;
            if (offline)
            {
                model = new ScriptedLanguageModel();
            }
            else
            {
                client = OpenAiCompatibleClient.Create(config);
                model = client;
            }

            string memoryPath = memory ?? config.MemoryLogPath ?? Path.Combine(Home(), "memory", "trading_memory.md");
            var log = new TradingMemoryLog(memoryPath, config.MemoryLogMaxEntries);
            JsonCheckpointStore? store = null;
            if (checkpoint || config.CheckpointEnabled)
            {
                string directory = checkpointDir ?? config.CheckpointDirectory ?? Path.Combine(Home(), "cache", "checkpoints");
                store = new JsonCheckpointStore(directory);
            }

            var graph = new TradingGraph(model, new OfflineMarketData(), config, log, store);
            TradingResult result = graph.Propagate(ticker, date, portfolio);
            stdout.WriteLine(result.State.CompanyOfInterest + " " + result.State.TradeDate);
            stdout.WriteLine("Rating: " + result.Rating);
            stdout.WriteLine("Research note only. No order was sent.");
            stdout.WriteLine();
            stdout.WriteLine(result.Decision);
            if (!string.IsNullOrWhiteSpace(output))
            {
                string path = ReportWriter.Write(result, output);
                stdout.WriteLine();
                stdout.WriteLine("Report: " + path);
            }
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            client?.Dispose();
        }
    }

    static bool TakesValue(string arg)
    {
        return arg is "--ticker" or "--date" or "--analysts" or "--debate-rounds" or "--risk-rounds"
            or "--language" or "--portfolio" or "--output" or "--memory" or "--checkpoint-dir";
    }

    static string Home()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tradingagents");
    }

    static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("TradingAgents C# research workflow. It writes a five-tier research rating and does not send orders.");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  tradingagents --ticker SYMBOL --date yyyy-MM-dd [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --ticker SYMBOL          Instrument to study, for example NVDA");
        writer.WriteLine("  --date yyyy-MM-dd        Decision date");
        writer.WriteLine("  --analysts LIST          market, social, news, fundamentals (default: all)");
        writer.WriteLine("  --offline                Scripted model, no network and no API key");
        writer.WriteLine("  --debate-rounds N        Bull and bear rounds (default 1)");
        writer.WriteLine("  --risk-rounds N          Risk rounds (default 1)");
        writer.WriteLine("  --language NAME          Report language; the debate stays in English");
        writer.WriteLine("  --portfolio FILE         JSON book: cash, currency, positions");
        writer.WriteLine("  --output DIR             Write a markdown research note");
        writer.WriteLine("  --memory FILE            Decision log (default: ~/.tradingagents/memory/trading_memory.md)");
        writer.WriteLine("  --checkpoint             Resume a JSON checkpoint for this ticker, date, analysts, and book");
        writer.WriteLine("  --checkpoint-dir DIR     Checkpoint directory");
        writer.WriteLine("  --help                   Show this text");
        writer.WriteLine();
        writer.WriteLine("Without --offline the client calls the Responses API at " + TradingConfig.DefaultBackendUrl);
        writer.WriteLine("with model " + TradingConfig.DefaultModel + ". Set XAI_API_KEY or TRADINGAGENTS_LLM_API_KEY.");
        writer.WriteLine("TRADINGAGENTS_LLM_BACKEND_URL selects another OpenAI-compatible host.");
        writer.WriteLine("TRADINGAGENTS_LLM_API=chat uses /chat/completions instead of /responses.");
        writer.WriteLine("Prices, news, filings, and sentiment in this build are an offline sample, not a live market.");
    }
}
