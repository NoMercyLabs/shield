namespace Shield.Parsers.Npm.Tests;

internal static class FixtureLoader
{
    public static Stream Open(string filename)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", filename);
        return File.OpenRead(path);
    }
}
