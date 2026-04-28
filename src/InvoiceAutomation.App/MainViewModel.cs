using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InvoiceAutomation.App.Configuration;
using InvoiceAutomation.App.Logging;
using InvoiceAutomation.Core;
using InvoiceAutomation.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceAutomation.App;

public partial class MainViewModel : ObservableObject
{
    private readonly IJobRunner _jobRunner;
    private readonly IFileProcessor _fileProcessor;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IOptions<FlowsOptions> _flows;
    private readonly IOptions<BrowserOptions> _browser;
    private readonly IOptions<DownloadsOptions> _downloads;
    private CancellationTokenSource? _cts;

    public MainViewModel(
        IJobRunner jobRunner,
        IFileProcessor fileProcessor,
        ILogger<MainViewModel> logger,
        IOptions<FlowsOptions> flows,
        IOptions<BrowserOptions> browser,
        IOptions<DownloadsOptions> downloads,
        ObservableLogSink logSink)
    {
        _jobRunner = jobRunner;
        _fileProcessor = fileProcessor;
        _logger = logger;
        _flows = flows;
        _browser = browser;
        _downloads = downloads;
        Logs = logSink.Lines;
        FlowPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, flows.Value.DefaultPath));
        var dl = string.IsNullOrWhiteSpace(downloads.Value.RootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvoiceAutomation", "Downloads")
            : downloads.Value.RootPath;
        DownloadsRoot = dl;
        Directory.CreateDirectory(DownloadsRoot);

        StartCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning);
        TestLoginCommand = new AsyncRelayCommand(TestLoginAsync, () => !IsRunning && !string.IsNullOrWhiteSpace(_flows.Value.LoginPath));
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
    }

    public ObservableCollection<string> Logs { get; }

    public string[] InvoiceKindOptions { get; } = ["sales", "purchase"];

    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand TestLoginCommand { get; }
    public IRelayCommand CancelCommand { get; }

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private string _invoiceKind = "sales";

    [ObservableProperty]
    private string _flowPath = "";

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
        CancelCommand.NotifyCanExecuteChanged();
    }

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
            await using var host = new PlaywrightBrowserHost();
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
                JobId = Guid.NewGuid()
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

    private string? ResolveStorageStatePath()
    {
        var p = _browser.Value.StorageStatePath;
        if (string.IsNullOrWhiteSpace(p))
            return null;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, p));
    }
}
