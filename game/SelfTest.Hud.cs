using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;
using Dialoid.Game.Input;
using Dialoid.Game.Ui;
using Dialoid.Game.Ui.Hud;
using Dialoid.Game.Ui.Art;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Self-test steps of the combat HUD's reusable components (plan 4 task 14; HUD spec §4 items 3, 6–8, §9): each component is
/// built from a stand-in record without the room; in a fight the components' records equal <see cref="TrainingRoom.State"/>;
/// a row of 15+ effects folds into «+N» with the full list on hover and on focus; the debuff row has its own (torn) frame; the
/// target plate follows the rule for the mouse and for the gamepad; fury is amber-ochre.
/// </summary>
public sealed partial class SelfTest
{
    private IEnumerable<Step> HudSteps()
    {
        yield return new("hud.components-standalone", HudComponentsStandalone);
        yield return new("hud.assigned-slots-without-fight", HudAssignedSlotsWithoutFight);
        yield return new("hud.components-match-state", HudComponentsMatchState);
        yield return new("hud.resource-colours", HudResourceColours);
        yield return new("hud.rows-fold-and-list", HudRowsFold);
        yield return new("hud.plate-target-rule", HudPlateTargetRule);
        yield return new("hud.pad-rows-route", HudPadRowsRoute);
        yield return new("hud.restore", async () =>
        {
            _main.Hud!.PreviewHeroRows(null, null);
            Device.Set(InputKind.KeyboardMouse);
            await CombatRestore();
        });
    }

    // ---------- stand-in data ----------

    /// <summary><paramref name="count"/> stand-in buffs with long Russian names, stacks and timers.</summary>
    private static List<EffectChipData> StandInBuffs(int count)
    {
        var names = new[]
        {
            "Безумие", "Неистовство", "Посвящение", "Боевой клич", "Воодушевление", "Кровавая жатва", "Ярость предков", "Клич несокрушимой стойкости",
            "Каменная кожа", "Жажда битвы", "Отголосок клинка", "Пепельный доспех", "Знак охотника", "Вихрь стали", "Последний рубеж",
            "Песнь войны", "Сила зверя", "Огненная кровь", "Благословение древних курганов", "Неудержимый натиск",
        };
        return Enumerable.Range(0, count).Select(i =>
        {
            var e = new TimedEffect($"standin_buff_{i}", i % 3 == 2 ? null : 2.5 + i * 0.7, i % 4 == 1 ? 3 : 1) { Kind = TimedEffectKind.Buff };
            return EffectChipData.From(e, names[i % names.Length]);
        }).ToList();
    }

    /// <summary><paramref name="count"/> stand-in ailments and debuffs on the hero (enemies spec 6.4, 9.2).</summary>
    private static List<EffectChipData> StandInDebuffs(int count)
    {
        var list = new (string Id, string Name, TimedEffectKind Kind, int Stacks)[]
        {
            ("chill", "охлаждение", TimedEffectKind.Ailment, 1), ("ignite", "поджог", TimedEffectKind.Ailment, 3), ("bleed", "кровотечение", TimedEffectKind.Ailment, 2),
            ("poison", "яд", TimedEffectKind.Ailment, 5), ("shackles", "Оковы", TimedEffectKind.Debuff, 1), ("shock", "шок", TimedEffectKind.Ailment, 1),
            ("weakness", "Слабость", TimedEffectKind.Debuff, 1), ("rot", "Гниль", TimedEffectKind.Debuff, 2), ("freeze", "заморозка", TimedEffectKind.Ailment, 1),
            ("blind", "Ослепление", TimedEffectKind.Debuff, 1), ("curse", "Проклятие немощи", TimedEffectKind.Debuff, 1), ("mark", "Метка обреченной жертвы", TimedEffectKind.Debuff, 1),
            ("stupor", "Оцепенение", TimedEffectKind.Debuff, 1), ("wound", "Гниющая рана", TimedEffectKind.Debuff, 3), ("sunder", "Разлом брони", TimedEffectKind.Debuff, 1),
            ("drain", "Иссушение воли", TimedEffectKind.Debuff, 1),
        };
        return Enumerable.Range(0, count).Select(i =>
        {
            var (id, name, kind, stacks) = list[i % list.Length];
            var e = new TimedEffect(id, 1.6 + i * 0.9, stacks) { Kind = kind };
            return EffectChipData.From(e, name);
        }).ToList();
    }

    // ---------- steps ----------

