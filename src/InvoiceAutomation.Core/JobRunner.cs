using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using InvoiceAutomation.Core.Json;
using InvoiceAutomation.Core.Models;
using InvoiceAutomation.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceAutomation.Core;

public sealed class JobRunner : IJobRunner
{
    private readonly IFlowLoader _flowLoader;
    private readonly IVariableResolver _resolver;
    private readonly IStepExecutor _executor;
    private readonly IOptions<AutomationOptions> _options;
    private readonly ILogger<JobRunner> _logger;

    public JobRunner(
        IFlowLoader flowLoader,
        IVariableResolver resolver,
        IStepExecutor executor,
        IOptions<AutomationOptions> options,
        ILogger<JobRunner> logger)
    {
        _flowLoader = flowLoader;
        _resolver = resolver;
        _executor = executor;
        _options = options;
        _logger = logger;
    }

    public async Task<JobResult> RunAsync(
        string flowPath,
        JobParameters parameters,
        IAutomationPage page,
        IFileProcessor? fileProcessor = null,
        CancellationToken cancellationToken = default)
    {
        var result = new JobResult { Status = JobStatus.Completed };
        AutomationFlow flow;
        try
        {
            flow = await _flowLoader.LoadAsync(flowPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load or validate flow {Path}", flowPath);
            return new JobResult
            {
                Status = JobStatus.Failed,
                ErrorMessage = ex.Message
            };
        }

        var ctx = BuildContext(flow, parameters);
        var strict = flow.StrictVariables;
        var opt = _options.Value;

        try
        {
            foreach (var step in flow.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = await RunStepAsync(step, ctx, page, fileProcessor, strict, opt, result, cancellationToken)
                    .ConfigureAwait(false);
                if (outcome == LoopOutcome.AbortJob)
                {
                    result.Status = JobStatus.Failed;
                    result.ErrorMessage ??= "Aborted by step onError policy.";
                    return result;
                }

                if (outcome == LoopOutcome.FailJob)
                {
                    result.Status = JobStatus.Failed;
                    return result;
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Status = JobStatus.Cancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job failed");
            result.Status = JobStatus.Failed;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static FlowContext BuildContext(AutomationFlow flow, JobParameters parameters)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        if (flow.Variables is not null)
        {
            foreach (var kv in flow.Variables)
                pairs.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
        }

        pairs.Add(new("fromDate", parameters.FromDate));
        pairs.Add(new("toDate", parameters.ToDate));
        pairs.Add(new("fromDateVi", ToVietnameseDate(parameters.FromDate)));
        pairs.Add(new("toDateVi", ToVietnameseDate(parameters.ToDate)));
        pairs.Add(new("invoiceKind", parameters.InvoiceKind));
        pairs.Add(new("tab", parameters.InvoiceKind));
        pairs.Add(new("downloadsRoot", parameters.DownloadsRoot));
        pairs.Add(new("jobId", parameters.JobId.ToString("N")));
        if (!string.IsNullOrEmpty(parameters.GdtMst))
            pairs.Add(new("gdtMst", parameters.GdtMst));
        if (!string.IsNullOrEmpty(parameters.GdtPassword))
            pairs.Add(new("gdtPassword", parameters.GdtPassword));
        return new FlowContext(pairs);
    }

    private static string ToVietnameseDate(string? isoOrAny)
    {
        if (string.IsNullOrWhiteSpace(isoOrAny))
            return "";
        if (DateTime.TryParse(isoOrAny, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        if (DateTime.TryParse(isoOrAny, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out var dVi))
            return dVi.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return isoOrAny;
    }

    private async Task<LoopOutcome> RunStepAsync(
        AutomationStep step,
        FlowContext ctx,
        IAutomationPage page,
        IFileProcessor? fileProcessor,
        bool strict,
        AutomationOptions opt,
        JobResult result,
        CancellationToken ct)
    {
        if (step.Enabled == false)
        {
            _logger.LogInformation("Skipping disabled step {Name}", step.Name);
            return LoopOutcome.Continue;
        }

        var action = step.Action.Trim().ToLowerInvariant();
        if (action == "loop")
            return await RunLoopAsync(step, ctx, page, fileProcessor, strict, opt, result, ct).ConfigureAwait(false);

        return await ExecuteLeafWithRetriesAsync(step, ctx, page, fileProcessor, strict, opt, result, ct).ConfigureAwait(false);
    }

    private async Task<LoopOutcome> RunLoopAsync(
        AutomationStep loopStep,
        FlowContext ctx,
        IAutomationPage page,
        IFileProcessor? fileProcessor,
        bool strict,
        AutomationOptions opt,
        JobResult result,
        CancellationToken ct)
    {
        var loopShallow = CloneWithoutChildren(loopStep);
        var loopResolved = _resolver.ResolveStep(loopShallow, ctx, strict);
        var kind = (loopResolved.LoopKind ?? "rows").Trim().ToLowerInvariant();
        int iterations;
        if (kind == "rows")
        {
            var sel = loopResolved.RowSelector ?? "";
            iterations = await page.CountAsync(sel, ct).ConfigureAwait(false);
        }
        else
        {
            iterations = loopResolved.Count ?? 0;
        }

        var max = loopResolved.MaxIterations ?? 1000;
        iterations = Math.Min(Math.Max(iterations, 0), max);
        var rowVar = string.IsNullOrWhiteSpace(loopResolved.RowVariable) ? "rowIndex" : loopResolved.RowVariable!;

        _logger.LogInformation("Loop {Name}: {Iterations} iterations (kind {Kind})", loopResolved.Name, iterations, kind);

        if (loopStep.Children is null || loopStep.Children.Count == 0)
            return LoopOutcome.Continue;

        for (var i = 1; i <= iterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Set(rowVar, i.ToString());
            foreach (var child in loopStep.Children)
            {
                var outcome = await RunStepAsync(child, ctx, page, fileProcessor, strict, opt, result, ct).ConfigureAwait(false);
                if (outcome == LoopOutcome.AbortJob)
                    return LoopOutcome.AbortJob;
                if (outcome == LoopOutcome.FailJob)
                    return LoopOutcome.FailJob;
            }
        }

        return LoopOutcome.Continue;
    }

    private async Task<LoopOutcome> ExecuteLeafWithRetriesAsync(
        AutomationStep step,
        FlowContext ctx,
        IAutomationPage page,
        IFileProcessor? fileProcessor,
        bool strict,
        AutomationOptions opt,
        JobResult result,
        CancellationToken ct)
    {
        var maxAttempts = step.Retry?.Count ?? opt.DefaultRetries;
        maxAttempts = Math.Max(1, maxAttempts);
        var backoffs = step.Retry?.BackoffMs is { Length: > 0 } b ? b : opt.RetryBackoffMs;

        Exception? last = null;
        var swTotal = Stopwatch.StartNew();
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var resolved = _resolver.ResolveStep(step, ctx, strict);
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("Step {Name} ({Action}) attempt {Attempt}/{Max}", resolved.Name, resolved.Action, attempt, maxAttempts);
                await _executor.ExecuteAsync(resolved, page, fileProcessor, opt.DefaultTimeoutMs, ct).ConfigureAwait(false);
                if (resolved.Expect is not null)
                    await page.ExpectAsync(resolved.Expect, resolved.TimeoutMs ?? opt.DefaultTimeoutMs, ct).ConfigureAwait(false);

                sw.Stop();
                result.Steps.Add(new StepExecutionRecord
                {
                    Name = resolved.Name,
                    Action = resolved.Action,
                    Success = true,
                    Attempts = attempt,
                    DurationMs = sw.ElapsedMilliseconds
                });
                _logger.LogInformation("Step {Name} succeeded in {Ms}ms", resolved.Name, sw.ElapsedMilliseconds);
                return LoopOutcome.Continue;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
                sw.Stop();
                _logger.LogWarning(ex, "Step {Name} failed attempt {Attempt}/{Max}", resolved.Name, attempt, maxAttempts);

                if (IsNonRetryable(ex, opt))
                    break;

                if (attempt < maxAttempts)
                {
                    var delay = GetBackoffMs(backoffs, attempt - 1);
                    if (delay > 0)
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
        }

        swTotal.Stop();
        var onError = (step.OnError ?? "fail").Trim().ToLowerInvariant();
        var message = last?.Message ?? "Unknown error";
        result.Steps.Add(new StepExecutionRecord
        {
            Name = step.Name,
            Action = step.Action,
            Success = false,
            Attempts = maxAttempts,
            DurationMs = swTotal.ElapsedMilliseconds,
            Error = message
        });

        return onError switch
        {
            "continue" => LoopOutcome.Continue,
            "abortjob" => LoopOutcome.AbortJob,
            _ => LoopOutcome.FailJob
        };
    }

    private static bool IsNonRetryable(Exception ex, AutomationOptions opt)
    {
        var text = ex.Message ?? "";
        foreach (var s in opt.NonRetryableSubstrings)
        {
            if (text.Contains(s, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int GetBackoffMs(int[] backoffs, int zeroBasedIndex)
    {
        if (backoffs.Length == 0)
            return 1000;
        if (zeroBasedIndex < backoffs.Length)
            return backoffs[zeroBasedIndex];
        return backoffs[^1];
    }

    private static AutomationStep CloneWithoutChildren(AutomationStep s)
    {
        var json = JsonSerializer.Serialize(s, FlowJsonDefaults.Options);
        var clone = JsonSerializer.Deserialize<AutomationStep>(json, FlowJsonDefaults.Options)
                    ?? throw new InvalidOperationException("Clone failed.");
        clone.Children = null;
        return clone;
    }

    private enum LoopOutcome
    {
        Continue,
        FailJob,
        AbortJob
    }
}
