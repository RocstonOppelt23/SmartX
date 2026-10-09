using System.Text.Json.Serialization;
using SmartX.Api.Endpoints;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Part 2 — Real-Time Command Stream.
// Singletons: one shared copy of the queues/dictionaries for the whole API.
builder.Services.AddSingleton<ActionEngine>();
builder.Services.AddSingleton<CommandStreamService>();

// Background workers: one drains the queues, one pretends to be the IoT devices.
builder.Services.AddHostedService<QueueProcessorWorker>();
builder.Services.AddHostedService<DeviceSimulatorWorker>();

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

// Part 1
app.MapSensorEndpoints();
app.MapFileEndpoints();
app.MapTelemetryEndpoints();

// Part 2
app.MapCommandStreamEndpoints();

TelemetryEndpoints.SeedMockData();

app.Run();