    private async Task HudAssignedSlotsWithoutFight()
    {
        var session = SandboxSession.Create(_main.Session!.Data, "warrior");
        var training = new TrainingRoom(session);
        training.Attach();
        var view = new SkillSlotView(1);
        _main.AddChild(view);
        try
        {
            Check(session.Build.WeaponId == null, "новый воитель уже вооружён");
            view.Show(CombatHud.DescribeSlot(session, training, 1, "1"), 0);
            Check(view.NameText == "пусто" && view.IconTexture == null && view.TooltipText.StartsWith("Слот 1 пуст", StringComparison.Ordinal), "новый слот не пуст");
            Check(session.SetSlotSkill(0, "cleave").Ok, "не назначен cleave без оружия");
            CheckAssigned("cleave");

            Check(session.SetLevel(60).Ok && session.SetSlotSkill(0, "crush").Ok, "не заменён скилл");
            CheckAssigned("crush");
            Check(!view.TooltipText.Contains(session.Data.Skills["cleave"].Name, StringComparison.Ordinal), "подсказка сохранила cleave");

            Check(session.SetSlotSkill(0, null).Ok, "не очищен слот");
            view.Show(CombatHud.DescribeSlot(session, training, 1, "1"), 0);
            Check(view.Data!.AssignedSkillId == null && view.Info == null && view.NameText == "пусто" && view.IconTexture == null,
                "после очистки остались назначение, имя или иконка");
            Check(view.TooltipText.StartsWith("Слот 1 пуст", StringComparison.Ordinal) && !view.TooltipText.Contains("Недоступен:", StringComparison.Ordinal), "после очистки осталась причина");
            Check(session.SetSlotSkill(0, "cleave").Ok, "не возвращён cleave");
            CheckAssigned("cleave");
            Check(view.CustomMinimumSize == new Vector2(56, 52), "размер слота не 56×52");
            await NextFrame();
        }
        finally
        {
            training.Detach();
            view.QueueFree();
            await NextFrame();
        }

        void CheckAssigned(string id)
        {
            var data = CombatHud.DescribeSlot(session, training, 1, "1");
            view.Show(data, 0);
            Check(!training.Running && training.State == null && training.Slots.All(s => s == null), "без оружия появился бой");
            Check(data.Info == null && data.AssignedSkillId == id && view.NameText == session.Data.Skills[id].Name, "назначенный скилл скрыт без боя");
            Check(view.IconTexture != null && view.IconTexture == GameIcons.Skill(id), "иконка не соответствует назначению");
            Check(training.Violations.Count > 0 && !string.IsNullOrWhiteSpace(data.UnavailableReason)
                && training.Violations.All(v => view.TooltipText.Contains(v.Message, StringComparison.Ordinal)), "нет причины отсутствия боя");
            Check(data.CostNow == null && data.CooldownLeft == null && !data.ResourceShort && !data.Queued && !data.Held
                && !view.TooltipText.Contains("Стоимость:", StringComparison.Ordinal), "без боя показаны выдуманные боевые значения");
        }
    }

