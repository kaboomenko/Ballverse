namespace Ballverse.Core.Projects
{
    /// <summary>Operations of <see cref="IFileStore"/>, used by <see cref="InMemoryFileStore"/> for logging and failure injection.</summary>
    public enum FileOp
    {
        FileExists,
        DirectoryExists,
        ReadText,
        ReadBytes,
        WriteText,
        WriteBytes,
        Flush,
        Replace,
        Move,
        Copy,
        DeleteFile,
        CreateDirectory,
        DeleteDirectory,
        ListDirectories,
        ListFiles,
        GetFileSize,
    }
}
