namespace Dialoid.Sandbox;

/// <summary>Outcome of a session command. Error is a Russian message for the owner when Ok is false.</summary>
public sealed record CommandResult(bool Ok, string? Error)
{
    public static CommandResult Success { get; } = new(true, null);

    public static CommandResult Fail(string error) => new(false, error);
}
