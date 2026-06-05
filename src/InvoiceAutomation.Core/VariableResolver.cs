using System.Text.Json;
using System.Text.RegularExpressions;
using InvoiceAutomation.Core.Json;
using InvoiceAutomation.Core.Models;

namespace InvoiceAutomation.Core;

public sealed partial class VariableResolver : IVariableResolver
{
    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    public string Resolve(string? input, FlowContext context, bool strict)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? "";

        var s = input.Replace("{{{{", "\uE000").Replace("}}}}", "\uE001");
        for (var pass = 0; pass < 32; pass++)
        {
            var replaced = PlaceholderRegex().Replace(s, m =>
            {
                var key = m.Groups[1].Value.Trim();
                if (context.TryGet(key, out var value))
                    return value;
                if (strict)
                    throw new InvalidOperationException($"Missing variable '{{{{{key}}}}}'.");
                return "";
            });
            if (replaced == s)
                break;
            s = replaced;
        }

        return s.Replace("\uE000", "{{").Replace("\uE001", "}}");
    }

    public AutomationStep ResolveStep(AutomationStep step, FlowContext context, bool strict)
    {
        var clone = CloneStep(step);
        ResolveStrings(clone, context, strict);
        return clone;
    }

    private void ResolveStrings(AutomationStep step, FlowContext context, bool strict)
    {
        step.Name = Resolve(step.Name, context, strict);
        step.Action = Resolve(step.Action, context, strict);
        step.Selector = Resolve(step.Selector, context, strict);
        if (!ShouldDeferValueResolve(step))
            step.Value = Resolve(step.Value, context, strict);
        step.WaitUntil = Resolve(step.WaitUntil, context, strict);
        step.WaitKind = Resolve(step.WaitKind, context, strict);
        step.OnError = Resolve(step.OnError, context, strict);
        step.Comment = Resolve(step.Comment, context, strict);
        if (!ShouldDeferSavePathResolve(step))
            step.SavePath = Resolve(step.SavePath, context, strict);
        step.LoopKind = Resolve(step.LoopKind, context, strict);
        step.RowSelector = Resolve(step.RowSelector, context, strict);
        step.RowVariable = Resolve(step.RowVariable, context, strict);
        if (step.Expect is not null)
        {
            step.Expect.Selector = Resolve(step.Expect.Selector, context, strict);
            step.Expect.State = Resolve(step.Expect.State, context, strict);
            step.Expect.UrlContains = Resolve(step.Expect.UrlContains, context, strict);
        }

        if (step.Children is not null)
        {
            foreach (var child in step.Children)
                ResolveStrings(child, context, strict);
        }
    }

    private static bool ShouldDeferValueResolve(AutomationStep step)
    {
        if (string.IsNullOrWhiteSpace(step.Value) || !step.Value.Contains("{{", StringComparison.Ordinal))
            return false;
        var action = step.Action.Trim();
        return action.Equals("extractzip", StringComparison.OrdinalIgnoreCase) &&
               step.Value.Contains("rowFilePath", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldDeferSavePathResolve(AutomationStep step)
    {
        if (string.IsNullOrWhiteSpace(step.SavePath) || !step.SavePath.Contains("{{", StringComparison.Ordinal))
            return false;
        return step.Action.Trim().Equals("download", StringComparison.OrdinalIgnoreCase) &&
               (step.SavePath.Contains("rowFilePath", StringComparison.OrdinalIgnoreCase) ||
                step.SavePath.Contains("filePath", StringComparison.OrdinalIgnoreCase));
    }

    private static AutomationStep CloneStep(AutomationStep s) =>
        JsonSerializer.Deserialize<AutomationStep>(
            JsonSerializer.Serialize(s, FlowJsonDefaults.Options),
            FlowJsonDefaults.Options) ?? throw new InvalidOperationException("Clone failed.");
}
