using System;
using UnityEngine;

// Fields are assigned by JsonUtility through serialization.
#pragma warning disable 0649

namespace Ballverse.Media.Internal
{
    // JSON payloads produced by the native plugins (Java org.json / NSJSONSerialization). Parsed with JsonUtility,
    // which is allocation-light, thread-safe and needs no reflection metadata under IL2CPP. Field names are the wire
    // contract with Plugins/Android/BallverseMedia/*.java and Plugins/iOS/BallverseMedia/*.mm.

    [Serializable]
    internal sealed class NativeErrorDto
    {
        public string code;
        public string message;
    }

    [Serializable]
    internal sealed class NativeMediaInfoDto
    {
        public long fileSizeBytes;
        public long durationUs;
        public bool hasVideo;
        public int width;
        public int height;
        public int rotation;
        public double frameRate;
        public string videoCodec;
        /// <summary>"sdr", "pq", "hlg", "dolbyvision".</summary>
        public string transfer;
        public int bitDepth;
        public bool hasAudio;
        public string audioCodec;
        public int audioSampleRate;
        public int audioChannels;
    }

    [Serializable]
    internal sealed class NativePickDto
    {
        public string path;
    }

    [Serializable]
    internal sealed class NativeAudioDto
    {
        public string path;
        public int sampleRate;
        public int channels;
        public long frames;
    }

    [Serializable]
    internal sealed class NativeSaveDto
    {
        /// <summary>"saved", "permission_denied", "failed".</summary>
        public string status;
        public string location;
        public bool addedToAlbum;
        public string message;
    }

    [Serializable]
    internal sealed class NativeWriterDto
    {
        public int frames;
        public long durationUs;
    }

    [Serializable]
    internal sealed class NativeSignalsDto
    {
        public int thermal;
        public bool lowPower;
        public bool reduceMotion;
        public bool reduceTransparency;
        public long freeSpaceBytes;
    }

    internal static class NativeJson
    {
        public static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) throw new MediaException(MediaErrorCode.Unknown, "empty native response");
            try
            {
                T value = JsonUtility.FromJson<T>(json);
                if (value == null) throw new MediaException(MediaErrorCode.Unknown, "null native response");
                return value;
            }
            catch (ArgumentException e)
            {
                throw new MediaException(MediaErrorCode.Unknown, "malformed native response: " + json, e);
            }
        }

        public static MediaException ParseError(string json)
        {
            if (string.IsNullOrEmpty(json)) return new MediaException(MediaErrorCode.Unknown, "native operation failed without details");
            try
            {
                NativeErrorDto dto = JsonUtility.FromJson<NativeErrorDto>(json);
                if (dto == null) return new MediaException(MediaErrorCode.Unknown, json);
                return new MediaException(MediaException.ParseCode(dto.code), dto.message);
            }
            catch (ArgumentException)
            {
                return new MediaException(MediaErrorCode.Unknown, json);
            }
        }

        public static MediaInfo ToMediaInfo(string path, NativeMediaInfoDto d)
        {
            return new MediaInfo(path, d.fileSizeBytes, d.durationUs, d.hasVideo, d.width, d.height, d.rotation, d.frameRate,
                d.videoCodec, ParseTransfer(d.transfer), d.bitDepth, d.hasAudio, d.audioCodec, d.audioSampleRate, d.audioChannels);
        }

        public static HdrTransfer ParseTransfer(string s)
        {
            switch (s)
            {
                case "pq": return HdrTransfer.Pq;
                case "hlg": return HdrTransfer.Hlg;
                case "dolbyvision": return HdrTransfer.DolbyVision;
                default: return HdrTransfer.Sdr;
            }
        }

        public static GallerySaveResult ToSaveResult(NativeSaveDto d)
        {
            GallerySaveStatus status;
            switch (d.status)
            {
                case "saved": status = GallerySaveStatus.Saved; break;
                case "permission_denied": status = GallerySaveStatus.PermissionDenied; break;
                default: status = GallerySaveStatus.Failed; break;
            }
            return new GallerySaveResult(status, d.location, d.addedToAlbum, d.message);
        }

        public static DeviceSignals ToSignals(NativeSignalsDto d)
        {
            ThermalState t = d.thermal >= 0 && d.thermal <= 3 ? (ThermalState)d.thermal : ThermalState.Unknown;
            return new DeviceSignals(t, d.lowPower, d.reduceMotion, d.reduceTransparency, d.freeSpaceBytes);
        }
    }
}
