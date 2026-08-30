using System.Text.Json;
using PokeTokenBar.Windows.Core;

namespace PokeTokenBar.Windows.Infrastructure;

public sealed class JsonAppSettingsPersistence : IAppSettingsPersistence
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public JsonAppSettingsPersistence(string? filePath = null)
    {
        FilePath = Path.GetFullPath(filePath ?? GetDefaultFilePath());
    }

    public string FilePath { get; }

    public static string GetDefaultFilePath()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "PokeTokenBar", "settings.json");
    }

    public AppSettings? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var settings = JsonSerializer.Deserialize<AppSettings>(stream, SerializerOptions);
            if (settings is null || !IsValid(settings))
            {
                BackupCorruptFile();
                return null;
            }

            return settings;
        }
        catch (JsonException)
        {
            BackupCorruptFile();
            return null;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("The settings path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsValid(AppSettings settings) =>
        Enum.IsDefined(settings.RefreshInterval) &&
        (settings.FloatingPetPosition is not { } position ||
         (double.IsFinite(position.Left) && double.IsFinite(position.Top)));

    private void BackupCorruptFile()
    {
        var backupPath = $"{FilePath}.corrupt";
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        if (File.Exists(FilePath))
        {
            File.Move(FilePath, backupPath);
        }
    }
}
