namespace Dialoid.Core.Character;

/// <summary>Character attribute. Its stat id is the lowercase name: "str", "int", "will", "dex".</summary>
public enum Attribute { Str, Int, Will, Dex }

public static class AttributeExtensions
{
    public static string StatId(this Attribute a) => a switch
    {
        Attribute.Str => "str",
        Attribute.Int => "int",
        Attribute.Will => "will",
        Attribute.Dex => "dex",
        _ => throw new ArgumentOutOfRangeException(nameof(a)),
    };
}
