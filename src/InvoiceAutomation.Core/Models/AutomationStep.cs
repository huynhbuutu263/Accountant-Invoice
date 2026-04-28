namespace InvoiceAutomation.Core.Models;

/// <summary>JSON: single step object.</summary>
public sealed class AutomationStep
{
    public string Name { get; set; } = "";
    public string Action { get; set; } = "";
    public bool? Enabled { get; set; }
    public string? Selector { get; set; }
    public string? Value { get; set; }
    public int? TimeoutMs { get; set; }
    public string? WaitUntil { get; set; }
    /// <summary>selectorVisible, selectorHidden, delay, networkIdle</summary>
    public string? WaitKind { get; set; }
    public StepExpect? Expect { get; set; }
    public StepRetry? Retry { get; set; }
    /// <summary>fail, continue, abortJob</summary>
    public string? OnError { get; set; }
    public string? Comment { get; set; }
    public string? SavePath { get; set; }
    public string? LoopKind { get; set; }
    public string? RowSelector { get; set; }
    public int? Count { get; set; }
    public string? RowVariable { get; set; }
    public int? MaxIterations { get; set; }
    public bool? ClearFirst { get; set; }
    /// <summary>When true with action fill: set input value via page script (for readonly Ant Design DatePicker, etc.).</summary>
    public bool? JavaScriptFill { get; set; }
    public List<AutomationStep>? Children { get; set; }
}
