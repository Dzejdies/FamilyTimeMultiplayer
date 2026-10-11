using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Logging;

namespace FamilyTimeMultiplayer.Diagnostics;

public sealed class PersistentLog : ILogListener
{
    private const int KeptFiles = 30;

    private readonly StreamWriter _writer;
    private readonly object _lock = new object();

    private PersistentLog(string path)
    {
        Path = path;
        _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    }

    public string Path { get; }

    public static PersistentLog Start()
    {
        string directory = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "ftmp-logs");
        Directory.CreateDirectory(directory);
        foreach (FileInfo old in new DirectoryInfo(directory).GetFiles("*.log").OrderByDescending(f => f.Name).Skip(KeptFiles - 1))
        {
            old.Delete();
        }
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string path = System.IO.Path.Combine(directory, $"{stamp}_pid{System.Diagnostics.Process.GetCurrentProcess().Id}.log");
        return new PersistentLog(path);
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{eventArgs.Level,-7}:{eventArgs.Source.SourceName}] {eventArgs.Data}";
        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Dispose();
        }
    }
}
