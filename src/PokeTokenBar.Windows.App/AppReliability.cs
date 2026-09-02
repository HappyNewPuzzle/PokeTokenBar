using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PokeTokenBar.Windows.App;

internal static class AppReliability
{
    public static void Run(Task task) => _ = ObserveAsync(task);

    internal static async Task ObserveAsync(Task task)
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
