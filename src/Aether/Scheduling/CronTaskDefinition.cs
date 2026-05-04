namespace Aether.Scheduling;

public record CronTaskDefinition(
    string Schedule,
    string Agent,
    string Channel,
    bool Enabled,
    string Body,
    string FilePath
);
