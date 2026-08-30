namespace PokeTokenBar.Windows.Core;

public interface IAppSettingsPersistence
{
    AppSettings? Load();

    void Save(AppSettings settings);
}
