namespace InvoiceAutomation.App.Configuration;

public sealed class FlowsOptions
{
    public const string SectionName = "Flows";

    public string DefaultPath { get; set; } = "flows/smoke.json";
    public string? LoginPath { get; set; }
}
