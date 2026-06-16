namespace InvoiceAutomation.Core;

public interface IInvoicePdfLookupService
{
    /// <summary>True when XML has a tra cứu URL, lookup code, or issuer pdfUrlTemplate can be built.</summary>
    bool HasTraCuuLookup(string xmlFilePath);

    /// <summary>Parse XML, resolve issuer, build tra cứu / download URL (Link in XML or pdfUrlTemplate).</summary>
    string ResolveLookupUrl(string xmlFilePath);

    /// <summary>Parse XML, resolve issuer, call configured endpoint / link, save PDF next to XML.</summary>
    Task<string> DownloadPdfAsync(string xmlFilePath, string? pdfOutputDirectory = null, CancellationToken cancellationToken = default);
}
