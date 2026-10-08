using SmartX.Shared;

namespace SmartX.Api.Services;

public class CommandStreamService
{
    // Lock object — the API handles many requests at the same time.
    // Only one thread may touch the collections at once, or data gets corrupted.
    private readonly object _gate = new();

    // Key = MAC address, Value = the device. O(1) lookup when a packet arrives.
    private readonly Dictionary<string, DeviceInfo> _devices = new();

    public CommandStreamService()
    {
        SeedDevices();
    }

    private void SeedDevices()
    {
        AddDevice("AA:BB:CC:00:00:01", "Moisture Sensor A", SensorCategory.Environmental, "Zone 1 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:02", "Moisture Sensor B", SensorCategory.Environmental, "Zone 1 / Sub-Zone B");
        AddDevice("AA:BB:CC:00:00:03", "Temp Sensor C", SensorCategory.Environmental, "Zone 2 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:04", "Smart Meter 1", SensorCategory.Power, "Zone 1 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:05", "Smart Meter 2", SensorCategory.Power, "Zone 2 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:06", "Pump A", SensorCategory.Actuator, "Zone 1 / Sub-Zone B");
        AddDevice("AA:BB:CC:00:00:07", "Pump B", SensorCategory.Actuator, "Zone 2 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:08", "Relay 3", SensorCategory.Actuator, "Zone 1 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:09", "Main Valve", SensorCategory.Actuator, "Zone 1 / Sub-Zone B");
    }

    private void AddDevice(string mac, string name, SensorCategory category, string location)
    {
        _devices[mac] = new DeviceInfo
        {
            NodeId = mac,
            Name = name,
            Category = category,
            Location = location,
            LastSeen = DateTime.UtcNow,
            State = TileState.Normal
        };
    }
}