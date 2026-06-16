using InvoiceAutomation.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace InvoiceAutomation.Services;

public sealed class PlaywrightBrowserHost : IAsyncDisposable
{
    public const string GdtHostFragment = "gdt.gov.vn";
    public const string TracuuHostFragment = "tracuuhoadon.vn";
    /// <summary>Only tracuuhoadon PDF API — excludes login.saveinvoice.vn signup links.</summary>
    public const string TracuuResultPdfLinkSelector =
        "a[href*='saveinvoice-pdf']:not([href*='login.saveinvoice'])";
    public const string TracuuLookupLinkSelector =
        ".p-datatable-tbody tr:has(td:text-is('Link tra cứu')) a[href^='http'], " +
        ".p-datatable-table tbody tr:first-child td a[href^='http']";

    public const string TracuuInvoiceActionSelector = ".invoice-action";

    public const string TracuuSearchHintSelector =
        "fieldset.search-hint, fieldset:has(.p-fieldset-legend-text:text-is('Gợi ý tra cứu'))";

    public static readonly string[] TracuuSearchHintRowLabels =
    [
        "Link tra cứu",
        "Mã tra cứu",
        "Link hóa đơn gốc"
    ];

    public static readonly string[] TracuuSearchHintLinkTexts =
    [
        "Click vào đây để xem",
        "Click để xem hóa đơn gốc"
    ];

    /// <summary>First action button on tracuuhoadon result — "In PDF" when no API download link.</summary>
    public static readonly string[] TracuuPrintPdfButtonSelectors =
    [
        ".invoice-action button:first-child",
        "div.invoice-action > div > button:nth-child(1)",
        ".invoice-action .p-button:first-of-type",
        ".invoice-action button:has-text('In PDF')",
        ".invoice-action button:has-text('In hóa đơn')"
    ];

    /// <summary>Viettel vinvoice — user-provided path; prefer parent <c>button</c> over inner <c>span</c>.</summary>
    public static readonly string[] ViettelPdfDownloadSelectors =
    [
        "#table > div.row.d-flex.justify-content-center.pr-1 > button:nth-child(1) > span",
        "#table > div.row.d-flex.justify-content-center.pr-1 > button:nth-child(1)",
        "#table div.row.d-flex.justify-content-center.pr-1 button:nth-child(1)",
        "#table div.row.d-flex.justify-content-center button:first-child"
    ];

    /// <summary>Issuer portals (Viettel vinvoice, etc.) after captcha shows invoice PDF.</summary>
    public static readonly string[] IssuerPdfDownloadSelectors =
    [
        "button:has-text('Tải file PDF')",
        "a:has-text('Tải file PDF')",
        ".p-button:has-text('Tải file PDF')",
        "button:has-text('Tải PDF')",
        "a:has-text('Tải PDF')",
        "button:has-text('Tai file PDF')",
        "a[download]",
        "a[href*='.pdf' i]"
    ];

    private readonly ILogger<PlaywrightBrowserHost>? _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private PlaywrightAutomationPage? _wrapper;

    public IAutomationPage Page => _wrapper ?? throw new InvalidOperationException("Browser not launched.");

