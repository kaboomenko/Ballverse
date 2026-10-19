using System;
using System.Globalization;
using Ballverse.Core;
using Ballverse.Core.Config;
using Ballverse.UI.ViewModels;

namespace Ballverse.App
{
    /// <summary>
    /// Device tier (spec §8.1): the Remote Config override table (<c>tier_overrides</c> by device model) first, then a table of
    /// known SoCs / GPUs (iPhone generation; Adreno, Mali, Immortalis, Xclipse, PowerVR series), capped by RAM (≥ 6 GB for A,
    /// ≥ 4 GB for B). Unknown devices default to B (the dynamic quality fallback of the renderer lowers the profile when the
    /// frame rate stays below 55 FPS, spec §7.7). The user's quality choice and the system power saver adjust the result
    /// (<see cref="Effective"/>). Pure.
    /// </summary>
    public static class DeviceTierDetector
    {
        public static DeviceTier Detect(IDeviceInfo device, RemoteConfigValues remote)
        {
            if (device == null) return DeviceTier.B;
            if (remote != null && !string.IsNullOrEmpty(device.Model) && remote.TryGetTierOverride(device.Model, out DeviceTier forced)) return forced;
            DeviceTier byChip = FromChip(device.Platform, device.Model, device.GpuName);
            return CapByMemory(byChip, device.SystemMemoryMb);
        }

        /// <summary>The tier the renderer and generator use: the detected tier adjusted by the user's choice and power saving.</summary>
        public static DeviceTier Effective(DeviceTier detected, QualityPreference preference, bool lowPowerMode)
        {
            DeviceTier tier = detected;
            if (preference == QualityPreference.High) tier = DeviceTier.A;
            else if (preference == QualityPreference.Economy) tier = DeviceTier.C;
            if (lowPowerMode && tier != DeviceTier.C) tier = tier + 1; // spec §8.2: one profile step down
            return tier;
        }

        public static DeviceTier CapByMemory(DeviceTier tier, int memoryMb)
        {
            if (memoryMb <= 0) return tier;
            if (memoryMb < 3600) return DeviceTier.C;
            if (memoryMb < 5600 && tier == DeviceTier.A) return DeviceTier.B;
            return tier;
        }

        public static DeviceTier FromChip(string platform, string model, string gpu)
        {
            if (string.Equals(platform, "ios", StringComparison.OrdinalIgnoreCase)) return FromAppleModel(model);
            if (string.Equals(platform, "editor", StringComparison.OrdinalIgnoreCase)) return DeviceTier.A;
            return FromGpu(gpu);
        }

        /// <summary>"iPhone14,5" → generation 14 (A15) → A; 11–13 (A12–A14) → B; older → C. iPads by generation of the SoC.</summary>
        public static DeviceTier FromAppleModel(string model)
        {
            if (string.IsNullOrEmpty(model)) return DeviceTier.B;
            int gen = LeadingNumber(model, "iPhone");
            if (gen > 0) return gen >= 14 ? DeviceTier.A : gen >= 11 ? DeviceTier.B : DeviceTier.C;
            gen = LeadingNumber(model, "iPad");
            if (gen > 0) return gen >= 13 ? DeviceTier.A : gen >= 8 ? DeviceTier.B : DeviceTier.C;
            return DeviceTier.B;
        }

        public static DeviceTier FromGpu(string gpu)
        {
            if (string.IsNullOrEmpty(gpu)) return DeviceTier.B;
            string g = gpu.ToLowerInvariant();
            int n;
            if (g.Contains("adreno"))
            {
                n = NumberAfter(g, "adreno");
                if (n >= 730) return DeviceTier.A;          // Snapdragon 8 Gen 1+
                if (n >= 642) return DeviceTier.B;          // 778G (642L), 7 Gen 1+ (644 / 710 / 720)
                return n > 0 ? DeviceTier.C : DeviceTier.B; // 610 / 619 (680 / 695)
            }
            if (g.Contains("immortalis")) return DeviceTier.A;
            if (g.Contains("xclipse")) return DeviceTier.A;
            if (g.Contains("mali"))
            {
                n = NumberAfter(g, "mali-g");
                if (n >= 710) return DeviceTier.A;          // Dimensity 9000+ (G710 MC10)
                if (n >= 610 || n == 78 || n == 77 || n == 68) return DeviceTier.B; // Dimensity 7000 series, Exynos 1280/1380
                return DeviceTier.C;                        // G52 / G57 (Helio G85 / G99)
            }
            if (g.Contains("powervr")) return DeviceTier.C;
            if (g.Contains("apple")) return DeviceTier.A;
            return DeviceTier.B;
        }

        static int LeadingNumber(string model, string prefix)
        {
            int i = model.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return 0;
            return ParseDigits(model, i + prefix.Length);
        }

        static int NumberAfter(string text, string marker)
        {
            int i = text.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return 0;
            i += marker.Length;
            while (i < text.Length && !char.IsDigit(text[i])) i++;
            return ParseDigits(text, i);
        }

        static int ParseDigits(string text, int start)
        {
            int end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == start) return 0;
            return int.TryParse(text.Substring(start, Math.Min(6, end - start)), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
        }
    }
}
