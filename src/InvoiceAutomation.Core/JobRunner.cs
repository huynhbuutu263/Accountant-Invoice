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
        if (kind == "pages")
            return await RunPagesLoopAsync(loopStep, loopResolved, ctx, page, fileProcessor, strict, opt, result, ct)
                .ConfigureAwait(false);

        int iterations;
        var rowSelector = loopResolved.RowSelector ?? "";
        if (kind == "rows")
        {
            if (ctx.TryGet("pageIndex", out var pageIndex) && pageIndex != "1")
                await page.DelayAsync(250, ct).ConfigureAwait(false);

            iterations = await page.CountDataRowsAsync(rowSelector, ct).ConfigureAwait(false);
            if (iterations == 0)
            {
                await page.DelayAsync(400, ct).ConfigureAwait(false);
                iterations = await page.CountDataRowsAsync(rowSelector, ct).ConfigureAwait(false);
            }
        }
        else
            iterations = loopResolved.Count ?? 0;

        var max = loopResolved.MaxIterations ?? 1000;
        var rowVar = string.IsNullOrWhiteSpace(loopResolved.RowVariable) ? "rowIndex" : loopResolved.RowVariable!;

        _logger.LogInformation("Loop {Name}: {Iterations} data row(s) on page (kind {Kind})", loopResolved.Name, iterations, kind);

        if (loopStep.Children is null || loopStep.Children.Count == 0)
            return LoopOutcome.Continue;

        if (kind == "rows" && iterations == 0)
        {
            _logger.LogWarning("Loop {Name}: no data rows detected — check table selector / search results", loopResolved.Name);
            return LoopOutcome.Continue;
        }

        var rowLimit = kind == "rows" ? Math.Min(iterations, max) : Math.Min(Math.Max(iterations, 0), max);

        for (var i = 1; i <= rowLimit; i++)
        {
            ct.ThrowIfCancellationRequested();

            ctx.Set(rowVar, i.ToString());
            var rowNthOffset = ctx.TryGet("rowNthOffset", out var offsetStr) && int.TryParse(offsetStr, out var off)
                ? off
                : 0;
            var rowNth = i - 1 + rowNthOffset;
            ctx.Set("rowNth", rowNth.ToString());
            ctx.Set("rowIndex0", rowNth.ToString());
            _logger.LogInformation(
                "Loop {Name}: row {Current}/{Total} (rowNth={RowNth}, offset={Offset})",
                loopResolved.Name, i, rowLimit, rowNth, rowNthOffset);

            var skipRestOfRow = false;
            foreach (var child in loopStep.Children)
            {
                if (skipRestOfRow)
                    continue;

                var stepsBefore = result.Steps.Count;
                var outcome = await RunStepAsync(child, ctx, page, fileProcessor, strict, opt, result, ct).ConfigureAwait(false);
                if (outcome == LoopOutcome.AbortJob)
                    return LoopOutcome.AbortJob;
                if (outcome == LoopOutcome.FailJob)
                    return LoopOutcome.FailJob;

                if (kind == "rows"
                    && child.Action.Equals("click", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(child.OnError, "continue", StringComparison.OrdinalIgnoreCase)
                    && result.Steps.Count > stepsBefore
                    && !result.Steps[^1].Success)
                {
                    skipRestOfRow = true;
                    _logger.LogInformation(
                        "Loop {Name}: row {Current} click failed — skipping download for this row",
                        loopResolved.Name, i);
                }
            }
        }

        return LoopOutcome.Continue;
    }

    private async Task<LoopOutcome> RunPagesLoopAsync(
        AutomationStep loopStep,
        AutomationStep loopResolved,
        FlowContext ctx,
        IAutomationPage page,
        IFileProcessor? fileProcessor,
        bool strict,
        AutomationOptions opt,
        JobResult result,
        CancellationToken ct)
    {
        if (loopStep.Children is null || loopStep.Children.Count == 0)
            return LoopOutcome.Continue;

        var maxPages = loopResolved.MaxIterations ?? 100;
        var nextSelector = loopResolved.Selector;
        var pageTimeout = Math.Min(loopResolved.TimeoutMs ?? 5_000, 6_000);

        for (var pageNum = 1; pageNum <= maxPages; pageNum++)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Set("pageIndex", pageNum.ToString());
            _logger.LogInformation("Loop {Name}: page {Page}", loopResolved.Name, pageNum);

            foreach (var child in loopStep.Children)
            {
                var outcome = await RunStepAsync(child, ctx, page, fileProcessor, strict, opt, result, ct)
                    .ConfigureAwait(false);
                if (outcome == LoopOutcome.AbortJob)
                    return LoopOutcome.AbortJob;
                if (outcome == LoopOutcome.FailJob)
                    return LoopOutcome.FailJob;
            }

            if (pageNum >= maxPages)
                break;

            if (!await page.TryClickPaginationNextAsync(nextSelector, pageTimeout, ct).ConfigureAwait(false))
            {
                _logger.LogInformation("Loop {Name}: finished after page {Page} (no next page)", loopResolved.Name, pageNum);
                break;
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
                var stepOutput = await _executor.ExecuteAsync(resolved, page, fileProcessor, ctx, opt.DefaultTimeoutMs, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(stepOutput))
                {
                    ctx.Set("rowFilePath", stepOutput);
                    ctx.Set("filePath", stepOutput);
                    _logger.LogInformation("Step {Name} set rowFilePath={Path}", resolved.Name, stepOutput);
                }
                else if (resolved.BuildRowPath == true &&
                         resolved.Action.Equals("click", StringComparison.OrdinalIgnoreCase))
                {
                    var fallback = Path.Combine("gdt", ctx.GetOrEmpty("jobId"), $"row_{ctx.GetOrEmpty("rowIndex")}");
                    ctx.Set("rowFilePath", fallback);
                    ctx.Set("filePath", fallback);
                    _logger.LogWarning("Step {Name}: using fallback rowFilePath={Path}", resolved.Name, fallback);
                }

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
