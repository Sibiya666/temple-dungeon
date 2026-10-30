namespace Dialoid.Core.Tower;

/// <summary>Why a tower action was refused. <see cref="None"/> = done.</summary>
public enum TowerRefusal
{
    None, UnknownCurrency, CurrencyLocked, WrongTarget, KindNotAllowed, SlotOutOfRange, UnknownType, EmptySlot, SlotFull, NoMods,
    PoolEmpty, NotATrial, NothingToUpgrade, ModNotFound, NotEnoughCurrency, RankLocked, ModAboveRank, NoUberBoss,
}

/// <summary>Player-facing texts of the tower module.</summary>
public static class TowerTexts
{
    public static string Refusal(TowerRefusal r) => r switch
    {
        TowerRefusal.None => "Готово",
        TowerRefusal.UnknownCurrency => "Неизвестная валюта",
        TowerRefusal.CurrencyLocked => "Эта валюта еще не действует на этом ранге",
        TowerRefusal.WrongTarget => "Эту валюту нельзя применить к этой цели",
        TowerRefusal.KindNotAllowed => "Свойства этого вида на этом ранге недоступны",
        TowerRefusal.SlotOutOfRange => "Такого этажа в башне нет",
        TowerRefusal.UnknownType => "Неизвестный тип этажа",
        TowerRefusal.EmptySlot => "Сначала выберите тип этажа",
        TowerRefusal.SlotFull => "Все места под свойства заняты",
        TowerRefusal.NoMods => "У цели нет свойств",
        TowerRefusal.PoolEmpty => "Подходящих свойств не осталось",
        TowerRefusal.NotATrial => "Разновидность бывает только у Испытания",
        TowerRefusal.NothingToUpgrade => "Все свойства уже на наибольшем тире для этого ранга",
        TowerRefusal.ModNotFound => "У цели нет такого свойства",
        TowerRefusal.NotEnoughCurrency => "Не хватает валюты",
        TowerRefusal.RankLocked => "Этот ранг еще закрыт",
        TowerRefusal.ModAboveRank => "В башне есть свойство, недоступное на этом ранге",
        TowerRefusal.NoUberBoss => "Не выбран убер-босс вершины",
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, "Неизвестная причина отказа"),
    };
}
