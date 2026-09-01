namespace PokeTokenBar.Windows.Infrastructure;

public static class PokeTokenBarDataPaths
{
    public const string RootEnvironmentVariable = "POKETOKENBAR_DATA_ROOT";

    public static string Root => Resolve(Environment.GetEnvironmentVariable(RootEnvironmentVariable));

    public static string Resolve(string? overrideRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokeTokenBar")
            : overrideRoot;
        return Path.GetFullPath(root);
    }
}
