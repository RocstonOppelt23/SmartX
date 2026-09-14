namespace SmartX.Shared;

// Overloaded operator with real structural purpose — not just a demo.
// Subtracting two readings gives the delta your report describes,
// which is what the anomaly check compares against the threshold.
public class SensorReading
{
    public required double Value { get; init; }

    public static double operator -(SensorReading a, SensorReading b)
        => a.Value - b.Value;

    public static bool operator >(SensorReading a, SensorReading b)
        => a.Value > b.Value;

    public static bool operator <(SensorReading a, SensorReading b)
        => a.Value < b.Value;
}

public static class AnomalyThreshold
{
    // Uses the overloaded operators above — this is the check your
    // report describes: delta between incoming and baseline vs threshold.
    public static bool IsAnomalous(SensorReading incoming, SensorReading baseline, double threshold)
    {
        double delta = incoming - baseline;   // operator -
        return delta > threshold || delta < -threshold;
    }
}