using InvoiceAutomation.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace InvoiceAutomation.Services;

public sealed class PlaywrightBrowserHost : IAsyncDisposable
{
    public const string GdtHostFragment = "gdt.gov.vn";

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
                _logger?.LogInformation("Reloading page before upload: {Url}", targetPage.Url);
                await targetPage.ReloadAsync(new PageReloadOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 90_000
                }).ConfigureAwait(false);
            }

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
