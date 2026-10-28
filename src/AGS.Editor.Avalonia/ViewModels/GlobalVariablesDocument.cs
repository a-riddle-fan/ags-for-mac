using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using AGS.Types;

namespace AGS.Editor.Avalonia.ViewModels
{
    /// <summary>One row of the global variables list.</summary>
    public sealed class GlobalVariableRow
    {
        public GlobalVariableRow(GlobalVariable variable) { Variable = variable; }
        public GlobalVariable Variable { get; }
        public string Name => Variable.Name;
        public string Type => Variable.Type + (Variable.ArrayType == GlobalVariableArrayType.Array ? $"[{Variable.ArraySize}]"
            : Variable.ArrayType == GlobalVariableArrayType.DynamicArray ? "[]" : "");
        public string DefaultValue => Variable.DefaultValue;
    }

    /// <summary>Global variables editor (upstream Panes/GlobalVariablesEditor): list with add/edit/delete.</summary>
    public sealed partial class GlobalVariablesDocument : DocumentViewModel
    {
        private readonly Game _game;

        public GlobalVariablesDocument(Game game) : base("Global Variables", game.GlobalVariables)
        {
            _game = game;
            Rebuild();
        }

        public ObservableCollection<GlobalVariableRow> Variables { get; } = new ObservableCollection<GlobalVariableRow>();

        [ObservableProperty] private GlobalVariableRow _selected;

        /// <summary>Set by the app: the variable dialog (edits the variable; returns true on OK).</summary>
        public Func<GlobalVariable, bool> EditVariable { get; set; }

        /// <summary>Set by the app: yes/no question (null: yes).</summary>
        public Func<string, bool> Confirm { get; set; }

        /// <summary>Raised after the variables changed (needs saving; scripts see them after the next build).</summary>
        public event Action Edited;

        private void Rebuild(GlobalVariable select = null)
        {
            Variables.Clear();
            foreach (GlobalVariable v in _game.GlobalVariables.ToList())
                Variables.Add(new GlobalVariableRow(v));
            Selected = Variables.FirstOrDefault(r => r.Variable == select);
        }

        public void Add()
        {
            var variable = new GlobalVariable();
            if (EditVariable == null || !EditVariable(variable)) return;
            _game.GlobalVariables.Add(variable);
            Rebuild(variable);
            Edited?.Invoke();
        }

        public void Edit(GlobalVariableRow row = null)
        {
            row ??= Selected;
            if (row == null || EditVariable == null || !EditVariable(row.Variable)) return;
            Rebuild(row.Variable);
            Edited?.Invoke();
        }

        public void Delete()
        {
            GlobalVariable variable = Selected?.Variable;
            if (variable == null) return;
            if (Confirm != null && !Confirm("Are you sure you want to remove the variable '" + variable.Name + "'? If you are using it in your scripts, your game will no longer compile."))
                return;
            _game.GlobalVariables.Remove(variable);
            Rebuild();
            Edited?.Invoke();
        }
    }
}
