namespace PokeTokenBar.Windows.Infrastructure;

public sealed record CodexCanonicalEvent(
    CodexEpochRollout SourceRollout,
    CodexEpochTokenEvent TokenEvent,
    CodexCanonicalUsageKey? CanonicalKey)
{
    public string FilePath => SourceRollout.FilePath;

    public CodexSessionMetaParseResult? RolloutMetadata => SourceRollout.RolloutMetadata;
}
