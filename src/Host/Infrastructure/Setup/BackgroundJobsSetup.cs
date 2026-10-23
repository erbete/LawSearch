using System;
using Microsoft.AspNetCore.Builder;
using SharedKernel.Database;
using Wolverine;
using Wolverine.Postgresql;

namespace Host.Infrastructure.Setup;

public static class AddBackgroundJobsSetup
{
    public static WebApplicationBuilder AddBackgroundJobs(this WebApplicationBuilder builder)
    {
        string connectionString =
            builder.Configuration[$"{DatabaseSettings.SectionName}:{nameof(DatabaseSettings.DefaultConnection)}"]
            ?? throw new InvalidOperationException("Database connection string is required");

        builder.Host.UseWolverine(opts =>
        {
            opts.PersistMessagesWithPostgresql(connectionString, "wolverine");
        });

        return builder;
    }
}