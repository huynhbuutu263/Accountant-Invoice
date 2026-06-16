namespace InvoiceAutomation.App.Configuration;

public sealed class InvoiceLookupOptions
{
    public const string SectionName = "InvoiceLookup";

    /// <summary>tracuuhoadon | issuerLink | tracuuhoadonAuto | tracuuhoadonApiLink | issuerPdf</summary>
    public string DefaultMode { get; set; } = "tracuuhoadon";

    public string IssuersConfigPath { get; set; } = "issuers.json";

    /// <summary>Subfolder under the XML directory where PDFs are saved (issuerPdf mode).</summary>
    public string PdfSubfolder { get; set; } = "pdf";
}
