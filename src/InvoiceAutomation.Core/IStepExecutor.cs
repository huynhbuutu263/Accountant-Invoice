using InvoiceAutomation.Core.Models;

namespace InvoiceAutomation.Core;

public interface IStepExecutor
{
    /// <summary>Executes a single non-loop step (resolved placeholders).</summary>
    Task ExecuteAsync(
        AutomationStep step,
        IAutomationPage page,
        IFileProcessor? fileProcessor,
        int defaultTimeoutMs,
        CancellationToken cancellationToken = default);
}
