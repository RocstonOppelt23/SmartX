using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared
{
    public enum AlertSeverity  //Displays the reading
    {
        Normal,
        Warning,
        Critical
              
    }

    public class DeviceInfo //Model class
    {
        public required string NodeId { get; init; }
        public required string Name { get; init; }
        public required SensorCategory Category{  get; init; }
        public required string Location { get; init; }
        public double LastValue { get; set; }
        public DateTime LastSeen { get; set; }
        public TileState State { get; set; }        //This is a custom type
        public bool IsOn { get; set; }

    }

    public record IncomingTelemetry(string NodeId, double Value, DateTime Timestamp);
   
}
