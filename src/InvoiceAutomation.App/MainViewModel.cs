using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoiceAutomation.App.Configuration;
using InvoiceAutomation.App.Logging;
using InvoiceAutomation.Core;
using InvoiceAutomation.Core.Models;
using InvoiceAutomation.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceAutomation.App;

public partial class MainViewModel : ObservableObject
{
    private const string GdtHomeUrl = "https://hoadondientu.gdt.gov.vn/";

    private readonly IJobRunner _jobRunner;
    private readonly IFileProcessor _fileProcessor;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IOptions<FlowsOptions> _flows;
    private readonly IOptions<BrowserOptions> _browser;
    private readonly IOptions<DownloadsOptions> _downloads;
    private CancellationTokenSource? _cts;
    private PlaywrightBrowserHost? _browserHost;
    private readonly IInvoiceUploadService _invoiceUploadService;
    private readonly IInvoicePdfLookupService _invoicePdfLookupService;
    private readonly IOptions<InvoiceLookupOptions> _invoiceLookup;

    public MainViewModel(
        IJobRunner jobRunner,
        IFileProcessor fileProcessor,
        ILogger<MainViewModel> logger,
        IOptions<FlowsOptions> flows,
        IOptions<BrowserOptions> browser,
        IOptions<DownloadsOptions> downloads,
        IOptions<InvoiceLookupOptions> invoiceLookup,
        IInvoiceUploadService invoiceUploadService,
        IInvoicePdfLookupService invoicePdfLookupService,
        ObservableLogSink logSink)
    {
        _jobRunner = jobRunner;
        _fileProcessor = fileProcessor;
        _logger = logger;
        _flows = flows;
        _browser = browser;
        _downloads = downloads;
        _invoiceLookup = invoiceLookup;
        _invoiceUploadService = invoiceUploadService;
        _invoicePdfLookupService = invoicePdfLookupService;
        Logs = logSink.Lines;
        FlowPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, flows.Value.DefaultPath));
        var dl = string.IsNullOrWhiteSpace(downloads.Value.RootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvoiceAutomation", "Downloads")
            : downloads.Value.RootPath;
        DownloadsRoot = dl;
        Directory.CreateDirectory(DownloadsRoot);
        InvoiceXMLDataPath = DownloadsRoot;
        InvoiceOpenMode = _invoiceLookup.Value.DefaultMode;

        StartCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning);
        TestLoginCommand = new AsyncRelayCommand(TestLoginAsync, () => !IsRunning && !string.IsNullOrWhiteSpace(_flows.Value.LoginPath));
        OpenGdtPortalCommand = new AsyncRelayCommand(OpenGdtPortalAsync, () => !IsRunning);
        DownloadOnlyCommand = new AsyncRelayCommand(RunDownloadOnlyAsync, () => !IsRunning);
        LoadDataCommand = new AsyncRelayCommand(LoadInvoicesAsync, () => !IsRunning);
        OpenInvoiceCommand = new AsyncRelayCommand<InvoiceFile?>(OpenInvoiceAsync, CanOpenInvoice);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
    }

    public ObservableCollection<string> Logs { get; }

    public string[] InvoiceKindOptions { get; } = ["sales", "purchase"];

    public InvoiceOpenModeOption[] InvoiceOpenModeOptions { get; } =
    [
        new("tracuuhoadon", "Upload XML → tracuuhoadon.vn"),
        new("issuerLink", "Mở link tra cứu — nhà phát hành (browser)"),
        new("issuerPdf", "Tải PDF — API nhà phát hành (HTTP)")
    ];

    [ObservableProperty]
    private string _invoiceOpenMode = "tracuuhoadon";

    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand TestLoginCommand { get; }
    public IAsyncRelayCommand OpenGdtPortalCommand { get; }
    public IAsyncRelayCommand DownloadOnlyCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IAsyncRelayCommand LoadDataCommand { get; }

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private string _invoiceKind = "sales";

    [ObservableProperty]
    private string _gdtMst = "";

    private string _gdtPassword = "";

    public void SetPassword(string password) => _gdtPassword = password;

    [ObservableProperty]
    private string _flowPath = "";

    [ObservableProperty]
    private string _invoiceXMLDataPath = "";

    [ObservableProperty]
    private string _downloadsRoot = "";

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private double _progress;

    partial void OnIsRunningChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
        TestLoginCommand.NotifyCanExecuteChanged();
        OpenGdtPortalCommand.NotifyCanExecuteChanged();
        DownloadOnlyCommand.NotifyCanExecuteChanged();
        OpenInvoiceCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenInvoice(InvoiceFile? invoice) =>
        !IsRunning && invoice is not null && File.Exists(invoice.FilePath);

    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "Cancelling…";
    }

    private async Task RunAsync()
    {
        if (!File.Exists(FlowPath))
        {
            StatusMessage = "Flow file not found.";
            _logger.LogError("Flow not found: {Path}", FlowPath);
            return;
        }

        if (FromDate.Date > ToDate.Date)
        {
            StatusMessage = "From date must be before or equal to To date.";
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        Progress = 0;
        StatusMessage = "Running…";

        try
        {
            // Dispose any browser that is still open from a previous run.
            if (_browserHost is not null)
            {
                await _browserHost.DisposeAsync().ConfigureAwait(true);
                _browserHost = null;
            }

            var host = new PlaywrightBrowserHost();
            _browserHost = host;

            await host.LaunchAsync(new BrowserLaunchSettings
            {
                Headless = _browser.Value.Headless,
                Channel = _browser.Value.Channel,
                StorageStatePath = ResolveStorageStatePath()
            }, _cts.Token).ConfigureAwait(true);

            var parameters = new JobParameters
            {
                FromDate = FromDate.ToString("yyyy-MM-dd"),
                ToDate = ToDate.ToString("yyyy-MM-dd"),
                InvoiceKind = InvoiceKind,
                DownloadsRoot = DownloadsRoot,
                JobId = Guid.NewGuid(),
                GdtMst = GdtMst,
                GdtPassword = _gdtPassword
            };

            _logger.LogInformation("Job {JobId} started", parameters.JobId);
            var result = await _jobRunner
                .RunAsync(FlowPath, parameters, host.Page, _fileProcessor, _cts.Token)
                .ConfigureAwait(true);

            Progress = 100;
            StatusMessage = result.Status switch
            {
                JobStatus.Completed => "Completed.",
                JobStatus.Cancelled => "Cancelled.",
                _ => "Failed: " + (result.ErrorMessage ?? "see logs")
            };

            var storagePath = ResolveStorageStatePath();
            if (result.Status == JobStatus.Completed && !string.IsNullOrWhiteSpace(storagePath))
            {
                var dir = Path.GetDirectoryName(storagePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                await host.SaveStorageStateAsync(storagePath, _cts.Token).ConfigureAwait(true);
            }
            // Browser intentionally left open so the user can inspect the portal after the run.
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run failed");
            StatusMessage = "Error: " + ex.Message;
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Closes the browser window and releases Playwright resources. Called on application exit.</summary>
    public async Task CleanupBrowserAsync()
    {
        if (_browserHost is not null)
        {
            await _browserHost.DisposeAsync().ConfigureAwait(false);
            _browserHost = null;
        }
    }

    private async Task TestLoginAsync()
    {
        var loginPath = _flows.Value.LoginPath;
        if (string.IsNullOrWhiteSpace(loginPath))
        {
            StatusMessage = "No login flow configured.";
            return;
        }

        var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, loginPath));
        if (!File.Exists(full))
        {
            StatusMessage = "Login flow file not found.";
            return;
        }

        FlowPath = full;
        await RunAsync().ConfigureAwait(true);
    }

    private async Task OpenGdtPortalAsync()
    {
        try
        {
            StatusMessage = "Opening GDT portal…";
            await EnsureBrowserHostAsync(CancellationToken.None).ConfigureAwait(true);

            if (_browserHost!.FindBestGdtPage() is not null)
            {
                await _browserHost.FocusGdtTabForAutomationAsync(CancellationToken.None).ConfigureAwait(true);
                StatusMessage = "GDT tab focused — change month/filter and click Download when ready.";
            }
            else
            {
                await _browserHost.OpenUrlInTabAsync(GdtHomeUrl, CancellationToken.None).ConfigureAwait(true);
                StatusMessage = "GDT portal open — log in and run a search, then use Download.";
            }

            _logger.LogInformation("GDT portal ready for automation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open GDT portal");
            StatusMessage = "Error: " + ex.Message;
        }
    }

    public ObservableCollection<InvoiceFile> Invoices { get; }
            = new();

    private InvoiceFile? _selectedInvoice;
    public InvoiceFile? SelectedInvoice
    {
        get => _selectedInvoice;
        set
        {
            _selectedInvoice = value;
            OnPropertyChanged();
        }
    }

    public IAsyncRelayCommand<InvoiceFile?> OpenInvoiceCommand { get; }

    private async Task OpenInvoiceAsync(InvoiceFile? invoice)
    {
        if (invoice is null || !File.Exists(invoice.FilePath))
            return;

        try
        {
            if (string.Equals(InvoiceOpenMode, "issuerLink", StringComparison.OrdinalIgnoreCase))
            {
                var lookupUrl = _invoicePdfLookupService.ResolveLookupUrl(invoice.FilePath);
                StatusMessage = "Mở link tra cứu trong browser…";
                await EnsureBrowserHostAsync(CancellationToken.None).ConfigureAwait(true);
                await _browserHost!.OpenUrlInTabAsync(lookupUrl, CancellationToken.None).ConfigureAwait(true);
                StatusMessage = $"Đã mở link tra cứu — nhập captcha/mã bí mật nếu có: {lookupUrl}";
                _logger.LogInformation("Opened issuer lookup URL {Url} for {Path}", lookupUrl, invoice.FilePath);
                return;
            }

            if (string.Equals(InvoiceOpenMode, "issuerPdf", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Tải PDF qua API nhà phát hành…";
                var xmlDir = Path.GetDirectoryName(invoice.FilePath) ?? DownloadsRoot;
                var pdfDir = Path.Combine(xmlDir, _invoiceLookup.Value.PdfSubfolder);
                var pdfPath = await _invoicePdfLookupService
                    .DownloadPdfAsync(invoice.FilePath, pdfDir, CancellationToken.None)
                    .ConfigureAwait(true);
                StatusMessage = $"PDF saved: {pdfPath}";
                _logger.LogInformation("Issuer PDF lookup saved {Path}", pdfPath);
                return;
            }

            StatusMessage = "Opening tracuuhoadon.vn and uploading XML…";
            await EnsureBrowserHostAsync(CancellationToken.None).ConfigureAwait(true);
            await _invoiceUploadService
                .UploadAsync(_browserHost!, invoice.FilePath, CancellationToken.None)
                .ConfigureAwait(true);
            StatusMessage = $"Uploaded {Path.GetFileName(invoice.FilePath)} to tracuuhoadon.vn.";
            _logger.LogInformation("Uploaded invoice XML {Path}", invoice.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open invoice failed (mode={Mode})", InvoiceOpenMode);
            StatusMessage = "Failed: " + ex.Message;
        }
    }

    private bool isLoading;

    private async Task LoadInvoicesAsync()
    {
        //IsLoading = true;

        try
        {
            var invoices = await Task.Run(() =>
            {
                var result = new List<InvoiceFile>();

                var root = string.IsNullOrWhiteSpace(InvoiceXMLDataPath)
                    ? DownloadsRoot
                    : InvoiceXMLDataPath;
                if (!Directory.Exists(root))
                    return result;

                var files = Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    result.Add(ParseInvoice(file));
                }

                return result;
            });

            Invoices.Clear();

            foreach (var invoice in invoices)
            {
                Invoices.Add(invoice);
            }
        }
        finally
        {
        //    IsLoading = false;
        }
    }

    private InvoiceFile ParseInvoice(string filePath)
    {
        var doc = XDocument.Load(filePath);
        var errorMessage = doc.Descendants()
            .Where(x => x.Name.LocalName == "TTin")
            .Select(t => new
            {
                Field = t.Elements().FirstOrDefault(e => e.Name.LocalName == "TTruong")?.Value,
                Value = t.Elements().FirstOrDefault(e => e.Name.LocalName == "DLieu")?.Value
            })
            .FirstOrDefault(x => string.Equals(x.Field, "Error", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        var supplier = doc.Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "Ten")
            ?.Value ?? "";

        if (!string.IsNullOrWhiteSpace(errorMessage))
            supplier = "[Lỗi tải] " + errorMessage;

        return new InvoiceFile
        {
            FilePath = filePath,

            TaxCode =
                doc.Descendants()
                   .FirstOrDefault(x => x.Name.LocalName == "MST")
                   ?.Value ?? "",

            Supplier = supplier,

            InvoiceNo =
                doc.Descendants()
                   .FirstOrDefault(x => x.Name.LocalName == "SHDon")
                   ?.Value ?? "",

            InvoiceDate =
                DateTime.TryParse(
                    doc.Descendants()
                       .FirstOrDefault(x => x.Name.LocalName == "NLap")
                       ?.Value,
                    out var dt)
                ? dt
                : null,

            TotalAmount =
                decimal.TryParse(
                    doc.Descendants()
                       .FirstOrDefault(x => x.Name.LocalName == "TgTTTBSo")
                       ?.Value,
                    out var amount)
                ? amount
                : 0
        };
    }
    private async Task RunDownloadOnlyAsync()
    {
        var flowPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _flows.Value.DownloadOnlyPath));
        if (!File.Exists(flowPath))
        {
            StatusMessage = "Download-only flow file not found.";
            _logger.LogError("Flow not found: {Path}", flowPath);
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        Progress = 0;
        StatusMessage = "Auto-downloading all rows…";

        try
        {
            await EnsureBrowserHostAsync(_cts.Token).ConfigureAwait(true);
            await _browserHost!.FocusGdtTabForAutomationAsync(_cts.Token).ConfigureAwait(true);

            var parameters = new JobParameters
            {
                FromDate = FromDate.ToString("yyyy-MM-dd"),
                ToDate = ToDate.ToString("yyyy-MM-dd"),
                InvoiceKind = InvoiceKind,
                DownloadsRoot = DownloadsRoot,
                JobId = Guid.NewGuid(),
                GdtMst = GdtMst,
                GdtPassword = _gdtPassword
            };

            _logger.LogInformation("Download-only job {JobId} started", parameters.JobId);
            var result = await _jobRunner
                .RunAsync(flowPath, parameters, _browserHost!.Page, _fileProcessor, _cts.Token)
                .ConfigureAwait(true);

            Progress = 100;
            StatusMessage = result.Status switch
            {
                JobStatus.Completed => "Download completed.",
                JobStatus.Cancelled => "Cancelled.",
                _ => "Failed: " + (result.ErrorMessage ?? "see logs")
            };
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("GDT tab", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Download-only: GDT tab not ready");
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download-only run failed");
            StatusMessage = "Error: " + ex.Message;
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task EnsureBrowserHostAsync(CancellationToken cancellationToken)
    {
        if (_browserHost is not null && _browserHost.IsAlive)
        {
            await _browserHost.EnsureActivePageAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (_browserHost is not null)
        {
            _logger.LogInformation("Browser was closed; launching a new session.");
            await _browserHost.DisposeAsync().ConfigureAwait(true);
            _browserHost = null;
        }

        var host = new PlaywrightBrowserHost();
        _browserHost = host;
        await host.LaunchAsync(new BrowserLaunchSettings
        {
            Headless = _browser.Value.Headless,
            Channel = _browser.Value.Channel,
            StorageStatePath = ResolveStorageStatePath()
        }, cancellationToken).ConfigureAwait(true);
    }

    private string? ResolveStorageStatePath()
    {
        var p = _browser.Value.StorageStatePath;
        if (string.IsNullOrWhiteSpace(p))
            return null;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, p));
    }
}
