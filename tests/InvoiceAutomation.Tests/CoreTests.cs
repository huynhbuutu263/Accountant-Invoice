using InvoiceAutomation.Core;
using InvoiceAutomation.Core.Models;
using InvoiceAutomation.Core.Options;
using InvoiceAutomation.Core.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InvoiceAutomation.Tests;

public sealed class VariableResolverTests
{
    [Fact]
    public void Resolves_multiple_placeholders()
    {
        var r = new VariableResolver();
        var ctx = new FlowContext(new[]
        {
            new KeyValuePair<string, string>("fromDate", "2025-01-01"),
            new KeyValuePair<string, string>("toDate", "2025-01-31")
        });
        var s = r.Resolve("{{fromDate}}..{{toDate}}", ctx, strict: false);
        Assert.Equal("2025-01-01..2025-01-31", s);
    }

    [Fact]
    public void Strict_throws_on_missing()
    {
        var r = new VariableResolver();
        var ctx = new FlowContext(Array.Empty<KeyValuePair<string, string>>());
        Assert.Throws<InvalidOperationException>(() => r.Resolve("{{missing}}", ctx, strict: true));
    }

    [Fact]
    public void Resolves_nested_placeholders()
    {
        var r = new VariableResolver();
        var ctx = new FlowContext(new[]
        {
            new KeyValuePair<string, string>("rowIndex", "3"),
            new KeyValuePair<string, string>("tpl", "tr:nth-child({{rowIndex}}) a"),
            new KeyValuePair<string, string>("sel", "{{tpl}}")
        });
        var s = r.Resolve("{{sel}}", ctx, strict: false);
        Assert.Equal("tr:nth-child(3) a", s);
    }
}

public sealed class FlowValidatorTests
{
    [Fact]
    public void Rejects_unknown_action()
    {
        var flow = new AutomationFlow
        {
            Name = "t",
            Version = "1",
            Steps =
            [
                new AutomationStep { Name = "x", Action = "jumps", Value = "https://x" }
            ]
        };
        Assert.Throws<FlowValidationException>(() => FlowValidator.Validate(flow));
    }
}

public sealed class MemoryFlowLoader : IFlowLoader
{
    public required AutomationFlow Flow { get; init; }
    public Task<AutomationFlow> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(Flow);
}

public sealed class RecordingExecutor : IStepExecutor
{
    public List<string> Executed { get; } = new();
    public Task ExecuteAsync(AutomationStep step, IAutomationPage page, IFileProcessor? fileProcessor, int defaultTimeoutMs, CancellationToken cancellationToken = default)
    {
        Executed.Add(step.Name);
        return Task.CompletedTask;
    }
}

public sealed class StubPage : IAutomationPage
{
    public string? Url { get; set; } = "https://stub";
    public Task GotoAsync(string url, string? waitUntil, int? timeoutMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ClickAsync(string selector, int? timeoutMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task FillAsync(string selector, string value, bool clearFirst, int? timeoutMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetInputValueWithJavaScriptAsync(string selector, string value, int? timeoutMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task WaitForSelectorAsync(string selector, string state, int? timeoutMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<string> DownloadAsync(string selector, string savePath, int? timeoutMs, CancellationToken cancellationToken = default) => Task.FromResult(savePath);
    public Task<int> CountAsync(string selector, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task PressAsync(string? selector, string key, int? timeoutMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SelectOptionAsync(string selector, string optionValueOrLabel, int? timeoutMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UploadAsync(string selector, string filePath, int? timeoutMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ExpectAsync(StepExpect expect, int? defaultTimeoutMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class JobRunnerTests
{
    [Fact]
    public async Task Runs_loop_count_and_expands_children()
    {
        var flow = new AutomationFlow
        {
            Name = "t",
            Version = "1",
            Steps =
            [
                new AutomationStep { Name = "open", Action = "navigate", Value = "https://example.com" },
                new AutomationStep
                {
                    Name = "rows",
                    Action = "loop",
                    LoopKind = "count",
                    Count = 2,
                    RowVariable = "rowIndex",
                    Children =
                    [
                        new AutomationStep { Name = "inner", Action = "click", Selector = "#btn" }
                    ]
                }
            ]
        };

        var loader = new MemoryFlowLoader { Flow = flow };
        var resolver = new VariableResolver();
        var executor = new RecordingExecutor();
        var opts = Options.Create(new AutomationOptions());
        var runner = new JobRunner(loader, resolver, executor, opts, NullLogger<JobRunner>.Instance);
        var parameters = new JobParameters
        {
            FromDate = "a",
            ToDate = "b",
            InvoiceKind = "sales",
            DownloadsRoot = "C:\\tmp",
            JobId = Guid.NewGuid()
        };

        var result = await runner.RunAsync("ignored.json", parameters, new StubPage(), null, CancellationToken.None);
        Assert.Equal(JobStatus.Completed, result.Status);
        Assert.Equal(new[] { "open", "inner", "inner" }, executor.Executed);
    }
}
