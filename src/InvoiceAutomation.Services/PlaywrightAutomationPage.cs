using System.Text.RegularExpressions;
using InvoiceAutomation.Core;
using InvoiceAutomation.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace InvoiceAutomation.Services;

public sealed class PlaywrightAutomationPage : IAutomationPage
{
    private static readonly Regex PlaywrightNthChainRegex = new(
        @"\s*>>\s*nth=\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<PlaywrightAutomationPage>? _logger;
    private IPage _page;

    public PlaywrightAutomationPage(IPage page, ILogger<PlaywrightAutomationPage>? logger = null)
    {
        _page = page;
        _logger = logger;
    }

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

    public async Task<string> ClickAsync(
        string selector,
        int? timeoutMs,
        int? nthIndex = null,
        bool buildRowPath = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loc = _page.Locator(selector);
        if (nthIndex.HasValue)
            loc = loc.Nth(nthIndex.Value);

        await loc.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        }).ConfigureAwait(false);

        await loc.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);

        string relativePath = "";
        if (buildRowPath)
        {
            var cells = await ReadRowCellTextsAsync(loc).ConfigureAwait(false);
            if (!GdtRowPathBuilder.TryBuildFromCells(cells, out relativePath))
            {
                var text = await loc.InnerTextAsync().ConfigureAwait(false);
                GdtRowPathBuilder.TryBuildRelativePath(text, out relativePath);
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                _logger?.LogWarning(
                    "Could not build row path from row (cells={CellCount}, sample={Sample})",
                    cells.Count,
                    cells.Count > 0 ? string.Join(" | ", cells.Take(8)) : await loc.InnerTextAsync().ConfigureAwait(false));
            }
            else
            {
                _logger?.LogInformation("Built rowFilePath from row: {Path}", relativePath);
            }
        }

        await loc.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs }).ConfigureAwait(false);
        return relativePath;
    }

    public async Task FillAsync(string selector, string value, bool clearFirst, int? timeoutMs, int? nthIndex = null, CancellationToken cancellationToken = default)
    {
        var loc = _page.Locator(selector);
        if (nthIndex.HasValue)
            loc = loc.Nth(nthIndex.Value);
        if (clearFirst)
            await loc.ClearAsync(new LocatorClearOptions { Timeout = timeoutMs }).ConfigureAwait(false);
        await loc.FillAsync(value, new LocatorFillOptions { Timeout = timeoutMs }).ConfigureAwait(false);
    }

    public async Task SetInputValueWithJavaScriptAsync(string selector, string value, int? timeoutMs, int? nthIndex = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loc = _page.Locator(selector);
        if (nthIndex.HasValue)
            loc = loc.Nth(nthIndex.Value);
        await loc.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeoutMs }).ConfigureAwait(false);
        await loc.EvaluateAsync(
            "(el, v) => { el.value = v; el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true })); }",
            value).ConfigureAwait(false);
    }

    public async Task WaitForSelectorAsync(
        string selector,
        string state,
        int? timeoutMs,
        int? nthIndex = null,
        CancellationToken cancellationToken = default)
    {
        var s = (state ?? "visible").Trim().ToLowerInvariant();
        var wait = s switch
        {
            "hidden" => WaitForSelectorState.Hidden,
            "attached" => WaitForSelectorState.Attached,
            _ => WaitForSelectorState.Visible
        };

        var target = await ResolveClickTargetAsync(_page.Locator(selector), nthIndex, selector)
            .ConfigureAwait(false);
        await target.WaitForAsync(new LocatorWaitForOptions
        {
            State = wait,
            Timeout = timeoutMs
        }).ConfigureAwait(false);
    }

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) =>
        Task.Delay(milliseconds, cancellationToken);

    public async Task<string> DownloadAsync(
        string selector,
        string savePath,
        int? timeoutMs,
        int? nthIndex = null,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = savePath + ".tmp";
        try
        {
            var download = await _page.RunAndWaitForDownloadAsync(async () =>
            {
                var clickTarget = await ResolveDownloadClickTargetAsync(selector, nthIndex)
                    .ConfigureAwait(false);
                await clickTarget.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs }).ConfigureAwait(false);
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
            await WaitForSelectorAsync(expect.Selector!, state, timeout, nthIndex: null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Download button target. When <paramref name="nthIndex"/> is set (0-based), it wins:
    /// strips any <c>>> nth=N</c> from the selector string and uses <c>.Nth(nthIndex)</c> on the base selector.
    /// </summary>
    private async Task<ILocator> ResolveDownloadClickTargetAsync(string selector, int? nthIndex)
    {
        if (nthIndex is int n)
        {
            var baseSelector = PlaywrightNthChainRegex.Replace(selector, "").Trim();
            var loc = _page.Locator(baseSelector);
            var count = await loc.CountAsync().ConfigureAwait(false);
            _logger?.LogInformation(
                "DownloadAsync: nthIndex={NthIndex} on '{BaseSelector}' ({MatchCount} matches, 0=first)",
                n, baseSelector, count);
            return loc.Nth(n);
        }

        var locator = _page.Locator(selector);
        if (SelectorSpecifiesNth(selector))
        {
            _logger?.LogInformation("DownloadAsync: using nth from selector chain '{Selector}'", selector);
            return locator;
        }

        return await ResolveClickTargetAsync(locator, nthIndex: null, selector).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves which element to click. Priority: step <c>nthIndex</c> → <c>>> nth=</c> in selector → first visible match.
    /// Playwright <c>nth</c> is 0-based (nth=0 is first, nth=1 is second).
    /// </summary>
    private static async Task<ILocator> ResolveClickTargetAsync(
        ILocator locator,
        int? nthIndex,
        string selector)
    {
        if (nthIndex.HasValue)
            return locator.Nth(nthIndex.Value);

        if (SelectorSpecifiesNth(selector))
            return locator.First;

        var count = await locator.CountAsync().ConfigureAwait(false);
        if (count <= 1)
            return locator.First;

        for (var i = 0; i < count; i++)
        {
            var nth = locator.Nth(i);
            if (await nth.IsVisibleAsync().ConfigureAwait(false))
                return nth;
        }

        return locator.First;
    }

    private static bool SelectorSpecifiesNth(string selector) =>
        selector.Contains(">> nth=", StringComparison.OrdinalIgnoreCase) ||
        selector.Contains("nth=", StringComparison.OrdinalIgnoreCase) ||
        selector.Contains("nth-child(", StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<string>> ReadRowCellTextsAsync(ILocator row)
    {
        var cellLocator = row.Locator("td");
        var count = await cellLocator.CountAsync().ConfigureAwait(false);
        if (count == 0)
        {
            cellLocator = row.Locator(".ant-table-cell");
            count = await cellLocator.CountAsync().ConfigureAwait(false);
        }

        if (count > 0)
        {
            var cells = new List<string>(count);
            for (var i = 0; i < count; i++)
                cells.Add((await cellLocator.Nth(i).InnerTextAsync().ConfigureAwait(false)).Trim());
            return cells;
        }

        return GdtRowPathBuilder.TokenizeRowText(await row.InnerTextAsync().ConfigureAwait(false));
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
