using System.Windows.Input;

namespace PokeTokenBar.Windows.App.Commands;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onException;
    private int _isExecuting;

    public AsyncCommand(
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute = null,
        Action<Exception>? onException = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onException = onException;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting => Volatile.Read(ref _isExecuting) != 0;

    public bool CanExecute(object? parameter) =>
        !IsExecuting && (_canExecute?.Invoke() ?? true);

    public void Execute(object? parameter) => _ = ExecuteAsync();

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if ((_canExecute is not null && !_canExecute()) ||
            Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            return;
        }

        RaiseCanExecuteChanged();
        try
        {
            await _execute(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _onException?.Invoke(exception);
        }
        finally
        {
            Volatile.Write(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
