using System;

namespace HttpMonitor.Models
{
    public class ServerMessage
    {
        public int Id { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; }
    }
}