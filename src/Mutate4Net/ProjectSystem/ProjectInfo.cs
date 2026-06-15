namespace Mutate4Net.ProjectSystem;

public sealed record ProjectInfo(
    string ProjectFile,
    string ProjectDirectory,
    bool IsTestProject);

