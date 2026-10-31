namespace CarbonStudio.Core.Commands;

/// <summary>A user-invokable command shown in menus and the command palette.</summary>
public sealed record CommandDefinition(
    string Id,
    string Title,
    string Category,
    Func<CancellationToken, Task> ExecuteAsync)
{
    /// <summary>Display-only shortcut hint, for example "Ctrl+S".</summary>
    public string? Shortcut { get; init; }

    /// <summary>Optional dynamic availability check.</summary>
    public Func<bool>? CanExecute { get; init; }

    /// <summary>When set the command is permanently unavailable in this build (for example "Planned for Phase 2").</summary>
    public string? UnavailableReason { get; init; }

    public bool IsAvailable => UnavailableReason is null && (CanExecute?.Invoke() ?? true);
}

public interface ICommandRegistry
{
    IReadOnlyCollection<CommandDefinition> All { get; }

    void Register(CommandDefinition command);

    CommandDefinition? Find(string id);

    /// <summary>Runs the command if it exists and is available; returns false otherwise.</summary>
    Task<bool> ExecuteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, CommandDefinition> _commands = new(StringComparer.Ordinal);

    public IReadOnlyCollection<CommandDefinition> All => _commands.Values;

    public void Register(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_commands.TryAdd(command.Id, command))
        {
            throw new InvalidOperationException($"A command with id '{command.Id}' is already registered.");
        }
    }

    public CommandDefinition? Find(string id) => _commands.GetValueOrDefault(id);

    public async Task<bool> ExecuteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_commands.TryGetValue(id, out var command) || !command.IsAvailable)
        {
            return false;
        }

        await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
