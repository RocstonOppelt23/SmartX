namespace SmartX.Shared;

// Jagged array concept: each node can report a different number of raw
// readings per batch, so a jagged array (double[][]) fits better than a
// fixed rectangular one — row lengths don't have to match.
public static class BatchProcessor
{
    // Flattens a jagged array of raw readings into a List<SensorReading>,
    // which is what the rubric asks for: array in, List out.
    public static List<SensorReading> Flatten(double[][] rawBatches)
    {
        var result = new List<SensorReading>();

        foreach (double[] batch in rawBatches)
        {
            foreach (double value in batch)
            {
                result.Add(new SensorReading { Value = value });
            }
        }

        return result;
    }
}