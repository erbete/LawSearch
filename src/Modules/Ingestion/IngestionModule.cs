using System.IO;
using Ingestion.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Ingestion;
using Wolverine;

namespace Ingestion;

public static class IngestionModule
{
    public static IServiceCollection AddIngestionModule(this IServiceCollection services)
    {
        services.AddOptions<IngestionSettings>()
            .BindConfiguration(IngestionSettings.SectionName)
            .ValidateDataAnnotations()
            .PostConfigure<IHostEnvironment>((settings, env) =>
                settings.DocumentsFolder = Path.GetFullPath(Path.Combine(env.ContentRootPath, settings.DocumentsFolder)))
            .ValidateOnStart();

        services.AddSingleton<IngestionRepository>();
        services.AddSingleton<PlanIngestion>();
        services.AddSingleton<IWolverineExtension, IngestionJobs>();
        services.AddHostedService<EnqueueChangedLaws>();
        return services;
    }
}