namespace InvoiceAutomation.Core;

public interface IJobRunner
{
    Task<JobResult> RunAsync(
        string flowPath,
        JobParameters parameters,
        IAutomationPage page,
        IFileProcessor? fileProcessor = null,
        CancellationToken cancellationToken = default);
}
