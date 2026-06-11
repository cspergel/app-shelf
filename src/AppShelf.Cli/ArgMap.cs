namespace AppShelf.Cli;

/// <summary>
/// Minimal hand-rolled argument parser (spec §4 — keep it light). Splits args into ordered
/// positionals and "--key [value]" options. A "--flag" with no following value is a boolean.
/// </summary>
public sealed class ArgMap
{
    public List<string> Positionals { get; } = new();
    public Dictionary<string, string?> Options { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ArgMap Parse(IReadOnlyList<string> args)
    {
        var map = new ArgMap();
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var key = token[2..];
                string? value = null;
                if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    value = args[++i];
                map.Options[key] = value;
            }
            else
            {
                map.Positionals.Add(token);
            }
        }
        return map;
    }

    public bool Has(string key) => Options.ContainsKey(key);

    public string? Get(string key) => Options.TryGetValue(key, out var v) ? v : null;

    public string? FirstPositional => Positionals.Count > 0 ? Positionals[0] : null;
}
