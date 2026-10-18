namespace Ballverse.Media
{
    /// <summary>Implementation behind <see cref="IMediaBridge"/>.</summary>
    public enum MediaBackend
    {
        /// <summary>Editor/desktop stand-in: synthetic test-pattern video, PNG sequence + WAV output. Not a real codec.</summary>
        Fake = 0,
        /// <summary>Android: Photo Picker, MediaExtractor/MediaCodec/MediaMuxer, MediaStore (Java plugin).</summary>
        Android = 1,
        /// <summary>iOS: PHPicker, AVAssetReader/AVAssetWriter, PHPhotoLibrary (Objective-C++ plugin).</summary>
        Ios = 2,
    }
}
