using System.Text.RegularExpressions;

namespace Aether.Providers;

/// <summary>Result of resolving an apiKey reference from a providers.d template.</summary>
public sealed record EnvResolveResult
{
    public bool Resolved { get; init; }
    public string? Value { get; init; }
    /// <summary>"env" | "anima-env" | "zshrc" | "literal" | "default" | null</summary>
    public string? Source { get; init; }
    public bool IsOAuth { get; init; }
    public string? OAuthProvider { get; init; }
}

    /// <summary>Result of resolving a single env var from one of the sources.</summary>
    public sealed record EnvVarResult(string Value, string Source);

    /// <summary>Options controlling env resolution sources.</summary>
    public sealed record EnvResolveOptions
    {
        /// <summary>Default ~/.anima/anima.env.</summary>
        public string? AnimaEnvPath { get; init; }
        /// <summary>Default ~/.zshrc.</summary>
        public string? ZshrcPath { get; init; }
        /// <summary>Default: live process environment (Environment.GetEnvironmentVariables).</summary>
        public IReadOnlyDictionary<string, string>? Env { get; init; }
    }

/// <summary>
/// Resolves <c>${ENV_VAR}</c> references in providers.d apiKey fields by searching
/// process.env → ~/.anima/anima.env → ~/.zshrc. Also handles <c>${VAR:-default}</c>,
/// <c>${OAUTH:provider}</c>, and literal keys.
/// </summary>
public static class EnvResolver
{
    // ${ENV_VAR}  or  ${ENV_VAR:-default}  or  ${OAUTH:provider}
    private static readonly Regex RefPattern =
        new(@"^\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::-(?<default>[^}]*))?\}$", RegexOptions.Compiled);

    private static readonly Regex OAuthPattern =
        new(@"^\$\{OAUTH:(?<provider>[^}]+)\}$", RegexOptions.Compiled);

    /// <summary>
    /// Resolve an apiKey reference from providers.d.
    /// </summary>
    public static EnvResolveResult ResolveApiKeyRef(string? refValue, EnvResolveOptions? options = null)
    {
        if (string.IsNullOrEmpty(refValue))
            return new EnvResolveResult { Resolved = false, Source = null };

        var value = refValue!;

        // OAuth: ${OAUTH:provider}
        var oauthMatch = OAuthPattern.Match(value);
        if (oauthMatch.Success)
        {
            return new EnvResolveResult
            {
                Resolved = false,
                IsOAuth = true,
                OAuthProvider = oauthMatch.Groups["provider"].Value
            };
        }

        // ${ENV_VAR} or ${ENV_VAR:-default}
        var refMatch = RefPattern.Match(value);
        if (refMatch.Success)
        {
            var name = refMatch.Groups["name"].Value;
            var resolved = ResolveEnvVar(name, options);
            if (resolved is not null)
                return new EnvResolveResult { Resolved = true, Value = resolved.Value, Source = resolved.Source };

            // try default
            var defaultValue = refMatch.Groups["default"];
            if (defaultValue.Success)
                return new EnvResolveResult { Resolved = true, Value = defaultValue.Value, Source = "default" };

            return new EnvResolveResult { Resolved = false, Source = null };
        }

        // literal key (no ${} pattern)
        if (!value.Contains("${", StringComparison.Ordinal))
            return new EnvResolveResult { Resolved = true, Value = value, Source = "literal" };

        // malformed ref (contains ${ but doesn't match patterns) — unresolved
        return new EnvResolveResult { Resolved = false, Source = null };
    }

    /// <summary>
    /// Resolve env var: process.env &gt; anima.env &gt; .zshrc. Returns null if not found.
    /// </summary>
    public static EnvVarResult? ResolveEnvVar(string name, EnvResolveOptions? options = null)
    {
        options ??= new EnvResolveOptions();

        // 1. process.env (or injected Env)
        if (options.Env is not null)
        {
            if (options.Env.TryGetValue(name, out var envValue) && !string.IsNullOrEmpty(envValue))
                return new EnvVarResult(envValue, "env");
        }
        else
        {
            var live = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(live))
                return new EnvVarResult(live, "env");
        }

        // 2. anima.env
        var animaEnv = ParseAnimaEnv(options.AnimaEnvPath);
        if (animaEnv.TryGetValue(name, out var animaValue) && !string.IsNullOrEmpty(animaValue))
            return new EnvVarResult(animaValue, "anima-env");

        // 3. .zshrc
        var zshrc = ParseZshrc(options.ZshrcPath);
        if (zshrc.TryGetValue(name, out var zshValue) && !string.IsNullOrEmpty(zshValue))
            return new EnvVarResult(zshValue, "zshrc");

        return null;
    }

    /// <summary>
    /// Parse ~/.anima/anima.env (KEY=VALUE lines). Skips # comments and blanks.
    /// Missing file → empty map (no throw).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseAnimaEnv(string? envPath = null)
    {
        var path = envPath ?? DefaultAnimaEnvPath();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return result;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch
        {
            return result;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line.Substring(0, eq).Trim();
            var val = line.Substring(eq + 1).Trim();
            // Strip inline comments: "KEY=value # comment" → "value".
            // (Don't strip inside quoted values; anima.env values are unquoted here.)
            var hash = val.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0)
                val = val.Substring(0, hash).TrimEnd();
            if (key.Length > 0)
                result[key] = val;
        }
        return result;
    }

    /// <summary>
    /// Parse ~/.zshrc, extract only <c>export KEY=VALUE</c> lines. Handles single/double
    /// quotes and inline comments. Missing file → empty map (no throw).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseZshrc(string? zshrcPath = null)
    {
        var path = zshrcPath ?? DefaultZshrcPath();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return result;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch
        {
            return result;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();

            // must start with "export "
            if (!trimmed.StartsWith("export ", StringComparison.Ordinal) &&
                !trimmed.StartsWith("export\t", StringComparison.Ordinal))
                continue;

            var rest = trimmed.Substring("export".Length).TrimStart();

            var eq = rest.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = rest.Substring(0, eq).Trim();
            var val = rest.Substring(eq + 1).Trim();

            // strip quotes + inline comment
            val = StripQuotesAndComment(val);

            if (key.Length > 0 && key.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                result[key] = val;
        }
        return result;
    }

    private static string StripQuotesAndComment(string value)
    {
        if (value.Length == 0)
            return value;

        // double-quoted: take content up to closing quote, ignore inline comment after
        if (value[0] == '"')
        {
            var close = value.IndexOf('"', 1);
            if (close > 0)
                return value.Substring(1, close - 1);
            return value.Substring(1);
        }

        // single-quoted: take content up to closing quote
        if (value[0] == '\'')
        {
            var close = value.IndexOf('\'', 1);
            if (close > 0)
                return value.Substring(1, close - 1);
            return value.Substring(1);
        }

        // unquoted: cut at inline comment (space + #)
        var hash = value.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0)
            return value.Substring(0, hash).TrimEnd();
        return value;
    }

    private static string DefaultAnimaEnvPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".anima", "anima.env");
    }

    private static string DefaultZshrcPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".zshrc");
    }
}