    /// <summary>Each component draws a stand-in record with no room behind it.</summary>
    private async Task HudComponentsStandalone()
    {
        var box = new VBoxContainer { Name = "HudStandalone", Position = new Vector2(20, 120) };
        _main.AddChild(box);
        try
        {
            var info = new SlotInfo(3, "standin_strike", "Удар наотмашь", TagSet.Empty, 30, 0, 6, 0.6, false);
            var slot = new SkillSlotView(3);
            box.AddChild(slot);
            var data = new SkillSlotData(3, info, "[B]", "Ярость", 2.54, 24, ResourceShort: true, Queued: false, Held: false);
            slot.Show(data, 0);
            Check(slot.Data == data && slot.KeyText == "[B]" && slot.NameText == "Удар наотмашь" && slot.CooldownText == "2,5", $"слот: «{slot.KeyText}» «{slot.NameText}» «{slot.CooldownText}»");
            Check(slot.ResourceShort && slot.FrameColor == SkillSlotView.ShortColor && slot.CostNow == 24, "слот: нет рамки нехватки ресурса");
            Check(slot.TooltipText.Contains("Стоимость: 30 ярости") && slot.TooltipText.Contains("Сейчас стоит: 24 ярости"), $"подсказка слота «{slot.TooltipText}»");
            slot.Show(data with { ResourceShort = false, Held = true, CooldownLeft = null }, 0);
            Check(slot.FrameColor == SkillSlotView.HeldColor && slot.CooldownText == "", "слот: канал не подсвечен");
            slot.Show(new SkillSlotData(4, null, "4", "Мана", null, null, false, false, false), 0);
            Check(slot.NameText == "пусто" && slot.TooltipText.StartsWith("Слот 3 пуст", StringComparison.Ordinal), $"пустой слот «{slot.NameText}»");

            var bar = new ResourceBarView(300);
            box.AddChild(bar);
            bar.Show(new ResourceBarData("Ярость", "fury", 45.9, 100));
            Check(bar.Visible && bar.Text == "Ярость 45 / 100" && bar.FillColor == HudColors.Fury, $"полоса «{bar.Text}», цвет {bar.FillColor}");
            bar.Show(new ResourceBarData("Скверна", "corruption", 60, 200));
            Check(bar.Text == "Скверна 60 / 200" && bar.FillColor == HudColors.Corruption, $"полоса скверны «{bar.Text}»");
            bar.Show(null);
            Check(!bar.Visible, "полоса без данных видна");

            var buffs = new EffectRowView(EffectRowKind.Buffs, 9);
            var debuffs = new EffectRowView(EffectRowKind.Debuffs, 9);
            box.AddChild(buffs);
            box.AddChild(debuffs);
            var b = StandInBuffs(4);
            var d = StandInDebuffs(4);
            buffs.Show(b);
            debuffs.Show(d);
            Check(buffs.ShownTexts.SequenceEqual(b.Select(x => x.Text)) && debuffs.ShownTexts.SequenceEqual(d.Select(x => x.Text)), "ряды показывают не те эффекты");
            Check(buffs.Frame == ChipFrame.Bronze && debuffs.Frame == ChipFrame.Torn, $"рамки рядов {buffs.Frame} / {debuffs.Frame}");
            Check(d.Any(x => x.Text == "Поджог ×3 · 2,5 с") && d.Any(x => x.Text.StartsWith("Охлаждение ", StringComparison.Ordinal)), "тексты недугов: " + string.Join(", ", d.Select(x => x.Text)));
            var torn = EffectChip.TornOutline(new Vector2(80, 24));
            Check(torn.Length > 20 && EffectChip.TornDepth >= 3 && EffectChip.TornWidth >= 2 && EffectChip.TornStep <= 6, "у рваной рамки мелкие зубцы");
            // Buffs carry no kind colour (a green edge read like poison's); the debuffs keep the damage type's edge.
            var buffChip = buffs.GetChildren().OfType<EffectChip>().First(c => c.Visible && !c.IsOverflow);
            var style = (StyleBoxFlat)buffChip.GetThemeStylebox("panel");
            Check(style.BorderColor == HudColors.Bronze && style.BorderWidthTop >= 2, "у баффа не бронзовая рамка");
            Check(EffectChip.TextOf(new TimedEffect("freeze", 0.03, 1) { Kind = TimedEffectKind.Ailment }, "заморозка") == "Заморозка", "таймер 0,0 с показан");

            var plate = new TargetPlateView(6);
            box.AddChild(plate);
            var plateData = new TargetPlateData(2, "Гниющий вожак", TargetRank.Rare, 412, 650, StandInDebuffs(3));
            plate.Show(plateData);
            Check(plate.Visible && plate.NameText == "Гниющий вожак" && plate.LifeText == "412 / 650", $"плашка «{plate.NameText}» «{plate.LifeText}»");
            Check(plate.NameColor == HudColors.RareName && plate.IconShown, "имя вожака без значка или не золотое");
            Check(plate.Effects.ShownTexts.Count() == 3 && plate.Effects.Frame == ChipFrame.Accent, "недуги на плашке");
            plate.Show(plateData with { Rank = TargetRank.Normal, LifeMax = double.PositiveInfinity });
            Check(!plate.IconShown && plate.NameColor == HudColors.NormalName && plate.LifeText == "бессмертен" && TargetPlateView.LifeFraction(plate.Data!) == 1, "обычная бессмертная цель");
            plate.Show(null);
            Check(!plate.Visible, "плашка без цели видна");
            await NextFrame();
        }
        finally
        {
            box.QueueFree();
            await NextFrame();
        }
    }

