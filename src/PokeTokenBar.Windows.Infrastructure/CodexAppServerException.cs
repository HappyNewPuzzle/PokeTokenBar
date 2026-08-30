namespace PokeTokenBar.Windows.Infrastructure;

public sealed class CodexAppServerException : Exception
{
    public CodexAppServerException(string message)
        : base(message)
    {
    }
}
