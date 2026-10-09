using System.Text.Json.Serialization;
using SmartX.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Register command stream service and background processor
builder.Services.AddSingleton<SmartX.Api.Services.CommandStreamService>();
builder.Services.AddHostedService<SmartX.Api.Services.QueueProcessorWorker>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Lets enums like SensorCategory bind from JSON strings
    // ("Environmental") instead of only raw numbers.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors();

app.MapSensorEndpoints();
app.MapFileEndpoints();
app.MapTelemetryEndpoints();

TelemetryEndpoints.SeedMockData();

app.Run();