using System.Windows;
using InvoiceAutomation.Core;

namespace InvoiceAutomation.App;

/// <summary>Shows a WPF <see cref="MessageBox"/> and waits for the user to click OK.</summary>
internal sealed class WpfUserPrompt : IUserPrompt
{
    public Task PromptAsync(string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // MessageBox.Show is synchronous; cancellation is not observed while the dialog is open.
        // The token is checked before showing and again immediately after the user dismisses it.
        MessageBox.Show(
            message,
            "Invoice Automation — waiting for you",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
