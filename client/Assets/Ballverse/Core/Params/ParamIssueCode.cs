namespace Ballverse.Core.Params
{
    /// <summary>What <see cref="ParamSet.Sanitize"/> had to change (spec §7.6 "валидация ... параметров").</summary>
    public enum ParamIssueCode
    {
        /// <summary>The value was absent; the default was used.</summary>
        Missing = 0,
        /// <summary>The id is not in the schema; the value was dropped.</summary>
        Unknown = 1,
        /// <summary>The value kind cannot represent this parameter type; the default was used.</summary>
        TypeMismatch = 2,
        /// <summary>NaN or infinity; the default was used.</summary>
        NonFinite = 3,
        /// <summary>Outside [min, max]; clamped.</summary>
        OutOfRange = 4,
        /// <summary>Above the device tier cap; clamped (spec §2.9).</summary>
        TierCapped = 5,
        /// <summary>Not on the min + k·step grid; snapped to the nearest step.</summary>
        Snapped = 6,
        /// <summary>Enum / template id not among the options; the default was used.</summary>
        InvalidOption = 7,
        /// <summary>Unparseable colour or seed; the default (or, inside a colour list, nothing) was used.</summary>
        InvalidValue = 8,
        /// <summary>A list had more items than allowed; the tail was cut.</summary>
        ListTruncated = 9,
        /// <summary>A colour list had fewer items than allowed; padded from the default list.</summary>
        ListPadded = 10,
        /// <summary>A string was longer than allowed; cut at a code point boundary.</summary>
        StringTruncated = 11,
        /// <summary>
        /// A string held unpaired UTF-16 surrogates, which UTF-8 cannot carry; each was replaced with U+FFFD so the
        /// value round-trips through the replay paramsJson unchanged (spec §7.4).
        /// </summary>
        InvalidText = 12,
    }
}
