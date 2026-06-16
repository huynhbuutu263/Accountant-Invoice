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

    private const string NoApiLinkSkipHint =
        "Bỏ qua — không có link saveinvoice-pdf (cần captcha hoặc In PDF).";

    private const string NoSearchHintApiSkipHint =
        "Bỏ qua — không có gợi ý tra cứu / datatable với link API.";

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
        mode switch
        {
            TracuuDownloadMode.AutoPrint => UploadAndAutoPrintPdfAsync(
                browserHost, xmlFilePath, pdfSavePath, strictModeMatch, cancellationToken),
            TracuuDownloadMode.AutoApiLink => UploadAndAutoApiLinkPdfAsync(
                browserHost, xmlFilePath, pdfSavePath, strictModeMatch, cancellationToken),
            _ => UploadAndManualCaptchaPdfAsync(
                browserHost, xmlFilePath, pdfSavePath, strictModeMatch, cancellationToken)
        };

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

    private async Task<TracuuWorkflowResult> UploadAndAutoApiLinkPdfAsync(
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
            _logger.LogWarning(ex, "Could not detect tracuuhoadon invoice action for API link download.");
            action = new TracuuInvoiceActionInfo();
        }

        if (strictModeMatch)
        {
            if (!action.HasSearchHint)
            {
                _logger.LogInformation("tracuuhoadon API link batch: skipped — no search hint for {Path}", xmlFilePath);
                return new TracuuWorkflowResult { Skipped = true, StatusHint = NoSearchHintApiSkipHint };
            }

            if (!action.HasDirectPdfLink)
            {
                _logger.LogInformation("tracuuhoadon API link batch: skipped — no saveinvoice-pdf link for {Path}", xmlFilePath);
                return new TracuuWorkflowResult { Skipped = true, StatusHint = NoApiLinkSkipHint };
            }
        }
        else if (!action.HasDirectPdfLink)
        {
            _logger.LogInformation("tracuuhoadon API link: no saveinvoice-pdf link for {Path}", xmlFilePath);
            return new TracuuWorkflowResult { Skipped = true, StatusHint = NoApiLinkSkipHint };
        }

        try
        {
            _logger.LogInformation(
                "tracuuhoadon API link: fetching PDF via session {Href}",
                action.PdfLinkHref);
            var saved = await browserHost.SaveTracuuPdfAsync(pdfSavePath, action.PdfLinkHref, cancellationToken)
                .ConfigureAwait(false);
            await browserHost.CloseTracuuAuxiliaryTabsAsync(cancellationToken).ConfigureAwait(false);
            return new TracuuWorkflowResult { PdfPath = saved };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "tracuuhoadon API link fetch failed for {Path}", xmlFilePath);
            return new TracuuWorkflowResult
            {
                Skipped = true,
                StatusHint = "Không tải được PDF qua link API — thử mode captcha thủ công."
            };
        }
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
