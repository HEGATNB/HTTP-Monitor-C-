using System;

namespace HttpMonitor.Models
{
    public class HttpRequestLog
    {
        public int Id { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Headers { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string ResponseStatus { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public long ProcessingTimeMs { get; set; }
        public bool IsIncoming { get; set; }
    }
}