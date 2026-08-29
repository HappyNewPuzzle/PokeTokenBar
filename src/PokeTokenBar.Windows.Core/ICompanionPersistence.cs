namespace PokeTokenBar.Windows.Core;

public interface ICompanionPersistence
{
    CompanionState? Load();

    void Save(CompanionState state);

    void Delete();
}
