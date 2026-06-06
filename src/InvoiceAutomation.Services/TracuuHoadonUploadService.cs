using Microsoft.Extensions.Logging;

namespace InvoiceAutomation.Services;

public sealed class TracuuHoadonUploadService : IInvoiceUploadService
{
    public const string DefaultUrl = "https://tracuuhoadon.vn/";
    public const string SiteHost = "tracuuhoadon.vn";
    public const string FileInputSelector =
        "div.border-dashed input[type='file'], input[type='file'][accept*='xml']";

    private readonly ILogger<TracuuHoadonUploadService> _logger;

    public TracuuHoadonUploadService(ILogger<TracuuHoadonUploadService> logger) => _logger = logger;

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
}
