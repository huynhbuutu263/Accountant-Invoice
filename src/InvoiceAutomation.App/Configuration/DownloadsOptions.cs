namespace InvoiceAutomation.App.Configuration;

public sealed class DownloadsOptions
{
    public const string SectionName = "Downloads";

    public string RootPath { get; set; } = "";

    /// <summary>Mirror of invoice folders for exported PDF/ZIP (no <c>pdf\</c> subfolder).</summary>
    public string ExportRootPath { get; set; } = "";
}
