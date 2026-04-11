using System;

namespace HttpMonitor.Models
{
    public class StatisticsViewModel
    {
        public int TotalRequests { get; set; }
        public int GetRequests { get; set; }
        public int PostRequests { get; set; }
        public int PutRequests { get; set; }
        public int DeleteRequests { get; set; }
        public double AverageProcessingTime { get; set; }
        public DateTime ServerStartTime { get; set; }
        public TimeSpan Uptime => DateTime.Now - ServerStartTime;

        public class LoadPoint
        {
            public DateTime Time { get; set; }
            public int RequestCount { get; set; }
        }
    }
}