using InvoiceAutomation.Core.Models;

namespace InvoiceAutomation.Core;

/// <summary>Browser page abstraction — implemented by Playwright in Services.</summary>
public interface IAutomationPage
{
    Task GotoAsync(string url, string? waitUntil, int? timeoutMs, CancellationToken cancellationToken = default);
    Task ClickAsync(string selector, int? timeoutMs, CancellationToken cancellationToken = default);
    Task FillAsync(string selector, string value, bool clearFirst, int? timeoutMs, CancellationToken cancellationToken = default);
    /// <summary>Assign <c>value</c> to the input/textarea and dispatch input/change (for readonly fields).</summary>
    Task SetInputValueWithJavaScriptAsync(string selector, string value, int? timeoutMs, CancellationToken cancellationToken = default);
    Task WaitForSelectorAsync(string selector, string state, int? timeoutMs, CancellationToken cancellationToken = default);
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default);
    Task<string> DownloadAsync(string selector, string savePath, int? timeoutMs, CancellationToken cancellationToken = default);
    Task<int> CountAsync(string selector, CancellationToken cancellationToken = default);
    Task PressAsync(string? selector, string key, int? timeoutMs, CancellationToken cancellationToken = default);
    Task SelectOptionAsync(string selector, string optionValueOrLabel, int? timeoutMs, CancellationToken cancellationToken = default);
    Task UploadAsync(string selector, string filePath, int? timeoutMs, CancellationToken cancellationToken = default);
    Task ExpectAsync(StepExpect expect, int? defaultTimeoutMs, CancellationToken cancellationToken = default);
    string? Url { get; }
}
