using System.Diagnostics;
using System.IO;

namespace MusicPlayer;

public static class PerfLog
{
    private static readonly object SyncRoot = new();

    public static string LogFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MusicPlayer",
        "logs",
        "perf.log");

    public static IDisposable Measure(string name)
    {
        Write($"{name} start");
        return new Scope(name);
    }

    public static void Mark(string message)
    {
        Write(message);
    }

    public static void Exception(string name, Exception exception)
    {
        Write($"{name} exception: {exception}");
    }

    private static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}";
        Debug.Write(line);

        lock (SyncRoot)
        {
            var directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(LogFilePath, line);
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly string name;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private bool disposed;

        public Scope(string name)
        {
            this.name = name;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stopwatch.Stop();
            Write($"{name} end: {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
