using System.ComponentModel;
using System.Windows;

namespace InvoiceAutomation.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        PasswordInput.PasswordChanged += (_, _) => _viewModel.SetPassword(PasswordInput.Password);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        // Dispose the browser synchronously so the Chromium process is not left running after the app exits.
        // Task.Run ensures the async work starts on a thread-pool thread, avoiding UI-thread deadlocks.
        Task.Run(() => _viewModel.CleanupBrowserAsync()).GetAwaiter().GetResult();
    }
}
