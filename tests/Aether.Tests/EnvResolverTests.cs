using Aether.Providers;

namespace Aether.Tests;

/// <summary>
/// Tests for EnvResolver — resolves ${ENV_VAR} references from providers.d templates.
/// Mirrors scenarios from openspec/changes/provider-onboarding-preset-raw/specs.
/// </summary>
public sealed class EnvResolverTests : IDisposable
{
    private readonly string _tempDir;

    public EnvResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_er_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private EnvResolveOptions OptionsWith(
        string? animaEnv = null,
        string? zshrc = null,
        Dictionary<string, string>? env = null)
        => new()
        {
            AnimaEnvPath = animaEnv,
            ZshrcPath = zshrc,
            Env = env
        };

    // --- ParseAnimaEnv ---

    [Fact]
    public void ParseAnimaEnv_extracts_key_value_pairs()
    {
        var path = WriteFile("anima.env", """
            GEMINI_API_KEY=AIzaSy123
            DEEPSEEK_API_KEY=sk-549
            # this is a comment
            ANIMA_TELEGRAM_BOT_TOKEN=8396abc
            """);

        var map = EnvResolver.ParseAnimaEnv(path);

        Assert.Equal("AIzaSy123", map["GEMINI_API_KEY"]);
        Assert.Equal("sk-549", map["DEEPSEEK_API_KEY"]);
        Assert.Equal("8396abc", map["ANIMA_TELEGRAM_BOT_TOKEN"]);
        Assert.False(map.ContainsKey("this"));
    }

    [Fact]
    public void ParseAnimaEnv_handles_value_with_equals()
    {
        var path = WriteFile("anima.env", "KEY=base64==value==\n");
        var map = EnvResolver.ParseAnimaEnv(path);
        Assert.Equal("base64==value==", map["KEY"]);
    }

    [Fact]
    public void ParseAnimaEnv_skips_blank_and_comment_lines()
    {
        var path = WriteFile("anima.env", """

            # comment

            KEY=val
            """);
        var map = EnvResolver.ParseAnimaEnv(path);
        Assert.Single(map);
        Assert.Equal("val", map["KEY"]);
    }

    [Fact]
    public void ParseAnimaEnv_strips_inline_comment_from_value()
    {
        // Regression: inline "# comment" after value must be stripped,
        // else the resolved key would be "sk-abc # production" and fail auth.
        var path = WriteFile("anima.env", "NAHCROF_API_KEY=sk-abc123 # production key\n");
        var map = EnvResolver.ParseAnimaEnv(path);
        Assert.Equal("sk-abc123", map["NAHCROF_API_KEY"]);
    }

    [Fact]
    public void ParseAnimaEnv_preserves_hash_inside_value()
    {
        // A # not preceded by a space is part of the value, not a comment.
        var path = WriteFile("anima.env", "KEY=sha#fragment\n");
        var map = EnvResolver.ParseAnimaEnv(path);
        Assert.Equal("sha#fragment", map["KEY"]);
    }

    [Fact]
    public void ParseAnimaEnv_returns_empty_for_missing_file()
    {
        var map = EnvResolver.ParseAnimaEnv(Path.Combine(_tempDir, "nope.env"));
        Assert.Empty(map);
    }

    // --- ParseZshrc ---

    [Fact]
    public void ParseZshrc_extracts_export_lines()
    {
        var path = WriteFile(".zshrc", """
            # my shell config
            export OPENAI_API_KEY=sk-abc123
            export ANTHROPIC_API_KEY="sk-ant-xyz"
            export GROQ_API_KEY='gsk_...'
            alias ll='ls -la'
            some_function() { echo hi; }
            """);

        var map = EnvResolver.ParseZshrc(path);

        Assert.Equal("sk-abc123", map["OPENAI_API_KEY"]);
        Assert.Equal("sk-ant-xyz", map["ANTHROPIC_API_KEY"]);
        Assert.Equal("gsk_...", map["GROQ_API_KEY"]);
        Assert.False(map.ContainsKey("ll"));
    }

    [Fact]
    public void ParseZshrc_strips_double_quotes()
    {
        var path = WriteFile(".zshrc", "export KEY=\"quoted value\"\n");
        var map = EnvResolver.ParseZshrc(path);
        Assert.Equal("quoted value", map["KEY"]);
    }

    [Fact]
    public void ParseZshrc_strips_single_quotes()
    {
        var path = WriteFile(".zshrc", "export KEY='single quoted'\n");
        var map = EnvResolver.ParseZshrc(path);
        Assert.Equal("single quoted", map["KEY"]);
    }

    [Fact]
    public void ParseZshrc_handles_inline_comment()
    {
        var path = WriteFile(".zshrc", "export KEY=val # comment here\n");
        var map = EnvResolver.ParseZshrc(path);
        Assert.Equal("val", map["KEY"]);
    }

    [Fact]
    public void ParseZshrc_handles_quoted_value_with_inline_comment()
    {
        var path = WriteFile(".zshrc", "export KEY=\"val\" # comment\n");
        var map = EnvResolver.ParseZshrc(path);
        Assert.Equal("val", map["KEY"]);
    }

    [Fact]
    public void ParseZshrc_skips_non_export_lines()
    {
        var path = WriteFile(".zshrc", """
            alias ll='ls -la'
            PATH=/usr/bin:/bin
            # plain comment
            export GOOD=key
            """);
        var map = EnvResolver.ParseZshrc(path);
        Assert.Single(map);
        Assert.Equal("key", map["GOOD"]);
    }