    /// <summary>In a fight the components' records are the core's state: slots, the resource, the buffs, the target plate.</summary>
    private async Task HudComponentsMatchState()
    {
        var hud = _main.Hud!;
        await LoadForCombat(WarriorMelee);
        PlaceNearDummy();
        var cry = SlotOf("battle_cry");
        var cleave = SlotOf("cleave");
        Check(Driver.PressSlot(cry).Accepted, "Боевой клич не принят");
        await Run(0.7); // the press fires at the next free step; a newer press would replace it
        await Run(4.0, _ => { if (Training.State is { Busy: false }) Driver.PressSlot(cleave); },
            until: () => Training.State!.Time > 0.8 && Training.State.TargetEffects.Count > 0 && Training.State.TargetEffects[0].Count > 0);
        hud.Refresh();
        var state = Training.State!;
        foreach (var view in hud.Slots)
        {
            var n = view.Number;
            var d = view.Data!;
            Check(d == hud.SlotData(n), $"слот {n}: запись вида не та, что строится из State");
            Check(d.Info == Training.Slots[n - 1], $"слот {n}: скилл");
            Check(d.CooldownLeft == (state.CooldownLeftBySlot.TryGetValue(n, out var cd) && cd > 0 ? cd : null), $"слот {n}: перезарядка {d.CooldownLeft}, в State {cd}");
            Check(d.Info == null || d.CostNow == state.CostBySlot[n], $"слот {n}: стоимость {d.CostNow}");
            Check(d.ResourceShort == (d.Info != null && state.ResourceShortSlots.Contains(n)), $"слот {n}: нехватка {d.ResourceShort}");
            Check(d.Queued == (state.QueuedSlot == n) && d.Held == (state.HeldSlot == n), $"слот {n}: очередь/канал");
        }
        var r = hud.Resource.Data!;
        Check(r.Value == state.Resource && r.Max == state.ResourceMax && r.ResourceId == state.ResourceId && r.Name == Training.ResourceName, $"ресурс {r}");
        Check(state.ResourceId == "fury" && hud.Resource.FillColor == HudColors.Fury, $"цвет Ярости на полосе {hud.Resource.FillColor}");
        var buffs = hud.HeroBuffs.Effects;
        Check(buffs.Count == state.HeroEffects.Count && buffs.Zip(state.HeroEffects).All(p => p.First.Effect == p.Second),
            $"баффы на экране {string.Join(", ", buffs.Select(b => b.Id))}, в State {string.Join(", ", state.HeroEffects.Select(e => e.Id))}");
        Check(buffs.Any(b => b.Id == "battle_cry"), "нет баффа Боевого клича: " + string.Join(", ", hud.HeroTexts));
        Check(!hud.HeroDebuffs.Visible && hud.HeroDebuffs.Effects.Count == 0, "у героя в комнате показаны дебаффы");
        var plate = hud.TargetPlate.Data!;
        Check(hud.PlateShown && hud.PlateTarget == plate.Target, "плашки цели нет");
        Check(plate.Life == state.TargetLife[plate.Target] && plate.LifeMax == state.TargetLifeMax[plate.Target] && !double.IsInfinity(plate.LifeMax),
            $"здоровье на плашке {plate.Life}/{plate.LifeMax}, в State {state.TargetLife[plate.Target]}/{state.TargetLifeMax[plate.Target]}");
        Check(hud.TargetPlate.LifeText == TargetPlateData.From(state, plate.Target, plate.Name, new EffectNames(_main.Session!.Data))!.LifeText, $"текст здоровья «{hud.TargetPlate.LifeText}»");
        var onTarget = state.TargetEffects[plate.Target];
        Check(plate.Effects.Count == onTarget.Count && plate.Effects.Zip(onTarget).All(p => p.First.Effect == p.Second),
            $"эффекты на плашке {string.Join(", ", plate.Effects.Select(e => e.Text))}");
        GD.Print($"SELFTEST NOTE HUD в бою: {hud.Resource.Text}; баффы {string.Join(", ", hud.HeroTexts)}; плашка {plate.Name} {plate.LifeText}: {string.Join(", ", hud.PlateTexts)}");
    }

    /// <summary>HUD spec §9: health dark red, mana blue, fury amber-ochre (not red, not fire's orange), corruption violet with a dark core.</summary>
    private Task HudResourceColours()
    {
        var fury = HudColors.Fury;
        var fire = EffectChip.ColorOf(new TimedEffect("ignite", 1, 1) { Kind = TimedEffectKind.Ailment });
        float Hue(Color c) => HudColors.HueDegrees(c);
        Check(Hue(fury) is >= 34 and <= 48 && fury.S is >= 0.6f and <= 0.85f && fury.V is >= 0.65f and <= 0.85f, $"Ярость не янтарно-охристая: тон {Hue(fury):0}°, насыщенность {fury.S:0.00}, яркость {fury.V:0.00}");
        Check(Hue(fury) - Hue(fire) >= 8, $"Ярость ({Hue(fury):0}°) близка к огню ({Hue(fire):0}°)");
        var life = HudColors.Life;
        Check((Hue(life) < 10 || Hue(life) > 350) && life.V < 0.65f, $"здоровье не темно-красное: {life}");
        Check(Hue(fury) - Math.Min(Hue(life), 360 - Hue(life)) >= 25, "Ярость близка к здоровью");
        Check(Hue(HudColors.Mana) is > 200 and < 250, $"мана не синяя: {Hue(HudColors.Mana):0}°");
        Check(Hue(HudColors.Corruption) is > 260 and < 300 && HudColors.CorruptionCore.V < HudColors.Corruption.V * 0.5f, "Скверна не фиолетовая с темным ядром");
        Check(HudColors.Resource("fury") == fury && HudColors.Resource("mana") == HudColors.Mana && HudColors.Resource("corruption") == HudColors.Corruption, "цвет ресурса по id");
        return Task.CompletedTask;
    }

