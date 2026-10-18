# Unity-проект: сборка, CI, установка на телефон

Здесь всё, что нужно владельцу, чтобы получить устанавливаемую сборку Ballverse на реальном телефоне: какие секреты добавить в GitHub, как получить лицензию Unity Personal, как поставить APK на Android и как запустить iOS-сборку на iPhone.

Чистое C#-ядро собирается и тестируется без Unity — см. [`build.md`](build.md).

## 1. Что зафиксировано

| Что | Значение | Почему |
|---|---|---|
| Редактор | **Unity 6000.3.26f1** (Unity 6.3 LTS) — `client/ProjectSettings/ProjectVersion.txt` | Последний патч 6.3 LTS на 2026-10-18, для которого есть образы GameCI `unityci/editor:ubuntu-6000.3.26f1-android-3` и `…-ios-3` (опубликованы 2026-10-08). 6.4–6.6 — не LTS (Update-релизы); 6.3 LTS поддерживается до декабря 2027 |
| URP | `com.unity.render-pipelines.universal` 17.3.0 | Версия, привязанная к 6000.3 |
| uGUI + TextMeshPro | `com.unity.ugui` 2.0.0 | В Unity 6 TextMeshPro входит в uGUI |
| Тесты | `com.unity.test-framework` 1.4.6 | Последняя версия в реестре. В 6.3 это пакет ядра: если редактор держит свою версию, он сам перепишет строку в `manifest.json` при первом открытии — закоммитить |
| JSON | `com.unity.nuget.newtonsoft-json` 3.2.2 (Newtonsoft.Json 13.0.2) | То же, что у ядра в `core-tests` |
| Покупки | `com.unity.purchasing` 5.4.4 (2026-10-02) | Unity IAP 5: StoreKit 2, Google Play Billing Library 9.0.0 |
| Реклама | `com.google.ads.mobile` 11.6.0 (2026-10-12) из реестра **OpenUPM** (`https://package.openupm.com`, scope `com.google.ads.mobile`) | Google Mobile Ads Unity Plugin; зависит от `com.unity.external-dependency-manager` 2.1.0 (реестр Unity), минимальный Android API 24 |
| Встроенные модули | audio, video, imageconversion, physics2d, ui, uielements, imgui, jsonserialize, animation, androidjni, unitywebrequest | Остальные модули выключены (меньше сборка); модули-зависимости пакетов подключаются сами |
| GameCI | `game-ci/unity-builder@v5`, `game-ci/unity-test-runner@v4.3.2` | Классические версии экшенов. `unity-builder@v6` и `unity-test-runner@v4.4` — новая обёртка над `game-ci/cli` (август 2026), её пока не берём |
| Идентификатор приложения | `com.ballverse.app` (заглушка) | Владелец может сменить до заведения приложений в сторах: `ProjectSetup.ApplicationId` и `BUNDLE_ID` в `.github/workflows/unity-ios.yml` |

## 2. Устройство проекта

```
client/
  ProjectSettings/ProjectVersion.txt   единственный файл настроек в репозитории; остальное Unity создаст сама
  Packages/manifest.json               пакеты (см. таблицу выше)
  Assets/Ballverse/
    Core/        Ballverse.Core   — чистое ядро (без UnityEngine)
    App/         Ballverse.App    — Bootstrap и корень композиции
    Editor/      Ballverse.Editor — ProjectSetup, BuildScript (только редактор)
    Tests/EditMode/ Ballverse.Tests — EditMode-тесты
  Assets/Scenes/Boot.unity             создаёт ProjectSetup
```

**Ballverse.App.** Сцена `Boot` содержит один объект `Bootstrap`. Он создаёт платформенные сервисы и `AppHost`, который запускает модули из `AppModuleCatalog`:

- `IAppModule` — точка подключения сборок UI, Services, Media, Render, Studio, Sim: `Register` (опубликовать сервисы) → `Start` (фаза `Immediate` — до первого кадра; `AfterFirstFrame` — после него, для Remote Config, IAP, согласия и рекламы, §7.9) → `Shutdown`. Модуль с исключением логируется и пропускается, остальные работают.
- Платформенные сервисы (регистрирует первый модуль `PlatformModule`): `IAppLifecycle` (пауза/возврат/фокус/нехватка памяти/выход), `IFrameRatePolicy` (60 FPS, `vSyncCount = 0`), `IScreenAwake` (не гасить экран, пока держится хотя бы один токен), `ISafeAreaProvider` (отступы безопасной зоны, событие при изменении), `IAppPaths` (постоянные данные в `persistentDataPath`, кэш в `temporaryCachePath`).
- Как подключить новую сборку — в комментарии к `AppModuleCatalog`.

