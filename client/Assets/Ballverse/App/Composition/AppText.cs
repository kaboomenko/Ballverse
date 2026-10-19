using System;
using System.Collections.Generic;
using System.Globalization;
using Ballverse.UI.Localization;

namespace Ballverse.App
{
    /// <summary>
    /// Texts the App raises whose keys the UI string tables (<c>UI/Resources/BallverseUI/Localization/{en,ru}.json</c>, owned
    /// by the UI track) do not have yet: the <c>app.*</c> toasts and the built-in preset names (<c>presets.m01.*</c>,
    /// <c>presets.m02.*</c>, <c>presets.m05.*</c>). <see cref="Of"/> returns the text as a literal in the interface language
    /// (spec §4.7: EN + RU) so a Russian user never sees the English humanized key; every other key stays a key for the UI
    /// tables. When the UI tables gain these keys, delete the entries here (the App test lists which keys need a fallback).
    /// </summary>
    public sealed class AppText
    {
        static readonly Dictionary<string, string[]> s_fallback = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // key → { en, ru }
            // Spec §2.4 step 5: nothing passed the quality threshold — the best variant runs with the advice to lengthen the video.
            { AppStrings.GenerateWeak, new[] { "Here is the best variant. Try a longer duration", "Показан лучший вариант. Попробуйте увеличить длительность" } },
            { AppStrings.GenerateFailed, new[] { "Generation failed. Try again", "Не удалось сгенерировать. Попробуйте ещё раз" } },
            { AppStrings.GenerateNone, new[] { "Couldn't find a working variant — try other settings", "Не удалось найти рабочий вариант — попробуйте другие настройки" } },
            { AppStrings.GenerateNearestWinner, new[] { "The chosen winner wasn't found — here is the closest variant", "Нужный победитель не найден — показан ближайший вариант" } },
            { AppStrings.GenerateStyleLastSecond, new[] { "Last-second win", "Победа в последнюю секунду" } },
            { AppStrings.GenerateGoalMissed, new[] { "The director goal wasn't reached — here is the best variant", "Цель режиссуры не достигнута — показан лучший вариант" } },
            { AppStrings.WinnerNotFoundTitle, new[] { "The chosen winner wasn't found", "Нужный победитель не найден" } },
            { AppStrings.WinnerNotFoundBody, new[] { "Search longer, or take the closest variant with another winner?", "Искать дольше или взять ближайший вариант с другим победителем?" } },
            { AppStrings.SearchLonger, new[] { "Search longer", "Искать дольше" } },
            { AppStrings.TakeNearest, new[] { "Take the closest variant", "Взять ближайший вариант" } },
            { AppStrings.HintRingEscapeDuration, new[] { "The escape may not make it in 180 s — the video will end by the ring count", "Побег может не успеть за 180 с — ролик закончится по счёту колец" } },
            { AppStrings.HintWiderGaps, new[] { "Wider gaps", "Шире прорези" } },
            { AppStrings.HintFewerRings, new[] { "Fewer rings", "Меньше колец" } },
            { AppStrings.SimStartFailed, new[] { "Couldn't start the simulation", "Не удалось запустить симуляцию" } },
            { AppStrings.ModeUnavailable, new[] { "This mode is temporarily unavailable", "Режим временно недоступен" } },
            { AppStrings.StudioUnavailable, new[] { "The studio is unavailable: not enough storage", "Студия недоступна: не хватает памяти" } },
            { AppStrings.RunTooShort, new[] { "The run is too short for a video", "Запуск слишком короткий для видео" } },
            { AppStrings.RecordingFailed, new[] { "This run can't be saved for a video", "Этот запуск не сохранится для видео" } },
            { AppStrings.OpenProjectFailed, new[] { "Couldn't open the project", "Не удалось открыть проект" } },
            { AppStrings.ImportFailed, new[] { "Couldn't import the video", "Не удалось импортировать видео" } },
            { AppStrings.ImportUnsupported, new[] { "This video format isn't supported", "Этот формат видео не поддерживается" } },
            { AppStrings.ImportNoSpace, new[] { "Not enough storage to import the video", "Недостаточно памяти для импорта видео" } },
            { AppStrings.ImportLargeFile, new[] { "Large video: editing may be slower", "Большое видео: редактирование может быть медленнее" } },
            { AppStrings.ImportHdr, new[] { "HDR video is converted to SDR", "HDR-видео будет преобразовано в SDR" } },
            { AppStrings.ImportTrimmed, new[] { "The video was trimmed to 3 minutes", "Видео обрезано до 3 минут" } },
            { AppStrings.PreviewFailed, new[] { "The preview couldn't be shown", "Не удалось показать превью" } },
            { AppStrings.SaveFailed, new[] { "Couldn't save the project", "Не удалось сохранить проект" } },
            { AppStrings.GallerySaveFailed, new[] { "Couldn't save to the gallery", "Не удалось сохранить в галерею" } },
            { AppStrings.GalleryPermission, new[] { "Allow access to Photos to save videos", "Разрешите доступ к галерее, чтобы сохранять видео" } },
            { AppStrings.ShareFailed, new[] { "Couldn't share the video", "Не удалось поделиться видео" } },
            { AppStrings.CaptionCopied, new[] { "Caption copied", "Подпись скопирована" } },
            { AppStrings.ResumeFailed, new[] { "Couldn't continue the export", "Не удалось продолжить экспорт" } },
            { AppStrings.PurchaseFailed, new[] { "The purchase didn't go through", "Покупка не прошла" } },
            { AppStrings.RestoreFailed, new[] { "Couldn't restore purchases", "Не удалось восстановить покупки" } },
            { AppStrings.PrivacyFailed, new[] { "Privacy settings are unavailable right now", "Настройки конфиденциальности сейчас недоступны" } },
            { AppStrings.ProjectActionFailed, new[] { "Something went wrong. Try again", "Что-то пошло не так. Попробуйте ещё раз" } },
            { AppStrings.GenericError, new[] { "Something went wrong", "Что-то пошло не так" } },
            // Built-in preset names (spec §2.9 cards).
            { "presets.m01.classic", new[] { "Classic", "Классика" } },
            { "presets.m01.neon_chaos", new[] { "Neon chaos", "Неон-хаос" } },
            { "presets.m01.meditation", new[] { "Meditation", "Медитация" } },
            { "presets.m01.hundred_rings", new[] { "100 rings", "100 колец" } },
            { "presets.m01.impossible_escape", new[] { "Impossible escape", "Невозможный побег" } },
            { "presets.m01.rainbow", new[] { "Rainbow", "Радуга" } },
            { "presets.m01.gap_spiral", new[] { "Gap spiral", "Спираль прорезей" } },
            { "presets.m01.color_race", new[] { "Color race", "Гонка цветов" } },
            { "presets.m02.hexagon", new[] { "Hexagon", "Шестиугольник" } },
            { "presets.m02.triangle_chaos", new[] { "Triangle chaos", "Треугольник-хаос" } },
            { "presets.m02.growing_ball", new[] { "Growing ball", "Растущий шар" } },
            { "presets.m02.multiply", new[] { "Multiply", "Размножение" } },
            { "presets.m02.escape_side", new[] { "Escape through a side", "Побег через грань" } },
            { "presets.m02.evolution", new[] { "Evolution", "Эволюция" } },
            { "presets.m02.zero_gravity", new[] { "Zero gravity", "Невесомость" } },
            { "presets.m05.classic_tower", new[] { "Classic tower", "Классическая башня" } },
            { "presets.m05.cascade", new[] { "Cascade", "Каскад" } },
            { "presets.m05.meditation", new[] { "Meditation", "Медитация" } },
            { "presets.m05.insane", new[] { "Insane", "Безумная" } },
            { "presets.m05.game", new[] { "Game", "Игра" } },
            { "presets.m05.hundred_floors", new[] { "100 floors", "100 этажей" } },
        };

