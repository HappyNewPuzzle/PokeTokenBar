namespace PokeTokenBar.Windows.Core;

public interface ICompanionPersistence
{
    CompanionState? Load();

    void Save(CompanionState state);

    // File-backed implementations preserve the pre-migration bytes before saving.
    void SaveProfileMigration(CompanionState state) => Save(state);

    void Delete();
}
