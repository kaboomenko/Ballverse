using System;

namespace Ballverse.Media
{
    /// <summary>A media operation failed. <see cref="Code"/> drives the user-facing message; <see cref="Exception.Message"/> is for logs.</summary>
    public sealed class MediaException : Exception
    {
        public MediaErrorCode Code { get; }

        public MediaException(MediaErrorCode code, string message)
            : base(code + ": " + (message ?? string.Empty))
        {
            Code = code;
        }

        public MediaException(MediaErrorCode code, string message, Exception inner)
            : base(code + ": " + (message ?? string.Empty), inner)
        {
            Code = code;
        }

        /// <summary>Maps a native UPPER_SNAKE code ("DRM_PROTECTED") to <see cref="MediaErrorCode"/>.</summary>
        public static MediaErrorCode ParseCode(string code)
        {
            switch (code)
            {
                case "NOT_SUPPORTED": return MediaErrorCode.NotSupported;
                case "FILE_NOT_FOUND": return MediaErrorCode.FileNotFound;
                case "IO": return MediaErrorCode.Io;
                case "INSUFFICIENT_SPACE": return MediaErrorCode.InsufficientSpace;
                case "PERMISSION_DENIED": return MediaErrorCode.PermissionDenied;
                case "UNSUPPORTED_FORMAT": return MediaErrorCode.UnsupportedFormat;
                case "DRM_PROTECTED": return MediaErrorCode.DrmProtected;
                case "CORRUPT": return MediaErrorCode.Corrupt;
                case "NO_VIDEO_TRACK": return MediaErrorCode.NoVideoTrack;
                case "HDR_UNSUPPORTED": return MediaErrorCode.HdrUnsupported;
                case "DECODER_FAILED": return MediaErrorCode.DecoderFailed;
                case "ENCODER_FAILED": return MediaErrorCode.EncoderFailed;
                case "MUXER_FAILED": return MediaErrorCode.MuxerFailed;
                case "TIMEOUT": return MediaErrorCode.Timeout;
                case "BUSY": return MediaErrorCode.Busy;
                case "INVALID_STATE": return MediaErrorCode.InvalidState;
                case "DOWNLOAD": return MediaErrorCode.Download;
                default: return MediaErrorCode.Unknown;
            }
        }

        /// <summary>Parses a native "CODE|message" error string (used by synchronous native calls).</summary>
        public static MediaException FromNative(string codeAndMessage, MediaErrorCode fallback)
        {
            if (string.IsNullOrEmpty(codeAndMessage)) return new MediaException(fallback, "native call failed without details");
            int bar = codeAndMessage.IndexOf('|');
            if (bar < 0) return new MediaException(fallback, codeAndMessage);
            MediaErrorCode code = ParseCode(codeAndMessage.Substring(0, bar));
            return new MediaException(code == MediaErrorCode.Unknown ? fallback : code, codeAndMessage.Substring(bar + 1));
        }
    }
}
