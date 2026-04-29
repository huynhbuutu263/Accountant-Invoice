namespace InvoiceAutomation.Core;

/// <summary>Abstraction for showing a blocking prompt to the user during automation.</summary>
public interface IUserPrompt
{
    /// <summary>Displays <paramref name="message"/> to the user and awaits confirmation before returning.</summary>
    Task PromptAsync(string message, CancellationToken cancellationToken = default);
}
