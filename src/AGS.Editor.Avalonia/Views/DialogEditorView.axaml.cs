using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    public partial class DialogEditorView : UserControl
    {
        private DialogEditorDocument _document;
        private bool _loading;

        public DialogEditorView()
        {
            InitializeComponent();
            Editor.Options.ConvertTabsToSpaces = true;
            Editor.Options.IndentationSize = 2;
            Editor.TextChanged += (s, e) =>
            {
                if (!_loading)
                    _document?.SetScript(Editor.Text);
            };
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_document != null)
            {
                _document.ScriptReplaced -= LoadScript;
                _document.LineRequested -= ShowLine;
            }
            _document = DataContext as DialogEditorDocument;
            if (_document == null) return;
            _document.ScriptReplaced += LoadScript;
            _document.LineRequested += ShowLine;
            Editor.SyntaxHighlighting = _document.Highlighting;
            LoadScript();
            if (_document.ErrorLine is int line)
                ShowLine(line);
            else if (_document.PendingLine is int pending)
                ShowLine(pending);
        }

        /// <summary>Selects a line and scrolls to it (as the script editor does for build errors).</summary>
        internal void ShowLine(int line)
        {
            if (Editor.Document == null || line < 1 || line > Editor.Document.LineCount) return;
            var docLine = Editor.Document.GetLineByNumber(line);
            Editor.Select(docLine.Offset, docLine.Length);
            Editor.CaretOffset = docLine.Offset;
            Editor.ScrollToLine(line);
            Editor.Focus();
        }

        internal int SelectedLine => Editor.TextArea.Caret.Line;

        private void LoadScript()
        {
            _loading = true;
            try { Editor.Text = _document?.Dialog.Script ?? string.Empty; }
            finally { _loading = false; }
        }

        internal string ScriptText => Editor.Text;

        private void OnNewOption(object sender, RoutedEventArgs e) => _document?.NewOption();
        private void OnDeleteOption(object sender, RoutedEventArgs e) => _document?.DeleteLastOption();
    }
}
