using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradingAgents;

public interface ILanguageModel
{
    string Complete(string role, string prompt);
}

public sealed class ModelCall
{
    public ModelCall(string role, string prompt)
    {
        Role = role;
        Prompt = prompt;
    }

    public string Role { get; }
    public string Prompt { get; }
}

// Fixed replies so the decision graph and tests run with no network and no API key.
public sealed class ScriptedLanguageModel : ILanguageModel
{
    public List<ModelCall> Calls { get; } = new List<ModelCall>();

    public string Complete(string role, string prompt)
    {
        Calls.Add(new ModelCall(role, prompt));
        switch (role)
        {
            case "Market Analyst":
                return "The sample closes rise by a fixed amount each day.";
            case "Sentiment Analyst":
                return "There is no social sample beyond the supplied note.";
            case "News Analyst":
                return "The only headline is the supplied sample.";
            case "Fundamentals Analyst":
                return "No filing figures were supplied.";
            case "Bull Researcher":
                return "The sample trend is upward, so a constructive view is consistent with the given bars.";
            case "Bear Researcher":
                return "The bars are a synthetic sample, so the trend is not evidence of a live market.";
            case "Research Manager":
                return "Rating: Overweight\n\nThe bull case matches the rising sample, and the bear case correctly limits how far to trust it.";
            case "Trader":
                return "Action: Buy\nEntry is the last sample close. A stop belongs below that close. This note does not place an order.";
            case "Aggressive Analyst":
                return "The constructive rating can be kept. The sample trend did not break.";
            case "Conservative Analyst":
                return "Keep the position small. The book is a sample, so the evidence is thin.";
            case "Neutral Analyst":
                return "Overweight fits both arguments: the trend is up, and the data is only a sample.";
            case "Portfolio Manager":
                return "Rating: Overweight\n\nExecutive Summary: keep a constructive research stance on the sample trend.\nInvestment Thesis: the debate and the rising closes agree, while the synthetic source limits conviction.";
            default:
                return "No scripted reply is defined for this role.";
        }
    }
}

public sealed class OpenAiCompatibleClient : ILanguageModel, IDisposable
{
    readonly TradingConfig _config;
    readonly HttpClient _http;
    readonly string? _apiKey;

    public OpenAiCompatibleClient(TradingConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _apiKey = config.ApiKey;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, config.TimeoutSeconds))
        };
    }

    public static OpenAiCompatibleClient Create(TradingConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("No API key. Set XAI_API_KEY or TRADINGAGENTS_LLM_API_KEY, or pass --offline.");
        return new OpenAiCompatibleClient(config);
    }

    public static Uri BuildEndpoint(string baseUrl, string apiStyle)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Backend URL is required.");
        string root = baseUrl.Trim().TrimEnd('/');
        string style = (apiStyle ?? "responses").Trim().ToLowerInvariant();
        if (style == "chat")
        {
            if (root.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return new Uri(root);
            return new Uri(root + "/chat/completions");
        }
        if (style != "responses")
            throw new ArgumentException("API style must be responses or chat.");
        if (root.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            return new Uri(root);
        return new Uri(root + "/responses");
    }

    public string Complete(string role, string prompt)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("No API key. Set XAI_API_KEY or TRADINGAGENTS_LLM_API_KEY, or pass --offline.");
        bool deep = role == "Research Manager" || role == "Portfolio Manager";
        string model = deep ? _config.DeepModel : _config.QuickModel;
        Uri endpoint = BuildEndpoint(_config.BackendUrl, _config.ApiStyle);
        string body = _config.ApiStyle == "chat"
            ? JsonSerializer.Serialize(new
            {
                model,
                stream = false,
                messages = new object[]
                {
                    new { role = "system", content = "You are the " + role + " in a research workflow. Do not place orders and do not invent live prices." },
                    new { role = "user", content = prompt }
                }
            })
            : JsonSerializer.Serialize(new
            {
                model,
                input = "You are the " + role + " in a research workflow. Do not place orders and do not invent live prices.\n\n" + prompt
            });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using HttpResponseMessage response = _http.Send(request);
            using Stream stream = response.Content.ReadAsStream();
            using var reader = new StreamReader(stream);
            string responseBody = reader.ReadToEnd();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Model request failed with HTTP " + (int)response.StatusCode + ". " + Sanitize(Excerpt(responseBody)));
            return ReadOutputText(responseBody);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Model request failed: " + Sanitize(ex.Message));
        }
    }

    public static string ReadOutputText(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind != JsonValueKind.Null)
        {
            string message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement messageElement)
                ? messageElement.GetString() ?? "unknown model error"
                : error.ToString();
            throw new InvalidOperationException("Model error: " + message);
        }
        if (root.TryGetProperty("output_text", out JsonElement outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            string? direct = outputText.GetString();
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;
        }
        if (root.TryGetProperty("output", out JsonElement output) && output.ValueKind == JsonValueKind.Array)
        {
            string joined = JoinContent(output);
            if (joined.Length > 0)
                return joined;
        }
        if (root.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("message", out JsonElement message) || !message.TryGetProperty("content", out JsonElement content))
                    continue;
                if (content.ValueKind == JsonValueKind.String)
                {
                    string? text = content.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
                else if (content.ValueKind == JsonValueKind.Array)
                {
                    string joined = ReadParts(content);
                    if (joined.Length > 0)
                        return joined;
                }
            }
        }
        throw new InvalidOperationException("The model response did not contain text.");
    }

    static string JoinContent(JsonElement output)
    {
        var parts = new List<string>();
        foreach (JsonElement item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out JsonElement content))
                continue;
            if (content.ValueKind == JsonValueKind.String)
            {
                string? text = content.GetString();
                if (!string.IsNullOrEmpty(text))
                    parts.Add(text);
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                string text = ReadParts(content);
                if (text.Length > 0)
                    parts.Add(text);
            }
        }
        return string.Join("", parts).Trim();
    }

    static string ReadParts(JsonElement content)
    {
        var parts = new List<string>();
        foreach (JsonElement part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                string? text = part.GetString();
                if (!string.IsNullOrEmpty(text))
                    parts.Add(text);
                continue;
            }
            if (part.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String)
            {
                string? text = textElement.GetString();
                if (!string.IsNullOrEmpty(text))
                    parts.Add(text);
            }
        }
        return string.Join("", parts).Trim();
    }

    string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        string cleaned = text;
        if (!string.IsNullOrEmpty(_apiKey))
            cleaned = cleaned.Replace(_apiKey, "[redacted]");
        cleaned = Regex.Replace(cleaned, "Bearer\\s+\\S+", "Bearer [redacted]");
        cleaned = Regex.Replace(cleaned, "sk-[A-Za-z0-9_\\-]+", "[redacted]");
        return Excerpt(cleaned);
    }

    static string Excerpt(string text)
    {
        string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length > 500 ? flat.Substring(0, 500) : flat;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
