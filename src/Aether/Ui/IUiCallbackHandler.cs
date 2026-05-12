namespace Aether.Ui;

public interface IUiCallbackHandler
{
    string Namespace { get; }
    Task<UiDocument?> HandleAsync(UiCallback callback, IServiceProvider services, string agentId);
}
