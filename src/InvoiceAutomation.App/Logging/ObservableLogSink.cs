using System.Collections.ObjectModel;
using System.Windows;

namespace InvoiceAutomation.App.Logging;

public sealed class ObservableLogSink
{
    public ObservableCollection<string> Lines { get; } = new();

    public void Push(string line)
    {
        var app = Application.Current;
        if (app?.Dispatcher is null)
        {
            lock (Lines)
            {
                Lines.Add(line);
                Trim();
            }

            return;
        }

        app.Dispatcher.Invoke(() =>
        {
            Lines.Add(line);
            Trim();
        });
    }

    private void Trim()
    {
        while (Lines.Count > 500)
            Lines.RemoveAt(0);
    }
}
