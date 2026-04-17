namespace EVEProfileSync.Tests;

public static class MappingHarness
{
    public static IReadOnlyList<string> CompareSnapshots(string beforeRoot, string afterRoot)
    {
        var beforeFiles = ReadFileMap(beforeRoot);
        var afterFiles = ReadFileMap(afterRoot);
        var changedFiles = new List<string>();

        foreach (var relativePath in beforeFiles.Keys.Union(afterFiles.Keys, StringComparer.OrdinalIgnoreCase))
        {
            beforeFiles.TryGetValue(relativePath, out var beforeContent);
            afterFiles.TryGetValue(relativePath, out var afterContent);

            if (!string.Equals(beforeContent, afterContent, StringComparison.Ordinal))
            {
                changedFiles.Add(relativePath);
            }
        }

        return changedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, string> ReadFileMap(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, filePath);
            result[relativePath] = File.ReadAllText(filePath);
        }

        return result;
    }
}
