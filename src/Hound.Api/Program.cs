using Hound.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// TODO: Wave 2 — Register:
// - IDocumentStore (RavenDB at http://ravendb:8080)
// - IActivityLogger
// - RavenDB indexes

var app = builder.Build();

app.UseCors();
app.MapControllers();
app.MapHub<ActivityHub>("/hubs/activity");

app.Run();
