using InvoiceAutomation.Core.Models;

namespace InvoiceAutomation.Core;

public interface IFlowLoader
{
    Task<AutomationFlow> LoadAsync(string path, CancellationToken cancellationToken = default);
}
