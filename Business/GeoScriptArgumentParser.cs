// GeoscientistToolkit/Business/GeoScript/GeoScriptArgumentParser.cs

using System.Collections;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace GeoscientistToolkit.Business.GeoScript;

public static class GeoScriptArgumentParser
{
    private static readonly Regex ArgumentRegex =
        new(@"(?<key>-?[A-Za-z0-9_]+)\s*=\s*(?<value>""[^""]+""|'[^']+'|\S+)",
            RegexOptions.Compiled);

    public static Dictionary<string, string> ParseArguments(string fullText)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ArgumentRegex.Matches(fullText))
        {
            var key = NormalizeKey(match.Groups["key"].Value);
            var value = StripQuotes(match.Groups["value"].Value);
            args[key] = value;
        }

        return args;
    }

    public static string NormalizeKey(string key)
    {
        return key.TrimStart('-').Replace("-", "_", StringComparison.Ordinal).Trim();
    }

    public static bool TryGetString(Dictionary<string, string> args, string key, out string value)
    {
        return args.TryGetValue(NormalizeKey(key), out value);
    }

    public static string GetString(Dictionary<string, string> args, string key, string defaultValue,
        GeoScriptContext context)
    {
        if (!TryGetString(args, key, out var token))
            return defaultValue;

        var resolved = ResolveToken(token, context);
        return resolved?.ToString() ?? defaultValue;
    }

    public static float GetFloat(Dictionary<string, string> args, string key, float defaultValue,
        GeoScriptContext context)
    {
        if (!TryGetString(args, key, out var token))
            return defaultValue;

        var resolved = ResolveToken(token, context);
        if (resolved == null)
            return defaultValue;

        return Convert.ToSingle(resolved, CultureInfo.InvariantCulture);
    }

    public static double GetDouble(Dictionary<string, string> args, string key, double defaultValue,
        GeoScriptContext context)
    {
        if (!TryGetString(args, key, out var token))
            return defaultValue;

        var resolved = ResolveToken(token, context);
        if (resolved == null)
            return defaultValue;

        return Convert.ToDouble(resolved, CultureInfo.InvariantCulture);
    }

    public static int GetInt(Dictionary<string, string> args, string key, int defaultValue,
        GeoScriptContext context)
    {
        if (!TryGetString(args, key, out var token))
            return defaultValue;

        var resolved = ResolveToken(token, context);
        if (resolved == null)
            return defaultValue;

        return Convert.ToInt32(resolved, CultureInfo.InvariantCulture);
    }

    public static bool GetBool(Dictionary<string, string> args, string key, bool defaultValue,
        GeoScriptContext context)
    {
        if (!TryGetString(args, key, out var token))
            return defaultValue;

        var resolved = ResolveToken(token, context);
        if (resolved is bool boolValue)
            return boolValue;

        var stringValue = resolved?.ToString();
        if (stringValue == null)
            return defaultValue;

        return bool.TryParse(stringValue, out var parsed) ? parsed : defaultValue;
    }

    public static Vector3 GetVector3(Dictionary<string, string> args, string key, Vector3 defaultValue,
        GeoScriptContext context)
    {
        if (!TryGetString(args, key, out var token))
            return defaultValue;

        var resolved = ResolveToken(token, context);
        if (resolved is Vector3 vector)
            return vector;

        var stringValue = resolved?.ToString();
        if (stringValue == null)
            return defaultValue;

        var parts = stringValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return defaultValue;

        return new Vector3(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture)
        );
    }

    public static HashSet<byte> GetByteSet(Dictionary<string, string> args, string key,
        GeoScriptContext context)
    {
        if (!TryGetString(args, key, out var token))
            return null;

        var resolved = ResolveToken(token, context);
        if (resolved is IEnumerable enumerable && resolved is not string)
        {
            var set = new HashSet<byte>();
            foreach (var item in enumerable)
                set.Add(Convert.ToByte(item, CultureInfo.InvariantCulture));
            return set;
        }

        var stringValue = resolved?.ToString();
        if (stringValue == null)
            return null;

        var parts = stringValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Select(p => byte.Parse(p, CultureInfo.InvariantCulture)).ToHashSet();
    }

    public static Dictionary<byte, double> GetByteDoubleMap(Dictionary<string, string> args, string key,
        GeoScriptContext context)
    {
        if (!TryGetString(args, key, out var token))
            return null;

        var stringValue = ResolveToken(token, context)?.ToString();
        if (stringValue == null)
            return null;

        var map = new Dictionary<byte, double>();
        var entries = stringValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var pair = entry.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (pair.Length != 2)
                continue;
            var keyByte = byte.Parse(pair[0], CultureInfo.InvariantCulture);
            var value = double.Parse(pair[1], CultureInfo.InvariantCulture);
            map[keyByte] = value;
        }

        return map;
    }

    public static TEnum GetEnum<TEnum>(Dictionary<string, string> args, string key, TEnum defaultValue,
        GeoScriptContext context) where TEnum : struct
    {
        if (!TryGetString(args, key, out var token))
            return defaultValue;

        var resolved = ResolveToken(token, context);
        if (resolved is TEnum enumValue)
            return enumValue;

        var stringValue = resolved?.ToString();
        if (stringValue == null)
            return defaultValue;

        return Enum.TryParse<TEnum>(stringValue, true, out var parsed) ? parsed : defaultValue;
    }

    private static object ResolveToken(string token, GeoScriptContext context)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var trimmed = StripQuotes(token);

        if (context?.AvailableDatasets != null &&
            (trimmed.Contains('.') || context.AvailableDatasets.ContainsKey(trimmed)))
        {
            return GeoScriptValueResolver.Resolve(trimmed, context);
        }

        return trimmed;
    }

    private static string StripQuotes(string token)
    {
        if (token.Length >= 2 &&
            ((token.StartsWith('"') && token.EndsWith('"')) ||
             (token.StartsWith('\'') && token.EndsWith('\''))))
        {
            return token[1..^1];
        }

        return token;
    }
}
