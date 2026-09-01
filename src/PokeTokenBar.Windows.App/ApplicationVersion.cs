using System.Reflection;

namespace PokeTokenBar.Windows.App;

internal static class ApplicationVersion
{
    public static string Current
    {
        get
        {
            var assembly = typeof(App).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion.Split('+')[0];
            return string.IsNullOrWhiteSpace(informational)
                ? assembly.GetName().Version?.ToString(3) ?? "0.0.0"
                : informational;
        }
    }
}
