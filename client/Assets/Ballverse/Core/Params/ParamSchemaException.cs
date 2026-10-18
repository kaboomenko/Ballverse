using System;

namespace Ballverse.Core.Params
{
    /// <summary>Invalid parameter schema definition or merge (spec §7.6). Thrown at build time, never while sanitizing values.</summary>
    public sealed class ParamSchemaException : Exception
    {
        /// <summary>Offending parameter id, or null for schema-level errors.</summary>
        public string ParamId { get; }

        public ParamSchemaException(string paramId, string message)
            : base(paramId == null ? message : "Parameter '" + paramId + "': " + message)
        {
            ParamId = paramId;
        }
    }
}