    /// <summary>True when Chromium is still connected (user may have closed the window).</summary>
    public bool IsAlive
    {
        get
        {
            try
            {
                return _browser?.IsConnected == true && _context is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    public PlaywrightBrowserHost(ILogger<PlaywrightBrowserHost>? logger = null) => _logger = logger;

    public async Task LaunchAsync(BrowserLaunchSettings settings, CancellationToken cancellationToken = default)
    {
        _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = settings.Headless,
            Channel = string.IsNullOrWhiteSpace(settings.Channel) ? null : settings.Channel
        }).ConfigureAwait(false);

        var contextOptions = new BrowserNewContextOptions
        {
            AcceptDownloads = true
        };
        if (!string.IsNullOrWhiteSpace(settings.StorageStatePath) && File.Exists(settings.StorageStatePath))
            contextOptions.StorageStatePath = settings.StorageStatePath;

        _context = await _browser.NewContextAsync(contextOptions).ConfigureAwait(false);
        _page = await _context.NewPageAsync().ConfigureAwait(false);
        _wrapper = new PlaywrightAutomationPage(_page);

        // When the login flow opens a new tab (or an OAuth redirect replaces the current page),
        // keep _wrapper pointed at the latest active page so subsequent steps don't use a stale reference.
        _context.Page += OnContextPage;
    }

    private void OnContextPage(object? sender, IPage newPage)
    {
        _page = newPage;
        _wrapper?.UpdatePage(newPage);

        // If this newly-created page is later closed (e.g. a login popup that dismisses itself),
        // fall back to the last surviving page in the context.
        newPage.Close += OnPageClose;
    }

    private void OnPageClose(object? sender, IPage closedPage)
    {
        closedPage.Close -= OnPageClose;

        var remaining = _context?.Pages.Where(p => !p.IsClosed).ToList();
        if (remaining is { Count: > 0 })
        {
            _page = remaining[^1];
            _wrapper?.UpdatePage(_page);
            _logger?.LogInformation("Tab closed; switched to {Url}", _page.Url);
        }
        else
        {
            _page = null;
            _logger?.LogWarning("All browser tabs were closed.");
        }
    }

    /// <summary>Ensure <see cref="_page"/> points at an open tab, or create one.</summary>
    public async Task EnsureActivePageAsync(CancellationToken cancellationToken = default)
    {
        if (_context is null || _browser?.IsConnected != true)
            throw new InvalidOperationException("Browser not launched or has been closed.");

        if (_page is not null && !_page.IsClosed)
            return;

        var alive = _context.Pages.Where(p => !p.IsClosed).ToList();
        if (alive.Count > 0)
        {
            _page = alive[^1];
            _wrapper?.UpdatePage(_page);
            _logger?.LogInformation("Recovered active tab: {Url}", _page.Url);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _page = await _context.NewPageAsync().ConfigureAwait(false);
        _wrapper?.UpdatePage(_page);
        _logger?.LogInformation("Opened new tab (no tabs were left).");
    }

    /// <summary>
    /// Points automation at the GDT tab (prefers tra-cuu search page).
    /// Does not navigate — keeps the month/filter the user already selected.
    /// </summary>
    public async Task FocusGdtTabForAutomationAsync(CancellationToken cancellationToken = default)
    {
        if (_context is null || _browser?.IsConnected != true)
            throw new InvalidOperationException("Browser not launched or has been closed.");

        var target = FindBestGdtPage();
        if (target is null)
        {
            throw new InvalidOperationException(
                "No GDT tab is open. Click 'Open GDT', log in, run your search, then start Download.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await target.BringToFrontAsync().ConfigureAwait(false);
        _page = target;
        _wrapper?.UpdatePage(target);
        _logger?.LogInformation("Automation focused on GDT tab: {Url}", target.Url);
    }

    public IPage? FindBestGdtPage()
    {
        if (_context is null)
            return null;

        var gdtPages = _context.Pages
            .Where(p => !p.IsClosed && (p.Url ?? "").Contains(GdtHostFragment, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (gdtPages.Count == 0)
            return null;

        return gdtPages.FirstOrDefault(p =>
                   (p.Url ?? "").Contains("tra-cuu", StringComparison.OrdinalIgnoreCase))
               ?? gdtPages[^1];
    }

    public IPage? FindBestTracuuPage()
    {
        if (_context is null)
            return null;

        var pages = _context.Pages
            .Where(p => !p.IsClosed && (p.Url ?? "").Contains(TracuuHostFragment, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pages.Count == 0)
            return null;

        return pages.FirstOrDefault(p =>
                   (p.Url ?? "").Contains("saveinvoice-pdf", StringComparison.OrdinalIgnoreCase))
               ?? pages[^1];
    }

    /// <summary>tracuuhoadon result page with datatable (not API PDF tab).</summary>
    public IPage? FindTracuuResultPage()
    {
        if (_context is null)
            return null;

        var pages = _context.Pages
            .Where(p => !p.IsClosed && (p.Url ?? "").Contains(TracuuHostFragment, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pages.Count == 0)
            return null;

        return pages.LastOrDefault(p =>
        {
            var url = p.Url ?? "";
            return IsTracuuMainPortalTab(url);
        }) ?? pages[^1];
    }

    internal static bool IsTracuuAuxiliaryTab(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!url.Contains(TracuuHostFragment, StringComparison.OrdinalIgnoreCase))
            return false;

        return IsTracuuInvoiceHtmlHref(url)
               || IsTracuuPdfApiHref(url)
               || url.Contains("upload_invoice_xml-html", StringComparison.OrdinalIgnoreCase)
               || url.Contains("/api/", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTracuuMainPortalTab(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.Contains(TracuuHostFragment, StringComparison.OrdinalIgnoreCase)
        && !IsTracuuAuxiliaryTab(url);

    /// <summary>Closes print/HTML/PDF API tabs opened on tracuuhoadon; keeps main portal tab.</summary>
    public async Task CloseTracuuAuxiliaryTabsAsync(CancellationToken cancellationToken = default)
    {
        if (_context is null)
            return;

        foreach (var page in _context.Pages.Where(p => !p.IsClosed).ToList())
        {
            if (!IsTracuuAuxiliaryTab(page.Url))
                continue;

            try
            {
                _logger?.LogInformation("Closing tracuuhoadon auxiliary tab: {Url}", page.Url);
                await page.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to close auxiliary tab {Url}", page.Url);
            }
        }

        var main = FindTracuuResultPage();
        if (main is { IsClosed: false })
            await BringPageToFrontAsync(main, "tracuuhoadon", cancellationToken).ConfigureAwait(false);
    }

    public async Task FocusTracuuTabForAutomationAsync(CancellationToken cancellationToken = default)
    {
        if (_context is null || _browser?.IsConnected != true)
            throw new InvalidOperationException("Browser not launched or has been closed.");

        var target = FindTracuuResultPage() ?? FindBestTracuuPage();
        if (target is null)
            throw new InvalidOperationException("No tracuuhoadon.vn tab is open.");

        await BringPageToFrontAsync(target, "tracuuhoadon", cancellationToken).ConfigureAwait(false);
    }

    private async Task BringPageToFrontAsync(IPage page, string label, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await page.BringToFrontAsync().ConfigureAwait(false);
        _page = page;
        _wrapper?.UpdatePage(page);
        _logger?.LogInformation("Automation focused on {Label} tab: {Url}", label, page.Url);
    }

    /// <summary>
    /// After XML upload: click "Link tra cứu" when present in the result datatable (opens issuer portal).
    /// Returns null when the table has no lookup link — caller should do nothing.
    /// </summary>
    public async Task<string?> TryClickTracuuLookupLinkAsync(CancellationToken cancellationToken = default)
    {
        if (_context is null)
            throw new InvalidOperationException("Browser not launched.");

        await FocusTracuuTabForAutomationAsync(cancellationToken).ConfigureAwait(false);
        var page = _page!;

        var link = page.Locator(TracuuLookupLinkSelector).First;
        try
        {
            await link.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 30_000
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger?.LogInformation("tracuuhoadon: no Link tra cứu in result table.");
            return null;
        }

        var href = await link.GetAttributeAsync("href").ConfigureAwait(false);
        _logger?.LogInformation("tracuuhoadon lookup link: {Href}", href);

        await link.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);

        var newPageTask = _context.WaitForPageAsync(new BrowserContextWaitForPageOptions { Timeout = 15_000 });
        await link.ClickAsync(new LocatorClickOptions { Timeout = 30_000 }).ConfigureAwait(false);

        IPage targetPage;
        try
        {
            targetPage = await newPageTask.ConfigureAwait(false);
            await targetPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded).ConfigureAwait(false);
            _logger?.LogInformation("Lookup link opened new tab: {Url}", targetPage.Url);
        }
        catch (TimeoutException)
        {
            targetPage = page;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Lookup link opened same tab: {Url}", targetPage.Url);
        }

        await targetPage.BringToFrontAsync().ConfigureAwait(false);
        _page = targetPage;
        _wrapper?.UpdatePage(targetPage);
        return href;
    }

    /// <summary>
    /// Clicks tracuuhoadon <c>/api/invoices/.../saveinvoice-pdf</c> when present; skips login.saveinvoice.vn links.
    /// </summary>
    public async Task<string?> TryOpenTracuuPdfResultLinkAsync(CancellationToken cancellationToken = default)
    {
        if (_context is null)
            throw new InvalidOperationException("Browser not launched.");

        // Keep issuer lookup tab in front while user finished captcha; query tracuuhoadon in background.
        var issuerPage = _page is { IsClosed: false } ? _page : null;
        var tracuuPage = FindTracuuResultPage() ?? FindBestTracuuPage();
        if (tracuuPage is null)
        {
            _logger?.LogInformation("No tracuuhoadon tab — staying on issuer lookup tab.");
            return null;
        }

        var link = await FindTracuuPdfApiLinkAsync(tracuuPage).ConfigureAwait(false);
        if (link is null)
        {
            _logger?.LogInformation("No tracuuhoadon saveinvoice-pdf API link — using issuer lookup tab only.");
            if (issuerPage is not null)
                await BringPageToFrontAsync(issuerPage, "issuer lookup", cancellationToken).ConfigureAwait(false);
            return null;
        }

        var href = await link.GetAttributeAsync("href").ConfigureAwait(false);
        if (!IsTracuuPdfApiHref(href))
        {
            if (issuerPage is not null)
                await BringPageToFrontAsync(issuerPage, "issuer lookup", cancellationToken).ConfigureAwait(false);
            return null;
        }

        _logger?.LogInformation("tracuuhoadon PDF API link: {Href}", href);

        await link.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
        await link.ClickAsync(new LocatorClickOptions { Timeout = 30_000 }).ConfigureAwait(false);
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        var target = FindBestTracuuPage();
        if (target is not null)
            await BringPageToFrontAsync(target, "tracuuhoadon PDF", cancellationToken).ConfigureAwait(false);

        return href;
    }

    /// <summary>After XML upload: detect saveinvoice-pdf link vs print-only button on tracuuhoadon.</summary>
    public async Task<TracuuInvoiceActionInfo> DetectTracuuInvoiceActionAsync(CancellationToken cancellationToken = default)
    {
        if (_context is null)
            throw new InvalidOperationException("Browser not launched.");

        await FocusTracuuTabForAutomationAsync(cancellationToken).ConfigureAwait(false);
        var page = _page!;

        try
        {
            await page.Locator($"{TracuuInvoiceActionSelector}, .p-datatable, {TracuuResultPdfLinkSelector}")
                .First.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 60_000
                }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger?.LogInformation("tracuuhoadon: result UI not visible within 60s after upload.");
        }

        var pdfLink = await FindTracuuPdfApiLinkAsync(page).ConfigureAwait(false);
        var href = pdfLink is not null ? await pdfLink.GetAttributeAsync("href").ConfigureAwait(false) : null;
        var (printBtn, printLabel) = await FindTracuuPrintPdfButtonAsync(page).ConfigureAwait(false);
        var hasSearchHint = await DetectTracuuSearchHintAsync(page).ConfigureAwait(false);

        var info = new TracuuInvoiceActionInfo
        {
            HasDirectPdfLink = pdfLink is not null,
            PdfLinkHref = href,
            HasPrintPdfButton = printBtn is not null,
            PrintButtonLabel = printLabel,
            HasSearchHint = hasSearchHint
        };

        _logger?.LogInformation(
            "tracuuhoadon action: searchHint={SearchHint}, directLink={DirectLink}, printButton={PrintButton} ({Label})",
            info.HasSearchHint, info.HasDirectPdfLink, info.HasPrintPdfButton, info.PrintButtonLabel ?? "(none)");

        return info;
    }

    private static async Task<bool> DetectTracuuSearchHintAsync(IPage page)
    {
        var hint = page.Locator(TracuuSearchHintSelector).First;
        if (await hint.CountAsync().ConfigureAwait(false) > 0)
        {
            if (await hint.Locator(".p-datatable").CountAsync().ConfigureAwait(false) > 0)
                return true;

            foreach (var label in TracuuSearchHintRowLabels)
            {
                if (await hint.Locator($"td:text-is('{label}')").CountAsync().ConfigureAwait(false) > 0)
                    return true;
            }

            foreach (var text in TracuuSearchHintLinkTexts)
            {
                if (await hint.Locator($"a:text-is('{text}')").CountAsync().ConfigureAwait(false) > 0)
                    return true;

                if (await hint.GetByText(text, new LocatorGetByTextOptions { Exact = false }).CountAsync()
                        .ConfigureAwait(false) > 0)
                    return true;
            }
        }

        var datatable = page.Locator(".p-datatable-table").First;
        return await datatable.CountAsync().ConfigureAwait(false) > 0
               && await datatable.IsVisibleAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Clicks "In PDF", waits for <c>/api/upload_invoice_xml-html/</c> tab, saves via <see cref="IPage.PdfAsync"/>.
    /// Bypasses the native print dialog — no browser download event needed.
    /// </summary>
    public async Task<string?> TryCaptureTracuuPrintPdfAsync(
        string pdfSavePath,
        CancellationToken cancellationToken = default)
    {
        if (_context is null)
            throw new InvalidOperationException("Browser not launched.");

        try
        {
            return await CaptureTracuuPrintPdfCoreAsync(pdfSavePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await CloseTracuuAuxiliaryTabsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> CaptureTracuuPrintPdfCoreAsync(
        string pdfSavePath,
        CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(pdfSavePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var page = FindTracuuResultPage() ?? _page;
        if (page is null || page.IsClosed)
            return null;

        var htmlUrl = await TryFindTracuuInvoiceHtmlHrefAsync(page).ConfigureAwait(false)
            ?? FindTracuuInvoiceHtmlPage()?.Url;

        if (string.IsNullOrWhiteSpace(htmlUrl))
        {
            var (printBtn, label) = await FindTracuuPrintPdfButtonAsync(page).ConfigureAwait(false);
            if (printBtn is null)
                return null;

            _logger?.LogInformation("tracuuhoadon: clicking print button ({Label})", label ?? "In PDF");

            var newPageTask = _context.WaitForPageAsync(new BrowserContextWaitForPageOptions { Timeout = 20_000 });
            await printBtn.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
            await printBtn.ClickAsync(new LocatorClickOptions { Timeout = 30_000 }).ConfigureAwait(false);

            IPage? printTab = null;
            try
            {
                printTab = await newPageTask.ConfigureAwait(false);
                _logger?.LogInformation("tracuuhoadon print opened new tab: {Url}", printTab.Url);
            }
            catch (TimeoutException)
            {
                printTab = FindTracuuInvoiceHtmlPage();
                if (printTab is null && !page.IsClosed && IsTracuuInvoiceHtmlHref(page.Url))
                    printTab = page;
            }

            htmlUrl = printTab?.Url;
            if (string.IsNullOrWhiteSpace(htmlUrl))
            {
                _logger?.LogWarning("tracuuhoadon: no invoice HTML URL after print click.");
                return null;
            }
        }

        if (IsTracuuPdfApiHref(htmlUrl) || htmlUrl.Contains(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return await SaveTracuuPdfAsync(pdfSavePath, htmlUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "tracuuhoadon print tab session fetch failed");
            }
        }

        if (IsTracuuInvoiceHtmlHref(htmlUrl))
        {
            return await SaveTracuuInvoiceHtmlUrlAsPdfAsync(htmlUrl, pdfSavePath, cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    public IPage? FindTracuuInvoiceHtmlPage()
    {
        if (_context is null)
            return null;

        return _context.Pages
            .Where(p => !p.IsClosed && IsTracuuInvoiceHtmlHref(p.Url))
            .LastOrDefault();
    }

    internal static bool IsTracuuInvoiceHtmlHref(string? href) =>
        !string.IsNullOrWhiteSpace(href)
        && href.Contains(TracuuHostFragment, StringComparison.OrdinalIgnoreCase)
        && href.Contains("upload_invoice_xml-html", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Opens invoice HTML in a fresh tab with <c>window.print</c> disabled (print dialog blocks CDP printToPDF).
    /// </summary>
    public async Task<string?> SaveTracuuInvoiceHtmlUrlAsPdfAsync(
        string invoiceHtmlUrl,
        string pdfSavePath,
        CancellationToken cancellationToken = default)
    {
        if (_context is null || !IsTracuuInvoiceHtmlHref(invoiceHtmlUrl))
            return null;

        IPage? cleanPage = null;
        try
        {
            cleanPage = await _context.NewPageAsync().ConfigureAwait(false);
            await cleanPage.AddInitScriptAsync(
                "window.print = () => {};" +
                "addEventListener('DOMContentLoaded', () => { window.print = () => {}; }, { once: true });" +
                "addEventListener('load', () => { window.print = () => {}; }, { once: true });")
                .ConfigureAwait(false);

            _logger?.LogInformation("tracuuhoadon: loading invoice HTML in clean tab (no print dialog): {Url}", invoiceHtmlUrl);
            await cleanPage.GotoAsync(invoiceHtmlUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60_000
            }).ConfigureAwait(false);

            return await RenderTracuuInvoiceHtmlPageAsPdfAsync(cleanPage, pdfSavePath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "tracuuhoadon clean HTML tab failed for {Url}", invoiceHtmlUrl);
            return null;
        }
        finally
        {
            if (cleanPage is { IsClosed: false })
            {
                try { await cleanPage.CloseAsync().ConfigureAwait(false); }
                catch { /* tab may already be closed */ }
            }
        }
    }

    private async Task<string?> RenderTracuuInvoiceHtmlPageAsPdfAsync(
        IPage page,
        string pdfSavePath,
        CancellationToken cancellationToken)
    {
        if (page.IsClosed)
            return null;

        try
        {
            await page.Locator("text=HÓA ĐƠN").First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 20_000
            }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger?.LogInformation("tracuuhoadon HTML tab: invoice heading not found — trying PdfAsync anyway.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var tmp = pdfSavePath + ".tmp";
        try
        {
            _logger?.LogInformation("tracuuhoadon: rendering HTML to PDF via page.PdfAsync ({Url})", page.Url);
            var bytes = await TryPagePdfAsync(page).ConfigureAwait(false);
            if (bytes is null || !LooksLikePdfBytes(bytes))
            {
                _logger?.LogWarning("tracuuhoadon page.PdfAsync returned no valid PDF bytes.");
                return null;
            }

            await File.WriteAllBytesAsync(tmp, bytes, cancellationToken).ConfigureAwait(false);
            if (File.Exists(pdfSavePath))
                File.Delete(pdfSavePath);
            File.Move(tmp, pdfSavePath);
            _logger?.LogInformation("tracuuhoadon HTML tab saved as PDF: {Path}", pdfSavePath);
            return pdfSavePath;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "tracuuhoadon page.PdfAsync failed for {Url}", page.Url);
            if (File.Exists(tmp))
                File.Delete(tmp);
            return null;
        }
    }

    private async Task<byte[]?> TryPagePdfAsync(IPage page)
    {
        var attempts = new[]
        {
            new PagePdfOptions
            {
                Format = "A4",
                PrintBackground = true,
                PreferCSSPageSize = true,
                Margin = new Margin { Top = "0", Right = "0", Bottom = "0", Left = "0" }
            },
            new PagePdfOptions
            {
                Format = "A4",
                PrintBackground = true
            }
        };

        foreach (var options in attempts)
        {
            try
            {
                return await page.PdfAsync(options).ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("printToPDF", StringComparison.OrdinalIgnoreCase)
                                                || ex.Message.Contains("Printing failed", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogWarning(ex, "tracuuhoadon page.PdfAsync attempt failed — retrying with simpler options.");
            }
        }

        return null;
    }

    private static async Task<string?> TryFindTracuuInvoiceHtmlHrefAsync(IPage page)
    {
        var baseUrl = page.Url ?? "";
        var candidates = page.Locator("a[href*='upload_invoice_xml-html'], [href*='upload_invoice_xml-html']");
        var count = await candidates.CountAsync().ConfigureAwait(false);
        for (var i = 0; i < count; i++)
        {
            var href = await candidates.Nth(i).GetAttributeAsync("href").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(href))
                continue;

            var absolute = ToAbsoluteUrl(baseUrl, href);
            if (IsTracuuInvoiceHtmlHref(absolute))
                return absolute;
        }

        return null;
    }

    private static async Task<(ILocator? Button, string? Label)> FindTracuuPrintPdfButtonAsync(IPage page)
    {
        foreach (var selector in TracuuPrintPdfButtonSelectors)
        {
            var btn = page.Locator(selector).First;
            if (await btn.CountAsync().ConfigureAwait(false) == 0)
                continue;

            try
            {
                await btn.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 2_000
                }).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                continue;
            }

            var label = (await btn.InnerTextAsync().ConfigureAwait(false))?.Trim();
            if (string.IsNullOrWhiteSpace(label))
                label = await btn.GetAttributeAsync("aria-label").ConfigureAwait(false);

            if (IsDownloadLabel(label))
                continue;

            if (IsPrintPdfLabel(label) || selector.Contains("invoice-action", StringComparison.OrdinalIgnoreCase))
                return (btn, label);
        }

        return (null, null);
    }

    private static bool IsPrintPdfLabel(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && (text.Contains("In PDF", StringComparison.OrdinalIgnoreCase)
            || text.Contains("In hóa đơn", StringComparison.OrdinalIgnoreCase)
            || text.Contains("In HĐ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Print", StringComparison.OrdinalIgnoreCase));

    private static bool IsDownloadLabel(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && (text.Contains("Tải", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Download", StringComparison.OrdinalIgnoreCase));

    public static readonly TimeSpan ManualPdfDownloadTimeout = TimeSpan.FromMinutes(10);

    private readonly object _pdfDownloadWaitGate = new();
    private CancellationTokenSource? _pdfDownloadWaitCts;

    /// <summary>Stops an in-flight <see cref="WaitForManualPdfDownloadAsync"/> (e.g. user double-clicked another row).</summary>
    public void CancelManualPdfDownloadWait()
    {
        lock (_pdfDownloadWaitGate)
        {
            _pdfDownloadWaitCts?.Cancel();
        }
    }

    /// <summary>
    /// Waits for the user to trigger a PDF download on any open browser tab (tracuuhoadon, Viettel, etc.).
    /// Playwright intercepts the download and saves to <paramref name="pdfSavePath"/>.
    /// </summary>
    public async Task<string?> WaitForManualPdfDownloadAsync(
        string pdfSavePath,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (_context is null)
            throw new InvalidOperationException("Browser not launched.");

        await EnsureActivePageAsync(cancellationToken).ConfigureAwait(false);

        var dir = Path.GetDirectoryName(pdfSavePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var waitFor = timeout ?? ManualPdfDownloadTimeout;
        var timeoutMs = (int)Math.Clamp(waitFor.TotalMilliseconds, 1_000, 600_000);
        var tmp = pdfSavePath + ".tmp";

        _logger?.LogInformation(
            "Waiting up to {Seconds}s for manual PDF download on any browser tab.",
            waitFor.TotalSeconds);

        var download = await WaitForDownloadOnAnyPageAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
        if (download is null)
        {
            if (cancellationToken.IsCancellationRequested)
                _logger?.LogInformation("Manual PDF download wait cancelled.");
            else
                _logger?.LogWarning("Timed out waiting for manual PDF download.");
            return null;
        }

        await download.SaveAsAsync(tmp).ConfigureAwait(false);
        _logger?.LogInformation("Manual download captured: {Filename}", download.SuggestedFilename);

        if (CommitTempManualDownloadIfValid(tmp, pdfSavePath, download.SuggestedFilename, out var savedPath))
            return savedPath;

        _logger?.LogWarning("Manual download is not a supported PDF/ZIP file: {Filename}", download.SuggestedFilename);
        return null;
    }

    private async Task<IDownload?> WaitForDownloadOnAnyPageAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        CancellationTokenSource linkedCts;
        lock (_pdfDownloadWaitGate)
        {
            _pdfDownloadWaitCts?.Cancel();
            _pdfDownloadWaitCts?.Dispose();
            _pdfDownloadWaitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts = _pdfDownloadWaitCts;
        }

        var token = linkedCts.Token;
        var downloadTcs = new TaskCompletionSource<IDownload>(TaskCreationOptions.RunContinuationsAsynchronously);
        var armedPages = new HashSet<IPage>();
        var armGate = new object();

        void ArmPage(IPage page)
        {
            if (page.IsClosed || token.IsCancellationRequested)
                return;

            lock (armGate)
            {
                if (!armedPages.Add(page))
                    return;
            }

            _ = ArmPageDownloadAsync(page, timeoutMs, token, downloadTcs);
        }

        void OnNewPage(object? _, IPage page) => ArmPage(page);

        _context!.Page += OnNewPage;
        foreach (var page in _context.Pages.Where(p => !p.IsClosed).ToList())
            ArmPage(page);

        try
        {
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedTimeout = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            using (linkedTimeout.Token.Register(static state =>
            {
                var tcs = (TaskCompletionSource<IDownload>)state!;
                tcs.TrySetCanceled();
            }, downloadTcs))
            {
                try
                {
                    return await downloadTcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }
        finally
        {
            _context.Page -= OnNewPage;
            lock (_pdfDownloadWaitGate)
            {
                if (ReferenceEquals(_pdfDownloadWaitCts, linkedCts))
                {
                    _pdfDownloadWaitCts.Dispose();
                    _pdfDownloadWaitCts = null;
                }
            }
        }
    }

    private static async Task ArmPageDownloadAsync(
        IPage page,
        int timeoutMs,
        CancellationToken cancellationToken,
        TaskCompletionSource<IDownload> downloadTcs)
    {
        try
        {
            var download = await page.WaitForDownloadAsync(new PageWaitForDownloadOptions
            {
                Timeout = timeoutMs
            }).ConfigureAwait(false);

            if (!cancellationToken.IsCancellationRequested)
                downloadTcs.TrySetResult(download);
        }
        catch (OperationCanceledException)
        {
            // Session cancelled — ignore.
        }
        catch (TimeoutException)
        {
            // This tab had no download within the window.
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            // This tab had no download within the window.
        }
        catch (PlaywrightException) when (page.IsClosed || cancellationToken.IsCancellationRequested)
        {
            // Tab closed or session superseded.
        }
    }

    /// <summary>
    /// After captcha on issuer portal (e.g. vinvoice.viettel.vn/utilities/invoice-search):
    /// clicks "Tải file PDF" or fetches embedded PDF URL.
    /// </summary>
    public async Task<string?> TrySaveIssuerPdfAsync(
        string pdfSavePath,
        CancellationToken cancellationToken = default)
    {
        if (_context is null)
            throw new InvalidOperationException("Browser not launched.");

        var issuerPage = FindIssuerLookupPage();
        if (issuerPage is null)
        {
            _logger?.LogInformation("No issuer lookup tab found for PDF download.");
            return null;
        }

        await BringPageToFrontAsync(issuerPage, "issuer lookup", cancellationToken).ConfigureAwait(false);

        var dir = Path.GetDirectoryName(pdfSavePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = pdfSavePath + ".tmp";

        // PDF viewer may already be visible before clicking download.
        if (await TryFetchIssuerPdfFromPageAsync(issuerPage, tmp, pdfSavePath, cancellationToken).ConfigureAwait(false))
            return pdfSavePath;

        if (await TryClickIssuerPdfDownloadAsync(issuerPage, tmp, pdfSavePath, cancellationToken).ConfigureAwait(false))
            return pdfSavePath;

        if (await TryFetchIssuerPdfFromPageAsync(issuerPage, tmp, pdfSavePath, cancellationToken).ConfigureAwait(false))
            return pdfSavePath;

        _logger?.LogWarning("Could not save PDF from issuer tab {Url}", issuerPage.Url);
        return null;
    }

    public IPage? FindIssuerLookupPage()
    {
        if (_context is null)
            return null;

        var issuerPages = _context.Pages
            .Where(p => !p.IsClosed && IsIssuerPortalPage(p))
            .ToList();

        if (issuerPages.Count == 0)
            return null;

        return issuerPages.FirstOrDefault(p =>
                   (p.Url ?? "").Contains("vinvoice.", StringComparison.OrdinalIgnoreCase))
               ?? issuerPages.FirstOrDefault(p => ReferenceEquals(p, _page))
               ?? issuerPages[^1];
    }

    internal static bool IsIssuerPortalPage(IPage page)
    {
        var url = page.Url ?? "";
        if (string.IsNullOrWhiteSpace(url) || url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            return false;
        if (url.Contains(TracuuHostFragment, StringComparison.OrdinalIgnoreCase))
            return false;
        if (url.Contains(GdtHostFragment, StringComparison.OrdinalIgnoreCase))
            return false;
        if (url.Contains("login.saveinvoice", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private async Task<bool> TryClickIssuerPdfDownloadAsync(
        IPage page,
        string tmpPath,
        string pdfSavePath,
        CancellationToken cancellationToken)
    {
        var (button, matchedSelector) = await FindIssuerPdfDownloadControlAsync(page).ConfigureAwait(false);
        if (button is null)
        {
            _logger?.LogWarning("No issuer PDF button on {Url} (searched main frame + {FrameCount} frames)",
                page.Url, page.Frames.Count);
            return false;
        }

        _logger?.LogInformation("Clicking issuer PDF download on {Url} via {Selector}", page.Url, matchedSelector);
        await button.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30_000
        }).ConfigureAwait(false);
        await button.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);

        var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 120_000 });
        var responseTask = page.WaitForResponseAsync(IsIssuerPdfResponse, new PageWaitForResponseOptions { Timeout = 120_000 });

        try
        {
            await button.ClickAsync(new LocatorClickOptions { Timeout = 30_000 }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Issuer PDF button click failed; retrying with force");
            await button.ClickAsync(new LocatorClickOptions { Force = true, Timeout = 30_000 }).ConfigureAwait(false);
        }

        var winner = await Task.WhenAny(downloadTask, responseTask).ConfigureAwait(false);

        if (winner == downloadTask)
        {
            try
            {
                var download = await downloadTask.ConfigureAwait(false);
                await download.SaveAsAsync(tmpPath).ConfigureAwait(false);
                if (CommitTempPdfIfValid(tmpPath, pdfSavePath))
                    return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Issuer PDF download event save failed on {Url}", page.Url);
            }
        }

        try
        {
            var response = await responseTask.ConfigureAwait(false);
            var body = await response.BodyAsync().ConfigureAwait(false);
            if (LooksLikePdfBytes(body))
            {
                await File.WriteAllBytesAsync(tmpPath, body, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Issuer PDF saved from HTTP response: {Url}", response.Url);
                return CommitTempPdfIfValid(tmpPath, pdfSavePath);
            }

            _logger?.LogWarning("Issuer PDF response was not PDF bytes: {Url}", response.Url);
        }
        catch (TimeoutException)
        {
            _logger?.LogWarning("Issuer PDF: no download event or PDF response after click on {Url}", page.Url);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Issuer PDF response capture failed on {Url}", page.Url);
        }

        return false;
    }

    private static bool IsIssuerPdfResponse(IResponse response)
    {
        if (!response.Ok)
            return false;

        if (response.Headers.TryGetValue("content-type", out var contentType)
            && contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return true;

        var url = response.Url ?? "";
        return url.Contains("/pdf", StringComparison.OrdinalIgnoreCase)
               || url.Contains("download", StringComparison.OrdinalIgnoreCase)
               || url.Contains(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryFetchIssuerPdfFromPageAsync(
        IPage page,
        string tmpPath,
        string pdfSavePath,
        CancellationToken cancellationToken)
    {
        var blobBytes = await TryReadVisiblePdfBlobAsync(page).ConfigureAwait(false);
        if (blobBytes is not null && LooksLikePdfBytes(blobBytes))
        {
            await File.WriteAllBytesAsync(tmpPath, blobBytes, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Issuer PDF saved from blob URL on {Url}", page.Url);
            return CommitTempPdfIfValid(tmpPath, pdfSavePath);
        }

        var urls = await CollectIssuerPdfUrlsAsync(page).ConfigureAwait(false);
        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _logger?.LogInformation("Fetching issuer PDF via session: {Url}", url);
                var response = await _context!.APIRequest.GetAsync(url).ConfigureAwait(false);
                if (!response.Ok)
                    continue;

                var body = await response.BodyAsync().ConfigureAwait(false);
                if (!LooksLikePdfBytes(body))
                    continue;

                await File.WriteAllBytesAsync(tmpPath, body, cancellationToken).ConfigureAwait(false);
                return CommitTempPdfIfValid(tmpPath, pdfSavePath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Issuer PDF fetch failed for {Url}", url);
            }
        }

        return false;
    }

    private static async Task<(ILocator? Control, string? MatchedSelector)> FindIssuerPdfDownloadControlAsync(IPage page)
    {
        foreach (var frame in page.Frames)
        {
            var frameLabel = string.IsNullOrWhiteSpace(frame.Name) ? frame.Url : frame.Name;

            foreach (var selector in ViettelPdfDownloadSelectors)
            {
                var found = await TrySelectorInFrameAsync(frame, selector).ConfigureAwait(false);
                if (found is not null)
                    return (found, $"{frameLabel} :: {selector}");
            }

            foreach (var selector in IssuerPdfDownloadSelectors)
            {
                var found = await TrySelectorInFrameAsync(frame, selector).ConfigureAwait(false);
                if (found is not null)
                    return (found, $"{frameLabel} :: {selector}");
            }

            try
            {
                var byButton = frame.GetByRole(AriaRole.Button, new FrameGetByRoleOptions { Name = "Tải file PDF" });
                if (await byButton.CountAsync().ConfigureAwait(false) > 0)
                {
                    var control = await ResolveClickableAncestorAsync(byButton.First).ConfigureAwait(false);
                    return (control, $"{frameLabel} :: role=button Tải file PDF");
                }

                var byLink = frame.GetByRole(AriaRole.Link, new FrameGetByRoleOptions { Name = "Tải file PDF" });
                if (await byLink.CountAsync().ConfigureAwait(false) > 0)
                {
                    var control = await ResolveClickableAncestorAsync(byLink.First).ConfigureAwait(false);
                    return (control, $"{frameLabel} :: role=link Tải file PDF");
                }

                var textMatch = frame.Locator("button, a, .p-button, [role='button']")
                    .Filter(new LocatorFilterOptions { HasText = "Tải file PDF" });
                if (await textMatch.CountAsync().ConfigureAwait(false) > 0)
                {
                    var control = await ResolveClickableAncestorAsync(textMatch.First).ConfigureAwait(false);
                    return (control, $"{frameLabel} :: text Tải file PDF");
                }
            }
            catch (PlaywrightException)
            {
                // Cross-origin or detached frame — skip.
            }
        }

        return (null, null);
    }

    private static async Task<ILocator?> TrySelectorInFrameAsync(IFrame frame, string selector)
    {
        try
        {
            var candidates = frame.Locator(selector);
            if (await candidates.CountAsync().ConfigureAwait(false) == 0)
                return null;

            return await ResolveClickableAncestorAsync(candidates.First).ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static async Task<ILocator> ResolveClickableAncestorAsync(ILocator element)
    {
        var tag = await element.EvaluateAsync<string>("el => el.tagName.toLowerCase()").ConfigureAwait(false);
        if (tag is "button" or "a")
            return element;

        var ancestor = element.Locator(
            "xpath=ancestor::button[1] | ancestor::a[1] | ancestor::*[contains(@class,'p-button')][1]");
        if (await ancestor.CountAsync().ConfigureAwait(false) > 0)
            return ancestor.First;

        return element;
    }

    private static async Task<List<string>> CollectIssuerPdfUrlsAsync(IPage page)
    {
        var urls = new List<string>();
        var baseUrl = page.Url ?? "";

        if (IsLikelyPdfResourceUrl(baseUrl))
            urls.Add(baseUrl);

        foreach (var frame in page.Frames)
        {
            foreach (var (selector, attr) in IssuerPdfEmbedSelectors)
            {
                try
                {
                    var loc = frame.Locator(selector);
                    var count = await loc.CountAsync().ConfigureAwait(false);
                    for (var i = 0; i < count; i++)
                    {
                        var href = await loc.Nth(i).GetAttributeAsync(attr).ConfigureAwait(false);
                        if (IsLikelyPdfResourceUrl(href))
                            urls.Add(ToAbsoluteUrl(baseUrl, href!));
                    }
                }
                catch (PlaywrightException)
                {
                    // Detached / cross-origin frame.
                }
            }
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly (string Selector, string Attribute)[] IssuerPdfEmbedSelectors =
    [
        ("iframe[src]", "src"),
        ("embed[src]", "src"),
        ("object[data]", "data"),
        ("a[href*='.pdf' i]", "href"),
        ("a[href*='download' i]", "href")
    ];

    private static async Task<byte[]?> TryReadVisiblePdfBlobAsync(IPage page)
    {
        foreach (var frame in page.Frames)
        {
            try
            {
                var blobLoc = frame.Locator("iframe[src^='blob:'], embed[src^='blob:'], object[data^='blob:']");
                if (await blobLoc.CountAsync().ConfigureAwait(false) == 0)
                    continue;

                var blobUrl = await blobLoc.First.GetAttributeAsync("src")
                              ?? await blobLoc.First.GetAttributeAsync("data");
                if (string.IsNullOrWhiteSpace(blobUrl) || !blobUrl.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var base64 = await frame.EvaluateAsync<string>(
                    """
                    async (url) => {
                        const response = await fetch(url);
                        const buffer = await response.arrayBuffer();
                        const bytes = new Uint8Array(buffer);
                        let binary = '';
                        const chunk = 0x8000;
                        for (let i = 0; i < bytes.length; i += chunk)
                            binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
                        return btoa(binary);
                    }
                    """,
                    blobUrl).ConfigureAwait(false);

                return Convert.FromBase64String(base64);
            }
            catch (PlaywrightException)
            {
                // Frame not readable.
            }
            catch (Exception)
            {
                // Invalid blob / evaluate failure.
            }
        }

        return null;
    }

    private static bool IsLikelyPdfResourceUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return false;
        if (href.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            return true;
        return href.Contains(".pdf", StringComparison.OrdinalIgnoreCase)
               || href.Contains("/pdf", StringComparison.OrdinalIgnoreCase)
               || href.Contains("download", StringComparison.OrdinalIgnoreCase);
    }

    private bool CommitTempPdfIfValid(string tmpPath, string pdfSavePath)
    {
        if (!File.Exists(tmpPath) || !FileProcessor.LooksLikePdf(tmpPath))
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
            return false;
        }

        if (File.Exists(pdfSavePath))
            File.Delete(pdfSavePath);
        File.Move(tmpPath, pdfSavePath);
        _logger?.LogInformation("Issuer PDF saved: {Path}", pdfSavePath);
        return true;
    }

    private bool CommitTempManualDownloadIfValid(
        string tmpPath,
        string defaultSavePath,
        string? suggestedFilename,
        out string savedPath)
    {
        savedPath = defaultSavePath;
        if (!File.Exists(tmpPath))
            return false;

        string? extension = null;
        if (FileProcessor.LooksLikePdf(tmpPath))
            extension = ".pdf";
        else if (FileProcessor.LooksLikeZip(tmpPath))
            extension = ".zip";
        else if (!string.IsNullOrWhiteSpace(suggestedFilename))
        {
            var suggestedExt = Path.GetExtension(suggestedFilename);
            if (suggestedExt.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                || suggestedExt.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                extension = suggestedExt;
        }

        if (extension is null)
        {
            File.Delete(tmpPath);
            return false;
        }

        var dir = Path.GetDirectoryName(defaultSavePath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(defaultSavePath);
        savedPath = Path.Combine(dir, baseName + extension);

        if (File.Exists(savedPath))
            File.Delete(savedPath);
        File.Move(tmpPath, savedPath);
        _logger?.LogInformation("Manual download saved: {Path}", savedPath);
        return true;
    }

    private static async Task<ILocator?> FindTracuuPdfApiLinkAsync(IPage page)
    {
        var candidates = page.Locator(TracuuResultPdfLinkSelector);
        var count = await candidates.CountAsync().ConfigureAwait(false);
        for (var i = 0; i < count; i++)
        {
            var candidate = candidates.Nth(i);
            var href = await candidate.GetAttributeAsync("href").ConfigureAwait(false);
            if (IsTracuuPdfApiHref(href))
                return candidate;
        }

        return null;
    }

    internal static bool IsTracuuPdfApiHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return false;
        if (href.Contains("login.saveinvoice", StringComparison.OrdinalIgnoreCase))
            return false;
        return href.Contains("saveinvoice-pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Saves PDF using browser session cookies (after user solved captcha).
    /// </summary>
    public async Task<string> SaveTracuuPdfAsync(
        string pdfSavePath,
        string? pdfLinkHref,
        CancellationToken cancellationToken = default)
    {
        if (_context is null)
            throw new InvalidOperationException("Browser not launched.");

        await FocusTracuuTabForAutomationAsync(cancellationToken).ConfigureAwait(false);
        var page = _page!;
        var dir = Path.GetDirectoryName(pdfSavePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = pdfSavePath + ".tmp";
        var urls = CollectTracuuPdfUrls(page, pdfLinkHref);
        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _logger?.LogInformation("Fetching PDF via session: {Url}", url);
                var response = await _context.APIRequest.GetAsync(url).ConfigureAwait(false);
                if (!response.Ok)
                    continue;

                var body = await response.BodyAsync().ConfigureAwait(false);
                if (!LooksLikePdfBytes(body))
                    continue;

                await File.WriteAllBytesAsync(tmp, body, cancellationToken).ConfigureAwait(false);
                if (File.Exists(pdfSavePath))
                    File.Delete(pdfSavePath);
                File.Move(tmp, pdfSavePath);
                _logger?.LogInformation("PDF saved via APIRequest: {Path}", pdfSavePath);
                return pdfSavePath;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Session fetch failed for {Url}", url);
            }
        }

        var link = await FindTracuuPdfApiLinkAsync(page).ConfigureAwait(false);
        if (link is not null)
        {
            _logger?.LogInformation("Trying Playwright download event on PDF API link click");
            var download = await page.RunAndWaitForDownloadAsync(
                async () => await link.ClickAsync(new LocatorClickOptions { Timeout = 30_000 }).ConfigureAwait(false),
                new PageRunAndWaitForDownloadOptions { Timeout = 120_000 }).ConfigureAwait(false);

            await download.SaveAsAsync(tmp).ConfigureAwait(false);
            if (!FileProcessor.LooksLikePdf(tmp))
            {
                File.Delete(tmp);
                throw new InvalidOperationException("Downloaded file is not a valid PDF.");
            }

            if (File.Exists(pdfSavePath))
                File.Delete(pdfSavePath);
            File.Move(tmp, pdfSavePath);
            _logger?.LogInformation("PDF saved via download event: {Path}", pdfSavePath);
            return pdfSavePath;
        }

        throw new InvalidOperationException(
            "Could not download PDF. Ensure captcha is solved and the invoice/PDF page is visible, then try again.");
    }

    private static List<string> CollectTracuuPdfUrls(IPage page, string? pdfLinkHref)
    {
        var urls = new List<string>();
        var pageUrl = page.Url ?? "";

        if (IsTracuuPdfApiHref(pageUrl))
            urls.Add(pageUrl);

        if (IsTracuuPdfApiHref(pdfLinkHref))
            urls.Add(ToAbsoluteUrl(pageUrl, pdfLinkHref!));

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ToAbsoluteUrl(string basePageUrl, string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
            return absolute.ToString();
        if (Uri.TryCreate(basePageUrl, UriKind.Absolute, out var baseUri))
            return new Uri(baseUri, href).ToString();
        return href;
    }

    private static bool LooksLikePdfBytes(byte[] data) =>
        data.Length >= 5 && data[0] == (byte)'%' && data[1] == (byte)'P' && data[2] == (byte)'D' && data[3] == (byte)'F';

    /// <summary>
    /// Uploads a file on tracuuhoadon (or any site). Reuses an open tab whose URL contains
    /// <paramref name="urlHostContains"/>; only opens a new tab when none exists.
    /// </summary>
    public async Task UploadFileOnSiteTabAsync(
        string url,
        string urlHostContains,
        string fileInputSelector,
        string filePath,
        CancellationToken cancellationToken = default,
        bool reloadBeforeUpload = false)
    {
        if (_context is null)
            throw new InvalidOperationException("Browser not launched.");
        if (!File.Exists(filePath))
            throw new FileNotFoundException(filePath);

        await EnsureActivePageAsync(cancellationToken).ConfigureAwait(false);

        _context.Page -= OnContextPage;
        IPage? targetPage = null;
        try
        {
            targetPage = FindOpenPageByHost(urlHostContains);
            if (targetPage is not null)
            {
                _logger?.LogInformation("Reusing open tab for {Host}: {Url}", urlHostContains, targetPage.Url);
                await targetPage.BringToFrontAsync().ConfigureAwait(false);
            }
            else
            {
                _logger?.LogInformation("No open tab for {Host}; opening new tab", urlHostContains);
                targetPage = await _context.NewPageAsync().ConfigureAwait(false);
                await targetPage.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 90_000
                }).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (reloadBeforeUpload)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.LogInformation("Reloading page before upload: {Url}", targetPage.Url);
                await targetPage.ReloadAsync(new PageReloadOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 90_000
                }).WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var input = targetPage.Locator(fileInputSelector).First;
            await input.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = 30_000
            }).ConfigureAwait(false);
            await input.SetInputFilesAsync(filePath).ConfigureAwait(false);
            await targetPage.BringToFrontAsync().ConfigureAwait(false);

            _page = targetPage;
            _wrapper?.UpdatePage(targetPage);
        }
        finally
        {
            _context.Page += OnContextPage;
        }
    }

    /// <summary>
    /// Opens a URL in the browser. Reuses an existing tab on the same host when possible;
    /// user can complete captcha / mã bí mật manually on the page.
    /// </summary>
    public async Task OpenUrlInTabAsync(string url, CancellationToken cancellationToken = default, bool navigateIfFound = true)
    {
        if (_context is null)
            throw new InvalidOperationException("Browser not launched.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Invalid URL.", nameof(url));

        await EnsureActivePageAsync(cancellationToken).ConfigureAwait(false);

        _context.Page -= OnContextPage;
        IPage? targetPage = null;
        try
        {
            var host = uri.Host;
            targetPage = FindOpenPageByHost(host);
            if (targetPage is not null)
            {
                _logger?.LogInformation("Reusing tab for {Host}: {Url}", host, targetPage.Url);
                await targetPage.BringToFrontAsync().ConfigureAwait(false);
                if (navigateIfFound)
                {
                    _logger?.LogInformation("Navigating reused tab to {Url}", url);
                    cancellationToken.ThrowIfCancellationRequested();
                    await targetPage.GotoAsync(url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 90_000
                    }).ConfigureAwait(false);
                }
            }
            else
            {
                _logger?.LogInformation("Opening new tab for {Url}", url);
                targetPage = await _context.NewPageAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await targetPage.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 90_000
                }).ConfigureAwait(false);
            }

            await targetPage.BringToFrontAsync().ConfigureAwait(false);

            _page = targetPage;
            _wrapper?.UpdatePage(targetPage);
        }
        finally
        {
            _context.Page += OnContextPage;
        }
    }

    private IPage? FindOpenPageByHost(string urlHostContains)
    {
        if (_context is null || string.IsNullOrWhiteSpace(urlHostContains))
            return null;

        foreach (var page in _context.Pages)
        {
            var pageUrl = page.Url ?? "";
            if (pageUrl.Contains(urlHostContains, StringComparison.OrdinalIgnoreCase))
                return page;
        }

        return null;
    }

    public async Task SaveStorageStateAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_context is null)
            return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await _context.StorageStateAsync(new() { Path = path }).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            _context.Page -= OnContextPage;

        if (_page is not null)
        {
            try { await _page.CloseAsync().ConfigureAwait(false); } catch { /* ignore */ }
            _page = null;
        }

        if (_context is not null)
        {
            try { await _context.CloseAsync().ConfigureAwait(false); } catch { /* ignore */ }
            _context = null;
        }

        if (_browser is not null)
        {
            try { await _browser.CloseAsync().ConfigureAwait(false); } catch { /* ignore */ }
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _wrapper = null;
    }
}
