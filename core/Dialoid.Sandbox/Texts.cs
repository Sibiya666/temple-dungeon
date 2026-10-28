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
}
