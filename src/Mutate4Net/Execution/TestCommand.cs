namespace Mutate4Net.Execution;

public sealed record TestCommand
{
    public TestCommand(
        IReadOnlyList<string> command,
        string workingDirectory,
        bool IsCustom = false,
        string? DisplayCommand = null)
        : this([command], workingDirectory, IsCustom, DisplayCommand)
    {
    }

    public TestCommand(
        IReadOnlyList<IReadOnlyList<string>> commands,
        string workingDirectory,
        bool IsCustom = false,
        string? DisplayCommand = null)
    {
        if (commands.Count == 0)
        {
            throw new ArgumentException("At least one command is required.", nameof(commands));
        }

        Commands = commands;
        WorkingDirectory = workingDirectory;
        this.IsCustom = IsCustom;
        this.DisplayCommand = DisplayCommand;
    }

    public IReadOnlyList<IReadOnlyList<string>> Commands { get; }

    public IReadOnlyList<string> Command => Commands[0];

    public string WorkingDirectory { get; }

    public bool IsCustom { get; }

    public string? DisplayCommand { get; }
}
