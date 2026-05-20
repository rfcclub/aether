using System.Text.RegularExpressions;
using Aether.Plugins;
using Microsoft.Extensions.Logging;

namespace Aether.Agent;

public sealed class AxiomValidator
{
    private readonly string _soulFilePath;
    private readonly ILogger<AxiomValidator> _logger;
    private List<string> _axioms = new();

    public AxiomValidator(string soulFilePath, ILogger<AxiomValidator> logger)
    {
        _soulFilePath = soulFilePath;
        _logger = logger;
    }

    public async Task LoadAxiomsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_soulFilePath)) return;

        var content = await File.ReadAllTextAsync(_soulFilePath, ct);
        var match = Regex.Match(content, @"# Axioms\s*(.*?)(?=\n#|$)", RegexOptions.Singleline);
        if (match.Success)
        {
            _axioms = match.Groups[1].Value
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            
            _logger.LogInformation("Loaded {Count} axioms from {Path}", _axioms.Count, _soulFilePath);
        }
    }

    public async Task<ValidationResult> ValidateActionAsync(string toolName, string arguments, CancellationToken ct = default)
    {
        // For high-risk tools, we perform a lightweight check
        // In a real scenario, this might involve another LLM call or complex heuristics
        // For MVP, we log and enforce basic rules
        
        if (toolName == "bash" && (arguments.Contains("rm -rf /") || arguments.Contains("> /dev/sda")))
        {
            return new ValidationResult(false, "Action violates safety axioms: destructive system commands blocked.");
        }

        return new ValidationResult(true);
    }
}

public record ValidationResult(bool Success, string? ErrorMessage = null);
