using Hound.Api.Hubs;
using Hound.Api.Indexes;
using Hound.Api.Repositories;
using Hound.Api.Services;
using Hound.Core.Logging;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations;
using Raven.Client.Exceptions;
using Raven.Client.Exceptions.Database;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://localhost:4201")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// RavenDB IDocumentStore
var ravenUrl = builder.Configuration["RavenDB:Url"] ?? "http://ravendb:8080";
var ravenDatabase = builder.Configuration["RavenDB:Database"] ?? "HoundAI";
var store = new DocumentStore
{
    Urls = new[] { ravenUrl },
    Database = ravenDatabase
};
store.Initialize();

// Ensure the database exists
try
{
    store.Maintenance.ForDatabase(ravenDatabase).Send(new GetStatisticsOperation());
}
catch (DatabaseDoesNotExistException)
{
    store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(ravenDatabase)));
}

IndexCreation.CreateIndexes(typeof(ActivityLog_ByPackAndTime).Assembly, store);
builder.Services.AddSingleton<IDocumentStore>(store);

// Repositories and services
builder.Services.AddScoped<IPackRepository, RavenPackRepository>();
builder.Services.AddScoped<IActivityLogger, RavenActivityService>();
builder.Services.AddScoped<ITunerExperimentRepository, RavenTunerExperimentRepository>();
builder.Services.AddScoped<IWatchtowerRepository, RavenWatchtowerRepository>();
builder.Services.AddScoped<ITradeRepository, RavenTradeRepository>();
builder.Services.AddSingleton<TunerStateService>();
builder.Services.AddHttpClient("health");
builder.Services.AddScoped<IHealthCheckService, HealthCheckService>();

var app = builder.Build();

app.UseCors();
app.MapControllers();
app.MapHub<ActivityHub>("/hubs/activity");

app.Run();
