namespace PokeTokenBar.Windows.Infrastructure;

public static class CodexJsonlReader
{
    public static IReadOnlyList<CodexTokenCountParseResult> Read(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        var results = new List<CodexTokenCountParseResult>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (CodexTokenCountParser.TryParse(line, out var result))
            {
                results.Add(result);
            }
        }

        return results;
    }
}
