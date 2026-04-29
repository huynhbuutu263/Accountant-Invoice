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
}
