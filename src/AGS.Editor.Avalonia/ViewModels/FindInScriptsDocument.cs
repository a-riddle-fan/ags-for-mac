using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using AGS.Types;

namespace AGS.Editor.Avalonia.ViewModels
{
    /// <summary>
    /// Find / Replace in all scripts (upstream FindReplace with "Current project" + the Find Results pane):
    /// search options, Find All with a result list (double-click opens the script there) and Replace All.
    /// </summary>
    public sealed partial class FindInScriptsDocument : DocumentViewModel
    {
        private readonly Game _game;

        public FindInScriptsDocument(Game game) : base("Find in Scripts", typeof(FindInScriptsDocument))
        {
            _game = game;
        }

        [ObservableProperty] private string _searchText = "";
        [ObservableProperty] private string _replaceText = "";
        [ObservableProperty] private bool _matchCase;
        [ObservableProperty] private bool _wholeWord;
        [ObservableProperty] private string _summary = "Type a text and press Find All.";
        [ObservableProperty] private ScriptSearchResult _selected;

        public ObservableCollection<ScriptSearchResult> Results { get; } = new ObservableCollection<ScriptSearchResult>();

        public int LastSearchLength { get; private set; }

        public Func<string, bool> Confirm { get; set; }
        public event Action Edited;
        public event Action<ScriptSearchResult> OpenResult;

        public void FindAll()
        {
            Results.Clear();
            LastSearchLength = SearchText?.Length ?? 0;
            if (string.IsNullOrEmpty(SearchText)) return;
            var results = ScriptSearch.FindAll(ScriptSearch.AllScripts(_game), SearchText, MatchCase, WholeWord);
            foreach (ScriptSearchResult r in results)
                Results.Add(r);
            int files = results.Select(r => r.Script).Distinct().Count();
            Summary = results.Count == 0 ? $"No occurrences of '{SearchText}' were found."
                : $"{results.Count} occurrence(s) of '{SearchText}' in {files} script(s).";
        }

        /// <summary>Shows the result of Find All Usages (upstream's Find Symbol results).</summary>
        public void ShowUsages(string token, IReadOnlyList<ScriptSearchResult> results)
        {
            SearchText = token;
            MatchCase = true;
            WholeWord = true;
            LastSearchLength = token.Length;
            Results.Clear();
            foreach (ScriptSearchResult r in results)
                Results.Add(r);
            int files = results.Select(r => r.Script).Distinct().Count();
            Summary = $"{results.Count} usage(s) of '{token}' in {files} script(s) (not counting strings and comments).";
        }

        public void ReplaceAll()
        {
            if (string.IsNullOrEmpty(SearchText)) return;
            if (Confirm != null && !Confirm("Are you sure you want to replace all?")) return;
            int count = ScriptSearch.ReplaceAll(ScriptSearch.AllScripts(_game), SearchText, ReplaceText, MatchCase, WholeWord);
            Results.Clear();
            Summary = $"{count} occurrence(s) replaced.";
            if (count > 0)
                Edited?.Invoke();
        }

        public void Open(ScriptSearchResult result)
        {
            if (result != null)
                OpenResult?.Invoke(result);
        }
    }
}
