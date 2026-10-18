# 09. Аналитика, Remote Config, KPI

## 9.1. Инструменты

| Задача | Инструмент |
|---|---|
| Продуктовая аналитика, воронки, удержание | Firebase Analytics (экспорт в BigQuery включается с первого дня — бесплатный лимит покрывает старт) |
| Сбои и нефатальные ошибки | Firebase Crashlytics |
| Удалённая конфигурация, аварийные выключатели | Firebase Remote Config |
| A/B-тесты | Firebase A/B Testing (поверх Remote Config) |
| Доход от рекламы | AdMob + событие `ad_impression` с данными о доходе (impression-level revenue) в Firebase |
| Доход от покупок | Автоматические события покупок Firebase + собственное `purchase_success` с product ID |

Сбор аналитики начинается после решения пользователя в UMP (где требуется); режим согласия Google Consent Mode передаётся в Firebase.

## 9.2. События

Имена — `snake_case`, параметры — не более 25 на событие (ограничение Firebase). Персональные данные (имена участников, тексты пользователя, пути к файлам) **никогда** не отправляются.

| Событие | Когда | Ключевые параметры |
|---|---|---|
| `app_first_open` (авто) / `session_start` (авто) | | |
| `mode_open` | Вход на экран режима | `mode_id`, `is_free`, `is_owned`, `source` (gallery / project / quest) |
| `sim_start` | Запуск симуляции | `mode_id`, `preset_id`, `is_generated`, `target_duration`, `ball_count`, `tier` |
| `sim_generate` | Нажатие 🎲 | `mode_id`, `style`, `has_forced_winner`, `candidates`, `ms`, `quality_ok` |
| `param_change` | Изменение параметра (агрегируется: не чаще 1 события на параметр за 10 с) | `mode_id`, `param_id`, `group` |
| `game_completed` | `GameCompleted` (§2.1) | `mode_id`, `duration_s`, `interactive`, `result` |
| `preview_trial` | Пробный запуск платного режима | `mode_id` |
| `clip_create` | «В видео» или «Сохранить клип» | `mode_id`, `from_history` (запуск из ленты предыдущих), `duration_s` |
| `studio_open` | Вход в студию | `has_user_video` |
| `video_import` | Импорт видео | `result` (ok / cancel / error_code), `duration_s`, `res`, `hdr`, `ms` |
| `layout_change` | Смена раскладки | `layout`, `swap` |
| `text_add` | Добавление текста | `template_id` |
| `export_start` | Начало экспорта | `res`, `fps`, `duration_s`, `layout`, `mode_id`, `watermark` |
| `export_success` | Готово | те же + `ms`, `tier`, `segments`, `throttled` |
| `export_fail` / `export_cancel` | Ошибка / отмена | `error_code`, `stage`, `progress` |
| `share_tap` | Кнопка «Сохранить» / «Поделиться» / площадка | `target` |
| `ad_rewarded_offer` / `_show` / `_reward` / `_fail` / `_decline` | Rewarded-шлюз | `placement=export`, `error_code`, `wait_ms` |
| `ad_interstitial_show` / `_skip` | Interstitial | `trigger` (retry / generate / back), `reason` для пропуска |
| `paywall_view` | Показ витрины или листа режима | `source`, `variant` |
| `purchase_start` / `purchase_success` / `purchase_fail` / `purchase_pending` | Покупка | `product_id`, `source`, `error_code` |
| `restore_purchases` | Восстановление | `result`, `restored_count` |
| `quest_step` | Шаг квеста первого ролика | `step` |
| `setting_change` | Настройки доступности и качества | `setting`, `value` |

**Свойства пользователя:** `tier`, `owned_modes_count`, `has_no_export_ads`, `has_no_interstitials`, `exports_total` (корзины 0/1/2–5/6–20/21+), `ui_language`, `first_mode`.

## 9.3. Remote Config

Все значения имеют значения по умолчанию в сборке (приложение полностью работает без сети и без загрузки конфигурации).

