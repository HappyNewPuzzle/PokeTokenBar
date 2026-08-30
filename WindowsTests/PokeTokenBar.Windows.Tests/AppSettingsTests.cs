using PokeTokenBar.Windows.App.ViewModels;
using PokeTokenBar.Windows.Core;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeTokenBar-Settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void DefaultsMatchSwiftOptInBehavior()
    {
        Assert.False(AppSettings.Default.FloatingPetEnabled);
        Assert.Null(AppSettings.Default.FloatingPetPosition);
        Assert.False(AppSettings.Default.LaunchAtStartup);
    }

    [Fact]
    public void JsonPersistenceRoundTripsOnlyRequiredSettings()
    {
        var path = Path.Combine(_directory, "settings.json");
        var persistence = new JsonAppSettingsPersistence(path);
        var settings = new AppSettings(
            true,
            new FloatingPetPosition(-120.5, 480.25),
            true);

        persistence.Save(settings);

        Assert.Equal(settings, persistence.Load());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void CorruptSettingsFallBackWithoutStartupException()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{broken");
        var persistence = new JsonAppSettingsPersistence(path);

        var loaded = persistence.Load();

        Assert.Null(loaded);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void AutoStartWritesAndDeletesOnlyCurrentUserRunValueThroughSeam()
    {
        Directory.CreateDirectory(_directory);
        var executable = Path.Combine(_directory, "PokeTokenBar.Windows.App.exe");
        File.WriteAllBytes(executable, []);
        var runKey = new FakeRunKey();
        var service = new WindowsAutoStartService(runKey, executable);

        service.SetEnabled(true);
        Assert.True(service.IsEnabled);
        Assert.Equal($"\"{Path.GetFullPath(executable)}\"", runKey.Value);

        service.SetEnabled(false);
        Assert.False(service.IsEnabled);
        Assert.Null(runKey.Value);
    }

    [Fact]
    public void DotnetHostIsNotRegisteredAsApplicationAutoStart()
    {
        Directory.CreateDirectory(_directory);
        var dotnet = Path.Combine(_directory, "dotnet.exe");
        File.WriteAllBytes(dotnet, []);
        var service = new WindowsAutoStartService(new FakeRunKey(), dotnet);

        Assert.False(service.IsAvailable);
        Assert.Throws<InvalidOperationException>(() => service.SetEnabled(true));
    }

    [Fact]
    public void SettingsViewModelMapsPersistenceAndRevertsRegistryFailure()
    {
        var persistence = new FakePersistence(
            new AppSettings(true, new FloatingPetPosition(10, 20), false));
        var autoStart = new FakeAutoStart();
        var viewModel = new SettingsViewModel(persistence, autoStart);

        Assert.True(viewModel.IsFloatingPetEnabled);
        Assert.Equal(new FloatingPetPosition(10, 20), viewModel.SavedFloatingPetPosition);

        autoStart.Error = new InvalidOperationException("registry denied");
        viewModel.IsLaunchAtStartupEnabled = true;

        Assert.False(viewModel.IsLaunchAtStartupEnabled);
        Assert.Equal("registry denied", viewModel.ErrorMessage);
        Assert.False(persistence.LastSaved?.LaunchAtStartup ?? true);
    }

    private sealed class FakeRunKey : IUserRunKey
    {
        public string? Value { get; private set; }
        public string? Read(string valueName) => Value;
        public void Write(string valueName, string value) => Value = value;
        public void Delete(string valueName) => Value = null;
    }

    private sealed class FakePersistence : IAppSettingsPersistence
    {
        private readonly AppSettings _settings;

        public FakePersistence(AppSettings settings)
        {
            _settings = settings;
            LastSaved = settings;
        }

        public AppSettings? LastSaved { get; private set; }
        public AppSettings? Load() => _settings;
        public void Save(AppSettings value) => LastSaved = value;
    }

    private sealed class FakeAutoStart : IAutoStartService
    {
        public bool IsAvailable => true;
        public bool IsEnabled { get; private set; }
        public Exception? Error { get; set; }

        public void SetEnabled(bool enabled)
        {
            if (Error is not null)
            {
                throw Error;
            }

            IsEnabled = enabled;
        }
    }
}