    /// <summary>15+ effects fold into «+N»; the full list shows on hover (the shared tooltip) and on focus; the debuff row has a torn frame.</summary>
    private async Task HudRowsFold()
    {
        var hud = _main.Hud!;
        await LoadForCombat(WarriorMelee);
        Device.Set(InputKind.KeyboardMouse);
        // Exactly the maximum fits without «+N».
        hud.PreviewHeroRows(StandInBuffs(CombatHud.HeroRowMax), null);
        Check(!hud.HeroBuffs.Overflow.Visible && hud.HeroTexts.Count() == CombatHud.HeroRowMax, "ряд свернулся без переполнения");

        var buffs = StandInBuffs(16);
        var debuffs = StandInDebuffs(15);
        hud.PreviewHeroRows(buffs, debuffs);
        await Settle();
        foreach (var (row, list) in new[] { (hud.HeroBuffs, buffs), (hud.HeroDebuffs, debuffs) })
        {
            var shown = row.ShownTexts.ToList();
            var folded = list.Count - (CombatHud.HeroRowMax - 1);
            Check(row.Visible && shown.Count == CombatHud.HeroRowMax && shown[^1] == $"+{folded}" && row.Folded == folded, $"{row.Kind}: {string.Join(" | ", shown)}");
            Check(shown.Take(shown.Count - 1).SequenceEqual(list.Take(CombatHud.HeroRowMax - 1).Select(e => e.Text)), $"{row.Kind}: первые значки не по порядку");
            var full = row.Overflow.TooltipText;
            Check(full == EffectRowView.FullList(row.Kind, list) && list.All(e => full.Contains(e.Text, StringComparison.Ordinal)), $"{row.Kind}: полный список «{full}»");
        }
        Check(hud.HeroDebuffs.Frame == ChipFrame.Torn && hud.HeroBuffs.Frame == ChipFrame.Bronze, "дебаффы отличаются от баффов только цветом");
        var columnTop = hud.BottomRect.Position.Y;
        Check(hud.HeroDebuffs.GetGlobalRect().End.Y <= hud.HeroBuffs.GetGlobalRect().Position.Y + 0.5f && columnTop > _main.Size.Y / 2, $"ряды налезают: колонка с {columnTop}");

        // Focus: the row's own tooltip (the shared component) over the row, the «+N» itself uncovered.
        Check(hud.HeroBuffs.FocusOverflow(), "«+N» не взял фокус");
        await Settle();
        Check(hud.FocusTooltip == hud.HeroBuffs.Overflow.TooltipText, $"по фокусу показано «{hud.FocusTooltip}»");
        Check(!hud.FocusTooltipRect.Intersects(hud.HeroBuffs.Overflow.GetGlobalRect()) && new Rect2(Vector2.Zero, _main.Size).Encloses(hud.FocusTooltipRect),
            $"подсказка по фокусу {hud.FocusTooltipRect} на «+N» {hud.HeroBuffs.Overflow.GetGlobalRect()}");
        hud.HeroBuffs.Overflow.ReleaseFocus();
        await Settle();
        Check(hud.FocusTooltip == null && hud.HeroBuffs.Overflow.FocusMode == Control.FocusModeEnum.None, "подсказка по фокусу не скрылась");

        // Hover: the windows' shared tooltip over the debuff row's «+N».
        var more = hud.HeroDebuffs.Overflow;
        var centre = more.GetGlobalRect().GetCenter();
        _main.GetViewport().PushInput(new InputEventMouseMotion { Position = centre, GlobalPosition = centre });
        var waitFrom = Time.GetTicksMsec();
        while (_main.Windows.HoverTipControl != more && Time.GetTicksMsec() - waitFrom < 3000) await NextFrame();
        Check(_main.Windows.HoverTipControl == more && _main.Windows.FocusTooltip == more.TooltipText, $"по наведению показано «{_main.Windows.FocusTooltip}» над {_main.Windows.HoverTipControl?.Name}");
        var away = new Vector2(2, _main.Size.Y - 2);
        _main.GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away });

        hud.PreviewHeroRows(null, null);
        await Settle();
        Check(!hud.HeroDebuffs.Visible && !hud.HeroBuffs.Overflow.Visible, "после подставных данных ряды не вернулись к бою");
    }

    /// <summary>
    /// Whose plate: with the mouse the figure under the cursor, else the ring's (the newer of the last hit and Tab); with the
    /// gamepad the ring's, which holds the RB target (the rule itself, then on «Рой» through the real cursor path, Tab and RB).
    /// </summary>
    private async Task HudPlateTargetRule()
    {
        Check(CombatHud.ChooseTarget(false, 3, 2, 5) == 3, "мышь: не фигура под курсором");
        Check(CombatHud.ChooseTarget(false, null, 2, 5) == 2, "мышь: не фигура кольца");
        Check(CombatHud.ChooseTarget(true, 3, 1, 5) == 1, "геймпад: не фигура кольца");
        Check(CombatHud.ChooseTarget(false, null, null, 1) == 0, "до первого удара нет плашки манекена");
        Check(CombatHud.ChooseTarget(false, 0, 0, 0) == null, "без фигур есть плашка");

        var hud = _main.Hud!;
        await LoadForCombat(WarriorMelee);
        Device.Set(InputKind.KeyboardMouse);
        Driver.SetDummy("swarm");
        var dummy = Room.Dummy;
        Room.PlaceHero(dummy.TargetPosition(0) + TowardsCamera * 3.2f);
        Room.CameraRig.ResetZoom();
        Room.CameraRig.Follow();
        await Fight(2.5, close: false);
        await Settle();
        Check(Driver.LastHitFigure != null && dummy.RingTarget == Driver.LastHitFigure, $"по «Рою» не было попаданий или кольцо {dummy.RingTarget} не у последней {Driver.LastHitFigure}");
        var last = Driver.LastHitFigure!.Value;
        hud.Refresh();
        Check(hud.PlateTarget == last && hud.PlateName == $"Рой · цель {last + 1}", $"без курсора плашка «{hud.PlateName}» у фигуры {hud.PlateTarget}, кольцо {last}");

        // The cursor over another figure: the plate goes there, over its head, with the chevron marking that figure.
        // The hero steps behind the pack (the camera follows him), so the pack stands low on screen with room for the plate.
        Room.PlaceHero(dummy.TargetPosition(0) - TowardsCamera * 3.0f);
        Room.CameraRig.Follow();
        await Settle();
        // The lowest figure on screen that the cursor finds (its plate is least likely to meet the top bar).
        int? hovered = null;
        Vector2 at = default;
        for (int i = 0; i < dummy.FigureCount; i++)
        {
            if (i == last || _main.RoomToScreen(dummy.HitPoint(i)) is not { } p || _main.ScreenToFigure(p) != i) continue;
            if (hovered == null || p.Y > at.Y) (hovered, at) = (i, p);
        }
        Check(hovered != null, "ни одна фигура «Роя» не находится курсором");
        _main.GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await NextFrame();
        hud.Refresh();
        Check(hud.HoveredFigure() == hovered && hud.PlateTarget == hovered, $"курсор над фигурой {hovered}: под курсором {hud.HoveredFigure()}, плашка {hud.PlateTarget}");
        var head = _main.RoomToScreen(dummy.TargetPosition(hovered!.Value) + new Vector3(0, dummy.Figures[hovered.Value].Model.TargetHeight + 0.05f, 0))!.Value;
        var rect = hud.PlateRect;
        Check(Mathf.Abs(rect.GetCenter().X - head.X) < 2, $"плашка {rect} не над фигурой {head}");
        if (rect.Position.Y > TopBar.Height + 4.5f)
            Check(rect.End.Y <= head.Y + 1 && hud.MarkerShown && hud.MarkerTip.DistanceTo(head) < 2 && rect.End.Y <= hud.MarkerTip.Y - CombatHud.MarkerHeight + 0.5f,
                $"плашка {rect} или метка {hud.MarkerTip} не над головой фигуры {head}");
        else Check(!hud.MarkerShown, "плашка прижата к верхней панели, а метка видна"); // a head near the top edge: the plate covers it
        GD.Print($"SELFTEST NOTE плашка «Роя» над фигурой {hovered}: {rect}, голова {head}, метка {(hud.MarkerShown ? hud.MarkerTip.ToString() : "нет")}");
        Check(hud.TargetPlate.Data!.Life == Training.State!.TargetLife[hovered.Value], "здоровье на плашке не той фигуры");

        // The cursor leaves the window: it hovers nothing any more.
        hud.Notification((int)Node.NotificationWMMouseExit);
        hud.Refresh();
        Check(hud.HoveredFigure() == null && hud.PlateTarget == dummy.RingTarget, "курсор ушел из окна, а плашка осталась у фигуры под ним");

        // Tab (newer than the last hit) moves the ring and the plate; the next hit takes them back.
        var floor = _main.RoomToScreen(Room.Hero.Position - TowardsCamera * 1.5f)!.Value; // behind the hero, away from the pack
        _main.GetViewport().PushInput(new InputEventMouseMotion { Position = floor, GlobalPosition = floor });
        await NextFrame();
        Driver.SelectNextTarget();
        hud.Refresh();
        Check(hud.HoveredFigure() == null && dummy.RingTarget == Driver.SelectedTarget && hud.PlateTarget == Driver.SelectedTarget, $"Tab: кольцо {dummy.RingTarget}, плашка {hud.PlateTarget}, выбор {Driver.SelectedTarget}");

        // The gamepad: the ring holds the RB target through the hits until the next RB; the plate shows the ring's figure.
        Room.PlaceHero(dummy.TargetPosition(0) + TowardsCamera * 3.2f); // back within reach: hits land again
        Room.CameraRig.Follow();
        await Settle();
        at = _main.RoomToScreen(dummy.HitPoint(hovered.Value)) ?? at;
        _main.GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await NextFrame();
        Device.Set(InputKind.Gamepad);
        await NextFrame();
        do Driver.SelectNextTarget(); while (Driver.SelectedTarget == hovered);
        var rb = Driver.SelectedTarget;
        await Fight(1.5, close: false);
        hud.Refresh();
        Check(dummy.RingTarget == rb && hud.PlateTarget == rb && hud.PlateTarget != hovered, $"геймпад: кольцо {dummy.RingTarget}, плашка {hud.PlateTarget}, цель RB {rb}, последний удар {Driver.LastHitFigure}");
        Device.Set(InputKind.KeyboardMouse);
        var away = new Vector2(2, _main.Size.Y - 2);
        _main.GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away });
        Driver.SetDummy(TrainingRoom.DefaultDummyId);
        await Settle();
    }

    /// <summary>
    /// The gamepad reaches the folded rows with real presses: Start (top bar), ↓ to the debuff «+N» (its list shows beside the
    /// column, the prompt row under the top bar), ↓ to the buff «+N», A keeps the list, the stick does not move the hero, B back
    /// to the fight.
    /// </summary>
    private async Task HudPadRowsRoute()
    {
        var hud = _main.Hud!;
        await LoadForCombat(WarriorMelee);
        PlaceNearDummy();
        await PadMode();
        hud.PreviewHeroRows(StandInBuffs(16), StandInDebuffs(15));
        await Settle();
        Check(hud.PadTargets().SequenceEqual(new Control[] { hud.HeroDebuffs.Overflow, hud.HeroBuffs.Overflow }), "цели геймпада: " + string.Join(", ", hud.PadTargets().Select(c => c.GetParent().Name)));
        try
        {
            PadTap(JoyButton.Start);
            await Settle();
            Check(Host.FocusedWindow == WindowHost.TopBarId && Host.BarPromptText?.Contains("Эффекты") == true, $"Start: фокус {Host.FocusedWindow}, строка «{Host.BarPromptText}»");
            PadTap(JoyButton.DpadDown);
            await Settle();
            Check(Host.FocusedWindow == WindowHost.HudId && FocusOwner == hud.HeroDebuffs.Overflow, $"↓: фокус {Host.FocusedWindow}, {FocusOwner?.Name}");
            Check(hud.FocusTooltip == hud.HeroDebuffs.Overflow.TooltipText && Host.FocusTooltip == null, $"список дебаффов по фокусу «{hud.FocusTooltip}»");
            Check(Host.BarPromptText == ButtonPrompts.Line(WindowHost.HudPrompts), $"строка кнопок «{Host.BarPromptText}»");
            var tip = hud.FocusTooltipRect;
            Check(!tip.Intersects(hud.HeroDebuffs.GetGlobalRect()) && !tip.Intersects(hud.HeroBuffs.GetGlobalRect()), $"список {tip} закрывает ряды");
            PadTap(JoyButton.DpadDown);
            await Settle();
            Check(FocusOwner == hud.HeroBuffs.Overflow && hud.FocusTooltip == hud.HeroBuffs.Overflow.TooltipText, $"второе ↓: фокус {FocusOwner?.Name}");
            PadTap(JoyButton.A);
            await Settle();
            Check(Host.FocusedWindow == WindowHost.HudId && hud.FocusTooltip == hud.HeroBuffs.Overflow.TooltipText, "A сбросил список");
            var from = Room.Hero.Position;
            Axis(JoyAxis.LeftX, 1f);
            for (int i = 0; i < 20; i++) { Driver.Tick(FrameSec); }
            await NextFrame();
            Check(Room.Hero.MoveInput == Vector2.Zero && Room.Hero.Position.DistanceTo(from) < 0.01f, $"герой идет, пока фокус на «+N»: {Room.Hero.MoveInput}");
            Axis(JoyAxis.LeftX, 0f);
            PadTap(JoyButton.B);
            await Settle();
            Check(Host.FocusedWindow == null && FocusOwner == null && hud.FocusTooltip == null && Host.BarPromptText == null, $"B: фокус {Host.FocusedWindow}, {FocusOwner?.Name}");
        }
        finally
        {
            Host.FocusCombat();
            hud.PreviewHeroRows(null, null);
            Device.Set(InputKind.KeyboardMouse);
        }
    }

    /// <summary>Screenshots for `dialoid-ui-curator` and `dialoid-art-curator` (window run only).</summary>
    private async Task HudScreenshots()
    {
        var hud = _main.Hud!;
        var room = Room;
        var driver = Driver;
        try
        {
            await LoadForCombat(WarriorMelee);
            Device.Set(InputKind.KeyboardMouse);
            room.CameraRig.ResetZoom();
            room.PlaceHero(room.Dummy.TargetPosition(0) + new Vector3(1, 0, -1).Normalized() * 1.3f + TowardsCamera * 0.4f);
            int cry = SlotOf("battle_cry"), cleave = SlotOf("cleave"), crush = SlotOf("crush");
            Check(driver.PressSlot(cry).Accepted, "Боевой клич не принят");
            await Run(0.7);
            await Run(6.0, _ => { if (Training.State is { Busy: false }) driver.PressSlot(cleave); },
                until: () => Training.State!.Time > 3 && Training.State.TargetEffects.Count > 0 && Training.State.TargetEffects[0].Any(e => e.Id == "ignite"));
            await Run(2.0, _ => { if (Training.State is { Busy: false }) driver.PressSlot(crush); });
            await Run(FrameSec * 3);
            await Shot("task-14-hud-fight");

            hud.PreviewHeroRows(null, StandInDebuffs(5));
            await Shot("task-14-debuff-row");

            hud.PreviewHeroRows(StandInBuffs(16), StandInDebuffs(15));
            await Shot("task-14-fold");
            hud.HeroBuffs.FocusOverflow();
            await Shot("task-14-fold-focus-list");
            hud.HeroBuffs.Overflow.ReleaseFocus();
            var more = hud.HeroDebuffs.Overflow.GetGlobalRect().GetCenter();
            Godot.Input.WarpMouse(more); // the hover tooltip stands at the real cursor in a window
            _main.GetViewport().PushInput(new InputEventMouseMotion { Position = more, GlobalPosition = more });
            var waitFrom = Time.GetTicksMsec();
            while (_main.Windows.HoverTipControl != hud.HeroDebuffs.Overflow && Time.GetTicksMsec() - waitFrom < 3000) await NextFrame();
            await Shot("task-14-fold-hover-list");
            hud.PreviewHeroRows(null, null);

            Device.Set(InputKind.Gamepad);
            await Shot("task-14-hud-gamepad");
            // The gamepad's way to a full list: Start, ↓ (the prompt row hangs under the top bar).
            hud.PreviewHeroRows(StandInBuffs(16), StandInDebuffs(15));
            PadTap(JoyButton.Start);
            await Settle();
            PadTap(JoyButton.DpadDown);
            await Shot("task-14-pad-rows-focus");
            PadTap(JoyButton.B);
            await Settle();
            hud.PreviewHeroRows(null, null);
            Device.Set(InputKind.KeyboardMouse);

            driver.SetDummy("swarm");
            var dummy = room.Dummy;
            room.PlaceHero(dummy.TargetPosition(0) + TowardsCamera * 3.2f);
            await Fight(3.0, close: false);
            room.PlaceHero(dummy.TargetPosition(0) - TowardsCamera * 3.0f); // behind the pack: it stands low on screen, the plate clear of the top bar
            room.CameraRig.Follow();
            await Settle();
            var last = driver.LastHitFigure ?? 0;
            for (int i = 0; i < dummy.FigureCount; i++)
            {
                if (i == last || _main.RoomToScreen(dummy.HitPoint(i)) is not { } p || _main.ScreenToFigure(p) != i) continue;
                Godot.Input.WarpMouse(p);
                _main.GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p });
                break;
            }
            await Shot("task-14-swarm-hover-plate");
            GD.Print($"SELFTEST NOTE снимок «Роя»: плашка у фигуры {hud.PlateTarget}, последняя под ударом {last}");
        }
        finally
        {
            hud.PreviewHeroRows(null, null);
            var away = new Vector2(2, _main.Size.Y - 2);
            _main.GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away });
            Device.Set(InputKind.KeyboardMouse);
            await CombatRestore();
        }
    }
}
