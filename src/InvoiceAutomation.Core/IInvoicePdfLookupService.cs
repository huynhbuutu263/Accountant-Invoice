namespace InvoiceAutomation.Core;

public interface IInvoicePdfLookupService
{
    /// <summary>Parse XML, resolve issuer, build tra cứu / download URL (Link in XML or pdfUrlTemplate).</summary>
    string ResolveLookupUrl(string xmlFilePath);

    /// <summary>Parse XML, resolve issuer, call configured endpoint / link, save PDF next to XML.</summary>
    Task<string> DownloadPdfAsync(string xmlFilePath, string? pdfOutputDirectory = null, CancellationToken cancellationToken = default);
}
