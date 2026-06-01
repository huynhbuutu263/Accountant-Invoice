namespace InvoiceAutomation.Services;

public sealed class BrowserLaunchSettings
{
    public bool Headless { get; init; }
    public string? Channel { get; init; }
    public string? StorageStatePath { get; init; }
}
