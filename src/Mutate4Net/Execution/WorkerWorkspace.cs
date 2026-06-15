namespace Mutate4Net.Execution;

public sealed record WorkerWorkspace(
    string RunRoot,
    string ModuleRoot,
    string SourceFile);

