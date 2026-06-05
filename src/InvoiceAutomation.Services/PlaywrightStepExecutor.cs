using InvoiceAutomation.Core;
using InvoiceAutomation.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvoiceAutomation.Services;

public sealed class PlaywrightStepExecutor : IStepExecutor
{
    private readonly ILogger<PlaywrightStepExecutor> _logger;
    private readonly IUserPrompt _userPrompt;
    private readonly IVariableResolver _resolver;

    public PlaywrightStepExecutor(
        ILogger<PlaywrightStepExecutor> logger,
        IUserPrompt userPrompt,
        IVariableResolver resolver)
    {
        _logger = logger;
        _userPrompt = userPrompt;
        _resolver = resolver;
    }

    public async Task<string> ExecuteAsync(
        AutomationStep step,
        IAutomationPage page,
        IFileProcessor? fileProcessor,
        FlowContext context,
        int defaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var action = step.Action.Trim().ToLowerInvariant();
        var timeout = step.TimeoutMs ?? defaultTimeoutMs;
        var filePath = string.Empty;
        switch (action)
        {
            case "navigate":
                await page.GotoAsync(step.Value ?? "", step.WaitUntil, timeout, cancellationToken).ConfigureAwait(false);
                break;
            case "click":
                filePath = await page.ClickAsync(
                    step.Selector!,
                    timeout,
                    step.NthIndex,
                    step.BuildRowPath == true,
                    cancellationToken).ConfigureAwait(false);
                if (step.BuildRowPath == true)
                {
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        filePath = Path.Combine("gdt", context.GetOrEmpty("jobId"), $"row_{context.GetOrEmpty("rowIndex")}");
                        _logger.LogWarning("Row path parse failed; fallback {Path}", filePath);
                    }

                    context.Set("rowFilePath", filePath);
                    context.Set("filePath", filePath);
                    _logger.LogInformation("Click row → filePath={Path} (used by next download step)", filePath);
                }
                break;
            case "fill":
                if (step.JavaScriptFill == true)
                    await page.SetInputValueWithJavaScriptAsync(step.Selector!, step.Value ?? "", timeout, step.NthIndex, cancellationToken).ConfigureAwait(false);
                else
                    await page.FillAsync(step.Selector!, step.Value ?? "", step.ClearFirst != false, timeout, step.NthIndex, cancellationToken).ConfigureAwait(false);
                break;
            case "wait":
                await ExecuteWaitAsync(step, page, timeout, cancellationToken).ConfigureAwait(false);
                break;
            case "download":
                var savePath = step.SavePath?.Contains("{{", StringComparison.Ordinal) == true
                    ? _resolver.Resolve(step.SavePath, context, strict: false)
                    : step.SavePath!;
                if (string.IsNullOrWhiteSpace(context.GetOrEmpty("rowFilePath")))
                    _logger.LogWarning("Download step: rowFilePath is empty — run click row with buildRowPath first.");
                _logger.LogInformation(
                    "Download: selector={Selector}, nthIndex={NthIndex}",
                    step.Selector,
                    step.NthIndex);
                var path = await page.DownloadAsync(
                    step.Selector!, savePath, timeout, step.NthIndex, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Download saved to {Path}", path);
                break;
            case "press":
                await page.PressAsync(step.Selector, step.Value ?? "", timeout, cancellationToken).ConfigureAwait(false);
                break;
            case "selectoption":
                await page.SelectOptionAsync(step.Selector!, step.Value ?? "", timeout, cancellationToken).ConfigureAwait(false);
                break;
            case "upload":
                await page.UploadAsync(step.Selector!, step.Value ?? "", timeout, cancellationToken).ConfigureAwait(false);
                break;
            case "extractzip":
                if (fileProcessor is null)
                    throw new InvalidOperationException("extractZip requires IFileProcessor registration.");
                var zipPath = step.Value?.Contains("{{", StringComparison.Ordinal) == true
                    ? _resolver.Resolve(step.Value, context, strict: false)
                    : step.Value ?? "";
                await fileProcessor.ExtractZipAsync(zipPath, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Extracted zip {Path}", zipPath);
                break;
            case "pauseforuser":
                var message = string.IsNullOrWhiteSpace(step.Value)
                    ? "Please complete the required action in the browser, then click OK to continue."
                    : step.Value;
                _logger.LogInformation("Pausing for user: {Message}", message);
                await _userPrompt.PromptAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unsupported action '{step.Action}' for executor.");
        }

        return filePath;
    }

    private static async Task ExecuteWaitAsync(AutomationStep step, IAutomationPage page, int timeout, CancellationToken cancellationToken)
    {
        var kind = (step.WaitKind ?? "").Trim().ToLowerInvariant();
        if (kind == "delay" || string.IsNullOrWhiteSpace(step.Selector))
        {
            await page.DelayAsync(100, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(step.Selector))
        {
            var state = kind switch
            {
                "selectorhidden" => "hidden",
                "hidden" => "hidden",
                "attached" => "attached",
                _ => "visible"
            };
            await page.WaitForSelectorAsync(step.Selector!, state, timeout, step.NthIndex, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
