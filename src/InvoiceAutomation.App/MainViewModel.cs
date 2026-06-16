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
    private CancellationTokenSource? _openInvoiceCts;
    private int _openInvoiceOpId;
    private readonly object _openInvoiceStartLock = new();
    private string? _lastOpenInvoicePath;
    private long _lastOpenInvoiceTickMs;
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
        ExportRoot = string.IsNullOrWhiteSpace(downloads.Value.ExportRootPath)
            ? ""
            : downloads.Value.ExportRootPath;
        if (!string.IsNullOrWhiteSpace(ExportRoot))
            Directory.CreateDirectory(ExportRoot);
        InvoiceOpenMode = _invoiceLookup.Value.DefaultMode;

        StartCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning);
        TestLoginCommand = new AsyncRelayCommand(TestLoginAsync, () => !IsRunning && !string.IsNullOrWhiteSpace(_flows.Value.LoginPath));
        OpenGdtPortalCommand = new AsyncRelayCommand(OpenGdtPortalAsync, () => !IsRunning);
        DownloadOnlyCommand = new AsyncRelayCommand(RunDownloadOnlyAsync, () => !IsRunning);
        LoadDataCommand = new AsyncRelayCommand(LoadInvoicesAsync, () => !IsRunning);
        ExportDownloadsCommand = new AsyncRelayCommand(ExportAllDownloadsAsync, () => !IsRunning);
        AutoDownloadInvoicesCommand = new AsyncRelayCommand(RunAutoDownloadInvoicesAsync, CanAutoDownloadInvoices);
        OpenInvoiceCommand = new AsyncRelayCommand<InvoiceFile?>(OpenInvoiceAsync, CanOpenInvoice);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
    }

    public ObservableCollection<string> Logs { get; }

    public string[] InvoiceKindOptions { get; } = ["sales", "purchase"];

    public InvoiceOpenModeOption[] InvoiceOpenModeOptions { get; } =
    [
        new("tracuuhoadon", "Upload XML → tracuuhoadon.vn → PDF (captcha thủ công)"),
        new("issuerLink", "Mở link tra cứu — nhà phát hành (browser)"),
        new("tracuuhoadonAuto", "Tải PDF tự động — tracuuhoadon (In PDF, bỏ qua gợi ý tra cứu)"),
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
    public IAsyncRelayCommand ExportDownloadsCommand { get; }
    public IAsyncRelayCommand AutoDownloadInvoicesCommand { get; }

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
    private string _exportRoot = "";

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
        LoadDataCommand.NotifyCanExecuteChanged();
        ExportDownloadsCommand.NotifyCanExecuteChanged();
        AutoDownloadInvoicesCommand.NotifyCanExecuteChanged();
        OpenInvoiceCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenInvoice(InvoiceFile? invoice) =>
        !IsRunning && invoice is not null && File.Exists(invoice.FilePath);

    private bool CanAutoDownloadInvoices() =>
        !IsRunning && Invoices.Count > 0 && IsAutoDownloadSupportedMode(InvoiceOpenMode);

    private static bool IsAutoDownloadSupportedMode(string mode) =>
        string.Equals(mode, "tracuuhoadonAuto", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "issuerPdf", StringComparison.OrdinalIgnoreCase);

    partial void OnInvoiceOpenModeChanged(string value) =>
        AutoDownloadInvoicesCommand.NotifyCanExecuteChanged();

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

    private (int OpId, CancellationToken Token) BeginOpenInvoiceOperation()
    {
        var opId = Interlocked.Increment(ref _openInvoiceOpId);
        _openInvoiceCts?.Cancel();
        _openInvoiceCts?.Dispose();
        _openInvoiceCts = new CancellationTokenSource();
        _browserHost?.CancelManualPdfDownloadWait();
        return (opId, _openInvoiceCts.Token);
    }

    private bool IsCurrentOpenInvoiceOperation(int opId) => opId == _openInvoiceOpId;

    /// <summary>Returns immediately so the next double-click can cancel the previous PDF wait.</summary>
    private Task OpenInvoiceAsync(InvoiceFile? invoice)
    {
        if (invoice is null || !File.Exists(invoice.FilePath))
            return Task.CompletedTask;

        if (invoice.Supplier.StartsWith("[Lỗi tải]", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Bỏ qua — hóa đơn lỗi tải từ GDT.";
            _logger.LogInformation("Double-click skipped — download error XML: {Path}", invoice.FilePath);
            return Task.CompletedTask;
        }

        lock (_openInvoiceStartLock)
        {
            var now = Environment.TickCount64;
            if (string.Equals(_lastOpenInvoicePath, invoice.FilePath, StringComparison.OrdinalIgnoreCase)
                && now - _lastOpenInvoiceTickMs < 500)
            {
                _logger.LogDebug("Ignored duplicate double-click on {Path}", invoice.FilePath);
                return Task.CompletedTask;
            }

            _lastOpenInvoicePath = invoice.FilePath;
            _lastOpenInvoiceTickMs = now;
        }

        var (opId, ct) = BeginOpenInvoiceOperation();
        _ = RunOpenInvoiceWorkflowAsync(invoice, opId, ct, isAutoBatch: false);
        return Task.CompletedTask;
    }

    private async Task RunOpenInvoiceWorkflowAsync(
        InvoiceFile invoice,
        int opId,
        CancellationToken ct,
        bool isAutoBatch = false)
    {
        try
        {
            var invoiceXmlDir = Path.GetDirectoryName(invoice.FilePath) ?? DownloadsRoot;
            var invoicePdfDir = Path.Combine(invoiceXmlDir, _invoiceLookup.Value.PdfSubfolder);

            if (string.Equals(InvoiceOpenMode, "issuerLink", StringComparison.OrdinalIgnoreCase))
            {
                if (isAutoBatch)
                {
                    _logger.LogDebug("Auto batch skipped — issuerLink không hỗ trợ auto: {Path}", invoice.FilePath);
                    return;
                }

                if (!_invoicePdfLookupService.HasTraCuuLookup(invoice.FilePath))
                {
                    StatusMessage = "Bỏ qua — XML không có mã/link tra cứu (Fkey, KeySearch, MaTraCuu…).";
                    _logger.LogInformation("Double-click skipped — no tra cứu link/code for {Path}", invoice.FilePath);
                    return;
                }

                var lookupUrl = _invoicePdfLookupService.ResolveLookupUrl(invoice.FilePath);
                if (!IsCurrentOpenInvoiceOperation(opId))
                    return;

                StatusMessage = "Mở link tra cứu trong browser…";
                await EnsureBrowserHostAsync(ct).ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();
                await _browserHost!.OpenUrlInTabAsync(lookupUrl, ct).ConfigureAwait(true);

                if (!IsCurrentOpenInvoiceOperation(opId))
                    return;

                StatusMessage = $"Đã mở link tra cứu — nhập captcha/mã bí mật nếu có: {lookupUrl}";
                _logger.LogInformation("Opened issuer lookup URL {Url} for {Path}", lookupUrl, invoice.FilePath);
                return;
            }

            if (string.Equals(InvoiceOpenMode, "issuerPdf", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsCurrentOpenInvoiceOperation(opId))
                    return;

                if (!_invoicePdfLookupService.HasTraCuuLookup(invoice.FilePath))
                {
                    if (isAutoBatch)
                    {
                        _logger.LogDebug("Auto batch skipped — no tra cứu code for {Path}", invoice.FilePath);
                        return;
                    }

                    StatusMessage = "Bỏ qua — XML không có mã/link tra cứu (Fkey, KeySearch, MaTraCuu…).";
                    _logger.LogInformation("Double-click skipped — no tra cứu link/code for {Path}", invoice.FilePath);
                    return;
                }

                StatusMessage = "Tải PDF qua API nhà phát hành…";
                var issuerPdfPath = await _invoicePdfLookupService
                    .DownloadPdfAsync(invoice.FilePath, invoicePdfDir, ct)
                    .ConfigureAwait(true);

                if (!IsCurrentOpenInvoiceOperation(opId))
                    return;

                StatusMessage = $"PDF saved: {issuerPdfPath}";
                _logger.LogInformation("Issuer PDF lookup saved {Path}", issuerPdfPath);
                MarkInvoiceHasPdf(invoice.FilePath);
                return;
            }

            if (string.Equals(InvoiceOpenMode, "tracuuhoadonAuto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(InvoiceOpenMode, "tracuuhoadon", StringComparison.OrdinalIgnoreCase))
            {
                if (isAutoBatch && string.Equals(InvoiceOpenMode, "tracuuhoadon", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Auto batch skipped — mode captcha thủ công: {Path}", invoice.FilePath);
                    return;
                }

                if (!IsCurrentOpenInvoiceOperation(opId))
                    return;

                var tracuuMode = string.Equals(InvoiceOpenMode, "tracuuhoadonAuto", StringComparison.OrdinalIgnoreCase)
                    ? TracuuDownloadMode.AutoPrint
                    : TracuuDownloadMode.ManualCaptcha;

                await RunTracuuHoadonWorkflowAsync(invoice, opId, tracuuMode, ct, isAutoBatch).ConfigureAwait(true);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsCurrentOpenInvoiceOperation(opId))
                _logger.LogDebug("Open invoice superseded for {Path}", invoice.FilePath);
        }
        catch (Exception ex)
        {
            if (!IsCurrentOpenInvoiceOperation(opId))
                return;

            _logger.LogError(ex, "Open invoice failed (mode={Mode})", InvoiceOpenMode);
            StatusMessage = "Failed: " + ex.Message;
        }
    }

    private async Task RunTracuuHoadonWorkflowAsync(
        InvoiceFile invoice,
        int opId,
        TracuuDownloadMode mode,
        CancellationToken ct,
        bool isAutoBatch = false)
    {
        var invoiceXmlDir = Path.GetDirectoryName(invoice.FilePath) ?? DownloadsRoot;
        var invoicePdfDir = Path.Combine(invoiceXmlDir, _invoiceLookup.Value.PdfSubfolder);
        var tracuuPdfPath = InvoicePdfPaths.BuildPdfPath(invoice.FilePath, _invoiceLookup.Value.PdfSubfolder);
        Directory.CreateDirectory(Path.GetDirectoryName(tracuuPdfPath) ?? invoicePdfDir);

        if (isAutoBatch && mode == TracuuDownloadMode.AutoPrint
            && InvoicePdfPaths.HasDownloadedFile(invoice.FilePath, _invoiceLookup.Value.PdfSubfolder))
        {
            _logger.LogDebug("Auto batch skipped — PDF exists for {Path}", invoice.FilePath);
            return;
        }

        StatusMessage = mode == TracuuDownloadMode.AutoPrint
            ? "Upload XML → tracuuhoadon (auto In PDF)…"
            : "Upload XML → tracuuhoadon…";
        await EnsureBrowserHostAsync(ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();

        if (mode == TracuuDownloadMode.ManualCaptcha)
            StatusMessage = "Upload xong — tải PDF/ZIP trên browser (app chờ 10 phút)…";

        var tracuuResult = await _invoiceUploadService
            .UploadAndDownloadPdfAsync(_browserHost!, invoice.FilePath, tracuuPdfPath, mode, isAutoBatch, ct)
            .ConfigureAwait(true);

        if (!IsCurrentOpenInvoiceOperation(opId))
            return;

        if (tracuuResult.Cancelled)
        {
            _logger.LogDebug("PDF wait cancelled for {Path} — superseded by another row", invoice.FilePath);
            return;
        }

        if (tracuuResult.Skipped)
        {
            if (!isAutoBatch)
                StatusMessage = tracuuResult.StatusHint ?? "Bỏ qua.";
            _logger.LogInformation("tracuuhoadon skipped for {Path}: {Hint}", invoice.FilePath, tracuuResult.StatusHint);
            return;
        }

        if (tracuuResult.PdfSaved)
        {
            MarkInvoiceHasPdf(invoice.FilePath);
            StatusMessage = $"File saved: {tracuuResult.PdfPath}";
            _logger.LogInformation("Download saved {Path}", tracuuResult.PdfPath);
        }
        else if (tracuuResult.RequiresManualPrint)
        {
            StatusMessage = tracuuResult.StatusHint
                ?? "tracuuhoadon: chỉ có In PDF — lưu thủ công vào thư mục pdf\\.";
            _logger.LogInformation("tracuuhoadon print-only for {Path}: {Hint}", invoice.FilePath, StatusMessage);
        }
        else
        {
            StatusMessage = "Không bắt được file tải về (PDF/ZIP) trong 10 phút.";
            _logger.LogInformation("Manual download not captured for {Path}", invoice.FilePath);
        }
    }

    private bool ShouldAutoDownloadInvoice(InvoiceFile invoice)
    {
        if (invoice.Supplier.StartsWith("[Lỗi tải]", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(InvoiceOpenMode, "issuerPdf", StringComparison.OrdinalIgnoreCase))
            return _invoicePdfLookupService.HasTraCuuLookup(invoice.FilePath);

        if (string.Equals(InvoiceOpenMode, "tracuuhoadonAuto", StringComparison.OrdinalIgnoreCase))
            return !InvoicePdfPaths.HasDownloadedFile(invoice.FilePath, _invoiceLookup.Value.PdfSubfolder);

        return false;
    }

    private async Task RunAutoDownloadInvoicesAsync()
    {
        if (Invoices.Count == 0)
            return;

        if (!IsAutoDownloadSupportedMode(InvoiceOpenMode))
        {
            StatusMessage = "Auto download chỉ hỗ trợ mode 3 (Tải PDF tự động) hoặc API nhà phát hành.";
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        Progress = 0;

        var list = Invoices.ToList();
        var total = list.Count;
        var processed = 0;
        var skipped = 0;

        try
        {
            await EnsureBrowserHostAsync(_cts.Token).ConfigureAwait(true);

            for (var i = 0; i < total; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var invoice = list[i];

                if (!ShouldAutoDownloadInvoice(invoice))
                {
                    skipped++;
                    continue;
                }

                processed++;
                Progress = (i + 1) / (double)total * 100;
                SelectedInvoice = invoice;
                StatusMessage = $"Auto download {processed}/{total} (bỏ qua {skipped}): {invoice.InvoiceNo}";

                var opId = Interlocked.Increment(ref _openInvoiceOpId);
                await RunOpenInvoiceWorkflowAsync(invoice, opId, _cts.Token, isAutoBatch: true).ConfigureAwait(true);
            }

            Progress = 100;
            StatusMessage = $"Auto download xong — xử lý {processed}, bỏ qua {skipped}.";
            _logger.LogInformation("Auto download completed: processed={Processed}, skipped={Skipped}", processed, skipped);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Auto download cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto download failed");
            StatusMessage = "Auto download failed: " + ex.Message;
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task ExportAllDownloadsAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportRoot))
        {
            StatusMessage = "Chưa cấu hình Export folder.";
            return;
        }

        try
        {
            var dataRoot = string.IsNullOrWhiteSpace(InvoiceXMLDataPath)
                ? DownloadsRoot
                : InvoiceXMLDataPath;

            var count = await Task.Run(() =>
                InvoiceExportPaths.ExportAllDownloads(
                    dataRoot, ExportRoot, _invoiceLookup.Value.PdfSubfolder)).ConfigureAwait(true);

            Directory.CreateDirectory(ExportRoot);
            StatusMessage = count > 0
                ? $"Đã export {count} file PDF/ZIP → {ExportRoot}"
                : $"Không có file trong thư mục pdf\\ để export.";
            _logger.LogInformation("Exported {Count} downloads to {ExportRoot}", count, ExportRoot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export all downloads failed");
            StatusMessage = "Export failed: " + ex.Message;
        }
    }

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

            for (var i = 0; i < invoices.Count; i++)
            {
                invoices[i].Stt = i + 1;
                Invoices.Add(invoices[i]);
            }

            AutoDownloadInvoicesCommand.NotifyCanExecuteChanged();
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
                : 0,

            HasPdf = InvoicePdfPaths.HasDownloadedFile(filePath, _invoiceLookup.Value.PdfSubfolder)
        };
    }

    private void MarkInvoiceHasPdf(string xmlFilePath)
    {
        var invoice = Invoices.FirstOrDefault(i =>
            string.Equals(i.FilePath, xmlFilePath, StringComparison.OrdinalIgnoreCase));
        if (invoice is not null)
            invoice.HasPdf = true;
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
