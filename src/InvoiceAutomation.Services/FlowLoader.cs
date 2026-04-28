using System.Text.Json;
using InvoiceAutomation.Core;
using InvoiceAutomation.Core.Json;
using InvoiceAutomation.Core.Models;
using InvoiceAutomation.Core.Validation;

namespace InvoiceAutomation.Services;

public sealed class FlowLoader : IFlowLoader
{
    public async Task<AutomationFlow> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Flow file not found.", path);

        await using var stream = File.OpenRead(path);
        var flow = await JsonSerializer.DeserializeAsync<AutomationFlow>(stream, FlowJsonDefaults.Options, cancellationToken)
                   .ConfigureAwait(false);
        if (flow is null)
            throw new FlowValidationException("Flow JSON deserialized to null.");
        FlowValidator.Validate(flow);
        return flow;
    }
}
