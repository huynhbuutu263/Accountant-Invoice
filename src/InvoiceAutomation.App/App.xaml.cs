using System.IO;
using System.Windows;
using InvoiceAutomation.App.Configuration;
using InvoiceAutomation.App.Logging;
using InvoiceAutomation.Core;
using InvoiceAutomation.Core.Options;
using InvoiceAutomation.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace InvoiceAutomation.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var baseDir = AppContext.BaseDirectory;
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvoiceAutomation", "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, "automation-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logFile, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var sink = new ObservableLogSink();
        var services = new ServiceCollection();
        services.AddSingleton(sink);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.Configure<AutomationOptions>(configuration.GetSection(AutomationOptions.SectionName));
        services.Configure<BrowserOptions>(configuration.GetSection(BrowserOptions.SectionName));
        services.Configure<FlowsOptions>(configuration.GetSection(FlowsOptions.SectionName));
        services.Configure<DownloadsOptions>(configuration.GetSection(DownloadsOptions.SectionName));

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddSerilog(dispose: true);
            builder.AddDebug();
            builder.AddProvider(new ObservableLoggerProvider(sink));
        });

        services.AddInvoiceAutomationServices();
        services.AddSingleton<IUserPrompt, WpfUserPrompt>();
        services.AddSingleton<IVariableResolver, VariableResolver>();
        services.AddSingleton<IJobRunner, JobRunner>();
        services.AddSingleton<MainViewModel>();

        _services = services.BuildServiceProvider();

        var main = new MainWindow(_services.GetRequiredService<MainViewModel>());
        main.Show();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        Log.CloseAndFlush();
        _services?.Dispose();
    }
}