**Ballverse.Editor.**

- `ProjectSetup` (меню **Ballverse → Project Setup**) идемпотентно: создаёт URP 2D Renderer и URP Asset (`Assets/Ballverse/App/Settings/`) и назначает их в Graphics и во все уровни Quality; создаёт `Assets/Scenes/Boot.unity` с `Bootstrap` и ставит её первой в Build Settings; задаёт компанию и продукт «Ballverse», `com.ballverse.app`, только портрет, линейный цвет, Android (IL2CPP, ARM64, minSdk 26) и iOS (IL2CPP, iOS 16); если в настройках Google Mobile Ads пусто, подставляет **тестовые** app id Google (без них плагин валит сборку; реальные id вписываются в Assets → Google Mobile Ads → Settings и уже не перезаписываются).
- `BuildScript` — методы для `-executeMethod` и меню **Ballverse → Build**: `BuildAndroidApk` (development APK; debug-ключ или ключ загрузки, если переданы аргументы keystore), `BuildAndroidAab` (release AAB), `BuildIosXcode` (Xcode-проект). Перед сборкой всегда вызывается `ProjectSetup`, поэтому CI собирает и «голый» checkout.
- Страницы 16 КБ: Unity 6.3 собирает нативные библиотеки IL2CPP с выравниванием 16 КБ по умолчанию; Google Mobile Ads 11.6 и IAP 5.4 тоже совместимы.

### Первое открытие в редакторе (когда будет Mac/ПК с Unity)

1. Unity Hub → Installs → Install Editor → 6000.3.26f1 с модулями **Android Build Support** (вместе с OpenJDK и Android SDK & NDK) и **iOS Build Support**.
2. Открыть папку `client/`. Unity скачает пакеты (нужен интернет, в том числе до `package.openupm.com`) и создаст `ProjectSettings/*`, `Packages/packages-lock.json`, `.meta`-файлы.
3. **Ballverse → Project Setup**.
4. Закоммитить всё созданное: `.meta`, `Assets/Scenes/`, `Assets/Ballverse/App/Settings/`, `Assets/GoogleMobileAds/`, `ProjectSettings/`, `Packages/packages-lock.json`. После первой Android-сборки плагин рекламы добавит `Assets/Plugins/Android/` (шаблоны Gradle) — их тоже.

Пока этого не сделано, CI работает и так: каждая сборка запускает `ProjectSetup` в чистом контейнере.

## 3. Секреты GitHub

GitHub → репозиторий → **Settings → Secrets and variables → Actions → New repository secret**.

### Обязательные (без них Unity-джобы пропускаются с предупреждением)

| Секрет | Что положить |
|---|---|
| `UNITY_LICENSE` | **Всё содержимое** файла `Unity_lic.ulf` (текст XML целиком, от `<?xml` до конца) |
| `UNITY_EMAIL` | Email аккаунта Unity, на который активирована лицензия |
| `UNITY_PASSWORD` | Пароль этого аккаунта. Если входите в Unity через Google/Apple, сначала задайте пароль в настройках Unity ID (id.unity.com) |

### Как получить `Unity_lic.ulf` (Unity Personal, бесплатно)

Ручная активация через сайт для Personal больше не работает — файл берётся из Unity Hub:

1. Зарегистрироваться на unity.com (Unity ID), установить Unity Hub (Windows, macOS или Linux).
2. В Unity Hub войти этим аккаунтом → **Preferences (шестерёнка) → Licenses → Add → Get a free personal license**. Редактор ставить не обязательно.
3. Найти файл лицензии:

| ОС | Путь |
|---|---|
| Windows | `C:\ProgramData\Unity\Unity_lic.ulf` (папка `ProgramData` скрытая — вставьте путь в адресную строку Проводника) |
| macOS | `/Library/Application Support/Unity/Unity_lic.ulf` (именно системная `/Library`, не `~/Library`; Finder → Go → Go to Folder…) |
| Linux | `~/.local/share/unity3d/Unity/Unity_lic.ulf` |

