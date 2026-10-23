using Ingestion.Pipeline;
using Wolverine;

namespace Ingestion;

internal sealed class IngestionJobs : IWolverineExtension
{
    private const string QueueName = "ingestion";

    public void Configure(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(IngestionJobs).Assembly);

        options.LocalQueue(QueueName)
            .UseDurableInbox()
            .Sequential();

        options.PublishMessage<IngestLaw>().ToLocalQueue(QueueName);
        options.PublishMessage<RemoveLaw>().ToLocalQueue(QueueName);
    }
}