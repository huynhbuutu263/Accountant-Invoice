namespace InvoiceAutomation.Core.Models;

/// <summary>JSON root flow.</summary>
public sealed class AutomationFlow
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Description { get; set; }
    public bool StrictVariables { get; set; }
    /// <summary>When true, the browser stays open after the job completes until the user clicks OK.</summary>
    public bool KeepBrowserOpen { get; set; }
    public Dictionary<string, string>? Variables { get; set; }
    public List<AutomationStep> Steps { get; set; } = new();
}
