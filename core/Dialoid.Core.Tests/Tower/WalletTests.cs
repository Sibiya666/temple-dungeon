using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

public class WalletTests
{
    private static readonly TowerData D = TowerDataLoader.Load(FixturePaths.TowerFixture).Data!;

    private static Dictionary<int, int> Snapshot(Wallet w, string id) => w.Levels(id).ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Spends_lowest_usable_level_first()
    {
        var w = new Wallet(D);
        w.Add("imbue", 3, 2);
        w.Add("imbue", 5, 2);
        w.Add("imbue", 2, 4);
        Assert.Equal(4, w.CountUsable("imbue", 3));
        Assert.True(w.TrySpend("imbue", 3, 3));
        Assert.Equal(new Dictionary<int, int> { [2] = 4, [5] = 1 }, Snapshot(w, "imbue"));
    }

    [Fact]
    public void Low_level_is_unusable_on_higher_rank()
    {
        var w = new Wallet(D);
        w.Add("imbue", 5, 3);
        Assert.Equal(0, w.CountUsable("imbue", 6));
        Assert.False(w.TrySpend("imbue", 6, 1));
        Assert.Equal(3, w.CountUsable("imbue", 5));
        Assert.Equal(3, w.CountUsable("imbue", 1));
    }

    [Fact]
    public void Spend_is_all_or_nothing()
    {
        var w = new Wallet(D);
        w.Add("imbue", 4, 1);
        w.Add("imbue", 6, 1);
        w.Add("imbue", 2, 5);
        var before = Snapshot(w, "imbue");
        Assert.False(w.TrySpend("imbue", 4, 3));
        Assert.Equal(before, Snapshot(w, "imbue"));
        Assert.True(w.TrySpend("imbue", 4, 2));
        Assert.Equal(new Dictionary<int, int> { [2] = 5 }, Snapshot(w, "imbue"));
    }

    [Fact]
    public void Exchange_up_is_4_to_1_neighbor_only()
    {
        var w = new Wallet(D);
        w.Add("imbue", 3, 9);
        Assert.True(w.TryExchangeUp("imbue", 3));
        Assert.Equal(new Dictionary<int, int> { [3] = 5, [4] = 1 }, Snapshot(w, "imbue"));
        Assert.True(w.TryExchangeUp("imbue", 3));
        Assert.Equal(new Dictionary<int, int> { [3] = 1, [4] = 2 }, Snapshot(w, "imbue"));
        Assert.False(w.TryExchangeUp("imbue", 3));
        Assert.False(w.TryExchangeUp("imbue", 4));
        Assert.Equal(new Dictionary<int, int> { [3] = 1, [4] = 2 }, Snapshot(w, "imbue"));
    }

    [Fact]
    public void Level_1_cannot_be_exchanged()
    {
        var w = new Wallet(D);
        w.Add("imbue", 1, 40);
        Assert.False(w.TryExchangeUp("imbue", 1));
        Assert.Equal(new Dictionary<int, int> { [1] = 40 }, Snapshot(w, "imbue"));
    }

    [Fact]
    public void Top_level_cannot_be_exchanged()
    {
        var w = new Wallet(D);
        w.Add("imbue", 15, 8);
        Assert.False(w.TryExchangeUp("imbue", 15));
        w.Add("imbue", 14, 4);
        Assert.True(w.TryExchangeUp("imbue", 14));
        Assert.Equal(new Dictionary<int, int> { [15] = 9 }, Snapshot(w, "imbue"));
    }

    [Fact]
    public void Levelless_currency_works_on_any_rank()
    {
        var w = new Wallet(D);
        w.Add("clear", 7, 2);
        Assert.Equal(new Dictionary<int, int> { [0] = 2 }, Snapshot(w, "clear"));
        Assert.Equal(2, w.CountUsable("clear", 15));
        Assert.Equal(2, w.CountUsable("clear", 1));
        Assert.True(w.TrySpend("clear", 15, 1));
        Assert.Equal(1, w.CountUsable("clear", 1));
        Assert.False(w.TryExchangeUp("clear", 0));
        Assert.False(w.TryExchangeUp("clear", 2));
    }

    [Fact]
    public void Currencies_are_kept_apart()
    {
        var w = new Wallet(D);
        w.Add("imbue", 3, 2);
        Assert.Equal(0, w.CountUsable("imbue_type", 3));
        Assert.Empty(w.Levels("imbue_type"));
        Assert.False(w.TrySpend("imbue_type", 3, 1));
    }

    [Fact]
    public void Bad_input_throws()
    {
        var w = new Wallet(D);
        Assert.Throws<KeyNotFoundException>(() => w.Add("gold", 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Add("imbue", 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Add("imbue", 16, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Add("imbue", 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.TrySpend("imbue", 2, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.CountUsable("imbue", 16));
    }
}
