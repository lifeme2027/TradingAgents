using System.Text;
using System.Text.Json;

namespace TradingAgents;

public sealed class JsonCheckpointStore
{
    static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };
    readonly string _directory;

    public JsonCheckpointStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Checkpoint directory is required.");
        _directory = directory;
    }

    public AgentState? Load(string threadId)
    {
        string path = PathFor(threadId);
        if (!File.Exists(path))
            return null;
        try
        {
            AgentState? state = JsonSerializer.Deserialize<AgentState>(File.ReadAllText(path, Encoding.UTF8), Options);
            if (state == null)
                throw new InvalidOperationException("checkpoint " + path + " is empty");
            state.Normalize();
            return state;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("checkpoint " + path + " is not usable: " + ex.Message, ex);
        }
    }

    public void Save(string threadId, AgentState state)
    {
        Directory.CreateDirectory(_directory);
        string path = PathFor(threadId);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    string PathFor(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId) || threadId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || threadId.Contains(".."))
            throw new ArgumentException("Checkpoint id is not a file name.");
        return Path.Combine(_directory, threadId + ".json");
    }
}
