namespace Dialoid.Sandbox;

/// <summary>Russian owner-facing messages of the sandbox commands.</summary>
internal static class Texts
{
    public static string UnknownClass(string id) => $"Неизвестный класс «{id}»";
    public static string LevelOutOfRange(int level, int max) => $"Уровень {level} вне диапазона 1–{max}";

    public static string UnknownPassive(string id) => $"Неизвестный пассивный узел «{id}»";
    public static string StartNodeFixed(string name) => $"Стартовый узел «{name}» взят всегда, его нельзя снять или взять";
    public static string PassiveUnreachable(string name) => $"Узел «{name}» недоступен: он не соседствует со взятыми узлами";
    public static string PassiveBreaksPath(string name) => $"Нельзя снять «{name}»: другие узлы потеряют связь со стартом";

    public static string NotAttributeNode(string name) => $"Узел «{name}» не узел атрибута";
    public static string AttributeNodeNotTaken(string name) => $"Узел атрибута «{name}» не взят";

    public static string AscendancyWrongClass(string id, string classId) =>
        $"Асцендант «{id}» не существует или не принадлежит классу «{classId}»";
    public static string NoAscendancy => "Асцендант не выбран";
    public static string UnknownAscendancyNode(string id, string ascName) => $"В асценданте «{ascName}» нет узла «{id}»";

    public static string SlotOutOfRange(int slot, int count) => $"Слота {slot + 1} нет: слотов скиллов {count}";
    public static string SlotLocked(int slot, int level, int open) =>
        $"Слот {slot + 1} закрыт: на уровне {level} открыто слотов {open}";
    public static string SlotEmpty(int slot) => $"Слот {slot + 1} пуст";
    public static string UnknownSkill(string id) => $"Неизвестный скилл «{id}»";
    public static string SkillAlreadySlotted(string name, int slot) => $"Скилл «{name}» уже стоит в слоте {slot + 1}";
    public static string SkillNotAvailable(string name) =>
        $"Скилл «{name}» недоступен: он не из набора класса и не выдан надетым предметом";
    public static string NothingToUndo(int slot) => $"В слоте {slot + 1} нечего возвращать";
    public static string SkillLevelOutOfRange(string name, int level, int cap) =>
        $"Уровень скилла «{name}» {level} вне диапазона 1–{cap}";

    public static string UnknownSkillNode(string id, string skillName) => $"В дереве скилла «{skillName}» нет узла «{id}»";
    public static string SkillNodeFull(string name, int max) => $"В узле «{name}» уже максимум очков: {max}";
    public static string NoFreeSkillPoints(string skillName, int total) =>
        $"В дереве скилла «{skillName}» нет свободных очков: все {total} потрачены";
    public static string SkillNodeRequirements(string name, string requirements) =>
        $"Узел «{name}» требует: {requirements}";
    public static string SkillNodeRequirement(string name, int points) => $"«{name}» {points} очк.";
    public static string SkillNodeExclusive(string name, string taken) =>
        $"Узел «{name}» нельзя взять вместе с «{taken}»: они из одной группы";
    public static string SkillNodeNoPoints(string name) => $"В узле «{name}» нет очков";
    public static string SkillNodeRequired(string name, string dependent) =>
        $"Нельзя снять очко с «{name}»: на нем держится узел «{dependent}»";

    public static string UnknownWeapon(string id) => $"Неизвестное оружие «{id}»";
    public static string UnknownItem(string id) => $"Неизвестный предмет «{id}»";
    public static string ItemSkillSlotted(string itemName, string skillName) =>
        $"Нельзя снять «{itemName}»: выданный им скилл «{skillName}» стоит в слоте";
    public static string RotationSkillNotSlotted(string name) => $"Скилла «{name}» нет в слотах, его нельзя поставить в ротацию";

    public static string ImportRefused(string reasons) => $"Билд не загружен: {reasons}";
    public static string ImportTooManySlots(int count, int max) => $"Билд не загружен: слотов скиллов {count}, а всего их {max}";

    public static string ReferenceUnreadable(string title, string reason) => $"Не удалось прочитать эталон «{title}»: {reason}";
    public static string ReferenceRefused(string title, string reason) => $"Эталон «{title}»: {reason}";
}
