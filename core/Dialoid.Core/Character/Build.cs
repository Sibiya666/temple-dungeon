namespace Dialoid.Core.Character;

/// <summary>Mutable build description; serialized in Task 17.</summary>
public sealed class Build
{
    public string ClassId = "";
    public int Level = 1;
    public string? AscendancyId;
    public List<string> AscendancyNodes = new();
    public List<string> PassiveNodes = new();
    public Dictionary<string, Attribute> AttributeChoices = new();
    public List<SkillSlot> Slots = new();
    public string? WeaponId;
    public List<string> ItemIds = new();
    public List<string> Rotation = new();
}

public sealed class SkillSlot
{
    public string SkillId = "";
    public int Level = 1;
    public Dictionary<string, int> NodePoints = new();
}
