using InvoiceAutomation.Core.Models;

namespace InvoiceAutomation.Core;

public interface IVariableResolver
{
    string Resolve(string? input, FlowContext context, bool strict);

    AutomationStep ResolveStep(AutomationStep step, FlowContext context, bool strict);
}
