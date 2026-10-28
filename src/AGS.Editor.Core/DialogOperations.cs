using System;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>Dialog option editing of upstream Panes/DialogEditor.</summary>
    public static class DialogOperations
    {
        /// <summary>
        /// The line break of a dialog script: the one it already uses, else CR LF as the Windows editor
        /// writes (upstream uses Environment.NewLine).
        /// </summary>
        public static string LineBreak(string script) =>
            script != null && script.Contains("\n") && !script.Contains("\r\n") ? "\n" : "\r\n";

        /// <summary>
        /// btnNewOption_Click: a new option (copying Show/Say from the previous one, else both on) and an entry
        /// point "@N ... return" in the script if it has none.
        /// </summary>
        public static DialogOption AddOption(Dialog dialog)
        {
            var newOption = new DialogOption { ID = dialog.Options.Count + 1 };
            if (dialog.Options.Count > 0)
            {
                // Copy Show & Say settings from previous option
                newOption.Say = dialog.Options[dialog.Options.Count - 1].Say;
                newOption.Show = dialog.Options[dialog.Options.Count - 1].Show;
            }
            else
            {
                newOption.Say = true;
                newOption.Show = true;
            }
            dialog.Options.Add(newOption);

            // Ensure there is an entry point in the script for this
            string nl = LineBreak(dialog.Script);
            string script = dialog.Script ?? string.Empty;
            if (!script.Contains(nl + "@" + newOption.ID))
            {
                if (!script.EndsWith(nl))
                    script += nl;
                script += "@" + newOption.ID + nl + "return" + nl;
                dialog.Script = script;
            }
            return newOption;
        }

        /// <summary>btnDeleteOption_Click: removes the last option (its script entry point stays, as upstream).</summary>
        public static void DeleteLastOption(Dialog dialog)
        {
            if (dialog.Options.Count > 0)
                dialog.Options.RemoveAt(dialog.Options.Count - 1);
        }
    }
}
