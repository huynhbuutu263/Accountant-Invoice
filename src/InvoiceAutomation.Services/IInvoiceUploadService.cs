namespace InvoiceAutomation.Services;

public interface IInvoiceUploadService
{
    /// <summary>Reuses an open tracuuhoadon.vn tab if present; reloads the page (F5) then uploads XML into the drop zone.</summary>
    Task UploadAsync(PlaywrightBrowserHost browserHost, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Upload XML, click issuer lookup link, optional tracuuhoadon PDF API download.</summary>
    Task<TracuuWorkflowResult> UploadAndDownloadPdfAsync(
        PlaywrightBrowserHost browserHost,
        string xmlFilePath,
        string pdfSavePath,
        TracuuDownloadMode mode = TracuuDownloadMode.ManualCaptcha,
        bool strictModeMatch = false,
        CancellationToken cancellationToken = default);
}
