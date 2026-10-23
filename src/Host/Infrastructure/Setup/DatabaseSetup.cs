using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Database;
using SharedKernel.Embeddings;

namespace Host.Infrastructure.Setup;

public static class DatabaseSetup
{
    public static WebApplicationBuilder AddDatabase(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<DatabaseSettings>()
            .BindConfiguration(DatabaseSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<DatabaseSettings>>().Value;
            var dataSource = new NpgsqlDataSourceBuilder(settings.DefaultConnection);
            dataSource.UseVector();
            return dataSource.Build();
        });

        return builder;
    }

    public static WebApplication MigrateDatabase(this WebApplication app)
    {
        var database = app.Services.GetRequiredService<IOptions<DatabaseSettings>>().Value;
        var embedding = app.Services.GetRequiredService<IOptions<EmbeddingSettings>>().Value;

        Migrator.Run(database.DefaultConnection, embedding.Dimensions);
        return app;
    }
}