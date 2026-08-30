using PokeTokenBar.Windows.App.ViewModels;

namespace PokeTokenBar.Windows.Tests;

public sealed class FloatingPokemonWindowContractTests
{
    [Fact]
    public void WindowIsTransparentBorderlessNonActivatingFloatingToolSurface()
    {
        var xaml = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "FloatingPet", "FloatingPokemonWindow.xaml");

        Assert.Contains("AllowsTransparency=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowActivated=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"NoResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Topmost=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"Manual\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"96\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"96\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowUsesRepresentativePresentationAndEggFallback()
    {
        var xaml = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "FloatingPet", "FloatingPokemonWindow.xaml");

        Assert.Contains("AnimatedSpritePresenter", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding Sprite, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("&#x1F95A;", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CompanionSprite", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowOnlyConsumesPresentationState()
    {
        var code = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "FloatingPet", "FloatingPokemonWindow.xaml.cs");

        Assert.Contains("FloatingPetViewModel viewModel", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PokeApiClient", code, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonCompanionPersistence", code, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowUsesCurrentMonitorWorkingAreaAndWpfDpiScale()
    {
        var code = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "FloatingPet", "FloatingPokemonWindow.xaml.cs");

        Assert.Contains("Forms.Screen.FromPoint(Forms.Cursor.Position)", code, StringComparison.Ordinal);
        Assert.Contains("screen.WorkingArea", code, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetDpi(this)", code, StringComparison.Ordinal);
        Assert.Contains("FloatingPetPositioner.Calculate(", code, StringComparison.Ordinal);
        Assert.Contains("FloatingPetPositioner.Restore(", code, StringComparison.Ordinal);
        Assert.Contains("FloatingPetPositioner.Clamp(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AppStartsAndDisposesFloatingPetIndependentlyFromTrayPopup()
    {
        var code = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "App.xaml.cs");

        Assert.Contains("new FloatingPokemonWindow(_composition.FloatingPet)", code, StringComparison.Ordinal);
        Assert.Contains("viewModel.Settings", code, StringComparison.Ordinal);
        Assert.Contains("_floatingPet.Start()", code, StringComparison.Ordinal);
        Assert.Contains("_floatingPet?.Dispose()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowSeparatesFourDipClickFromDragAndProvidesSwiftContextActions()
    {
        var code = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.App", "FloatingPet", "FloatingPokemonWindow.xaml.cs");

        Assert.Contains("ClickThreshold = 4", code, StringComparison.Ordinal);
        Assert.Contains("OnMouseLeftButtonDown", code, StringComparison.Ordinal);
        Assert.Contains("OnMouseMove", code, StringComparison.Ordinal);
        Assert.Contains("OnMouseLeftButtonUp", code, StringComparison.Ordinal);
        Assert.Contains("CommitPosition()", code, StringComparison.Ordinal);
        Assert.Contains("Open Token Bar", code, StringComparison.Ordinal);
        Assert.Contains("Hide Floating Pokémon", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreAndInfrastructureRemainFreeOfWpfReferences()
    {
        var coreProject = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.Core", "PokeTokenBar.Windows.Core.csproj");
        var infrastructureProject = ReadRepositoryFile(
            "src", "PokeTokenBar.Windows.Infrastructure", "PokeTokenBar.Windows.Infrastructure.csproj");

        Assert.DoesNotContain("UseWPF", coreProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UseWPF", infrastructureProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PokeTokenBar.Windows.App", coreProject, StringComparison.Ordinal);
        Assert.DoesNotContain("PokeTokenBar.Windows.App", infrastructureProject, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatingBindingsExistOnDedicatedViewModel()
    {
        Assert.NotNull(typeof(FloatingPetViewModel).GetProperty(nameof(FloatingPetViewModel.Sprite)));
        Assert.NotNull(typeof(FloatingPetViewModel).GetProperty(nameof(FloatingPetViewModel.PokemonId)));
        Assert.NotNull(typeof(FloatingPetViewModel).GetProperty(nameof(FloatingPetViewModel.IsShiny)));
        Assert.NotNull(typeof(FloatingPetViewModel).GetProperty(nameof(FloatingPetViewModel.IsEgg)));
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PokeTokenBar.Windows.sln")))
            {
                return File.ReadAllText(
                    Path.Combine([directory.FullName, .. relativeParts]));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
