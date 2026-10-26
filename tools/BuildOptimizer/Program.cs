// Dialoid reference-build optimizer (dev tool, not part of `dotnet test`).
//
// Rebuilds the reference builds in data/builds/ after balance changes. Run from anywhere inside the repository
// (or set DIALOID_DATA). Every simulation is SimMode.Average, 60 s, exactly as the content tests run it.
//
// Objective (maximized):
//   obj = sqrt(DpsSingleTarget on «Тренировочный» × DpsPack on «Рой»)            (--train-only: training DPS alone)
//         × (min(TimeToDeath vs «Атакующий босс», 180) / 180)^w                  (w = --survival-weight, default 0)
//         × penalties
// Penalties (soft constraints, so local search can pass through infeasible states):
//   - each rotation skill never used in the training log: × 0.3;
//   - --skill S^m: S must deal ≥ m × the damage of every other skill (default m = 1.25), else × 0.3·ratio²;
//     --skill S>=x: S must deal ≥ x of all damage instead;
//   - --type T>=x: damage type T must be ≥ x of all damage (default x = 0.6), else × 0.3·(share/x)²;
//   - --ban a|b|...: passive or skill-tree node ids starting with any prefix, "slot:<skill>" and "asc:<notable>" are illegal (−∞);
//     keystones are always banned unless --allow-keystones (reference builds carry none, spec 15.6 adds them).
// Illegal builds (BuildRules) score −∞.
//
// Search (deterministic, seed 12345), repeated --rounds times:
//   all notable combinations of the ascendancy (--asc fixes it) → greedy passive paths (≤3 nodes, value per point) when
//   points are unspent → per slot: greedy tree from scratch when points are unspent, else 1-for-1 / 2-for-2 point moves →
//   passive 1-for-1 and 2-for-2 swaps → (--lns) 25 random destroy-and-regrow passive steps and 8 per tree →
//   attribute choices → every rotation permutation → (--slots) each unused class skill in each slot.
//
// Commands:
//   opt --in <build> --out <file> [--skill S^m | --skill S>=x] [--type T>=x] [--train-only] [--asc id] [--rounds n]
//       [--lns] [--slots] [--scratch-passives] [--scratch-trees] [--ban p1|p2] [--allow-keystones] [--survival-weight w]
//   eval <build>...            metrics (training, swarm, survival, boss time-to-kill, damage shares, uses)
//   check <build>              contribution of each notable, each removable passive leaf; skill points with no effect
//   attrvar --in <build> --out <file> [--skill ..] [--type ..] [--ban ..]   attribute-node swap variant (spec 15.9)
//   tolevel --in <build> --out <file> --level L                            seed for a lower-level build
//   l1 --out-dir <dir>                                                     exhaustive level-1 parity search
//   fmt <file>...              rewrite build files in BuildCodec.ToJson format
// <build> is a path to a .json file or a name under data/builds/ without .json.
// See rebuild_references.sh for the exact recipe of every reference build.

var mode = args.Length > 0 ? args[0] : "help";
Dictionary<string, string> Options()
{
    var o = new Dictionary<string, string>();
    for (int i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) throw new ArgumentException($"unexpected argument {args[i]}");
        var key = args[i][2..];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) o[key] = args[++i];
        else o[key] = "";
    }
    return o;
}
switch (mode)
{
    case "eval": foreach (var f in args.Skip(1)) H.Report(f, H.LoadBuild(f)); break;
    case "check": Check.Run(args[1]); break;
    case "fmt": foreach (var f in args.Skip(1)) File.WriteAllText(f, Dialoid.Core.Builds.BuildCodec.ToJson(H.LoadBuild(f))); break;
    case "attrvar": AttrVar.Run(Options()); break;
    case "tolevel": Parity.ToLevel(Options()); break;
    case "l1": Parity.L1(Options()); break;
    case "opt": Opt.Main2(Options()); break;
    default: Console.WriteLine("usage: see the header of Program.cs"); break;
}
