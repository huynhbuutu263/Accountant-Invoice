namespace InvoiceAutomation.Services;

public sealed class BrowserLaunchSettings
{
    public bool Headless { get; init; }
    public string? Channel { get; init; }
    public string? StorageStatePath { get; init; }
    /// <summary>
    /// Optional viewport size. Both values must be set together; otherwise Playwright defaults are used (overriding width alone can hide or move the portal login button on some breakpoints).
    /// </summary>
    public int? ViewportWidth { get; init; }
    /// <inheritdoc cref="ViewportWidth"/>
    public int? ViewportHeight { get; init; }
}
