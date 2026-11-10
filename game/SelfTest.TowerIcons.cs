using System.Security.Cryptography;
using Dialoid.Core.Tower;
using Dialoid.Game.Ui.Tower;
using Godot;

namespace Dialoid.Game;

public sealed partial class SelfTest
{
    private async Task TowerCurrencyIconsRuntime()
    {
        var board = _main.TowerBoard!;
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var hashesBySize = new Dictionary<int, HashSet<string>>();
        foreach (int size in new[] { 64, 32, 24 }) hashesBySize[size] = new HashSet<string>(StringComparer.Ordinal);
        Texture2D CheckIcon(Texture2D? texture, string id, string category)
        {
            string expected = $"res://Ui/Tower/Art/{category}/{id}.png";
            Check(texture != null && texture.ResourcePath == expected,
                $"иконка {id} не назначена на точный PNG: {texture?.ResourcePath ?? "null"}");
            Check(seenPaths.Add(texture!.ResourcePath), $"иконка {id} повторяет чужой ресурс");
            var original = texture.GetImage();
            Check(original != null && original.GetWidth() >= 64 && original.GetHeight() >= 64, $"иконка {id} меньше 64px");
            if (original!.IsCompressed()) Check(original.Decompress() == Error.Ok, $"не распакован runtime PNG {id}");
            original.Convert(Image.Format.Rgba8);
            Check(original.DetectAlpha() != Image.AlphaMode.None, $"иконка {id} потеряла прозрачность");
            foreach (int size in new[] { 64, 32, 24 })
            {
                var scaled = Image.CreateFromData(original.GetWidth(), original.GetHeight(), false, Image.Format.Rgba8, original.GetData());
                scaled.Resize(size, size, Image.Interpolation.Lanczos);
                Check(hashesBySize[size].Add(Convert.ToHexString(SHA256.HashData(scaled.GetData()))),
                    $"иконка {id} дублирует другой рисунок при {size}px");
                if (_screenshotDir != null)
                {
                    var folder = Path.Combine(_screenshotDir, "runtime-icons");
                    Directory.CreateDirectory(folder);
                    Check(scaled.SavePng(Path.Combine(folder, $"{id}-{size}.png")) == Error.Ok, $"не сохранена проверка {id}/{size}");
                }
            }
            return texture;
        }
        foreach (var currency in board.Data!.Currencies)
        {
            var icon = CheckIcon(TowerBoardArt.Currency(currency.Id), currency.Id, "currencies");
            if (currency.Op == CurrencyOp.Shuffle)
            {
                var shuffle = (Button)board.FindChild("ShuffleInventory", true, false)!;
                Check(shuffle.Icon?.ResourcePath == icon.ResourcePath, "PNG Перемешивания не показан на реальной кнопке");
            }
            else
            {
                var button = (Button)board.FindChild("Currency_" + currency.Id, true, false)!;
                Check(button.GetChild<TextureRect>(0).Texture?.ResourcePath == icon.ResourcePath,
                    $"кнопка {currency.Id} сохранила старую иконку");
                Check(button.GetNode<Label>("Count").Text == board.Wallet!.CountUsable(currency.Id, board.Assembly!.Spec.Rank).ToString(),
                    $"счётчик {currency.Id} не совпадает с кошельком");
            }
        }
        CheckIcon(TowerBoardArt.Symbol("tower"), "tower", "symbols");
        var summitIcon = CheckIcon(TowerBoardArt.Symbol("summit"), "summit", "symbols");
        Check(((TextureRect)board.FindChild("SummitIcon", true, false)!).Texture?.ResourcePath == summitIcon.ResourcePath,
            "новая иконка вершины не показана в UI");
        Check(TowerBoardArt.Activity(null) == null && TowerBoardArt.Activity("empty_floor") == null,
            "пустое гнездо подменено общим рисунком");
        Check(TowerBoardArt.Currency("__unknown_currency__") == null, "неизвестный ID скрыт fallback иконкой");
        var originalSize = _main.GetTree().Root.Size;
        try
        {
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1600, 900) })
            {
                if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = size;
                else DisplayServer.WindowSetSize(size);
                _main.OpenTowerBoard();
                await Settle();
                foreach (var currency in board.Data.Currencies.Where(c => c.Op != CurrencyOp.Shuffle))
                {
                    var button = (Button)board.FindChild("Currency_" + currency.Id, true, false)!;
                    var motif = button.GetChild<TextureRect>(0);
                    Check(motif.Size.IsEqualApprox(new Vector2(24, 24)), $"runtime размер иконки {currency.Id} изменился");
                    var count = button.GetNode<Label>("Count");
                    Check(button.GetGlobalRect().Encloses(motif.GetGlobalRect()) && button.GetGlobalRect().Encloses(count.GetGlobalRect()),
                        $"рисунок/счётчик {currency.Id} выходит за hit target");
                }
                if (DisplayServer.GetName() != "headless") await _main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                CaptureTower($"currency-icons-{size.Y}.png");
                _main.CloseTowerBoard();
            }
        }
        finally
        {
            _main.CloseTowerBoard();
            if (DisplayServer.GetName() == "headless") _main.GetTree().Root.Size = originalSize;
            else DisplayServer.WindowSetSize(originalSize);
        }
    }
}