    [Fact]
    public void ParseZshrc_returns_empty_for_missing_file()
    {
        var map = EnvResolver.ParseZshrc(Path.Combine(_tempDir, "nope.zshrc"));
        Assert.Empty(map);
    }

    // --- ResolveEnvVar ---

    [Fact]
    public void ResolveEnvVar_prefers_process_env()
    {
        var animaEnv = WriteFile("anima.env", "MY_KEY=from-anima\n");
        var opts = OptionsWith(animaEnv: animaEnv, env: new() { ["MY_KEY"] = "from-env" });

        var result = EnvResolver.ResolveEnvVar("MY_KEY", opts);

        Assert.NotNull(result);
        Assert.Equal("from-env", result!.Value);
        Assert.Equal("env", result.Source);
    }

    [Fact]
    public void ResolveEnvVar_falls_back_to_anima_env()
    {
        var animaEnv = WriteFile("anima.env", "MY_KEY=from-anima\n");
        var opts = OptionsWith(animaEnv: animaEnv, env: new());

        var result = EnvResolver.ResolveEnvVar("MY_KEY", opts);

        Assert.NotNull(result);
        Assert.Equal("from-anima", result!.Value);
        Assert.Equal("anima-env", result.Source);
    }

    [Fact]
    public void ResolveEnvVar_falls_back_to_zshrc()
    {
        var zshrc = WriteFile(".zshrc", "export MY_KEY=from-zshrc\n");
        var opts = OptionsWith(zshrc: zshrc, env: new());

        var result = EnvResolver.ResolveEnvVar("MY_KEY", opts);

        Assert.NotNull(result);
        Assert.Equal("from-zshrc", result!.Value);
        Assert.Equal("zshrc", result.Source);
    }

    [Fact]
    public void ResolveEnvVar_returns_null_when_not_found_anywhere()
    {
        var opts = OptionsWith(env: new());
        Assert.Null(EnvResolver.ResolveEnvVar("MISSING_KEY", opts));
    }

    // --- ResolveApiKeyRef ---

    [Fact]
    public void ResolveApiKeyRef_resolves_env_var_pattern()
    {
        var animaEnv = WriteFile("anima.env", "NAHCROF_API_KEY=nahcrof_secret\n");
        var opts = OptionsWith(animaEnv: animaEnv, env: new());

        var result = EnvResolver.ResolveApiKeyRef("${NAHCROF_API_KEY}", opts);

        Assert.True(result.Resolved);
        Assert.Equal("nahcrof_secret", result.Value);
        Assert.Equal("anima-env", result.Source);
        Assert.False(result.IsOAuth);
    }

    [Fact]
    public void ResolveApiKeyRef_resolves_from_process_env_first()
    {
        var opts = OptionsWith(env: new() { ["NAHCROF_API_KEY"] = "from-env" });

        var result = EnvResolver.ResolveApiKeyRef("${NAHCROF_API_KEY}", opts);

        Assert.True(result.Resolved);
        Assert.Equal("from-env", result.Value);
        Assert.Equal("env", result.Source);
    }

    [Fact]
    public void ResolveApiKeyRef_uses_default_when_var_missing()
    {
        var opts = OptionsWith(env: new());

        var result = EnvResolver.ResolveApiKeyRef("${NINE_ROUTER_KEY:-none}", opts);

        Assert.True(result.Resolved);
        Assert.Equal("none", result.Value);
        Assert.Equal("default", result.Source);
    }

    [Fact]
    public void ResolveApiKeyRef_prefers_resolved_var_over_default()
    {
        var opts = OptionsWith(env: new() { ["NINE_ROUTER_KEY"] = "real-key" });

        var result = EnvResolver.ResolveApiKeyRef("${NINE_ROUTER_KEY:-none}", opts);

        Assert.True(result.Resolved);
        Assert.Equal("real-key", result.Value);
        Assert.Equal("env", result.Source);
    }

    [Fact]
    public void ResolveApiKeyRef_detects_oauth_pattern()
    {
        var result = EnvResolver.ResolveApiKeyRef("${OAUTH:openai}");

        Assert.False(result.Resolved);
        Assert.True(result.IsOAuth);
        Assert.Equal("openai", result.OAuthProvider);
    }

    [Fact]
    public void ResolveApiKeyRef_handles_literal_key()
    {
        var result = EnvResolver.ResolveApiKeyRef("AIzaSyB5YyM7r...");

        Assert.True(result.Resolved);
        Assert.Equal("AIzaSyB5YyM7r...", result.Value);
        Assert.Equal("literal", result.Source);
        Assert.False(result.IsOAuth);
    }

    [Fact]
    public void ResolveApiKeyRef_unresolved_returns_false()
    {
        var opts = OptionsWith(env: new());

        var result = EnvResolver.ResolveApiKeyRef("${MISSING_KEY}", opts);

        Assert.False(result.Resolved);
        Assert.Null(result.Value);
        Assert.Null(result.Source);
        Assert.False(result.IsOAuth);
    }

    [Fact]
    public void ResolveApiKeyRef_empty_ref_returns_unresolved()
    {
        var result = EnvResolver.ResolveApiKeyRef("");

        Assert.False(result.Resolved);
        Assert.Null(result.Source);
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