4. Открыть файл текстовым редактором, скопировать всё и вставить в секрет `UNITY_LICENSE`.

Если лицензию позже вернуть или переактивировать в Hub, файл изменится — обновите секрет. Personal бесплатна при выручке и финансировании до $200 000 за 12 месяцев (§6.6).

### Необязательные: ключ подписи Android (нужен для Google Play)

Без них APK подписывается debug-ключом автоматически. С ними ключом загрузки подписываются и AAB, и APK (тогда новые APK ставятся поверх старых).

| Секрет | Что положить |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | Keystore загрузки в base64: `base64 -w0 upload.keystore` (Linux), `base64 -i upload.keystore` (macOS), `[Convert]::ToBase64String([IO.File]::ReadAllBytes("upload.keystore"))` (PowerShell) |
| `ANDROID_KEYSTORE_PASS` | Пароль keystore |
| `ANDROID_KEYALIAS_NAME` | Alias ключа |
| `ANDROID_KEYALIAS_PASS` | Пароль ключа |

Keystore создаётся один раз и хранится вне репозитория: `keytool -genkeypair -v -keystore upload.keystore -alias upload -keyalg RSA -keysize 2048 -validity 10000`. Потеря ключа загрузки решается через поддержку Play (Play App Signing), но лучше не терять.

### Необязательные: подпись iOS в CI (нужен платный Apple Developer Program, $99/год)

| Секрет | Что положить |
|---|---|
| `APPLE_TEAM_ID` | Team ID (10 символов): developer.apple.com → Account → Membership details. Один этот секрет включает автоматическую подпись в Xcode-проекте с вашей командой |
| `IOS_P12_BASE64` | Сертификат **Apple Development** (или Apple Distribution) с закрытым ключом, экспортированный из «Связки ключей» в `.p12`, в base64 (`base64 -i cert.p12`) |
| `IOS_P12_PASSWORD` | Пароль, заданный при экспорте `.p12` |
| `IOS_PROVISIONING_PROFILE_BASE64` | Профиль `.mobileprovision` для `com.ballverse.app` в base64. Development — для установки на зарегистрированные iPhone; Ad Hoc — тоже, но с Distribution-сертификатом; App Store — для TestFlight |

Все четыре вместе включают подписанный архив и IPA в macOS-джобе. Профиль создаётся на developer.apple.com → Certificates, Identifiers & Profiles: зарегистрировать App ID `com.ballverse.app`, добавить UDID своих iPhone в Devices, создать профиль iOS App Development с этим сертификатом и устройствами.

## 4. Как запускать CI

| Workflow | Что делает | Когда |
|---|---|---|
| `Unity tests` (`unity-tests.yml`) | EditMode-тесты в Unity: ядро внутри Unity, корень композиции, разбор аргументов сборки, ProjectSetup | push в `claude/**`, затрагивающий `client/**`; вручную |
| `Unity Android` (`unity-android.yml`) | Development APK → артефакт `ballverse-android-apk-0.1.N`; вручную с галкой **bundle** — ещё release AAB | то же |
| `Unity iOS` (`unity-ios.yml`) | Xcode-проект → артефакт `ballverse-ios-xcode-0.1.N` (`.tar.gz`); вручную с галкой **archive** — macOS-джоб: `pod install` и архив (подписанный IPA при наличии секретов подписи, иначе неподписанный архив для проверки компиляции) | то же |

Ручной запуск: вкладка **Actions** → выбрать workflow → **Run workflow** → ветка → (галки) → Run. Артефакты — внизу страницы запуска, раздел **Artifacts**, хранятся 14 дней.

Версия сборки — `0.1.<номер запуска>`, Android versionCode — `1000 + номер запуска` (считает GameCI). Target API Android — 36 (требование Google Play с 31.08.2026); GameCI ставит платформу SDK в контейнер перед сборкой.

Первая сборка идёт долго (скачивание образа ~10 ГБ, импорт пакетов, компиляция IL2CPP): Android ≈ 30–60 мин, iOS-проект ≈ 20–40 мин. Дальше помогает кэш папки `Library`.

## 5. Установка APK на Android

