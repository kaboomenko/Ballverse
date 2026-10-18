# Сборка и тесты

## Ядро (`Ballverse.Core`) без Unity

Исходники ядра лежат в Unity-проекте (`client/Assets/Ballverse/Core/`), а собираются и тестируются обычным .NET:

```bash
dotnet test core-tests/Ballverse.Core.sln
```

- `core-tests/Ballverse.Core/` — проект-обёртка: компилирует исходники ядра с ограничениями Unity (netstandard2.1, C# 9, Newtonsoft.Json 13.0.2).
- `core-tests/Ballverse.Core.Tests/` — тесты (xUnit, net8.0). Папки тестов повторяют папки модулей ядра.
- Предупреждения — ошибки. Запрещённые API (`System.Random`, `DateTime.Now/UtcNow`, `Guid.NewGuid`, `Stopwatch`, `Environment.TickCount`) ловит анализатор `BannedApiAnalyzers` по списку `core-tests/BannedSymbols.txt` (правило детерминизма §7.4).
- Нужен .NET SDK 8. В облачном контейнере разработки он ставится из архива Ubuntu: `apt-get install dotnet-sdk-8.0` (прямые загрузки Microsoft закрыты сетевой политикой).

## Ограничения языка ядра (совместимость с Unity)

- C# 9: без file-scoped namespace, global using, `record struct`, `required`, raw strings.
- Без `record` и `init` (в netstandard2.1 нет `IsExternalInit`).
- Без `System.Text.Json` — только Newtonsoft.Json через `Ballverse.Core.CoreJson`.
- Новый файл в `Assets/` при первом открытии в Unity получит `.meta` — закоммитить его.

## CI

`.github/workflows/core-tests.yml` — сборка и тесты ядра на каждый push/PR, затрагивающий ядро.

## Проверка чисел ТЗ

`python3 docs/dev/checks/spec_geometry_check.py` — статическая проверка формул и чисел из карточек режимов релиза 1 (§2.9: проходимость прорезей M01, упаковка M02, высота отскока и просвет M05), диапазонов квантования потока рендера (§7.4) и сроков roadmap (§11.4). Запускается после правки этих разделов; код от скрипта не зависит, те же правила реализуются и тестируются в ядре (§8.5).
