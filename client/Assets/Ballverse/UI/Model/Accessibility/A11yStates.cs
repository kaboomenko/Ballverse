using System;
using System.Collections.Generic;

namespace Ballverse.UI.Accessibility
{
    /// <summary>
    /// Selection markers read by the screen-reader bridge (spec §4.8: navigation is announced). An element whose class list
    /// holds one of <see cref="SelectedClasses"/> is the chosen item of its group (tab, studio tool tab, segment, chip,
    /// swatch, row, preset) and is announced as selected. Pure.
    /// </summary>
    public static class A11yStates
    {
        /// <summary>USS classes that mark the selected item of a choice (each is styled in the shipped USS).</summary>
        public static readonly string[] SelectedClasses =
        {
            "bv-tabbar__item--selected",
            "bv-tool-tab--selected",
            "bv-segmented__item--selected",
            "bv-chip--selected",
            "bv-swatch--selected",
            "bv-row--selected",
            "bv-preset--selected",
        };

        static readonly HashSet<string> s_selected = new HashSet<string>(SelectedClasses, StringComparer.Ordinal);

        /// <summary>True when <paramref name="classes"/> contains a selection marker.</summary>
        public static bool IsSelected(IEnumerable<string> classes)
        {
            if (classes == null) return false;
            foreach (string c in classes)
                if (c != null && s_selected.Contains(c)) return true;
            return false;
        }
    }
}
