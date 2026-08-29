namespace PokeTokenBar.Windows.Core;

public interface IPokeApiClient
{
    Task<EvoLine> GetLineAsync(
        int baseSpeciesId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BaseSpecies>> GetBaseSpeciesIndexAsync(
        CancellationToken cancellationToken = default);

    Task<BaseSpecies?> GetBaseSpeciesAsync(
        int id,
        CancellationToken cancellationToken = default);
}
