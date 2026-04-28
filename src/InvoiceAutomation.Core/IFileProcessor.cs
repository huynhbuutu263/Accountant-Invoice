namespace InvoiceAutomation.Core;

public interface IFileProcessor
{
    Task<IReadOnlyList<string>> ExtractZipAsync(string zipPath, CancellationToken cancellationToken = default);
}
