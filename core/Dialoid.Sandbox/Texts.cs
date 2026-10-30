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
    public static string RotationEmptyId => "В ротации пустой id скилла";
    public static string RotationSkillNotSlotted(string name) => $"Скилла «{name}» нет в слотах, его нельзя поставить в ротацию";

    public static string ImportRefused(string reasons) => $"Билд не загружен: {reasons}";
    public static string ImportUnparsed => "Билд не загружен: текст не удалось разобрать ни как код DLD1, ни как JSON билда";
    public static string ImportSkillLevel(string skillName, int level, int max) =>
        $"Билд не загружен: уровень скилла «{skillName}» {level} вне диапазона 1–{max}";
    public static string ImportTooManySlots(int count, int max) => $"Билд не загружен: слотов скиллов {count}, а всего их {max}";

    public static string ReferenceUnreadable(string title, string reason) => $"Не удалось прочитать эталон «{title}»: {reason}";
    public static string ReferenceRefused(string title, string reason) => $"Эталон «{title}»: {reason}";

    // Simulation and breakdown texts.
    public static string ModeName(Dialoid.Core.Simulation.SimMode mode) => mode switch
    {
        Dialoid.Core.Simulation.SimMode.Average => "среднее",
        Dialoid.Core.Simulation.SimMode.Roll => "бросок",
        _ => mode.ToString(),
    };

    /// <summary>Russian name of a simulation log event kind; an unknown kind is returned as is.</summary>
    public static string EventKindName(string kind) => kind switch
    {
        "use" => "применение",
        "hit" => "попадание",
        "crit" => "крит",
        "ailment" => "недуг",
        "kill" => "убийство",
        "no_resource" => "нет ресурса",
        "dummy_attack" => "атака манекена",
        "dodge" => "уклонение",
        "death" => "смерть",
        "proc" => "прок",
        "proc_blocked" => "прок заблокирован",
        "mechanic" => "механика",
        "log_truncated" => "лог обрезан",
        _ => kind,
    };

    public const string TimeToDeathSurvived = "Время до смерти: персонаж выжил";
    public const string TimeToDeathNotAttacking = "Время до смерти: манекен не бьет";
    public const string TimeToDeathDummyHasNoAttack = "Время до смерти: этот манекен не атакует";

    public static string DamageTypeName(Dialoid.Core.Combat.DamageType t) => t switch
    {
        Dialoid.Core.Combat.DamageType.Physical => "физический",
        Dialoid.Core.Combat.DamageType.Fire => "огонь",
        Dialoid.Core.Combat.DamageType.Cold => "холод",
        Dialoid.Core.Combat.DamageType.Lightning => "молния",
        Dialoid.Core.Combat.DamageType.Poison => "яд",
        Dialoid.Core.Combat.DamageType.Dark => "тьма",
        _ => t.ToString(),
    };

    /// <summary>The dummy's name from the data; an unknown id is returned as is.</summary>
    public static string DummyName(Dialoid.Core.Data.GameData data, string id) =>
        data.Dummies.TryGetValue(id, out var d) ? d.Name : id;

    private static readonly Dictionary<string, string> StatTitles = new(StringComparer.Ordinal)
    {
        ["str"] = "Сила", ["int"] = "Интеллект", ["will"] = "Воля", ["dex"] = "Ловкость",
        ["life"] = "Здоровье", ["life_regen_pct"] = "Регенерация здоровья, % в секунду",
        ["armor"] = "Броня", ["dodge_chance"] = "Шанс уклонения", ["dodge_chance_max"] = "Максимум шанса уклонения",
        ["crit_chance"] = "Шанс крита, %", ["crit_damage"] = "Урон крита, +%", ["damage"] = "Урон",
        ["attack_speed"] = "Скорость атаки", ["cast_speed"] = "Скорость сотворения",
        ["cooldown_recovery"] = "Восстановление перезарядки", ["area"] = "Область действия",
        ["resource_max"] = "Максимум ресурса", ["resource_regen"] = "Регенерация ресурса",
        ["resource_cost"] = "Стоимость ресурса", ["resource_gen"] = "Прирост ресурса",
        ["resist_fire"] = "Сопротивление огню", ["resist_cold"] = "Сопротивление холоду",
        ["resist_lightning"] = "Сопротивление молнии", ["resist_poison"] = "Сопротивление яду",
        ["resist_dark"] = "Сопротивление тьме",
        ["projectile_count"] = "Снарядов", ["pierce_count"] = "Пробиваний", ["chain_count"] = "Цепей",
    };

    private static readonly Dictionary<string, string> KeyTitles = new(StringComparer.Ordinal)
    {
        ["life_regen_per_sec"] = "Регенерация здоровья в секунду", ["resource_decay"] = "Затухание ресурса",
        ["primary_damage_more"] = "Бонус основного атрибута к урону",
        ["hit_avg_total"] = "Среднее попадание без крита", ["hit_avg_with_crit"] = "Среднее попадание с учетом крита",
        ["uses_per_sec"] = "Применений в секунду", ["cooldown"] = "Перезарядка, с",
        ["area_radius"] = "Радиус области", ["explosion_radius"] = "Радиус взрыва",
        ["duration"] = "Длительность, с", ["ground_duration"] = "Длительность зоны, с",
        ["ground_fraction"] = "Доля урона зоны в секунду", ["channel_interval"] = "Интервал канала, с",
    };

    private static readonly (string Prefix, string Title)[] KeyPrefixes =
    {
        ("hit_min_", "Попадание, минимум"), ("hit_max_", "Попадание, максимум"), ("hit_avg_", "Попадание, среднее"),
        ("hit_mult_", "Множитель урона попадания"), ("penetration_", "Пробивание сопротивления"),
        ("resist_", "Сопротивление"), ("ailment_chance_", "Шанс недуга"),
    };

    /// <summary>Russian title of a stat id; an unknown id is returned as is.</summary>
    public static string StatTitle(string statId) => StatTitles.GetValueOrDefault(statId, statId);

    /// <summary>Russian title of a NumberLine key; an unknown key is returned as is.</summary>
    public static string NumberKeyTitle(string key)
    {
        if (KeyTitles.TryGetValue(key, out var t)) return t;
        if (StatTitles.TryGetValue(key, out t)) return t;
        foreach (var (prefix, title) in KeyPrefixes)
            if (key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.Ordinal))
                return $"{title} ({key[prefix.Length..]})";
        return key;
    }

    public static string Base => "База";
    public static string FlatHeader => "Плюс";
    public static string IncreasedHeader(string sum) => $"Увеличение: {sum}%";
    public static string MoreLine(string mult, string source) => $"Больше: ×{mult} — {source}";
    public static string LessLine(string mult, string source) => $"Меньше: ×{mult} — {source}";
    public static string OverrideLine(string value, string source) => $"Переопределение: {value} — {source}";
    public static string Total(string value) => $"Итог: {value}";
    public static string TotalCapped(string uncapped, string capped) => $"Итог (до капа → после капа): {uncapped} → {capped}";

    // Training room: combat log, press refusals, meter.
    public const string LogSeparator = " · ";
    public static string LogTime(string seconds) => $"{seconds} с";
    public const string LogHit = "попадание";
    public static string LogTarget(int target) => $"цель {target + 1}";
    public const string LogCritMark = "(крит)";
    public const string LogBuff = "бафф";
    public static string LogSlot(int slot) => $"слот {slot}";
    public static string LogRefusal(string reason) => $"отказ: {reason}";
    public static string LogRepeat(string line, int repeat) => $"{line} ×{repeat}";

    private static readonly Dictionary<string, string> AilmentDamageNames = new(StringComparer.Ordinal)
    {
        ["bleed"] = "урон кровотечения", ["ignite"] = "урон поджога", ["poison"] = "урон отравления",
    };

    /// <summary>An aggregated DoT line: «урон поджога» for an ailment, «урон зоны» for a ground zone.</summary>
    public static string LogDotTick(string? ailmentId) =>
        ailmentId == null ? "урон зоны" : AilmentDamageNames.GetValueOrDefault(ailmentId, "урон со временем");

    public const string BuildChangedRestart = "Билд изменен — бой начат заново";
    public static string DummyChangedRestart(string dummyName) => $"Манекен «{dummyName}» — бой начат заново";
    public static string ModeChangedRestart(string modeName) => $"Режим «{modeName}» — бой начат заново";
    public static string FightNotStarted(string reasons) => $"Бой не начат: {reasons}";

    public static string RefusalReason(Dialoid.Core.Simulation.PressRefusal r, string? cooldownLeft) => r switch
    {
        Dialoid.Core.Simulation.PressRefusal.EmptySlot => "пустой слот",
        Dialoid.Core.Simulation.PressRefusal.Cooldown => cooldownLeft == null ? "перезарядка" : $"перезарядка {cooldownLeft} с",
        Dialoid.Core.Simulation.PressRefusal.NotEnoughResource => "не хватает ресурса",
        Dialoid.Core.Simulation.PressRefusal.Busy => "персонаж занят",
        Dialoid.Core.Simulation.PressRefusal.BuffActive => "бафф уже действует",
        Dialoid.Core.Simulation.PressRefusal.WeaponMismatch => "не подходит оружие",
        Dialoid.Core.Simulation.PressRefusal.TriggeredOnly => "скилл только срабатывает от других",
        Dialoid.Core.Simulation.PressRefusal.OutOfReach => "вне досягаемости",
        Dialoid.Core.Simulation.PressRefusal.TargetNotVisible => "цель не видна",
        Dialoid.Core.Simulation.PressRefusal.FightOver => "бой не идет",
        _ => "отказ",
    };

    private static readonly Dictionary<string, string> ResourceGenitive = new(StringComparer.Ordinal)
    {
        ["mana"] = "маны", ["fury"] = "ярости",
    };

    /// <summary>The short text over the hero (spec 4, «Экран боя»): «Перезарядка 2,1 с», «Не хватает маны», «Цель не видна».</summary>
    public static string RefusalPopup(Dialoid.Core.Simulation.PressRefusal r, string? cooldownLeft, string? resourceId)
    {
        var text = r == Dialoid.Core.Simulation.PressRefusal.NotEnoughResource && resourceId != null
                   && ResourceGenitive.TryGetValue(resourceId, out var resource)
            ? $"не хватает {resource}"
            : RefusalReason(r, cooldownLeft);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>"812 огнем": the damage type in the instrumental case.</summary>
    public static string DamageTypeInstrumental(Dialoid.Core.Combat.DamageType t) => t switch
    {
        Dialoid.Core.Combat.DamageType.Physical => "физическим",
        Dialoid.Core.Combat.DamageType.Fire => "огнем",
        Dialoid.Core.Combat.DamageType.Cold => "холодом",
        Dialoid.Core.Combat.DamageType.Lightning => "молнией",
        Dialoid.Core.Combat.DamageType.Poison => "ядом",
        Dialoid.Core.Combat.DamageType.Dark => "тьмой",
        _ => t.ToString(),
    };

    private static readonly Dictionary<string, string> AilmentNames = new(StringComparer.Ordinal)
    {
        ["bleed"] = "кровотечение", ["ignite"] = "поджог", ["poison"] = "отравление",
        ["shock"] = "шок", ["chill"] = "охлаждение", ["freeze"] = "заморозка",
    };

    /// <summary>Russian ailment name; an unknown id gives the generic «недуг».</summary>
    public static string AilmentName(string id) => AilmentNames.GetValueOrDefault(id, EventKindName("ailment"));

    // Every mechanic that writes a log event (MechanicSimContext.Log) needs its name and event text here.
    private static readonly Dictionary<string, (string Name, string Event)> MechanicEvents = new(StringComparer.Ordinal)
    {
        ["static_charge"] = ("Статический заряд", "заряды потрачены"),
        ["shatter"] = ("Раскол", "заморозка разбита"),
    };

    /// <summary>A mechanic log event by its <see cref="Dialoid.Core.Simulation.SimEvent.MechanicKey"/>: the mechanic's Russian name and
    /// what happened; null for an unknown key.</summary>
    public static (string Name, string Event)? MechanicEvent(string key) =>
        MechanicEvents.TryGetValue(key, out var e) ? e : null;

    // Every id of LiveFight.ShownCounters (and every mechanic debuff on a target) needs its Russian name here.
    private static readonly Dictionary<string, string> MechanicStackNames = new(StringComparer.Ordinal)
    {
        ["static_charge:charges"] = "Статический заряд",
    };

    /// <summary>Russian name of a shown mechanic stack or target debuff (<see cref="Dialoid.Core.Simulation.TimedEffect.Id"/>);
    /// null for an unknown one.</summary>
    public static string? MechanicStackName(string id) => MechanicStackNames.GetValueOrDefault(id);

    public const string MitigationFinal = "дошло";
    public const string MitigationArmor = "броня";
    public const string MitigationResist = "сопротивления";
    public const string MitigationTaken = "множитель получаемого урона";
    public const string MitigationNullified = "обнулено";
    public static string MitigationVulnerability(string amount) => $"+{amount} от уязвимости";
    public static string MitigationPenetration(string amount) => $"+{amount} от пробивания";

    /// <summary>The note under a meter mode; null when the mode needs none.</summary>
    public static string? MeterModeHint(MeterMode mode) => mode == MeterMode.Mitigation
        ? "Прибавки от уязвимости и пробивания — отдельные полосы, поэтому проценты не дают в сумме 100"
        : null;

    public static string SegmentLabel(int index, string duration, string dummyName, bool afterBuildChange) =>
        $"№{index}{LogSeparator}{duration}{LogSeparator}{dummyName}" + (afterBuildChange ? $"{LogSeparator}после правки билда" : "");

    /// <summary>"DPS +12%" next to a segment's label.</summary>
    public static string DpsChange(string percent) => $"DPS {percent}%";

    public static string MeterModeName(MeterMode mode) => mode switch
    {
        MeterMode.DpsBySkill => "DPS по скиллам",
        MeterMode.DamageBySkill => "Урон по скиллам",
        MeterMode.DamageByType => "Урон по типам",
        MeterMode.Mitigation => "Потеря на защите",
        _ => mode.ToString(),
    };
}
