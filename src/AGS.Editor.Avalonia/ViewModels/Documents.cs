using System.Collections.Generic;
using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using AGS.Types;

namespace AGS.Editor.Avalonia.ViewModels
{
    public abstract partial class DocumentViewModel : ObservableObject
    {
        protected DocumentViewModel(string title, object key)
        {
            Title = title;
            Key = key;
        }

        public string Title { get; }

        /// <summary>The object the document edits; used to find an already open tab.</summary>
        public object Key { get; }
    }

    public sealed class GameOverviewDocument : DocumentViewModel
    {
        public GameOverviewDocument(Game game, string summary) : base("Welcome", game)
        {
            GameName = game.Settings.GameName;
            Summary = summary;
        }

        public string GameName { get; }
        public string Summary { get; }
    }

    /// <summary>An open script (.asc/.ash); edits go straight to the Script object, which marks it modified.</summary>
    public sealed class ScriptDocument : DocumentViewModel
    {
        private readonly Script _script;
        private readonly Action _onEdited;

        public ScriptDocument(Script script, AvaloniaEdit.Highlighting.IHighlightingDefinition highlighting, Action onEdited,
            AGS.Editor.ScriptCompletion completion = null)
            : base(script.FileName, script)
        {
            _script = script;
            Highlighting = highlighting;
            _onEdited = onEdited;
            Completion = completion;
        }

        /// <summary>Autocomplete for this project (null: no completion).</summary>
        public AGS.Editor.ScriptCompletion Completion { get; }

        public AvaloniaEdit.Highlighting.IHighlightingDefinition Highlighting { get; }

        public Script Script => _script;

        /// <summary>Asks the view to show the first match of a regex (caret after the next opening brace).</summary>
        public event Action<string> NavigationRequested;

        /// <summary>Regex of a navigation requested before the view existed.</summary>
        public string PendingNavigation { get; set; }

        public void NavigateTo(string regex)
        {
            PendingNavigation = regex;
            NavigationRequested?.Invoke(regex);
        }

        /// <summary>Line (1-based) marked as erroneous by the last build, or null.</summary>
        public int? ErrorLine { get; private set; }
        public string ErrorMessage { get; private set; }

        /// <summary>Asks the view to show a line (1-based), optionally marked as an error.</summary>
        public event Action<int> LineRequested;

        public int? PendingLine { get; set; }

        public void ShowError(int line, string message)
        {
            ErrorLine = line;
            ErrorMessage = message;
            PendingLine = line;
            LineRequested?.Invoke(line);
        }

        /// <summary>Lines (1-based) with a breakpoint (stored in Script.BreakpointedLines, as upstream).</summary>
        public IReadOnlyCollection<int> Breakpoints => _script.BreakpointedLines ?? Array.Empty<int>();

        /// <summary>Raised when a breakpoint was added (true) or removed (false) on a line.</summary>
        public event Action<ScriptDocument, int, bool> BreakpointToggled;

        /// <summary>Upstream F9 / margin click: toggles a breakpoint on a line.</summary>
        public void ToggleBreakpoint(int line)
        {
            var lines = new List<int>(_script.BreakpointedLines ?? Array.Empty<int>());
            bool added = !lines.Remove(line);
            if (added) lines.Add(line);
            lines.Sort();
            _script.BreakpointedLines = lines.ToArray();
            BreakpointToggled?.Invoke(this, line, added);
            Redraw?.Invoke();
        }

        /// <summary>Line (1-based) where the debugger stopped, or null.</summary>
        public int? ExecutionLine { get; private set; }

        public void SetExecutionLine(int? line)
        {
            ExecutionLine = line;
            if (line is int l)
                ShowLine(l);
            Redraw?.Invoke();
        }

        private void ShowLine(int line)
        {
            PendingLine = line;
            LineRequested?.Invoke(line);
        }

        /// <summary>Asks the view to repaint (breakpoints, execution line).</summary>
        public event Action Redraw;

        /// <summary>Asks the view to select a character range (search results, Go to Definition).</summary>
        public event Action<int, int> SelectionRequested;

        /// <summary>A selection requested before the view existed.</summary>
        public (int Offset, int Length)? PendingSelection { get; set; }

        public void Select(int offset, int length)
        {
            PendingSelection = (offset, length);
            SelectionRequested?.Invoke(offset, length);
        }

        /// <summary>Raised by the view for F12 / ⌘-click: the caret offset to go to the definition of.</summary>
        public event Action<ScriptDocument, string, int> DefinitionRequested;

        public void RequestDefinition(string text, int offset) => DefinitionRequested?.Invoke(this, text, offset);

        /// <summary>Raised for ⇧F12 / "Find All Usages": the document, its text, the caret offset and the word there.</summary>
        public event Action<ScriptDocument, string, int, string> UsagesRequested;

        public void RequestUsages(string text, int offset)
        {
            string word = WordAt(text, offset);
            if (word != null) UsagesRequested?.Invoke(this, text, offset, word);
        }

        /// <summary>The script identifier at (or just before) <paramref name="offset"/>, or null.</summary>
        public static string WordAt(string text, int offset)
        {
            if (string.IsNullOrEmpty(text)) return null;
            offset = Math.Clamp(offset, 0, text.Length);
            static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
            int start = offset, end = offset;
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            while (end < text.Length && IsWordChar(text[end])) end++;
            if (end == start || char.IsDigit(text[start])) return null;
            return text.Substring(start, end - start);
        }

        public void ClearError()
        {
            ErrorLine = null;
            ErrorMessage = null;
        }

        public string Text
        {
            get => _script.Text;
            set
            {
                if (value == _script.Text) return;
                _script.Text = value;
                _onEdited?.Invoke();
            }
        }
    }

}
