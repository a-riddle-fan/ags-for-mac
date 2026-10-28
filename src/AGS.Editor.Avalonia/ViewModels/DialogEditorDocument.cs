using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AGS.Types;

namespace AGS.Editor.Avalonia.ViewModels
{
    /// <summary>One option row of the dialog editor (upstream DialogOptionEditor).</summary>
    public sealed partial class DialogOptionRow : ObservableObject
    {
        private readonly Action _changed;

        public DialogOptionRow(DialogOption option, Action changed)
        {
            Option = option;
            _changed = changed;
        }

        public DialogOption Option { get; }
        public int ID => Option.ID;

        public string Text
        {
            get => Option.Text;
            set { if (Option.Text != value) { Option.Text = value; OnPropertyChanged(); _changed(); } }
        }

        public bool Show
        {
            get => Option.Show;
            set { if (Option.Show != value) { Option.Show = value; OnPropertyChanged(); _changed(); } }
        }

        public bool Say
        {
            get => Option.Say;
            set { if (Option.Say != value) { Option.Say = value; OnPropertyChanged(); _changed(); } }
        }
    }

    /// <summary>
    /// Dialog editor (upstream Panes/DialogEditor): the options with text and Show/Say flags, New option /
    /// Delete last option, and the dialog script (stored in Game.agf) with AGS highlighting.
    /// </summary>
    public sealed partial class DialogEditorDocument : DocumentViewModel
    {
        public DialogEditorDocument(Dialog dialog, AvaloniaEdit.Highlighting.IHighlightingDefinition highlighting)
            : base("Dialog: " + dialog.Name, dialog)
        {
            Dialog = dialog;
            Highlighting = highlighting;
            RebuildOptions();
        }

        public Dialog Dialog { get; }
        public AvaloniaEdit.Highlighting.IHighlightingDefinition Highlighting { get; }
        public ObservableCollection<DialogOptionRow> Options { get; } = new ObservableCollection<DialogOptionRow>();

        public bool CanDeleteOption => Dialog.Options.Count > 0;

        /// <summary>Raised after the dialog was changed here (needs saving).</summary>
        public event Action Edited;

        /// <summary>Asks the view to reload the script text (after an option added an entry point).</summary>
        public event Action ScriptReplaced;

        /// <summary>Set by the app: asks yes/no (null: yes).</summary>
        public Func<string, bool> Confirm { get; set; }

        private void RebuildOptions()
        {
            Options.Clear();
            foreach (DialogOption option in Dialog.Options)
                Options.Add(new DialogOptionRow(option, () => Edited?.Invoke()));
            OnPropertyChanged(nameof(CanDeleteOption));
        }

        public void NewOption()
        {
            DialogOperations.AddOption(Dialog);
            RebuildOptions();
            ScriptReplaced?.Invoke();
            Edited?.Invoke();
        }

        public void DeleteLastOption()
        {
            if (Dialog.Options.Count == 0) return;
            if (Confirm != null && !Confirm("Are you sure you want to delete the last option?")) return;
            DialogOperations.DeleteLastOption(Dialog);
            RebuildOptions();
            Edited?.Invoke();
        }

        /// <summary>Line (1-based) of the last build error in this dialog's script, or null.</summary>
        public int? ErrorLine { get; private set; }
        public string ErrorMessage { get; private set; }

        /// <summary>Asks the view to show (and mark) a script line.</summary>
        public event Action<int> LineRequested;

        /// <summary>Marks a build error (line 0 clears it) and shows the line.</summary>
        public void ShowError(int line, string message)
        {
            ErrorLine = line > 0 ? line : (int?)null;
            ErrorMessage = message;
            if (line > 0)
                LineRequested?.Invoke(line);
        }

        /// <summary>A line to show when the view is attached (search results), or null.</summary>
        public int? PendingLine { get; private set; }

        /// <summary>Selects and shows a script line (Find All Usages results).</summary>
        public void GoToLine(int line)
        {
            PendingLine = line;
            LineRequested?.Invoke(line);
        }

        /// <summary>The view's script text changed.</summary>
        public void SetScript(string text)
        {
            if (Dialog.Script == text) return;
            Dialog.Script = text;
            ErrorLine = null;
            Edited?.Invoke();
        }
    }
}
