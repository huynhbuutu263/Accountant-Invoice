using System.Text.RegularExpressions;
using InvoiceAutomation.Core;
using System.Text.RegularExpressions;
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
        ILocator loc;
        if (buildRowPath && TryParseDataRowIndex(selector, nthIndex, out var dataRowIndex))
        {
            loc = await ResolvePopulatedDataRowLocatorAsync(selector, dataRowIndex, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            loc = _page.Locator(selector);
            if (nthIndex.HasValue)
                loc = loc.Nth(nthIndex.Value);
        }

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
        int? clickTimeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        var clickTimeout = clickTimeoutMs ?? 5_000;
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
                await clickTarget.ClickAsync(new LocatorClickOptions { Timeout = clickTimeout }).ConfigureAwait(false);
            }, new PageRunAndWaitForDownloadOptions { Timeout = timeoutMs }).ConfigureAwait(false);

            await download.SaveAsAsync(tmp).ConfigureAwait(false);
            if (File.Exists(savePath))
                File.Delete(savePath);
            File.Move(tmp, savePath);
            return savePath;
        }
        catch (Exception ex)
        {
            DownloadErrorInvoiceXml.Write(savePath, ex.Message);
            throw;
        }
        finally
        {
            if (File.Exists(tmp))
                try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    public async Task<int> CountAsync(string selector, CancellationToken cancellationToken = default) =>
        await _page.Locator(selector).CountAsync().ConfigureAwait(false);

    private static readonly Regex PaginationRangeRegex = new(
        @"(\d+)\s*[-–—]\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<int> CountDataRowsAsync(string selector, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fromPagination = await TryGetCurrentPageRowCountFromPaginationAsync(cancellationToken).ConfigureAwait(false);
        if (fromPagination is > 0)
        {
            _logger?.LogDebug("CountDataRows: pagination={Pagination}", fromPagination.Value);
            return fromPagination.Value;
        }

        var visibleCount = await CountVisibleDataTableRowsAsync(NormalizeDataRowSelector(selector), cancellationToken)
            .ConfigureAwait(false);
        if (visibleCount == 0)
        {
            visibleCount = await CountVisibleDataTableRowsAsync(".ant-table-tbody > tr", cancellationToken)
                .ConfigureAwait(false);
        }

        _logger?.LogDebug("CountDataRows: visible={Visible}", visibleCount);
        return visibleCount;
    }

    private async Task<int> CountVisibleDataTableRowsAsync(string rowSelector, CancellationToken cancellationToken)
    {
        var rows = _page.Locator(rowSelector);
        var count = await rows.CountAsync().ConfigureAwait(false);
        if (count == 0)
            return 0;

        // Ant Design: measure row is usually first; data rows follow.
        var visible = 0;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows.Nth(i);
            var cls = await row.GetAttributeAsync("class").ConfigureAwait(false) ?? "";
            if (cls.Contains("ant-table-measure-row", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("ant-table-placeholder", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("ant-table-expanded-row", StringComparison.OrdinalIgnoreCase))
                continue;

            if (await row.IsVisibleAsync().ConfigureAwait(false))
                visible++;
        }

        return visible;
    }

    private static async Task<bool> IsDataTableRowAsync(ILocator row)
    {
        if (!await row.IsVisibleAsync().ConfigureAwait(false))
            return false;

        var cls = await row.GetAttributeAsync("class").ConfigureAwait(false) ?? "";
        if (cls.Contains("ant-table-measure-row", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("ant-table-placeholder", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("ant-table-expanded-row", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private async Task<int?> TryGetCurrentPageRowCountFromPaginationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var sel in new[]
        {
            ".ant-table-pagination .ant-pagination-total-text",
            ".ant-pagination-total-text",
            ".ant-pagination .ant-pagination-total-text"
        })
        {
            var loc = _page.Locator(sel);
            if (await loc.CountAsync().ConfigureAwait(false) == 0)
                continue;

            var text = (await loc.First.InnerTextAsync().ConfigureAwait(false)).Trim();
            var match = PaginationRangeRegex.Match(text);
            if (!match.Success)
                continue;

            if (!int.TryParse(match.Groups[1].Value, out var start)
                || !int.TryParse(match.Groups[2].Value, out var end)
                || end < start)
                continue;

            return end - start + 1;
        }

        return null;
    }

    private static bool TryParseDataRowIndex(string selector, int? nthIndex, out int dataRowIndex)
    {
        dataRowIndex = nthIndex ?? 0;
        if (nthIndex.HasValue)
            return true;

        var match = Regex.Match(selector, @">>\s*nth=(\d+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;

        return int.TryParse(match.Groups[1].Value, out dataRowIndex);
    }

    private async Task<ILocator> ResolvePopulatedDataRowLocatorAsync(
        string selector,
        int dataRowZeroBased,
        CancellationToken cancellationToken)
    {
        var baseSelector = NormalizeDataRowSelector(PlaywrightNthChainRegex.Replace(selector, "").Trim());
        var rows = _page.Locator(baseSelector);

        // Fast path: data rows are contiguous at the top of tbody on GDT.
        var direct = rows.Nth(dataRowZeroBased);
        if (await IsDataTableRowAsync(direct).ConfigureAwait(false))
            return direct;

        var count = await rows.CountAsync().ConfigureAwait(false);
        var seen = 0;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows.Nth(i);
            if (!await IsDataTableRowAsync(row).ConfigureAwait(false))
                continue;

            if (seen == dataRowZeroBased)
                return row;

            seen++;
        }

        throw new TimeoutException(
            $"Data row index {dataRowZeroBased} not found ({seen} visible data row(s) in table).");
    }

    internal static string NormalizeDataRowSelector(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return ".ant-table-tbody > tr:not(.ant-table-measure-row):not(.ant-table-placeholder)";

        if (selector.Contains("ant-table-row", StringComparison.OrdinalIgnoreCase))
            return selector.Replace(
                ".ant-table-tbody > tr.ant-table-row",
                ".ant-table-tbody > tr:not(.ant-table-measure-row):not(.ant-table-placeholder)",
                StringComparison.OrdinalIgnoreCase);

        if (selector.Contains("ant-table-tbody", StringComparison.OrdinalIgnoreCase)
            && selector.Contains(" tr", StringComparison.OrdinalIgnoreCase))
        {
            return selector.Replace(
                ".ant-table-tbody tr",
                ".ant-table-tbody > tr:not(.ant-table-measure-row):not(.ant-table-placeholder)",
                StringComparison.OrdinalIgnoreCase);
        }

        return selector;
    }

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

    public async Task SelectAntDesignMaxOptionAsync(
        string comboboxSelector,
        string? dropdownItemSelector,
        int? timeoutMs,
        int? nthIndex = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = timeoutMs ?? 15_000;
        var combo = await ResolvePageSizeComboboxAsync(comboboxSelector, nthIndex, timeout, cancellationToken)
            .ConfigureAwait(false);

        var currentValue = await TryReadSelectedNumericValueAsync(combo).ConfigureAwait(false);
        await combo.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
        await combo.ClickAsync(new LocatorClickOptions { Timeout = timeout, Force = true }).ConfigureAwait(false);

        var items = await WaitForActiveDropdownItemsAsync(dropdownItemSelector, timeout, cancellationToken)
            .ConfigureAwait(false);

        var (maxValue, maxIndex) = await FindMaxNumericOptionIndexAsync(items).ConfigureAwait(false);
        if (maxIndex < 0)
            throw new InvalidOperationException("Ant Design dropdown has no numeric page-size options.");

        if (currentValue == maxValue)
        {
            _logger?.LogInformation("Page size already at max ({Value}); skipping.", maxValue);
            await _page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
            return;
        }

        _logger?.LogInformation("Ant Select page size: {Current} → {Max} (index {Index})", currentValue, maxValue, maxIndex);
        await items.Nth(maxIndex).ClickAsync(new LocatorClickOptions { Timeout = timeout, Force = true }).ConfigureAwait(false);
    }

    private async Task<ILocator> ResolvePageSizeComboboxAsync(
        string primarySelector,
        int? nthIndex,
        int timeout,
        CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(primarySelector))
            candidates.Add(primarySelector.Trim());
        candidates.AddRange(
        [
            ".ant-tabs-tabpane-active div.ant-row-flex-space-between [role='combobox']",
            ".ant-pagination-options-size-changer .ant-select-selection[role='combobox']",
            ".ant-pagination-options-size-changer .ant-select-selection",
            ".ant-pagination-options-size-changer [role='combobox']",
            ".ant-pagination .ant-select-selection[role='combobox']",
            ".ant-table-pagination .ant-select-selection[role='combobox']"
        ]);

        foreach (var selector in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loc = _page.Locator(selector);
            var count = await loc.CountAsync().ConfigureAwait(false);
            if (count == 0)
                continue;

            var index = nthIndex ?? count - 1;
            if (index < 0 || index >= count)
                index = count - 1;

            var target = loc.Nth(index);
            try
            {
                await target.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = timeout
                }).ConfigureAwait(false);
                _logger?.LogInformation("Page size combobox resolved: '{Selector}' index {Index}/{Count}", selector, index, count);
                return target;
            }
            catch (TimeoutException)
            {
                _logger?.LogDebug("Page size combobox not visible for selector '{Selector}'", selector);
            }
        }

        throw new InvalidOperationException(
            $"Page size combobox not found. Tried: {string.Join(", ", candidates.Distinct(StringComparer.OrdinalIgnoreCase))}");
    }

    private static async Task<int?> TryReadSelectedNumericValueAsync(ILocator combo)
    {
        try
        {
            var selected = combo.Locator(".ant-select-selection-selected-value");
            if (await selected.CountAsync().ConfigureAwait(false) > 0)
            {
                var text = (await selected.First.InnerTextAsync().ConfigureAwait(false)).Trim();
                if (int.TryParse(text, out var n))
                    return n;
            }

            var fallback = (await combo.InnerTextAsync().ConfigureAwait(false)).Trim();
            return int.TryParse(fallback, out var parsed) ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<ILocator> WaitForActiveDropdownItemsAsync(
        string? dropdownItemSelector,
        int timeout,
        CancellationToken cancellationToken)
    {
        var customItemSelector = string.IsNullOrWhiteSpace(dropdownItemSelector)
            ? null
            : dropdownItemSelector.Trim();
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dropdowns = _page.Locator(".ant-select-dropdown:not(.ant-select-dropdown-hidden)");
            var dropdownCount = await dropdowns.CountAsync().ConfigureAwait(false);
            for (var d = dropdownCount - 1; d >= 0; d--)
            {
                var dropdown = dropdowns.Nth(d);
                var items = dropdown.Locator(".ant-select-dropdown-menu-item, .ant-select-item-option, .ant-select-item");

                var itemCount = await items.CountAsync().ConfigureAwait(false);
                if (itemCount == 0)
                    continue;

                var (_, maxIndex) = await FindMaxNumericOptionIndexAsync(items).ConfigureAwait(false);
                if (maxIndex >= 0)
                    return items;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Ant Design page-size dropdown did not open or has no numeric options.");
    }

    private static async Task<(int MaxValue, int MaxIndex)> FindMaxNumericOptionIndexAsync(ILocator items)
    {
        var count = await items.CountAsync().ConfigureAwait(false);
        var maxValue = -1;
        var maxIndex = -1;
        for (var i = 0; i < count; i++)
        {
            var text = (await items.Nth(i).InnerTextAsync().ConfigureAwait(false)).Trim();
            if (int.TryParse(text, out var n) && n > maxValue)
            {
                maxValue = n;
                maxIndex = i;
            }
        }

        return (maxValue, maxIndex);
    }

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

    public async Task<bool> TryClickPaginationNextAsync(string? selector, int? timeoutMs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clickTimeout = Math.Min(timeoutMs ?? 4_000, 6_000);
        var waitTimeout = 5_000;

        if (await IsPaginationOnLastPageAsync(selector).ConfigureAwait(false))
        {
            _logger?.LogInformation("Pagination: already on last page.");
            return false;
        }

        var pageBefore = await GetActivePaginationPageAsync().ConfigureAwait(false);
        var fingerprintBefore = await GetTablePageFingerprintAsync().ConfigureAwait(false);
        var candidates = BuildPaginationNextSelectors(selector);

        foreach (var sel in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var btn = _page.Locator(sel).First;
            if (await btn.CountAsync().ConfigureAwait(false) == 0)
                continue;

            if (!await IsEnabledPaginationNextButtonAsync(btn).ConfigureAwait(false))
                continue;

            _logger?.LogInformation("Pagination: clicking next page via {Selector}", sel);
            try
            {
                await btn.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
                await btn.ClickAsync(new LocatorClickOptions { Timeout = clickTimeout }).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                _logger?.LogDebug(ex, "Pagination: click timed out for {Selector}", sel);
                continue;
            }

            if (await WaitForTablePageChangeAsync(fingerprintBefore, pageBefore, waitTimeout, cancellationToken)
                    .ConfigureAwait(false))
                return true;

            _logger?.LogWarning("Pagination: click on {Selector} did not advance the table.", sel);
        }

        _logger?.LogInformation("Pagination: next page button not available (last page).");
        return false;
    }

    private async Task<bool> IsPaginationOnLastPageAsync(string? preferredSelector = null)
    {
        foreach (var sel in BuildPaginationNextSelectors(preferredSelector).Take(3))
        {
            var btn = _page.Locator(sel).First;
            if (await btn.CountAsync().ConfigureAwait(false) == 0)
                continue;

            if (await IsEnabledPaginationNextButtonAsync(btn).ConfigureAwait(false))
                return false;
        }

        return true;
    }

    private static List<string> BuildPaginationNextSelectors(string? selector)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(selector))
        {
            foreach (var part in selector.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                candidates.Add(part);
        }

        candidates.AddRange(
        [
            ".ant-table-pagination button.ant-btn-primary.ant-btn-icon-only:not([disabled]):has(.anticon-right)",
            "button.ant-btn-primary.ant-btn-icon-only:not([disabled]):has(.anticon-right)",
            ".ant-table-pagination .ant-pagination-next:not(.ant-pagination-disabled) button",
            ".ant-pagination-next:not(.ant-pagination-disabled) button",
            "li.ant-pagination-next:not(.ant-pagination-disabled) button"
        ]);

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<bool> IsEnabledPaginationNextButtonAsync(ILocator btn)
    {
        if (await btn.IsDisabledAsync().ConfigureAwait(false))
            return false;

        var ariaDisabled = await btn.GetAttributeAsync("aria-disabled").ConfigureAwait(false);
        if (string.Equals(ariaDisabled, "true", StringComparison.OrdinalIgnoreCase))
            return false;

        var parentLi = btn.Locator("xpath=ancestor::li[contains(@class,'ant-pagination-next')][1]");
        if (await parentLi.CountAsync().ConfigureAwait(false) > 0)
        {
            var cls = await parentLi.GetAttributeAsync("class").ConfigureAwait(false) ?? "";
            if (cls.Contains("ant-pagination-disabled", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return await btn.IsVisibleAsync().ConfigureAwait(false);
    }

    private async Task<int?> GetActivePaginationPageAsync()
    {
        var activePage = _page.Locator(".ant-pagination-item-active");
        if (await activePage.CountAsync().ConfigureAwait(false) == 0)
            return null;

        var text = (await activePage.First.InnerTextAsync().ConfigureAwait(false)).Trim();
        return int.TryParse(text, out var page) ? page : null;
    }

    private async Task<string> GetTablePageFingerprintAsync()
    {
        var activePage = _page.Locator(".ant-pagination-item-active");
        if (await activePage.CountAsync().ConfigureAwait(false) > 0)
        {
            var pageNo = (await activePage.First.InnerTextAsync().ConfigureAwait(false)).Trim();
            if (!string.IsNullOrWhiteSpace(pageNo))
                return "page:" + pageNo;
        }

        var firstRow = _page.Locator(NormalizeDataRowSelector(".ant-table-tbody tr")).First;
        if (await firstRow.CountAsync().ConfigureAwait(false) > 0)
            return "row:" + (await firstRow.InnerTextAsync().ConfigureAwait(false)).Trim();

        return "";
    }

    private async Task<bool> WaitForTablePageChangeAsync(
        string fingerprintBefore,
        int? pageBefore,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _page.WaitForTimeoutAsync(150).ConfigureAwait(false);

            var pageAfter = await GetActivePaginationPageAsync().ConfigureAwait(false);
            if (pageBefore.HasValue && pageAfter.HasValue && pageAfter.Value > pageBefore.Value)
                return true;

            var fingerprintAfter = await GetTablePageFingerprintAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(fingerprintBefore)
                && !string.Equals(fingerprintBefore, fingerprintAfter, StringComparison.Ordinal))
                return true;
        }

        return false;
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