1. Открыть запуск `Unity Android` → **Artifacts** → скачать `ballverse-android-apk-…` (zip) → распаковать, внутри `Ballverse.apk`.
2. **Без компьютера:** передать APK на телефон (Google Drive, Telegram «Избранное», кабель) → открыть файл → разрешить «Установка неизвестных приложений» для этого приложения (Настройки → Приложения → Особый доступ) → Установить. Play Защита может предупредить о неизвестном разработчике — «Всё равно установить».
3. **Через USB (adb):** на телефоне включить режим разработчика (Настройки → О телефоне → 7 раз нажать «Номер сборки») и «Отладка по USB»; на компьютере установить Android Platform Tools и выполнить `adb install -r Ballverse.apk`. Логи: `adb logcat -s Unity`.
4. Требования: Android 8.0+ (API 26), 64-битный ARM.
5. Обновление поверх старой версии работает, только если обе подписаны одним ключом. Без секретов `ANDROID_KEYSTORE_*` каждый запуск CI подписывает APK новым debug-ключом — перед установкой новой сборки удалите старую (или добавьте секреты keystore, тогда все APK подписываются ключом загрузки).

Сборка development: в углу виден значок «Development Build» — это нормально для тестовой сборки.

## 6. iOS: запуск на iPhone с Mac

Нужны Mac с последним Xcode (App Store; Xcode 16+), кабель USB и iPhone с iOS 16+.

1. Скачать артефакт `ballverse-ios-xcode-…` → распаковать zip, затем `ballverse-ios-xcode.tar.gz` (двойной щелчок или `tar -xzf ballverse-ios-xcode.tar.gz`) — получится папка `Ballverse`. Архив tar нужен, чтобы сохранились права на исполнение скриптов сборки.
2. Установить CocoaPods (один раз): `brew install cocoapods` (или `sudo gem install cocoapods`).
3. В Терминале: `cd Ballverse && pod install` — подтянет Google Mobile Ads SDK и UMP.
4. Открыть **`Unity-iPhone.xcworkspace`** (если `Podfile` не было — `Unity-iPhone.xcodeproj`).
5. Xcode → Settings → Accounts → «+» → войти своим Apple ID.
6. Слева проект `Unity-iPhone` → таргет **Unity-iPhone** → **Signing & Capabilities** → включить *Automatically manage signing* → **Team**: ваша команда.
   - **Бесплатный Apple ID** (Personal Team): идентификатор `com.ballverse.app` может оказаться занят — поменяйте Bundle Identifier на уникальный, например `com.<ваше-имя>.ballverse`. Ограничения: приложение работает 7 дней (потом снова Run из Xcode), не больше 3 приложений на устройстве, без TestFlight.
   - **Платный аккаунт** ($99/год): подпись на год, TestFlight, можно оставить `com.ballverse.app` (если он зарегистрирован на вас).
7. Подключить iPhone кабелем, разблокировать, нажать «Доверять этому компьютеру». На iOS 16+ включить **Настройки → Конфиденциальность и безопасность → Режим разработчика** (iPhone перезагрузится).
8. Вверху Xcode выбрать свой iPhone как устройство → **Run** (▶). Первая сборка — несколько минут.
9. При первом запуске с бесплатным аккаунтом iPhone скажет «Ненадёжный разработчик»: **Настройки → Основные → VPN и управление устройством** → ваш Apple ID → Доверять.

Подписанный IPA из CI (при секретах подписи и Development/Ad Hoc-профиле) ставится на зарегистрированные в профиле iPhone: Finder → выбрать iPhone → перетащить `.ipa`, или Apple Configurator.

## 7. Известные ограничения

- Unity в этом контейнере недоступна: Unity-код проверен статически (API сверены с исходниками UnityCsReference 6000.3.26f1 и пакетов URP 17.3.0 и Google Mobile Ads 11.6.0; App/Editor/Tests компилируются против заглушек с теми же сигнатурами; логика App и разбор аргументов прогнаны тестами) — первая настоящая компиляция произойдёт в GitHub Actions после добавления секретов.
- Рекламные id в сборках — тестовые id Google, пока владелец не заведёт приложение в AdMob.
- В сцене `Boot` пока нет камеры и интерфейса — на телефоне будет чёрный экран, пока не подключены сборки UI/Render. Это ожидаемо для каркаса.
