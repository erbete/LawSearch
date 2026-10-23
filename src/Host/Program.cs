using System.Text.Encodings.Web;
using Host.Infrastructure.Setup;
using Ingestion;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Query;

var builder = WebApplication.CreateBuilder(args);

builder.AddDatabase()
    .AddEmbeddings()
    .AddChat()
    .AddBackgroundJobs();

builder.Services.AddIngestionModule();
builder.Services.AddQueryModule();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

var app = builder.Build();

app.MigrateDatabase();
app.MapQueryEndpoints();

app.Run();