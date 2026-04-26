using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LoopcastUA.Infrastructure
{
    internal static class Logger
    {
        private static RollingFileWriter _writer;
        private static readonly object _lock = new object();

        public static void Initialize(string directory, int maxFileSizeMb, int maxFiles)
        {
            var expanded = Environment.ExpandEnvironmentVariables(directory);
            Directory.CreateDirectory(expanded);
            lock (_lock)
            {
                _writer = new RollingFileWriter(expanded, maxFileSizeMb, maxFiles);
            }
        }

        public static void Info(string message) => Write("INFO ", message);
        public static void Warn(string message) => Write("WARN ", message);
        public static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            Trace.WriteLine(line);
            lock (_lock)
            {
                _writer?.WriteLine(line);
            }
        }
    }

    internal sealed class RollingFileWriter
    {
        private readonly string _directory;
        private readonly long _maxBytes;
        private readonly int _maxFiles;
        private StreamWriter _current;
        private string _currentPath;

        public RollingFileWriter(string directory, int maxFileSizeMb, int maxFiles)
        {
            _directory = directory;
            _maxBytes = maxFileSizeMb * 1024L * 1024L;
            _maxFiles = maxFiles;
            OpenNew();
        }

        public void WriteLine(string line)
        {
            if (_current == null) return;
            _current.WriteLine(line);
            _current.Flush();
            if (new FileInfo(_currentPath).Length >= _maxBytes)
                Roll();
        }

        private void OpenNew()
        {
            _currentPath = Path.Combine(_directory, $"app_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _current = new StreamWriter(_currentPath, append: true, encoding: Encoding.UTF8);
        }

        private void Roll()
        {
            _current?.Close();
            OpenNew();
            PurgeOld();
        }

        private void PurgeOld()
        {
            var files = Directory.GetFiles(_directory, "app_*.log");
            Array.Sort(files);
            for (int i = 0; i < files.Length - _maxFiles; i++)
            {
                try { File.Delete(files[i]); } catch { }
            }
        }
    }
}
