using System.Collections.Concurrent;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

internal static class AtomicFile
{
    // ponytail: paths are a small fixed set in production; use ref-counted gates only if paths become unbounded.
    private static readonly ConcurrentDictionary<string, object> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Write(string path, Action<Stream> write)
    {
        path = Path.GetFullPath(path);
        lock (Gates.GetOrAdd(path, static _ => new object()))
        {
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("The persistence path has no directory.");
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                           temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    ReliabilityEventLog.RecordError("persistence", exception);
                }
            }
        }
    }

    public static void WriteBytes(string path, ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        Write(path, stream => stream.Write(bytes));
    }

    public static bool Quarantine(string path, string component, int keep = 3)
    {
        try
        {
            if (!File.Exists(path)) return true;
            var directory = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var artifact = Path.Combine(directory,
                $"{name}.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}{extension}");
            File.Move(path, artifact);
            foreach (var stale in Directory.GetFiles(directory, $"{name}.corrupt-*{extension}")
                         .OrderByDescending(candidate => candidate, StringComparer.Ordinal)
                         .Skip(keep))
            {
                File.Delete(stale);
            }
            ReliabilityEventLog.RecordRecovery(component, "corrupt-file-isolated");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReliabilityEventLog.RecordError(component, exception);
            return false;
        }
    }
}
