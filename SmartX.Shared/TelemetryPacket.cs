using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared;

public enum SensorCategory
{
    Environmental,
    Power,
    Actuator
}

public enum TileState
{
    Normal,
    Anomalous,
    Stale,
    Dead
}

// Generic wrapper — handles float, int, bool readings without boxing/unboxing.
// T is constrained to struct so only value types are allowed, which is what
// keeps this boxing-free: no object allocation to wrap the value.
public class TelemetryPacket<T> where T : struct
{
    public required string NodeId { get; init; }
    public required T Value { get; init; }
    public required DateTime Timestamp { get; init; }
}
