using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TradingAgents;

public static class Sha256Hex
{
    public static string Compute(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (byte item in hash)
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}
