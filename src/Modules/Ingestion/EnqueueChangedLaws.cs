using System;
using System.Threading;
using System.Threading.Tasks;
using Ingestion.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine.Runtime;

namespace Ingestion;

internal sealed partial class EnqueueChangedLaws(
    IHostApplicationLifetime lifetime,
    IWolverineRuntime runtime,
    PlanIngestion planIngestion,
    ILogger<EnqueueChangedLaws> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        using (stoppingToken.Register(() => started.TrySetCanceled(stoppingToken)))
        {
            await started.Task;
        }

        try
        {
            var plan = await planIngestion.HandleAsync(stoppingToken);
            var bus = new MessageBus(runtime);

            foreach (var law in plan.ToRemove)
            {
                await bus.SendAsync(new RemoveLaw(law.DocumentId));
            }

            foreach (var fileName in plan.FilesToIngest)
            {
                await bus.SendAsync(new IngestLaw(fileName));
            }

            LogPlanned(logger, plan.ToRemove.Count, plan.FilesToIngest.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPlanningFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued {Removed} removals and {Ingested} ingestions")]
    private static partial void LogPlanned(ILogger logger, int removed, int ingested);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to plan ingestion")]
    private static partial void LogPlanningFailed(ILogger logger, Exception exception);
}