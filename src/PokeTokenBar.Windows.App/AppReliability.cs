using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.App;

internal static class AppReliability
{
    public static void Run(Task task, string context = "background") => _ = ObserveAsync(task, context);

    internal static async Task ObserveAsync(Task task, string context = "background")
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            ReliabilityEventLog.RecordError(context, exception);
        }
    }

    internal static bool IsFatal(Exception exception) => exception switch
    {
        OutOfMemoryException or StackOverflowException or AccessViolationException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsFatal),
        _ => false,
    };

    internal static bool IsRecoverableDispatcherException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or HttpRequestException or
        JsonException or Win32Exception or COMException or InvalidOperationException or
        ArgumentException or NotSupportedException;
}
