using Microsoft.Extensions.Logging;

namespace InvoiceAutomation.Services;

public sealed class TracuuHoadonUploadService : IInvoiceUploadService
{
    public const string DefaultUrl = "https://tracuuhoadon.vn/";
    public const string SiteHost = "tracuuhoadon.vn";
    public const string FileInputSelector =
        "div.border-dashed input[type='file'], input[type='file'][accept*='xml']";

    private const string PrintOnlyHint =
        "tracuuhoadon chỉ có nút In PDF — dialog in/lưu file không qua download browser. " +
        "Lưu thủ công vào thư mục pdf\\ của hóa đơn (hoặc dùng hóa đơn có link tải API).";

    private const string SearchHintSkipHint =
        "Bỏ qua — có gợi ý tra cứu (dùng mode captcha thủ công hoặc đã có PDF từ captcha).";

    private const string PrintOnlySkipHint =
        "Bỏ qua — chỉ có In PDF (dùng mode Tải PDF tự động).";

    private const string WrongModeSkipHint =
        "Bỏ qua — không thuộc mode đang chọn.";

    private readonly ILogger<TracuuHoadonUploadService> _logger;

    public TracuuHoadonUploadService(ILogger<TracuuHoadonUploadService> logger)
    {
        _logger = logger;
    }

    public Task UploadAsync(PlaywrightBrowserHost browserHost, string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Invoice XML not found.", filePath);

        _logger.LogInformation("Uploading {File} to {Url}", filePath, DefaultUrl);
        return browserHost.UploadFileOnSiteTabAsync(
            DefaultUrl, SiteHost, FileInputSelector, filePath, cancellationToken,
            reloadBeforeUpload: true);
    }

    public Task<TracuuWorkflowResult> UploadAndDownloadPdfAsync(
        PlaywrightBrowserHost browserHost,
        string xmlFilePath,
        string pdfSavePath,
        TracuuDownloadMode mode = TracuuDownloadMode.ManualCaptcha,
        bool strictModeMatch = false,
        CancellationToken cancellationToken = default) =>
        mode == TracuuDownloadMode.AutoPrint
            ? UploadAndAutoPrintPdfAsync(browserHost, xmlFilePath, pdfSavePath, strictModeMatch, cancellationToken)
            : UploadAndManualCaptchaPdfAsync(browserHost, xmlFilePath, pdfSavePath, strictModeMatch, cancellationToken);

    private async Task<TracuuWorkflowResult> UploadAndAutoPrintPdfAsync(
        PlaywrightBrowserHost browserHost,
        string xmlFilePath,
        string pdfSavePath,
        bool strictModeMatch,
        CancellationToken cancellationToken)
    {
        await UploadAsync(browserHost, xmlFilePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        TracuuInvoiceActionInfo action;
        try
        {
            action = await browserHost.DetectTracuuInvoiceActionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not detect tracuuhoadon invoice action for auto print.");
            action = new TracuuInvoiceActionInfo();
        }

        if (action.HasSearchHint)
        {
            _logger.LogInformation("tracuuhoadon auto print: skipped — search hint present for {Path}", xmlFilePath);
            return new TracuuWorkflowResult { Skipped = true, StatusHint = SearchHintSkipHint };
        }

        if (strictModeMatch && !action.IsPrintOnly)
        {
            _logger.LogInformation("tracuuhoadon auto print: skipped — not a print-only case for {Path}", xmlFilePath);
            return new TracuuWorkflowResult { Skipped = true, StatusHint = WrongModeSkipHint };
        }

        _logger.LogInformation("tracuuhoadon auto print: In PDF → render HTML tab to file.");
        var captured = await browserHost.TryCaptureTracuuPrintPdfAsync(pdfSavePath, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(captured))
            return new TracuuWorkflowResult { PdfPath = captured };

        return new TracuuWorkflowResult
        {
            RequiresManualPrint = true,
            StatusHint = PrintOnlyHint
        };
    }

    private async Task<TracuuWorkflowResult> UploadAndManualCaptchaPdfAsync(
        PlaywrightBrowserHost browserHost,
        string xmlFilePath,
        string pdfSavePath,
        bool strictModeMatch,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Listening for manual PDF download on any browser tab…");
        var pdfWaitTask = browserHost.WaitForManualPdfDownloadAsync(pdfSavePath, cancellationToken);

        await UploadAsync(browserHost, xmlFilePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        TracuuInvoiceActionInfo action;
        try
        {
            action = await browserHost.DetectTracuuInvoiceActionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not detect tracuuhoadon invoice action — falling back to download wait.");
            action = new TracuuInvoiceActionInfo();
        }

        if (strictModeMatch && !action.PreferManualDownload)
        {
            browserHost.CancelManualPdfDownloadWait();
            _logger.LogInformation("tracuuhoadon captcha batch: skipped — not a search-hint case for {Path}", xmlFilePath);
            return new TracuuWorkflowResult
            {
                Skipped = true,
                StatusHint = action.IsPrintOnly ? PrintOnlySkipHint : WrongModeSkipHint
            };
        }

        if (action.PreferManualDownload)
        {
            _logger.LogInformation(
                "tracuuhoadon: Gợi ý tra cứu / datatable — click link tải trên browser, app chờ download.");
            return await AwaitManualPdfDownloadAsync(browserHost, pdfWaitTask, cancellationToken).ConfigureAwait(false);
        }

        if (action.IsPrintOnly)
        {
            if (strictModeMatch)
            {
                browserHost.CancelManualPdfDownloadWait();
                _logger.LogInformation("tracuuhoadon captcha batch: skipped — print-only case for {Path}", xmlFilePath);
                return new TracuuWorkflowResult { Skipped = true, StatusHint = PrintOnlySkipHint };
            }

            _logger.LogInformation(
                "tracuuhoadon: print-only ({Label}) — auto-click In PDF → render HTML tab to file.",
                action.PrintButtonLabel);

            browserHost.CancelManualPdfDownloadWait();
            var captured = await browserHost.TryCaptureTracuuPrintPdfAsync(pdfSavePath, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(captured))
                return new TracuuWorkflowResult { PdfPath = captured };

            return new TracuuWorkflowResult
            {
                RequiresManualPrint = true,
                StatusHint = PrintOnlyHint
            };
        }

        _logger.LogInformation("XML uploaded — continue waiting for manual PDF download.");
        return await AwaitManualPdfDownloadAsync(browserHost, pdfWaitTask, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TracuuWorkflowResult> AwaitManualPdfDownloadAsync(
        PlaywrightBrowserHost browserHost,
        Task<string?> pdfWaitTask,
        CancellationToken cancellationToken)
    {
        try
        {
            var pdfPath = await pdfWaitTask.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(pdfPath))
                await browserHost.CloseTracuuAuxiliaryTabsAsync(cancellationToken).ConfigureAwait(false);

            return new TracuuWorkflowResult { PdfPath = pdfPath };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("PDF download wait cancelled (another invoice opened).");
            return new TracuuWorkflowResult { Cancelled = true };
        }
    }
}
