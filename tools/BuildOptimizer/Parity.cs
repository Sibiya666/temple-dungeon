using Dialoid.Core.Builds;
using Dialoid.Core.Character;

public static class Parity
{
    public static void ToLevel(Dictionary<string, string> o)
    {
        var b = H.LoadBuild(o["in"]); int L = int.Parse(o["level"]);
        b.Level = L; b.PassiveNodes.Clear(); b.AttributeChoices.Clear(); b.AscendancyNodes.Clear();
        foreach (var s in b.Slots) { s.Level = Math.Min(20, L); }
        File.WriteAllText(o["out"], BuildCodec.ToJson(b));
    }

    public static void L1(Dictionary<string, string> o)
    {
        foreach (var cls in H.Data.Classes.Values)
        {
            var results = new List<(double, Build)>();
            foreach (var sk in cls.SkillIds.Where(s => H.Data.Skills[s].UnlockLevel <= 1))
                foreach (var w in H.Data.Weapons.Keys.Append(null))
                    foreach (var node in H.Data.Skills[sk].Tree.Where(n => n.Requires.Count == 0).Select(n => n.Id))
                    {
                        var b = new Build { ClassId = cls.Id, Level = 1, WeaponId = w };
                        b.Slots.Add(new SkillSlot { SkillId = sk, Level = 1, NodePoints = new() { [node] = 1 } });
                        b.Rotation.Add(sk);
                        if (H.V(b).Count > 0) continue;
                        results.Add((H.Run(b, "training").DpsSingleTarget, b));
                    }
            foreach (var (v, b) in results.OrderByDescending(x => x.Item1).Take(6))
                Console.WriteLine($"{cls.Id} {v:F2} {b.Slots[0].SkillId} {b.Slots[0].NodePoints.Keys.First()} {b.WeaponId}");
            File.WriteAllText(Path.Combine(o["out-dir"], $"{cls.Id}_l1.json"), BuildCodec.ToJson(results.MaxBy(x => x.Item1).Item2));
        }
    }
}
