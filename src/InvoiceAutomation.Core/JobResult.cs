namespace InvoiceAutomation.Core;

public enum JobStatus
{
    Completed,
    Failed,
    Cancelled
}

public sealed class JobResult
{
    public JobStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public List<StepExecutionRecord> Steps { get; } = new();
}

public sealed class StepExecutionRecord
{
    public string Name { get; init; } = "";
    public string Action { get; init; } = "";
    public bool Success { get; init; }
    public int Attempts { get; init; }
    public long DurationMs { get; init; }
    public string? Error { get; init; }
}
