using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary><c>Enum(options)</c> parameter (spec §7.6); the value is an option id string.</summary>
    public sealed class EnumParam : OptionParam
    {
        public override ParamType Type => ParamType.Enum;

        public EnumParam(string id, IEnumerable<string> options, string defaultOption)
            : base(id, options, defaultOption)
        {
        }
    }
}
