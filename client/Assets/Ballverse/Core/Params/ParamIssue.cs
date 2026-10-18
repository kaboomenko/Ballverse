using System;

namespace Ballverse.Core.Params
{
    /// <summary>One change made while sanitizing a parameter set (spec §7.6).</summary>
    public sealed class ParamIssue : IEquatable<ParamIssue>
    {
        public string ParamId { get; }
        public ParamIssueCode Code { get; }
        /// <summary>English, for logs and diagnostics; not localized.</summary>
        public string Message { get; }

        public ParamIssue(string paramId, ParamIssueCode code, string message)
        {
            ParamId = paramId ?? throw new ArgumentNullException(nameof(paramId));
            Code = code;
            Message = message ?? "";
        }

        public bool Equals(ParamIssue other)
        {
            return other != null && ParamId == other.ParamId && Code == other.Code && Message == other.Message;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ParamIssue);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(ParamId) * 31 + (int)Code;
        }

        public override string ToString()
        {
            return ParamId + ": " + Code + " (" + Message + ")";
        }
    }
}
