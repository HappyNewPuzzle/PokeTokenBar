namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexInMemoryResolvedRollout(
    CodexEpochRollout OriginalRollout,
    CodexEpochRollout ResolvedRollout,
    IReadOnlyList<CodexEpochTokenEvent> ResolvedHistory,
    CodexEpochRollout? SelectedParent,
    int ReplayCount)
{
    public string FilePath => OriginalRollout.FilePath;
}
