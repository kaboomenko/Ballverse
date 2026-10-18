# client — Unity-проект Ballverse

Unity 6 LTS (не ниже 6.3 LTS, §6.9 ТЗ). Проект ещё не инициализирован редактором: `ProjectSettings/` и `Packages/` появятся при первом открытии папки `client/` в Unity и будут закоммичены вместе с `.meta`-файлами.

Сейчас здесь только чистое C#-ядро:

- `Assets/Ballverse/Core/` — сборка `Ballverse.Core` (`noEngineReferences: true`). Никаких `UnityEngine`, никакого `System.Random`, никакого системного времени: всё, что зависит от времени и идентификаторов, передаётся параметрами. Зависимость — только `Newtonsoft.Json` (пакет `com.unity.nuget.newtonsoft-json`).

Ядро собирается и тестируется без Unity — см. [`../docs/dev/build.md`](../docs/dev/build.md).
