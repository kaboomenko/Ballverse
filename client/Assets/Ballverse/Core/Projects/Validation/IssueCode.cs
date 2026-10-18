namespace Ballverse.Core.Projects
{
    /// <summary>Kind of problem found by <see cref="ProjectValidator"/> or the repository.</summary>
    public enum IssueCode
    {
        /// <summary>Numeric value outside its range; clamped.</summary>
        OutOfRange,
        /// <summary>Value not allowed (unknown enum, NaN, bad token, wrong array shape); replaced by a default.</summary>
        InvalidValue,
        /// <summary>Color is not "#RRGGBB" / "#RRGGBBAA".</summary>
        InvalidColor,
        /// <summary>Path is absolute, contains '..', backslashes or other forbidden characters.</summary>
        UnsafePath,
        /// <summary>Id is missing or not a safe id.</summary>
        InvalidId,
        /// <summary>Required value or section missing.</summary>
        Missing,
        /// <summary>Too many items (texts, export records); extras dropped.</summary>
        TooMany,
        /// <summary>Duplicate id; renamed.</summary>
        Duplicate,
        /// <summary>Timestamp is not ISO-8601.</summary>
        InvalidTimestamp,
        /// <summary>Reel or fragment longer than 180 s; shortened.</summary>
        DurationTooLong,
        /// <summary>Range start not before its end; repaired.</summary>
        InvalidRange,
        /// <summary>Repository: the file could not be read, parsed or migrated.</summary>
        Corrupt,
        /// <summary>Repository: the file was written by a newer schema version.</summary>
        TooNew,
        /// <summary>Repository: the main file was unusable and the backup was used.</summary>
        UsedBackup,
        /// <summary>Reel shorter than 1 s (spec §3.8); the trim was lengthened.</summary>
        DurationTooShort,
        /// <summary>Repository: the file could not be read because of an I/O error (may be temporary); nothing was used instead.</summary>
        Unavailable,
        /// <summary>Repository: a project folder with neither project.json nor its backup (e.g. an interrupted create or duplicate).</summary>
        Orphan,
    }
}
