namespace Mutate4Net.Tests;

internal sealed class SampleFile : IDisposable
{
    private readonly string _directory;

    private SampleFile(string directory, string path)
    {
        _directory = directory;
        Path = path;
    }

    public string Path { get; }

    public static SampleFile Create(string source)
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mutate4net-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, "Sample.cs");
        File.WriteAllText(path, source);
        return new SampleFile(directory, path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