| Ключ | По умолчанию | Назначение |
|---|---|---|
| `free_modes` | `["M01","M02","M03","M04","M05"]` | Состав бесплатных режимов (только расширение, §5.1) |
| `modes_disabled` | `[]` | Аварийное отключение режима |
| `gallery_order` | список | Порядок карточек галереи |
| `interstitial_every_n_games` | `3` | Р7 |
| `interstitial_min_interval_sec` | `60` | IN5 |
| `interstitial_grace_games` | `0` | IN6, вопрос В3 |
| `rewarded_export_enabled` | `true` | RW7 — аварийный выключатель шлюза |
| `rewarded_wait_timeout_sec` | `5` | RW3 |
| `rewarded_retry_window_min` | `10` | RW4 |
| `paid_mode_trial_enabled` | `true` | §5.2, вопрос В2 |
| `paid_mode_trial_seconds` | `30` | |
| `export_bitrates` | таблица §3.8 | Битрейты по разрешению и FPS |
| `export_default_fps_by_tier` | `{A:60,B:60,C:30}` | |
| `generation_weights` | по режимам | Веса `w1…w5` оценки зрелищности (§2.4) |
| `generation_budget_by_tier` | таблица §2.4 | |
| `tier_overrides` | `{}` | Принудительный тир для конкретных моделей устройств |
| `platform_safe_zones` | JSON масок | Зоны интерфейса TikTok/Reels/Shorts (§3.3) |
| `remote_presets` | `[]` | Новые пресеты режимов (JSON) |
| `remote_melodies` | `[]` | Новые мелодии (JSON нот) |
| `text_templates` | список | Шаблоны текста по языкам |
| `watermark_default_on` | `true` | §3.10, вопрос В1 |
| `rate_prompt_after_exports` | `2` | Запрос оценки после N-го успешного экспорта |

## 9.4. Ключевые воронки

1. **Активация:** `app_first_open` → `sim_start` → `sim_generate` → `clip_create` → `export_start` → `export_success` → `share_tap`. Цель: ≥ 30% новых пользователей экспортируют ролик в первый день.
2. **Студия:** `studio_open` → `video_import` → `export_start` → `export_success`. Где теряются авторы.
3. **Монетизация:** `paywall_view` (по источникам) → `purchase_start` → `purchase_success`.
4. **Реклама:** `ad_rewarded_offer` → `_show` → `_reward` → `export_success` против `_decline` → `purchase_start` (сколько людей шлюз превращает в покупателей, а сколько — в ушедших).

## 9.5. KPI запуска (гипотезы, уточняются на софт-лонче)

| Метрика | Цель |
|---|---|
| Удержание D1 / D7 / D30 | ≥ 35% / ≥ 12% / ≥ 5% |
| Доля новых пользователей с экспортом в D1 | ≥ 30% |
| Экспортов на активного автора в неделю | ≥ 3 |
| Конверсия в покупку (30 дней от установки) | ≥ 2,5% |
| Средний чек плательщика (30 дней) | ≥ $6 |
| Успешность экспорта | ≥ 99% |
| Пользователи без сбоев | ≥ 99,5% |
| Средняя оценка в сторах | ≥ 4,5 |
| Доля роликов с водяным знаком (если В1 принят) | ≥ 50% — индикатор органического охвата |

## 9.6. Первые A/B-тесты (после запуска)

| Тест | Варианты | Метрика |
|---|---|---|
| Льготный период interstitial | 0 / 3 игры / первая сессия | D1, D7, доход на пользователя (ARPU D7) |
| Пробный просмотр платных режимов | выкл / 30 с / 60 с | конверсия в покупку режима или набора |
| Порядок витрины | Ultimate первым / All Modes первым | средний чек, конверсия |
| Квест первого ролика | вкл / выкл | доля экспорта в D1 |
| Состав бесплатных режимов | M01–M05 / замена M04 на M07 | D7, конверсия |
