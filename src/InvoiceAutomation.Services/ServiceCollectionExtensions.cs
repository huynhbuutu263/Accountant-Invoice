using InvoiceAutomation.Core;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceAutomation.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInvoiceAutomationServices(this IServiceCollection services)
    {
        services.AddSingleton<IFlowLoader, FlowLoader>();
        services.AddSingleton<IStepExecutor, PlaywrightStepExecutor>();
        services.AddSingleton<IFileProcessor, FileProcessor>();
        return services;
    }
}
