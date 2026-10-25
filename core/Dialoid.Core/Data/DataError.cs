namespace Dialoid.Core.Data;

/// <summary>A problem found in game data. File is relative to the data directory; Message is a Russian sentence.</summary>
public sealed record DataError(string File, string? Id, string Message);

public sealed record LoadResult(GameData? Data, IReadOnlyList<DataError> Errors)
{
    public bool Ok => Data != null && Errors.Count == 0;
}
