using System;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using SharedKernel.Embeddings;

namespace Host.Infrastructure.Setup;

public static class EmbeddingSetup
{
    public static WebApplicationBuilder AddEmbeddings(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<EmbeddingSettings>()
            .BindConfiguration(EmbeddingSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddEmbeddingGenerator(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<EmbeddingSettings>>().Value;

            return new OllamaApiClient(
                new HttpClient
                {
                    BaseAddress = new Uri(settings.Url),
                    Timeout = TimeSpan.FromMinutes(10)
                },
                settings.Model);
        });

        return builder;
    }
}