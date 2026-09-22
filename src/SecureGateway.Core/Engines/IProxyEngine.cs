using System;
using System.Threading.Tasks;
using SecureGateway.Models;

namespace SecureGateway.Core.Engines
{
    public enum EngineStatus
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Error
    }

    public class EngineStatusChangedEventArgs : EventArgs
    {
        public EngineStatus Status { get; }
        public string Message { get; }

        public EngineStatusChangedEventArgs(EngineStatus status, string message = "")
        {
            Status = status;
            Message = message;
        }
    }

    public class EngineLogEventArgs : EventArgs
    {
        public string Message { get; }
        public string Level { get; }

        public EngineLogEventArgs(string message, string level = "Info")
        {
            Message = message;
            Level = level;
        }
    }

    public interface IProxyEngine : IDisposable
    {
        EngineStatus Status { get; }
        event EventHandler<EngineStatusChangedEventArgs> StatusChanged;
        event EventHandler<EngineLogEventArgs> LogReceived;

        Task StartAsync(ServerProfile profile);
        Task StopAsync();
        Task<double> TestLatencyAsync(ServerProfile profile);
        Task<(long uplink, long downlink)> QueryTrafficStatsAsync();
    }
}
