namespace Mutate4Net.ProjectSystem;

public sealed record ProjectSourceSet(ProjectInfo Project, IReadOnlyList<string> SourceFiles);
