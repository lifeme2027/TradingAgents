namespace TradingAgents;

public sealed class TradingConfig
{
    public const string DefaultBackendUrl = "https://api.x.ai/v1";
    public const string DefaultModel = "grok-4.7";

    public string QuickModel { get; set; } = DefaultModel;
    public string DeepModel { get; set; } = DefaultModel;
    public string BackendUrl { get; set; } = DefaultBackendUrl;
    public string ApiStyle { get; set; } = "responses";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 180;
    public int MaxDebateRounds { get; set; } = 1;
    public int MaxRiskDiscussRounds { get; set; } = 1;
    public string OutputLanguage { get; set; } = "English";
    public string? MemoryLogPath { get; set; }
    public int? MemoryLogMaxEntries { get; set; }
    public bool CheckpointEnabled { get; set; }
    public string? CheckpointDirectory { get; set; }
    public List<string> Analysts { get; set; } = new List<string> { "market", "social", "news", "fundamentals" };

    public static TradingConfig FromEnvironment(IReadOnlyDictionary<string, string>? environment = null)
    {
        var config = new TradingConfig();
        Apply(environment, "TRADINGAGENTS_QUICK_THINK_LLM", value => config.QuickModel = value);
        Apply(environment, "TRADINGAGENTS_DEEP_THINK_LLM", value => config.DeepModel = value);
        Apply(environment, "TRADINGAGENTS_LLM_BACKEND_URL", value => config.BackendUrl = value);
        Apply(environment, "TRADINGAGENTS_LLM_API", value =>
        {
            string style = value.Trim().ToLowerInvariant();
            if (style != "responses" && style != "chat")
                throw new ArgumentException("Invalid value for TRADINGAGENTS_LLM_API: expected responses or chat, got '" + value + "'.");
            config.ApiStyle = style;
        });
        Apply(environment, "TRADINGAGENTS_OUTPUT_LANGUAGE", value => config.OutputLanguage = value);
        Apply(environment, "TRADINGAGENTS_MAX_DEBATE_ROUNDS", value => config.MaxDebateRounds = ReadInt("TRADINGAGENTS_MAX_DEBATE_ROUNDS", value));
        Apply(environment, "TRADINGAGENTS_MAX_RISK_ROUNDS", value => config.MaxRiskDiscussRounds = ReadInt("TRADINGAGENTS_MAX_RISK_ROUNDS", value));
        Apply(environment, "TRADINGAGENTS_LLM_TIMEOUT_SECONDS", value => config.TimeoutSeconds = ReadInt("TRADINGAGENTS_LLM_TIMEOUT_SECONDS", value));
        Apply(environment, "TRADINGAGENTS_CHECKPOINT_ENABLED", value => config.CheckpointEnabled = ReadBool("TRADINGAGENTS_CHECKPOINT_ENABLED", value));
        Apply(environment, "TRADINGAGENTS_MEMORY_LOG_PATH", value => config.MemoryLogPath = value);
        Apply(environment, "TRADINGAGENTS_MEMORY_LOG_MAX_ENTRIES", value => config.MemoryLogMaxEntries = ReadInt("TRADINGAGENTS_MEMORY_LOG_MAX_ENTRIES", value));
        config.ApiKey = First(environment, "TRADINGAGENTS_LLM_API_KEY", "XAI_API_KEY", "OPENAI_API_KEY");
        return config;
    }

    public static IReadOnlyList<string> ParseAnalysts(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return new[] { "market", "social", "news", "fundamentals" };
        var selected = new List<string>();
        foreach (string part in csv.Split(','))
        {
            string name = part.Trim().ToLowerInvariant();
            if (name.Length == 0)
                continue;
            if (name == "sentiment")
                name = "social";
            if (name != "market" && name != "social" && name != "news" && name != "fundamentals")
                throw new ArgumentException("Unknown analyst: " + part.Trim() + ". Expected market, social, news, fundamentals.");
            if (!selected.Contains(name))
                selected.Add(name);
        }
        if (selected.Count == 0)
            throw new ArgumentException("Select at least one analyst.");
        return selected;
    }

    static void Apply(IReadOnlyDictionary<string, string>? environment, string name, Action<string> assign)
    {
        string? raw = Get(environment, name);
        if (string.IsNullOrEmpty(raw))
            return;
        assign(raw);
    }

    public static string? Get(IReadOnlyDictionary<string, string>? environment, string name)
    {
        if (environment != null)
            return environment.TryGetValue(name, out string? value) ? value : null;
        return Environment.GetEnvironmentVariable(name);
    }

    static string? First(IReadOnlyDictionary<string, string>? environment, params string[] names)
    {
        foreach (string name in names)
        {
            string? value = Get(environment, name);
            if (!string.IsNullOrEmpty(value))
                return value;
        }
        return null;
    }

    static int ReadInt(string name, string value)
    {
        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            throw new ArgumentException("Invalid value for " + name + ": expected an integer, got '" + value + "'.");
        return parsed;
    }

    static bool ReadBool(string name, string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized is "true" or "1" or "yes" or "on")
            return true;
        if (normalized is "false" or "0" or "no" or "off")
            return false;
        throw new ArgumentException("Invalid value for " + name + ": expected a boolean (true/false/1/0/yes/no/on/off), got '" + value + "'.");
    }
}