        readonly Func<string> _language;

        /// <param name="language">The interface language ("en" / "ru"), read at each call.</param>
        public AppText(Func<string> language)
        {
            _language = language ?? (() => LanguageResolver.English);
        }

        /// <summary>Keys this class supplies a text for (tests check each has both languages and is not yet in the UI tables).</summary>
        public static IEnumerable<string> FallbackKeys => s_fallback.Keys;

        /// <summary>The text of <paramref name="key"/> in <paramref name="language"/> when this class has one.</summary>
        public static bool TryGetFallback(string key, string language, out string text)
        {
            text = null;
            if (key == null || !s_fallback.TryGetValue(key, out string[] texts)) return false;
            text = string.Equals(language, LanguageResolver.Russian, StringComparison.Ordinal) ? texts[1] : texts[0];
            return true;
        }

        /// <summary>A literal in the interface language for the keys above, else the key itself for the UI tables.</summary>
        public LocalizedText Of(string key, params object[] args)
        {
            string language;
            try
            {
                language = _language();
            }
            catch (Exception)
            {
                language = LanguageResolver.English;
            }
            if (!TryGetFallback(key, language, out string text)) return LocalizedText.FromKey(key, args);
            if (args != null && args.Length > 0)
            {
                try
                {
                    text = string.Format(CultureInfo.InvariantCulture, text, args);
                }
                catch (FormatException)
                {
                    // keep the unformatted text
                }
            }
            return LocalizedText.FromLiteral(text);
        }
    }
}
