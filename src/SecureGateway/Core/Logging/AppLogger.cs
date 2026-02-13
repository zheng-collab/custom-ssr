using System;
using System.Collections.Generic;
using System.IO;

namespace SecureGateway.Core.Logging
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }

        public override string ToString() =>
            $"[{Timestamp:HH:mm:ss}] [{Level}] [{Source}] {Message}";
    }

    public class AppLogger
    {
        private readonly string _logDir;
        private readonly List<LogEntry> _entries = new();
        private readonly object _lock = new();
        private StreamWriter _writer;
        private string _currentLogFile;

        public event EventHandler<LogEntry> LogAdded;

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_lock)
                    return _entries.AsReadOnly();
            }
        }

        public AppLogger(string logDir)
        {
            _logDir = logDir;
            Directory.CreateDirectory(_logDir);
            RotateLogFile();
        }

        public void Log(string message, string level = "Info", string source = "App")
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Source = source
            };

            lock (_lock)
            {
                _entries.Add(entry);

                // Keep only last 10000 entries in memory
                if (_entries.Count > 10000)
                    _entries.RemoveRange(0, _entries.Count - 10000);

                try
                {
                    _writer?.WriteLine(entry.ToString());
                    _writer?.Flush();
                }
                catch
                {
                    // Best effort file logging
                }
            }

            LogAdded?.Invoke(this, entry);
        }

        public void Info(string message, string source = "App") => Log(message, "Info", source);
        public void Warning(string message, string source = "App") => Log(message, "Warning", source);
        public void Error(string message, string source = "App") => Log(message, "Error", source);
        public void Debug(string message, string source = "App") => Log(message, "Debug", source);

        public void Clear()
        {
            lock (_lock)
                _entries.Clear();
        }

        private void RotateLogFile()
        {
            try
            {
                _writer?.Dispose();
                _currentLogFile = Path.Combine(_logDir, $"securegateway-{DateTime.Now:yyyy-MM-dd}.log");
                _writer = new StreamWriter(_currentLogFile, append: true) { AutoFlush = true };

                // Clean old log files (keep last 7 days)
                var cutoff = DateTime.Now.AddDays(-7);
                foreach (var file in Directory.GetFiles(_logDir, "securegateway-*.log"))
                {
                    if (File.GetCreationTime(file) < cutoff)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch
            {
                // Continue without file logging
            }
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }
    }
}
