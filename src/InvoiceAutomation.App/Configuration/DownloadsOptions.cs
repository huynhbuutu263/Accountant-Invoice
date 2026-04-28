namespace InvoiceAutomation.App.Configuration;

public sealed class DownloadsOptions
{
    public const string SectionName = "Downloads";

    public string RootPath { get; set; } = "";
}
