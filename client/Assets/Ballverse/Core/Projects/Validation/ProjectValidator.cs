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
                        "Expected schema version " + Inv(ProjectMigrator.CurrentSchemaVersion) + ", got " + Inv(doc.SchemaVersion) + "; migrate first.");
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
                if (doc.UserVideo != null && !doc.UserVideo.Volume.Equals(doc.Audio.User))
                {
                    // audio.user is the mixer's source of truth; userVideo.volume mirrors it (see AudioSettings.User).
                    Warn("userVideo.volume", IssueCode.InvalidValue, "Differs from audio.user; audio.user copied.");
                    doc.UserVideo.Volume = doc.Audio.User;
                }

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
                if (clip.FinaleSlowMo.Seconds > 0 && clip.FinaleSlowMo.Seconds < ProjectLimits.MinActiveSlowMoSeconds)
                {
                    // Spec §3.4: an active finale slow-mo lasts 1-3 s; 0 stays "off".
                    Warn("clip.finaleSlowMo.seconds", IssueCode.OutOfRange, "Active slow-mo shorter than 1 s raised to 1 s.");
                    clip.FinaleSlowMo.Seconds = ProjectLimits.MinActiveSlowMoSeconds;
                }
                clip.FinaleSlowMo.Factor = ClampD(clip.FinaleSlowMo.Factor, ProjectLimits.MinSlowMoFactor, ProjectLimits.MaxSlowMoFactor, 0.5, "clip.finaleSlowMo.factor");
                clip.Fill = EnumValue(clip.Fill, SlotFill.Cover, "clip.fill");
                clip.Pan = Vector2(clip.Pan, ProjectLimits.MinPan, ProjectLimits.MaxPan, 0.0, "clip.pan");
                clip.Zoom = ClampD(clip.Zoom, ProjectLimits.MinZoom, ProjectLimits.MaxZoom, 1.0, "clip.zoom");
                clip.FitBackground = SanitizeFitBackground(clip.FitBackground, "clip.fitBackground");
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
                v.FitBackground = SanitizeFitBackground(v.FitBackground, "userVideo.fitBackground");
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
                    if (l.Pip.Border == null)
                    {
                        Warn("layout.pip.border", IssueCode.Missing, "PiP border missing; default (none) used.");
                        l.Pip.Border = new PipBorder();
                    }
                    l.Pip.Border.Px = ClampI(l.Pip.Border.Px, ProjectLimits.MinPipBorderPx, ProjectLimits.MaxPipBorderPx, "layout.pip.border.px");
                    l.Pip.Border.Color = Color(l.Pip.Border.Color, ProjectDefaults.PipBorderColor, "layout.pip.border.color");
                }
            }

            List<TextLayer> SanitizeTexts(List<TextLayer> texts)
            {
                var result = new List<TextLayer>();
                var paths = new List<string>(); // issue path of each kept layer: its index in the file, not in the result
                if (texts == null)
                {
                    Warn("texts", IssueCode.Missing, "Texts missing; empty list used.");
                    return result;
                }
                for (int i = 0; i < texts.Count; i++)
                {
                    string p = "texts[" + Inv(i) + "]";
                    if (texts[i] == null)
                    {
                        Warn(p, IssueCode.InvalidValue, "Null text layer removed.");
                        continue;
                    }
                    if (result.Count == ProjectLimits.MaxTextLayers)
                    {
                        Warn(p, IssueCode.TooMany, "More than " + Inv(ProjectLimits.MaxTextLayers) + " text layers; extras removed.");
                        break;
                    }
                    result.Add(texts[i]);
                    paths.Add(p);
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
                        Warn(paths[i] + ".id", IssueCode.InvalidId, "Missing or unsafe text id regenerated.");
                        needsId[i] = true;
                    }
                    else if (!used.Add(id))
                    {
                        Warn(paths[i] + ".id", IssueCode.Duplicate, "Duplicate text id regenerated.");
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

                for (int i = 0; i < result.Count; i++) SanitizeText(result[i], paths[i]);
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
                t.Align = EnumValue(t.Align, TextAlign.Center, p + ".align");
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

                // Minimum duration (spec §3.8 "1-180 с"). Manual is clamped above; Auto and '= V' lengthen the trim, moving its
                // start earlier first (that part of the source surely exists) and only then extending its end.
                if (ProjectTiming.ReelDurationUs(doc) < ProjectLimits.MinDurationUs)
                {
                    if (o.DurationMode == DurationMode.Auto && doc.Clip != null)
                    {
                        Warn("clip.trimOutTick", IssueCode.DurationTooShort, "Reel shorter than 1 s; clip trim lengthened.");
                        ExtendClipToMinimum(doc.Clip);
                    }
                    else if (o.DurationMode == DurationMode.MatchVideo && doc.UserVideo != null)
                    {
                        Warn("userVideo.trimOutUs", IssueCode.DurationTooShort, "Reel shorter than 1 s; video trim lengthened.");
                        var v = doc.UserVideo;
                        v.TrimInUs = Math.Max(0, v.TrimOutUs - ProjectLimits.MinDurationUs);
                        if (v.TrimOutUs - v.TrimInUs < ProjectLimits.MinDurationUs) v.TrimOutUs = v.TrimInUs + ProjectLimits.MinDurationUs;
                    }
                }

                if (o.Cover == null)
                {
                    Warn("output.cover", IssueCode.Missing, "Cover missing; default used.");
                    o.Cover = new CoverSettings();
                }
                o.Cover.AtUs = ClampL(o.Cover.AtUs, 0, Math.Max(0, ProjectTiming.ReelDurationUs(doc) - 1), "output.cover.atUs");
            }

            /// <summary>Shortest clip length (ticks) whose output lasts at least 1 s; then fits it into [0, MaxDurationTicks].</summary>
            static void ExtendClipToMinimum(ClipSettings clip)
            {
                var slow = clip.FinaleSlowMo;
                int lo = 1, hi = SimConstants.MaxDurationTicks;
                // Output grows with length; the full run is far above 1 s at any allowed speed.
                while (lo < hi)
                {
                    int mid = lo + (hi - lo) / 2;
                    if (ProjectTiming.ClipOutputUs(0, mid, clip.Speed, slow.Seconds, slow.Factor) >= ProjectLimits.MinDurationUs) hi = mid;
                    else lo = mid + 1;
                }
                clip.TrimInTick = Math.Max(0, clip.TrimOutTick - lo);
                if (clip.TrimOutTick - clip.TrimInTick < lo) clip.TrimOutTick = Math.Min(SimConstants.MaxDurationTicks, clip.TrimInTick + lo);
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
                    string p = "exports[" + Inv(i) + "]";
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
                    Warn("exports", IssueCode.TooMany, "Export history trimmed to the newest " + Inv(ProjectLimits.MaxExportRecords) + " records.");
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

            FitBackground SanitizeFitBackground(FitBackground bg, string p)
            {
                if (bg == null)
                {
                    Warn(p, IssueCode.Missing, "Fit background missing; default used.");
                    return new FitBackground();
                }
                bg.Kind = EnumValue(bg.Kind, FitBackgroundKind.Blur, p + ".kind");
                bg.Color = Color(bg.Color, ProjectDefaults.FitBackgroundColor, p + ".color");
                return bg;
            }

            static string Inv(long value)
            {
                return value.ToString(CultureInfo.InvariantCulture);
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
                Warn(path, IssueCode.OutOfRange, "Clamped to " + Inv(min) + ".." + Inv(max) + ".");
                return value < min ? min : max;
            }

            long ClampL(long value, long min, long max, string path)
            {
                if (value >= min && value <= max) return value;
                Warn(path, IssueCode.OutOfRange, "Clamped to " + Inv(min) + ".." + Inv(max) + ".");
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

            /// <summary>
            /// Removes C0 controls (keeping '\n' when allowed), DEL and unpaired surrogates, then truncates to
            /// <paramref name="maxLength"/> UTF-16 units without splitting a surrogate pair or an emoji sequence: when the cut
            /// falls inside a flag, ZWJ, keycap, tag or modifier sequence, the whole partial sequence is dropped (see
            /// <see cref="JoinedAcrossCut"/>). Uses fixed code point ranges, not StringInfo, so the result is the same on every runtime.
            /// </summary>
            static string CleanText(string value, int maxLength, bool allowNewline)
            {
                if (value == null) return null;
                var sb = new StringBuilder(Math.Min(value.Length, maxLength));
                int next = -1; // first code point that did not fit
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    int cp, units;
                    if (char.IsHighSurrogate(c))
                    {
                        if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) continue;
                        cp = char.ConvertToUtf32(c, value[i + 1]);
                        units = 2;
                    }
                    else if (char.IsLowSurrogate(c))
                    {
                        continue;
                    }
                    else
                    {
                        if ((c < 0x20 && !(allowNewline && c == '\n')) || c == 0x7F) continue;
                        cp = c;
                        units = 1;
                    }
                    if (sb.Length + units > maxLength)
                    {
                        next = cp;
                        break;
                    }
                    if (units == 2) sb.Append(c).Append(value[i + 1]);
                    else sb.Append(c);
                    i += units - 1;
                }
                if (next >= 0)
                {
                    while (sb.Length > 0)
                    {
                        int last = LastCodePoint(sb, out int lastUnits);
                        if (!JoinedAcrossCut(sb, last, next)) break;
                        sb.Length -= lastUnits;
                        next = last;
                    }
                }
                return sb.ToString();
            }

            /// <summary>
            /// True when <paramref name="last"/> (the last kept code point) and <paramref name="next"/> belong to one emoji
            /// sequence: <paramref name="next"/> is an extender, <paramref name="last"/> is a ZERO WIDTH JOINER, or both are
            /// regional indicators and <paramref name="last"/> opens a pair (odd count of indicators at the end of the kept text).
            /// </summary>
            static bool JoinedAcrossCut(StringBuilder kept, int last, int next)
            {
                if (IsExtender(next) || last == 0x200D) return true;
                if (!IsRegionalIndicator(last) || !IsRegionalIndicator(next)) return false;
                int run = 0;
                for (int end = kept.Length; end >= 2; end -= 2)
                {
                    if (!char.IsLowSurrogate(kept[end - 1]) || !char.IsHighSurrogate(kept[end - 2])) break;
                    if (!IsRegionalIndicator(char.ConvertToUtf32(kept[end - 2], kept[end - 1]))) break;
                    run++;
                }
                return run % 2 == 1;
            }

            /// <summary>Code points that attach to the previous one: ZWJ, variation selectors, keycap, skin tones, tags, combining diacritics.</summary>
            static bool IsExtender(int cp)
            {
                return cp == 0x200D
                    || (cp >= 0xFE00 && cp <= 0xFE0F)
                    || cp == 0x20E3
                    || (cp >= 0x1F3FB && cp <= 0x1F3FF)
                    || (cp >= 0xE0020 && cp <= 0xE007F)
                    || (cp >= 0x0300 && cp <= 0x036F);
            }

            static bool IsRegionalIndicator(int cp)
            {
                return cp >= 0x1F1E6 && cp <= 0x1F1FF;
            }

            static int LastCodePoint(StringBuilder sb, out int units)
            {
                char lastChar = sb[sb.Length - 1];
                if (char.IsLowSurrogate(lastChar) && sb.Length >= 2 && char.IsHighSurrogate(sb[sb.Length - 2]))
                {
                    units = 2;
                    return char.ConvertToUtf32(sb[sb.Length - 2], lastChar);
                }
                units = 1;
                return lastChar;
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
