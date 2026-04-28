using InvoiceAutomation.Core;
using InvoiceAutomation.Core.Models;
using Microsoft.Extensions.Logging;

namespace InvoiceAutomation.Services;

public sealed class PlaywrightStepExecutor : IStepExecutor
{
    private readonly ILogger<PlaywrightStepExecutor> _logger;

    public PlaywrightStepExecutor(ILogger<PlaywrightStepExecutor> logger) => _logger = logger;

    public async Task ExecuteAsync(
        AutomationStep step,
        IAutomationPage page,
        IFileProcessor? fileProcessor,
        int defaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var action = step.Action.Trim().ToLowerInvariant();
        var timeout = step.TimeoutMs ?? defaultTimeoutMs;

        switch (action)
        {
            case "navigate":
                await page.GotoAsync(step.Value ?? "", step.WaitUntil, timeout, cancellationToken).ConfigureAwait(false);
                break;
            case "click":
                await page.ClickAsync(step.Selector!, timeout, cancellationToken).ConfigureAwait(false);
                break;
            case "fill":
                if (step.JavaScriptFill == true)
                    await page.SetInputValueWithJavaScriptAsync(step.Selector!, step.Value ?? "", timeout, cancellationToken).ConfigureAwait(false);
                else
                    await page.FillAsync(step.Selector!, step.Value ?? "", step.ClearFirst != false, timeout, cancellationToken).ConfigureAwait(false);
                break;
            case "wait":
                await ExecuteWaitAsync(step, page, timeout, cancellationToken).ConfigureAwait(false);
                break;
            case "download":
                var path = await page.DownloadAsync(step.Selector!, step.SavePath!, timeout, cancellationToken).ConfigureAwait(false);
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
                await fileProcessor.ExtractZipAsync(step.Value ?? "", cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unsupported action '{step.Action}' for executor.");
        }
    }

    private static async Task ExecuteWaitAsync(AutomationStep step, IAutomationPage page, int timeout, CancellationToken cancellationToken)
    {
        var kind = (step.WaitKind ?? "").Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(step.Value) && int.TryParse(step.Value, out var ms) &&
            (kind == "delay" || string.IsNullOrWhiteSpace(step.Selector)))
        {
            await page.DelayAsync(ms, cancellationToken).ConfigureAwait(false);
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
            await page.WaitForSelectorAsync(step.Selector!, state, timeout, cancellationToken).ConfigureAwait(false);
        }
    }
}
