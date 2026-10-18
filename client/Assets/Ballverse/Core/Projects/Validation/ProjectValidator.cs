using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Validates a project document against the spec ranges (spec §3.3-§3.6, §3.8, §3.9, §7.4) and returns a repaired copy.
    /// Out-of-range numbers are clamped, invalid values replaced by defaults, unsafe paths reset to safe defaults
    /// (or nulled where null is allowed), and each change is reported as a <see cref="ValidationIssue"/>.
    /// Only problems that need outside context (missing clip, bad mode id, bad project id without an expected id,
    /// wrong schema version) are <see cref="IssueSeverity.Error"/>.
    /// </summary>
    public static class ProjectValidator
    {
        static readonly string[] s_isoFormats =
        {
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
            "yyyy-MM-dd'T'HH:mm:sszzz",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        };

        /// <summary>
        /// Returns a sanitized deep copy of <paramref name="document"/> and the issues found. The input is not modified.
        /// </summary>
        /// <param name="document">Document to check (already migrated to <see cref="ProjectMigrator.CurrentSchemaVersion"/>).</param>
        /// <param name="expectedId">
        /// Folder id the document was loaded from. When given, a missing, unsafe or different <c>id</c> is replaced by it
        /// (warning); when null, an unsafe id is an error.
        /// </param>
        public static SanitizeResult Sanitize(ProjectDocument document, string expectedId = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var copy = ProjectJson.Clone(document);
            var s = new Sanitizer();
            s.Run(copy, expectedId);
            return new SanitizeResult(copy, s.Issues);
        }

        /// <summary>True for "#RRGGBB" or "#RRGGBBAA" (hex digits, any case) — spec §3.3, §3.5 colors.</summary>
        public static bool IsColor(string value)
        {
            if (value == null || (value.Length != 7 && value.Length != 9) || value[0] != '#') return false;
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>
        /// Parses an ISO-8601 timestamp with an explicit zone ("2026-10-18T12:00:00Z", optional fraction, or "+03:00").
        /// </summary>
        public static bool TryParseTimestamp(string value, out DateTimeOffset result)
        {
            result = default;
            if (string.IsNullOrEmpty(value)) return false;
            return DateTimeOffset.TryParseExact(value, s_isoFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result);
        }

        /// <summary>True when <paramref name="value"/> is accepted by <see cref="TryParseTimestamp"/>.</summary>
        public static bool IsTimestamp(string value)
        {
            return TryParseTimestamp(value, out _);
        }

        sealed class Sanitizer
        {
            public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();

            public void Run(ProjectDocument doc, string expectedId)
            {
                if (doc.SchemaVersion != ProjectMigrator.CurrentSchemaVersion)
                {
                    Error("schemaVersion", IssueCode.InvalidValue,
                        "Expected schema version " + ProjectMigrator.CurrentSchemaVersion + ", got " + doc.SchemaVersion + "; migrate first.");
                }

                SanitizeId(doc, expectedId);
                doc.Name = CleanName(doc.Name);
                SanitizeTimestamps(doc);

                if (doc.Clip == null)
                {
                    Error("clip", IssueCode.Missing, "Clip section is missing.");
                }
                else
                {
                    SanitizeClip(doc.Clip);
                }

                if (doc.UserVideo != null) SanitizeVideo(doc.UserVideo);

                if (doc.Layout == null)
                {
                    Warn("layout", IssueCode.Missing, "Layout missing; default used.");
                    doc.Layout = new LayoutSettings();
                }
                SanitizeLayout(doc.Layout);

                doc.Texts = SanitizeTexts(doc.Texts);

                if (doc.Audio == null)
                {
                    Warn("audio", IssueCode.Missing, "Audio missing; default used.");
                    doc.Audio = new AudioSettings();
                }
                SanitizeAudio(doc.Audio);

                if (doc.Output == null)
                {
                    Warn("output", IssueCode.Missing, "Output missing; default used.");
                    doc.Output = new OutputSettings();
                }
                SanitizeOutput(doc);

                doc.Exports = SanitizeExports(doc.Exports);
            }

            void SanitizeId(ProjectDocument doc, string expectedId)
            {
                bool expectedOk = expectedId != null && ProjectPaths.IsSafeId(expectedId);
                if (!ProjectPaths.IsSafeId(doc.Id))
                {
                    if (expectedOk)
                    {
                        Warn("id", IssueCode.InvalidId, "Unsafe id replaced by the folder id.");
                        doc.Id = expectedId;
                    }
                    else
                    {
                        Error("id", IssueCode.InvalidId, "Project id is missing or unsafe.");
                    }
                }
                else if (expectedOk && !string.Equals(doc.Id, expectedId, StringComparison.Ordinal))
                {
                    Warn("id", IssueCode.InvalidId, "Id differs from the folder id; folder id used.");
                    doc.Id = expectedId;
                }
            }

            string CleanName(string name)
            {
                string cleaned = CleanText(name, ProjectLimits.MaxNameLength, false);
                if (cleaned == null || cleaned.Trim().Length == 0)
                {
                    Warn("name", IssueCode.Missing, "Empty name replaced.");
                    return ProjectDefaults.UntitledName;
                }
                if (!string.Equals(cleaned, name, StringComparison.Ordinal))
                {
                    Warn("name", IssueCode.InvalidValue, "Control characters removed or name truncated.");
                }
                return cleaned;
            }

            void SanitizeTimestamps(ProjectDocument doc)
            {
                bool createdOk = IsTimestamp(doc.CreatedAt);
                bool updatedOk = IsTimestamp(doc.UpdatedAt);
                if (!createdOk)
                {
                    Warn("createdAt", IssueCode.InvalidTimestamp, "Invalid timestamp replaced.");
                    doc.CreatedAt = updatedOk ? doc.UpdatedAt : ProjectDefaults.EpochIso;
                }
                if (!updatedOk)
                {
                    Warn("updatedAt", IssueCode.InvalidTimestamp, "Invalid timestamp replaced.");
                    doc.UpdatedAt = doc.CreatedAt;
                }
            }

            void SanitizeClip(ClipSettings clip)
            {
                clip.Replay = SafePath(clip.Replay, "clip.replay", ProjectPaths.ClipReplayFile, false);
                if (!ProjectPaths.IsSafeId(clip.ModeId))
                {
                    Error("clip.modeId", IssueCode.InvalidId, "Mode id is missing or not a safe id.");
                }

                int maxTicks = SimConstants.MaxDurationTicks;
                clip.TrimInTick = ClampI(clip.TrimInTick, 0, maxTicks - 1, "clip.trimInTick");
                clip.TrimOutTick = ClampI(clip.TrimOutTick, 1, maxTicks, "clip.trimOutTick");
                long tin = clip.TrimInTick, tout = clip.TrimOutTick;
                if (FixRange(ref tin, ref tout, 1, maxTicks))
                {
                    Warn("clip.trimInTick", IssueCode.InvalidRange, "trimInTick must be before trimOutTick.");
                }
                clip.TrimInTick = (int)tin;
                clip.TrimOutTick = (int)tout;

                clip.Speed = ClampD(clip.Speed, ProjectLimits.MinSpeed, ProjectLimits.MaxSpeed, 1.0, "clip.speed");
                if (clip.FinaleSlowMo == null)
                {
                    Warn("clip.finaleSlowMo", IssueCode.Missing, "Finale slow-mo missing; off.");
                    clip.FinaleSlowMo = new FinaleSlowMo();
                }
                clip.FinaleSlowMo.Seconds = ClampD(clip.FinaleSlowMo.Seconds, ProjectLimits.MinSlowMoSeconds, ProjectLimits.MaxSlowMoSeconds, 0.0, "clip.finaleSlowMo.seconds");
                clip.FinaleSlowMo.Factor = ClampD(clip.FinaleSlowMo.Factor, ProjectLimits.MinSlowMoFactor, ProjectLimits.MaxSlowMoFactor, 0.5, "clip.finaleSlowMo.factor");
                clip.AfterEnd = EnumValue(clip.AfterEnd, SimAfterEnd.Freeze, "clip.afterEnd");
                clip.SoundPack = OptionalToken(clip.SoundPack, "clip.soundPack");
                clip.Melody = OptionalToken(clip.Melody, "clip.melody");
            }

            void SanitizeVideo(UserVideoSettings v)
            {
                v.File = SafePath(v.File, "userVideo.file", ProjectPaths.DefaultUserVideoFile, false);

                const long farMax = long.MaxValue / 4;
                v.TrimInUs = ClampL(v.TrimInUs, 0, farMax - 1, "userVideo.trimInUs");
                v.TrimOutUs = ClampL(v.TrimOutUs, 1, farMax, "userVideo.trimOutUs");
                long tin = v.TrimInUs, tout = v.TrimOutUs;
                if (FixRange(ref tin, ref tout, ProjectLimits.MinDurationUs, farMax))
                {
                    Warn("userVideo.trimInUs", IssueCode.InvalidRange, "trimInUs must be before trimOutUs.");
                }
                if (tout - tin > ProjectLimits.MaxDurationUs)
                {
                    Warn("userVideo.trimOutUs", IssueCode.DurationTooLong, "Video fragment longer than 180 s shortened.");
                    tout = tin + ProjectLimits.MaxDurationUs;
                }
                v.TrimInUs = tin;
                v.TrimOutUs = tout;
                v.OffsetUs = ClampL(v.OffsetUs, -ProjectLimits.MaxOffsetUs, ProjectLimits.MaxOffsetUs, "userVideo.offsetUs");

                int rot = ((v.Rotation % 360) + 360) % 360;
                if (rot % 90 != 0)
                {
                    Warn("userVideo.rotation", IssueCode.InvalidValue, "Rotation must be a multiple of 90; reset to 0.");
                    rot = 0;
                }
                else if (rot != v.Rotation)
                {
                    Warn("userVideo.rotation", IssueCode.OutOfRange, "Rotation normalized to 0..270.");
                }
                v.Rotation = rot;

                v.Fill = EnumValue(v.Fill, SlotFill.Cover, "userVideo.fill");
                v.Pan = Vector2(v.Pan, ProjectLimits.MinPan, ProjectLimits.MaxPan, 0.0, "userVideo.pan");
                v.Zoom = ClampD(v.Zoom, ProjectLimits.MinZoom, ProjectLimits.MaxZoom, 1.0, "userVideo.zoom");
                v.Volume = ClampD(v.Volume, ProjectLimits.MinVolume, ProjectLimits.MaxVolume, 1.0, "userVideo.volume");
                v.AfterEnd = EnumValue(v.AfterEnd, VideoAfterEnd.Freeze, "userVideo.afterEnd");
            }

            void SanitizeLayout(LayoutSettings l)
            {
                l.Type = EnumValue(l.Type, LayoutType.L1, "layout.type");
                l.Ratio = ClampD(l.Ratio, ProjectLimits.MinRatio, ProjectLimits.MaxRatio, 0.5, "layout.ratio");
                if (l.Divider == null)
                {
                    Warn("layout.divider", IssueCode.Missing, "Divider missing; default used.");
                    l.Divider = new DividerSettings();
                }
                l.Divider.Px = ClampI(l.Divider.Px, ProjectLimits.MinDividerPx, ProjectLimits.MaxDividerPx, "layout.divider.px");
                l.Divider.Color = Color(l.Divider.Color, ProjectDefaults.DividerColor, "layout.divider.color");
                l.CornerRadius = ClampI(l.CornerRadius, ProjectLimits.MinCornerRadius, ProjectLimits.MaxCornerRadius, "layout.cornerRadius");
                l.Padding = ClampI(l.Padding, ProjectLimits.MinPadding, ProjectLimits.MaxPadding, "layout.padding");

                if (l.Pip == null && (l.Type == LayoutType.L3 || l.Type == LayoutType.L5))
                {
                    Warn("layout.pip", IssueCode.Missing, "PiP layout without a pip window; default used.");
                    l.Pip = ProjectDefaults.Pip();
                }
                if (l.Pip != null)
                {
                    l.Pip.Shape = EnumValue(l.Pip.Shape, PipShape.RoundedRect, "layout.pip.shape");
                    l.Pip.SizeFraction = ClampD(l.Pip.SizeFraction, ProjectLimits.MinPipSize, ProjectLimits.MaxPipSize, 0.35, "layout.pip.sizeFraction");
                    l.Pip.X = ClampD(l.Pip.X, 0.0, 1.0, 0.5, "layout.pip.x");
                    l.Pip.Y = ClampD(l.Pip.Y, 0.0, 1.0, 0.5, "layout.pip.y");
                }
            }

            List<TextLayer> SanitizeTexts(List<TextLayer> texts)
            {
                var result = new List<TextLayer>();
                if (texts == null)
                {
                    Warn("texts", IssueCode.Missing, "Texts missing; empty list used.");
                    return result;
                }
                for (int i = 0; i < texts.Count; i++)
                {
                    if (texts[i] == null)
                    {
                        Warn("texts[" + i + "]", IssueCode.InvalidValue, "Null text layer removed.");
                        continue;
                    }
                    if (result.Count == ProjectLimits.MaxTextLayers)
                    {
                        Warn("texts[" + i + "]", IssueCode.TooMany, "More than " + ProjectLimits.MaxTextLayers + " text layers; extras removed.");
                        break;
                    }
                    result.Add(texts[i]);
                }

                // Ids: keep the first occurrence of each safe id, then give the rest the smallest free "t{n}".
                // The set is used for membership only, so its order never reaches the output.
                var used = new HashSet<string>(StringComparer.Ordinal);
                var needsId = new bool[result.Count];
                for (int i = 0; i < result.Count; i++)
                {
                    string id = result[i].Id;
                    if (!ProjectPaths.IsSafeId(id))
                    {
                        Warn("texts[" + i + "].id", IssueCode.InvalidId, "Missing or unsafe text id regenerated.");
                        needsId[i] = true;
                    }
                    else if (!used.Add(id))
                    {
                        Warn("texts[" + i + "].id", IssueCode.Duplicate, "Duplicate text id regenerated.");
                        needsId[i] = true;
                    }
                }
                int next = 1;
                for (int i = 0; i < result.Count; i++)
                {
                    if (!needsId[i]) continue;
                    while (used.Contains("t" + next.ToString(CultureInfo.InvariantCulture))) next++;
                    string id = "t" + next.ToString(CultureInfo.InvariantCulture);
                    used.Add(id);
                    result[i].Id = id;
                }

                for (int i = 0; i < result.Count; i++) SanitizeText(result[i], "texts[" + i + "]");
                return result;
            }

            void SanitizeText(TextLayer t, string p)
            {
                t.Template = OptionalToken(t.Template, p + ".template");
                string text = CleanText(t.Text ?? "", ProjectLimits.MaxTextLength, true);
                if (!string.Equals(text, t.Text, StringComparison.Ordinal))
                {
                    Warn(p + ".text", IssueCode.InvalidValue, "Text was null, had control characters or was too long.");
                }
                t.Text = text;
                if (!ProjectPaths.IsSafeId(t.Font))
                {
                    Warn(p + ".font", IssueCode.InvalidValue, "Invalid font id; default used.");
                    t.Font = ProjectDefaults.Font;
                }
                t.Size = ClampI(t.Size, ProjectLimits.MinTextSize, ProjectLimits.MaxTextSize, p + ".size");
                t.Fill = Color(t.Fill, ProjectDefaults.TextFill, p + ".fill");
                if (t.Stroke != null)
                {
                    t.Stroke.Px = ClampI(t.Stroke.Px, ProjectLimits.MinStrokePx, ProjectLimits.MaxStrokePx, p + ".stroke.px");
                    t.Stroke.Color = Color(t.Stroke.Color, ProjectDefaults.StrokeColor, p + ".stroke.color");
                }
                t.Plate = EnumValue(t.Plate, TextPlate.None, p + ".plate");
                t.Pos = Vector2(t.Pos, 0.0, 1.0, 0.5, p + ".pos");

                long max = ProjectLimits.MaxDurationUs;
                t.InUs = ClampL(t.InUs, 0, max - 1, p + ".inUs");
                t.OutUs = ClampL(t.OutUs, 1, max, p + ".outUs");
                long tin = t.InUs, tout = t.OutUs;
                if (FixRange(ref tin, ref tout, ProjectLimits.MinDurationUs, max))
                {
                    Warn(p + ".inUs", IssueCode.InvalidRange, "inUs must be before outUs.");
                }
                t.InUs = tin;
                t.OutUs = tout;
                t.Anim = OptionalToken(t.Anim, p + ".anim");
            }

            void SanitizeAudio(AudioSettings a)
            {
                a.Sim = ClampD(a.Sim, ProjectLimits.MinVolume, ProjectLimits.MaxVolume, 1.0, "audio.sim");
                a.User = ClampD(a.User, ProjectLimits.MinVolume, ProjectLimits.MaxVolume, 1.0, "audio.user");
                if (a.Music == null) return;
                if (!ProjectPaths.IsSafeId(a.Music.Track))
                {
                    Warn("audio.music.track", IssueCode.InvalidValue, "Invalid music track id; music removed.");
                    a.Music = null;
                    return;
                }
                a.Music.Volume = ClampD(a.Music.Volume, ProjectLimits.MinVolume, ProjectLimits.MaxVolume, 1.0, "audio.music.volume");
                a.Music.StartUs = ClampL(a.Music.StartUs, 0, long.MaxValue, "audio.music.startUs");
                a.Music.FadeInUs = ClampL(a.Music.FadeInUs, 0, ProjectLimits.MaxMusicFadeUs, "audio.music.fadeInUs");
                a.Music.FadeOutUs = ClampL(a.Music.FadeOutUs, 0, ProjectLimits.MaxMusicFadeUs, "audio.music.fadeOutUs");
            }

            void SanitizeOutput(ProjectDocument doc)
            {
                var o = doc.Output;
                if (!string.Equals(o.Aspect, ProjectLimits.Aspect916, StringComparison.Ordinal))
                {
                    Warn("output.aspect", IssueCode.InvalidValue, "Unsupported aspect; 9:16 used.");
                    o.Aspect = ProjectLimits.Aspect916;
                }
                o.DurationMode = EnumValue(o.DurationMode, DurationMode.Auto, "output.durationMode");
                if (o.DurationMode == DurationMode.MatchVideo && doc.UserVideo == null)
                {
                    Warn("output.durationMode", IssueCode.InvalidValue, "Duration '= video' without a user video; auto used.");
                    o.DurationMode = DurationMode.Auto;
                }
                if (o.DurationMode == DurationMode.Manual && o.ManualDurationUs == null)
                {
                    Warn("output.manualDurationUs", IssueCode.Missing, "Manual duration missing; clip duration used.");
                    o.ManualDurationUs = doc.Clip == null ? ProjectLimits.MinDurationUs : ProjectTiming.ClipOutputUs(doc.Clip);
                }
                if (o.ManualDurationUs != null)
                {
                    o.ManualDurationUs = ClampL(o.ManualDurationUs.Value, ProjectLimits.MinDurationUs, ProjectLimits.MaxDurationUs, "output.manualDurationUs");
                }
                if (o.Resolution != null && !Enum.IsDefined(typeof(ExportResolution), o.Resolution.Value))
                {
                    Warn("output.resolution", IssueCode.InvalidValue, "Resolution must be 720 or 1080; cleared.");
                    o.Resolution = null;
                }
                if (o.Fps != null && !IsFps(o.Fps.Value))
                {
                    Warn("output.fps", IssueCode.InvalidValue, "Fps must be 30 or 60; cleared.");
                    o.Fps = null;
                }

                // Total duration cap (spec §3.4). Manual is clamped above, '= V' by the fragment cap; Auto shortens the clip trim.
                if (o.DurationMode == DurationMode.Auto && doc.Clip != null && ProjectTiming.ClipOutputUs(doc.Clip) > ProjectLimits.MaxDurationUs)
                {
                    Warn("clip.trimOutTick", IssueCode.DurationTooLong, "Reel longer than 180 s; clip trim shortened.");
                    doc.Clip.TrimOutTick = MaxTrimOutWithinCap(doc.Clip);
                }
            }

            static int MaxTrimOutWithinCap(ClipSettings clip)
            {
                var slow = clip.FinaleSlowMo;
                int lo = clip.TrimInTick + 1, hi = clip.TrimOutTick;
                // Invariant: answer in [lo, hi]; lo always fits (a single tick is far below the cap).
                while (lo < hi)
                {
                    int mid = lo + (hi - lo + 1) / 2;
                    if (ProjectTiming.ClipOutputUs(clip.TrimInTick, mid, clip.Speed, slow.Seconds, slow.Factor) <= ProjectLimits.MaxDurationUs) lo = mid;
                    else hi = mid - 1;
                }
                return lo;
            }

            List<ExportRecord> SanitizeExports(List<ExportRecord> exports)
            {
                var result = new List<ExportRecord>();
                if (exports == null)
                {
                    Warn("exports", IssueCode.Missing, "Export history missing; empty list used.");
                    return result;
                }
                for (int i = 0; i < exports.Count; i++)
                {
                    var e = exports[i];
                    string p = "exports[" + i + "]";
                    if (e == null || !IsTimestamp(e.At) || !Enum.IsDefined(typeof(ExportResolution), e.Res) || !IsFps(e.Fps))
                    {
                        Warn(p, IssueCode.InvalidValue, "Invalid export record removed.");
                        continue;
                    }
                    e.Ms = ClampL(e.Ms, 0, long.MaxValue, p + ".ms");
                    e.File = SafePath(e.File, p + ".file", null, true);
                    result.Add(e);
                }
                if (result.Count > ProjectLimits.MaxExportRecords)
                {
                    Warn("exports", IssueCode.TooMany, "Export history trimmed to the newest " + ProjectLimits.MaxExportRecords + " records.");
                    result.RemoveRange(0, result.Count - ProjectLimits.MaxExportRecords);
                }
                return result;
            }

            // ---- helpers ----

            static bool IsFps(int fps)
            {
                return fps == 30 || fps == 60;
            }

            /// <summary>Repairs [lo, hi): swaps an inverted range, widens an empty one by minLen (capped at max). Returns true if changed.</summary>
            static bool FixRange(ref long lo, ref long hi, long minLen, long max)
            {
                if (lo < hi) return false;
                if (lo > hi)
                {
                    long t = lo;
                    lo = hi;
                    hi = t;
                }
                else
                {
                    hi = Math.Min(lo + minLen, max);
                }
                return true;
            }

            string SafePath(string value, string path, string fallback, bool nullable)
            {
                if (value == null)
                {
                    if (nullable) return null;
                    Warn(path, IssueCode.Missing, "Path missing; default used.");
                    return fallback;
                }
                if (ProjectPaths.IsSafeRelativePath(value)) return value;
                Warn(path, IssueCode.UnsafePath, "Path is not a safe project-relative path; " + (fallback == null ? "cleared." : "default used."));
                return fallback;
            }

            string OptionalToken(string value, string path)
            {
                if (value == null || ProjectPaths.IsSafeId(value)) return value;
                Warn(path, IssueCode.InvalidValue, "Invalid id; cleared.");
                return null;
            }

            string Color(string value, string fallback, string path)
            {
                if (IsColor(value)) return value;
                Warn(path, IssueCode.InvalidColor, "Color must be #RRGGBB or #RRGGBBAA; default used.");
                return fallback;
            }

            T EnumValue<T>(T value, T fallback, string path) where T : struct, Enum
            {
                if (Enum.IsDefined(typeof(T), value)) return value;
                Warn(path, IssueCode.InvalidValue, "Unknown value; default used.");
                return fallback;
            }

            double ClampD(double value, double min, double max, double fallback, string path)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    Warn(path, IssueCode.InvalidValue, "Not a finite number; default used.");
                    return fallback;
                }
                if (value < min || value > max)
                {
                    Warn(path, IssueCode.OutOfRange, "Clamped to " + min.ToString(CultureInfo.InvariantCulture) + ".." + max.ToString(CultureInfo.InvariantCulture) + ".");
                    return value < min ? min : max;
                }
                return value;
            }

            int ClampI(int value, int min, int max, string path)
            {
                if (value >= min && value <= max) return value;
                Warn(path, IssueCode.OutOfRange, "Clamped to " + min + ".." + max + ".");
                return value < min ? min : max;
            }

            long ClampL(long value, long min, long max, string path)
            {
                if (value >= min && value <= max) return value;
                Warn(path, IssueCode.OutOfRange, "Clamped to " + min + ".." + max + ".");
                return value < min ? min : max;
            }

            double[] Vector2(double[] value, double min, double max, double fallback, string path)
            {
                if (value == null || value.Length != 2)
                {
                    Warn(path, IssueCode.InvalidValue, "Expected two numbers; default used.");
                    return new[] { fallback, fallback };
                }
                return new[]
                {
                    ClampD(value[0], min, max, fallback, path + "[0]"),
                    ClampD(value[1], min, max, fallback, path + "[1]"),
                };
            }

            /// <summary>Removes C0 controls (keeping '\n' when allowed), DEL and unpaired surrogates; truncates without splitting pairs.</summary>
            static string CleanText(string value, int maxLength, bool allowNewline)
            {
                if (value == null) return null;
                var sb = new StringBuilder(Math.Min(value.Length, maxLength));
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (char.IsHighSurrogate(c))
                    {
                        if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                        {
                            if (sb.Length + 2 > maxLength) break;
                            sb.Append(c).Append(value[i + 1]);
                            i++;
                        }
                        continue;
                    }
                    if (char.IsLowSurrogate(c)) continue;
                    if ((c < 0x20 && !(allowNewline && c == '\n')) || c == 0x7F) continue;
                    if (sb.Length + 1 > maxLength) break;
                    sb.Append(c);
                }
                return sb.ToString();
            }

            void Warn(string path, IssueCode code, string message)
            {
                Issues.Add(new ValidationIssue(path, code, IssueSeverity.Warning, message));
            }

            void Error(string path, IssueCode code, string message)
            {
                Issues.Add(new ValidationIssue(path, code, IssueSeverity.Error, message));
            }
        }
    }
}
