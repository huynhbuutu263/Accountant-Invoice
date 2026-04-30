using InvoiceAutomation.Core;
using InvoiceAutomation.Core.Models;
using Microsoft.Playwright;

namespace InvoiceAutomation.Services;

public sealed class PlaywrightAutomationPage : IAutomationPage
{
    private IPage _page;

    public PlaywrightAutomationPage(IPage page) => _page = page;

    /// <summary>Replaces the underlying Playwright page (e.g. when the browser context navigates to a new tab).</summary>
    internal void UpdatePage(IPage page) => _page = page;

    public string? Url => _page.Url;

    public async Task GotoAsync(string url, string? waitUntil, int? timeoutMs, CancellationToken cancellationToken = default)
    {
        var w = ParseWaitUntil(waitUntil);
        await _page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = w,
            Timeout = timeoutMs
        }).ConfigureAwait(false);
    }

    public async Task ClickAsync(string selector, int? timeoutMs, int? nthIndex = null, CancellationToken cancellationToken = default)
    {
        var loc = _page.Locator(selector);
        if (nthIndex.HasValue)
            loc = loc.Nth(nthIndex.Value);
        await loc.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs }).ConfigureAwait(false);
    }

    public async Task FillAsync(string selector, string value, bool clearFirst, int? timeoutMs, CancellationToken cancellationToken = default)
    {
        var loc = _page.Locator(selector);
        if (clearFirst)
            await loc.ClearAsync(new LocatorClearOptions { Timeout = timeoutMs }).ConfigureAwait(false);
        await loc.FillAsync(value, new LocatorFillOptions { Timeout = timeoutMs }).ConfigureAwait(false);
    }

    public async Task SetInputValueWithJavaScriptAsync(string selector, string value, int? timeoutMs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loc = _page.Locator(selector);
        await loc.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeoutMs }).ConfigureAwait(false);
        await loc.EvaluateAsync(
            "(el, v) => { el.value = v; el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true })); }",
            value).ConfigureAwait(false);
    }

    public async Task WaitForSelectorAsync(string selector, string state, int? timeoutMs, CancellationToken cancellationToken = default)
    {
        var s = (state ?? "visible").Trim().ToLowerInvariant();
        var wait = s switch
        {
            "hidden" => WaitForSelectorState.Hidden,
            "attached" => WaitForSelectorState.Attached,
            _ => WaitForSelectorState.Visible
        };
        await _page.Locator(selector).WaitForAsync(new LocatorWaitForOptions
        {
            State = wait,
            Timeout = timeoutMs
        }).ConfigureAwait(false);
    }

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) =>
        Task.Delay(milliseconds, cancellationToken);

    public async Task<string> DownloadAsync(string selector, string savePath, int? timeoutMs, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = savePath + ".tmp";
        try
        {
            var download = await _page.RunAndWaitForDownloadAsync(async () =>
            {
                await _page.Locator(selector).ClickAsync(new LocatorClickOptions { Timeout = timeoutMs }).ConfigureAwait(false);
            }, new PageRunAndWaitForDownloadOptions { Timeout = timeoutMs }).ConfigureAwait(false);

            await download.SaveAsAsync(tmp).ConfigureAwait(false);
            if (File.Exists(savePath))
                File.Delete(savePath);
            File.Move(tmp, savePath);
            return savePath;
        }
        finally
        {
            if (File.Exists(tmp))
                try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    public async Task<int> CountAsync(string selector, CancellationToken cancellationToken = default) =>
        await _page.Locator(selector).CountAsync().ConfigureAwait(false);

    public async Task PressAsync(string? selector, string key, int? timeoutMs, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(selector))
        {
            await _page.Locator(selector).PressAsync(key, new LocatorPressOptions { Timeout = timeoutMs }).ConfigureAwait(false);
        }
        else
        {
            await _page.Keyboard.PressAsync(key).ConfigureAwait(false);
        }
    }

    public async Task SelectOptionAsync(string selector, string optionValueOrLabel, int? timeoutMs, CancellationToken cancellationToken = default) =>
        await _page.Locator(selector).SelectOptionAsync(optionValueOrLabel, new LocatorSelectOptionOptions { Timeout = timeoutMs }).ConfigureAwait(false);

    public async Task UploadAsync(string selector, string filePath, int? timeoutMs, CancellationToken cancellationToken = default) =>
        await _page.Locator(selector).SetInputFilesAsync(filePath, new LocatorSetInputFilesOptions { Timeout = timeoutMs }).ConfigureAwait(false);

    public async Task ExpectAsync(StepExpect expect, int? defaultTimeoutMs, CancellationToken cancellationToken = default)
    {
        var timeout = expect.TimeoutMs ?? defaultTimeoutMs;
        if (!string.IsNullOrWhiteSpace(expect.UrlContains))
        {
            var fragment = expect.UrlContains!;
            var deadline = DateTime.UtcNow.AddMilliseconds(timeout ?? 30_000);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_page.Url.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException($"URL did not contain '{fragment}' within timeout.");
        }

        if (!string.IsNullOrWhiteSpace(expect.Selector))
        {
            var state = expect.State ?? "visible";
            await WaitForSelectorAsync(expect.Selector!, state, timeout, cancellationToken).ConfigureAwait(false);
        }
    }

    private static WaitUntilState? ParseWaitUntil(string? waitUntil)
    {
        if (string.IsNullOrWhiteSpace(waitUntil))
            return null;
        return waitUntil.Trim().ToLowerInvariant() switch
        {
            "load" => WaitUntilState.Load,
            "domcontentloaded" => WaitUntilState.DOMContentLoaded,
            "networkidle" => WaitUntilState.NetworkIdle,
            _ => null
        };
    }
}
