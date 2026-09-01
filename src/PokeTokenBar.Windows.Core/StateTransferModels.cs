namespace PokeTokenBar.Windows.Core;

public sealed record StateTransferSummary(int DexCount, long LifetimeTokens);

public sealed record StateTransferPreview(
    int FormatVersion,
    string AppVersion,
    DateTimeOffset ExportedAtUtc,
    string SourceDevice,
    StateTransferSummary State);

public enum StateTransferError
{
    NotASaveFile,
    NewerFormat,
    FileTooLarge,
    InvalidState,
    BackupFailed,
    CommitFailed,
}

public sealed class StateTransferException(StateTransferError reason, string message)
    : Exception(message)
{
    public StateTransferError Reason { get; } = reason;
}
