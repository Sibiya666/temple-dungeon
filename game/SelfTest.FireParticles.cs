using Dialoid.Game.Room;
using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
    private async Task CombatFireParticles()
    {
        var layer = new EffectLayer();
        _main.AddChild(layer);
        try
        {
            foreach (var low in new[] { false, true })
            {
                layer.LightsAllowed = () => !low;
                var projectile = layer.Projectile(EffectKind.Fire, Vector3.Zero, Vector3.Right, .1);
                var tail = layer.Active.OfType<FireParticles>().Single();
                Check(tail.GetParent() == layer, "хвост принадлежит снаряду");
                Check(tail.ParticleBudget == (low ? 14 : 34), "неверный бюджет хвоста");
                layer.Paused = true;
                layer.Advance(.2);
                Check(projectile.Age == 0 && tail.Age == 0, "hit-stop не остановил visual age");
                Check(tail.GetChildren().OfType<GpuParticles3D>().All(p => p.SpeedScale == 0), "GPU не на паузе");
                layer.Paused = false;
                layer.Advance(.11);
                Check(projectile.Arrived && tail.Stopped && layer.Active.Contains(tail), "хвост не доживает попадание");
                layer.Paused = true;
                layer.Impact(EffectKind.Fire, Vector3.Right, false, false);
                var hit = layer.Active.OfType<FireParticles>().Single(p => p != tail);
                Check(hit.ParticleBudget == (low ? 9 : 21), "неверный бюджет попадания");
                Check(hit.GetChildren().OfType<GpuParticles3D>().All(p => p.SpeedScale == 0), "новые частицы не учли паузу");
                layer.Paused = false;
                layer.Advance(1.2);
                Check(layer.Count == 0, "эффекты не очищены после drain");
                await NextFrame();
                Check(layer.GetChildCount() == 0, "узлы GPU остались в дереве");
            }
            for (var i = 0; i < FireParticles.MaxRigs + 4; i++)
                layer.Impact(EffectKind.Fire, Vector3.Zero, true, false);
            Check(layer.Active.OfType<FireParticles>().Count() == FireParticles.MaxRigs, "нет ограничения GPU rigs");
            layer.Clear();
            await NextFrame();
            Check(layer.Count == 0 && layer.GetChildCount() == 0, "Clear оставил GPU узлы");
        }
        finally { layer.QueueFree(); }
    }
}
