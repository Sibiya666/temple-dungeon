using Dialoid.Core.Stats;

namespace Dialoid.Core.Explain;

/// <summary>A condition context where no condition holds: conditional modifiers never apply (sheets, tests).</summary>
public sealed class NoConditions : IConditionContext
{
    public static NoConditions Instance { get; } = new();

    private NoConditions() { }

    public bool IsMet(Condition condition) => false;
}
