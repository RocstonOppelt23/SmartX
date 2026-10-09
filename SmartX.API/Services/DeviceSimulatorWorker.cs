using System.Net.Http.Json;
using SmartX.Shared;

namespace SmartX.Api.Services;

// Pretends to be a network of ESP32 sensors and smart meters.
// Every second it POSTs a reading for each sensor to the API over HTTP —
// exactly like real hardware would — so the queues are fed from outside.
public class DeviceSimulatorWorker : BackgroundService
{
    private readonly CommandStreamService _service;
    private readonly ILogger<DeviceSimulatorWorker> _logger;
    private readonly HttpClient _http;
    private readonly Random _random = new();

    // Current simulated value for each sensor.
    private readonly Dictionary<string, double> _values = new();

    private DateTime _nextDropout = DateTime.UtcNow.AddSeconds(30);
    private DateTime _nextCritical = DateTime.UtcNow.AddSeconds(20);

    public DeviceSimulatorWorker(CommandStreamService service, IConfiguration config, ILogger<DeviceSimulatorWorker> logger)
    {
        _service = service;
        _logger = logger;

        var baseUrl = config["Simulator:BaseUrl"] ?? "http://localhost:5119";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the API a moment to start listening.
        await SafeDelay(3000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_service.SimulatorEnabled)
                {
                    await TickAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                // Never let the simulator crash the API.
                _logger.LogWarning("Simulator tick failed: {Message}", ex.Message);
            }

            await SafeDelay(1000, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken token)
    {
        var sensors = _service.GetSensorIds();
        var now = DateTime.UtcNow;

        // Every ~30s one random sensor drops off the network for 20s.
        if (now >= _nextDropout && sensors.Count > 0)
        {
            _service.ForceOffline(sensors[_random.Next(sensors.Count)], TimeSpan.FromSeconds(20));
            _nextDropout = now.AddSeconds(30);
        }

        // Every ~25s one random sensor reports a critical value.
        string? criticalTarget = null;
        if (now >= _nextCritical && sensors.Count > 0)
        {
            criticalTarget = sensors[_random.Next(sensors.Count)];
            _nextCritical = now.AddSeconds(25);
        }

        foreach (var mac in sensors)
        {
            if (_service.IsOffline(mac)) continue; // simulated dropout — send nothing

            var device = _service.GetDevice(mac);
            if (device is null) continue;

            var value = NextValue(mac, device.Category, mac == criticalTarget);
            var packet = new IncomingTelemetry(mac, Math.Round(value, 1), DateTime.UtcNow);

            await _http.PostAsJsonAsync("api/stream/telemetry", packet, token);
        }
    }

    // Realistic drifting values: a little random noise, pulled gently back
    // towards a normal level so readings don't wander off forever.
    private double NextValue(string mac, SensorCategory category, bool forceCritical)
    {
        var (normal, noise, min, max, critical) = category switch
        {
            SensorCategory.Environmental => (45.0, 2.0, 10.0, 70.0, 12.0),      // soil moisture %
            SensorCategory.Power => (1500.0, 150.0, 600.0, 3800.0, 3500.0),     // watts
            _ => (0.0, 0.0, 0.0, 0.0, 0.0)
        };

        if (forceCritical)
        {
            _values[mac] = critical;
            return critical;
        }

        var current = _values.TryGetValue(mac, out var v) ? v : normal;
        var drift = (normal - current) * 0.05;                // pull back towards normal
        var jitter = (_random.NextDouble() * 2 - 1) * noise;  // random noise

        var next = Math.Clamp(current + drift + jitter, min, max);
        _values[mac] = next;
        return next;
    }

    private static async Task SafeDelay(int ms, CancellationToken token)
    {
        try { await Task.Delay(ms, token); }
        catch (TaskCanceledException) { }
    }

    public override void Dispose()
    {
        _http.Dispose();
        base.Dispose();
    }
}
