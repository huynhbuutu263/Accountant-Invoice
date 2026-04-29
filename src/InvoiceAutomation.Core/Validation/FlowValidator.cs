using InvoiceAutomation.Core.Models;

namespace InvoiceAutomation.Core.Validation;

public static class FlowValidator
{
    private static readonly HashSet<string> KnownActions =
    [
        "navigate", "click", "fill", "wait", "download", "loop",
        "press", "selectoption", "extractzip", "upload", "pauseforuser"
    ];

    public static void Validate(AutomationFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        if (string.IsNullOrWhiteSpace(flow.Name))
            throw new FlowValidationException("Flow name is required.");
        if (string.IsNullOrWhiteSpace(flow.Version))
            throw new FlowValidationException("Flow version is required.");
        if (flow.Steps is null || flow.Steps.Count == 0)
            throw new FlowValidationException("Flow must contain at least one step.");

        foreach (var step in flow.Steps)
            ValidateStep(step, isRoot: true);
    }

    private static void ValidateStep(AutomationStep step, bool isRoot)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (string.IsNullOrWhiteSpace(step.Name))
            throw new FlowValidationException("Each step requires a name.");
        if (string.IsNullOrWhiteSpace(step.Action))
            throw new FlowValidationException($"Step '{step.Name}' requires action.");

        var action = step.Action.Trim().ToLowerInvariant();
        if (!KnownActions.Contains(action))
            throw new FlowValidationException($"Step '{step.Name}' has unknown action '{step.Action}'.");

        switch (action)
        {
            case "navigate":
                if (string.IsNullOrWhiteSpace(step.Value))
                    throw new FlowValidationException($"Step '{step.Name}' (navigate) requires value (URL).");
                break;
            case "click":
            case "fill":
            case "download":
            case "upload":
                if (string.IsNullOrWhiteSpace(step.Selector))
                    throw new FlowValidationException($"Step '{step.Name}' ({action}) requires selector.");
                if (action is "fill" && step.Value is null)
                    throw new FlowValidationException($"Step '{step.Name}' (fill) requires value.");
                if (action is "download" && string.IsNullOrWhiteSpace(step.SavePath))
                    throw new FlowValidationException($"Step '{step.Name}' (download) requires savePath.");
                if (action is "upload" && string.IsNullOrWhiteSpace(step.Value))
                    throw new FlowValidationException($"Step '{step.Name}' (upload) requires value (file path).");
                break;
            case "wait":
                if (string.IsNullOrWhiteSpace(step.Selector) && string.IsNullOrWhiteSpace(step.Value))
                    throw new FlowValidationException($"Step '{step.Name}' (wait) requires selector or value (delay ms).");
                break;
            case "press":
                if (string.IsNullOrWhiteSpace(step.Value))
                    throw new FlowValidationException($"Step '{step.Name}' (press) requires value (key).");
                break;
            case "selectoption":
                if (string.IsNullOrWhiteSpace(step.Selector) || string.IsNullOrWhiteSpace(step.Value))
                    throw new FlowValidationException($"Step '{step.Name}' (selectOption) requires selector and value.");
                break;
            case "extractzip":
                if (string.IsNullOrWhiteSpace(step.Value))
                    throw new FlowValidationException($"Step '{step.Name}' (extractZip) requires value (zip path).");
                break;
            case "loop":
                if (step.Children is null || step.Children.Count == 0)
                    throw new FlowValidationException($"Step '{step.Name}' (loop) requires children.");
                var kind = (step.LoopKind ?? "rows").Trim().ToLowerInvariant();
                if (kind == "rows" && string.IsNullOrWhiteSpace(step.RowSelector))
                    throw new FlowValidationException($"Step '{step.Name}' (loop rows) requires rowSelector.");
                if (kind == "count" && step.Count is null or <= 0)
                    throw new FlowValidationException($"Step '{step.Name}' (loop count) requires positive count.");
                foreach (var child in step.Children)
                    ValidateStep(child, isRoot: false);
                break;
        }
    }
}
