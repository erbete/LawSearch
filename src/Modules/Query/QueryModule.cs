using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Query;

namespace Query;

public static class QueryModule
{
    public static IServiceCollection AddQueryModule(this IServiceCollection services)
    {
        services.AddOptions<QuerySettings>()
            .BindConfiguration(QuerySettings.SectionName)
            .ValidateDataAnnotations()
            .Validate(s => s.RerankCandidates >= s.TopK, "Query:RerankCandidates must be at least Query:TopK")
            .ValidateOnStart();

        services.AddOptions<RerankSettings>()
            .BindConfiguration(RerankSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(RerankClient.HttpClientName, (sp, http) =>
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<RerankSettings>>().Value.Url));

        services.AddSingleton<QueryRepository>();
        services.AddSingleton<RerankClient>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<AnswerService>();
        return services;
    }

    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/search", async (SearchService search, string q, int? k, CancellationToken ct) =>
            await search.SearchAsync(q, k, ct));

        app.MapGet("/ask", async (AnswerService answers, string q, Guid? c, CancellationToken ct) =>
        {
            var result = await answers.AnswerAsync(q, c, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return app;
    }
}