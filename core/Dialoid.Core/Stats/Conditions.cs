namespace Dialoid.Core.Stats;

public interface IConditionContext
{
    bool IsMet(Condition condition);
}
