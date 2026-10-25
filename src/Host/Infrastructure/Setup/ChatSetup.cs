using System;
using System.ClientModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using SharedKernel.Chat;

namespace Host.Infrastructure.Setup;

public static class ChatSetup
{
    public static WebApplicationBuilder AddChat(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ChatSettings>()
            .BindConfiguration(ChatSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddChatClient(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<ChatSettings>>().Value;

            return new OpenAIClient(
                    new ApiKeyCredential(settings.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) })
                .GetChatClient(settings.Model)
                .AsIChatClient()
                .AsBuilder()
                .ConfigureOptions(options => options.RawRepresentationFactory = _ => new ChatCompletionOptions
                {
#pragma warning disable OPENAI001
                    ReasoningEffortLevel = new ChatReasoningEffortLevel(settings.ReasoningEffort)
#pragma warning restore OPENAI001
                })
                .Build();
        });

        return builder;
    }
}