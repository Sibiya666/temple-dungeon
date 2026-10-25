# Dialoid

Диаблоид (ARPG в духе Diablo/PoE2/Last Epoch). Сейчас в репозитории только `Dialoid.Core` — чистая C#-библиотека правил игры без Godot.

## Документы

- Спецификация: [docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md](docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md)
- План 1 (движок правил): [docs/superpowers/plans/2026-10-25-dialoid-core-engine.md](docs/superpowers/plans/2026-10-25-dialoid-core-engine.md)

## Тесты

```bash
dotnet test core/Dialoid.sln
```

Требуется .NET SDK 8 или новее (цель сборки — `net8.0`, на новых рантаймах включён `RollForward=Major`).

## Статус

| План | Состояние |
|------|-----------|
| План 1: движок правил Dialoid.Core | в работе |
| План 2 | впереди |
| План 3 | впереди |
