using System;
using System.Globalization;
using DbUp;

namespace SharedKernel.Database;

public static class Migrator
{
    public static void Run(string connectionString, int embeddingDimensions)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(Migrator).Assembly,
                name => name.Contains(".Scripts."))
            .WithVariable("EmbeddingDimensions", embeddingDimensions.ToString(CultureInfo.InvariantCulture))
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed", result.Error);
        }
    }
}