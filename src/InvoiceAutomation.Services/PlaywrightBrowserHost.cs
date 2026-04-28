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
