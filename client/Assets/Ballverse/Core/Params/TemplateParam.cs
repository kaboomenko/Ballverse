using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// <c>Template</c> parameter: choice of an arena / track / construction template by id (spec §7.6).
    /// Behaves like <see cref="EnumParam"/>; the panel shows it as a template picker. Templates delivered as data
    /// (spec §7.6 "Что можно поставлять без обновления") are added by building the schema with the extended option list.
    /// </summary>
    public sealed class TemplateParam : OptionParam
    {
        public override ParamType Type => ParamType.Template;

        public TemplateParam(string id, IEnumerable<string> templateIds, string defaultTemplate)
            : base(id, templateIds, defaultTemplate)
        {
        }
    }
}
