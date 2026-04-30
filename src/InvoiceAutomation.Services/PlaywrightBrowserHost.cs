using InvoiceAutomation.Core;
using Microsoft.Playwright;

namespace InvoiceAutomation.Services;

public sealed class PlaywrightBrowserHost : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private PlaywrightAutomationPage? _wrapper;

    public IAutomationPage Page => _wrapper ?? throw new InvalidOperationException("Browser not launched.");

    public async Task LaunchAsync(BrowserLaunchSettings settings, CancellationToken cancellationToken = default)
    {
        _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = settings.Headless,
            Channel = string.IsNullOrWhiteSpace(settings.Channel) ? null : settings.Channel
        }).ConfigureAwait(false);

        var contextOptions = new BrowserNewContextOptions();
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
        // Unsubscribe to prevent the closed page from holding a reference to this host.
        closedPage.Close -= OnPageClose;

        var remaining = _context?.Pages;
        if (remaining is { Count: > 0 })
        {
            _page = remaining[^1];
            _wrapper?.UpdatePage(_page);
        }
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
