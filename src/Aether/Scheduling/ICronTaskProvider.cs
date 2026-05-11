namespace Aether.Scheduling;

public interface ICronTaskProvider
{
    IReadOnlyList<CronTaskDefinition> GetTasks();
}
