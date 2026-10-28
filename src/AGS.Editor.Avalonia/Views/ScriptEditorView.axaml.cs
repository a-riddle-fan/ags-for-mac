using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using System.Linq;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Search;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>AGS script editor: AvaloniaEdit with AGS highlighting and the search panel (⌘F).</summary>
    public partial class ScriptEditorView : UserControl
    {
        private ScriptDocument _document;
        private bool _loading;

        public ScriptEditorView()
        {
            InitializeComponent();
            SearchPanel.Install(Editor);
            Editor.Options.ConvertTabsToSpaces = true;
            Editor.Options.IndentationSize = 2; // upstream editor default
            Editor.TextChanged += OnTextChanged;
            Editor.TextArea.TextEntered += OnTextEntered;
            Editor.TextArea.KeyDown += OnKeyDown;
            Editor.TextArea.Caret.PositionChanged += (s, e) =>
            {
                if (CallTipWindow != null) ShowCallTip(); // follow the parameter, close outside the call
            };
            // ⌘-click: Go to Definition of the clicked word (as in Xcode / VS Code on macOS)
            Editor.TextArea.PointerReleased += (s, e) =>
            {
                if (_document != null && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
                {
                    var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor));
                    if (pos != null)
                        _document.RequestDefinition(Editor.Text, Editor.Document.GetOffset(pos.Value.Location));
                }
            };
            // right-click: move the caret there (unless inside the selection) and show the context menu
            Editor.TextArea.AddHandler(PointerPressedEvent, (s, e) =>
            {
                if (!e.GetCurrentPoint(Editor.TextArea).Properties.IsRightButtonPressed) return;
                var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor));
                if (pos == null) return;
                int offset = Editor.Document.GetOffset(pos.Value.Location);
                if (Editor.SelectionLength == 0 || offset < Editor.SelectionStart || offset > Editor.SelectionStart + Editor.SelectionLength)
                    Editor.TextArea.Caret.Offset = offset;
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Editor.ContextMenu = BuildContextMenu();
            Editor.TextArea.TextView.BackgroundRenderers.Add(new ErrorLineRenderer(() => _document?.ErrorLine));
            Editor.TextArea.TextView.BackgroundRenderers.Add(new DebugLineRenderer(() => _document));
            // click on the line numbers toggles a breakpoint (upstream: click in the margin)
            foreach (var margin in Editor.TextArea.LeftMargins)
            {
                margin.PointerPressed += (s, e) =>
                {
                    if (_document == null) return;
                    var textView = Editor.TextArea.TextView;
                    var pos = e.GetPosition(textView);
                    var visualLine = textView.GetVisualLineFromVisualTop(pos.Y + textView.ScrollOffset.Y);
                    if (visualLine != null)
                    {
                        _document.ToggleBreakpoint(visualLine.FirstDocumentLine.LineNumber);
                        e.Handled = true;
                    }
                };
            }
        }

        private void RedrawText() => Editor.TextArea.TextView.Redraw();

        /// <summary>Breakpoint lines (red) and the line where the debugger stopped (yellow), as upstream marks them.</summary>
        private sealed class DebugLineRenderer : AvaloniaEdit.Rendering.IBackgroundRenderer
        {
            private static readonly global::Avalonia.Media.IBrush BreakpointFill =
                new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromArgb(90, 200, 30, 30));
            private static readonly global::Avalonia.Media.IBrush BreakpointDot =
                new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(200, 30, 30));
            private static readonly global::Avalonia.Media.IBrush ExecutionFill =
                new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromArgb(160, 255, 230, 60));
            private readonly Func<ScriptDocument> _document;

            public DebugLineRenderer(Func<ScriptDocument> document) { _document = document; }

            public AvaloniaEdit.Rendering.KnownLayer Layer => AvaloniaEdit.Rendering.KnownLayer.Background;

            public void Draw(AvaloniaEdit.Rendering.TextView textView, global::Avalonia.Media.DrawingContext drawingContext)
            {
                ScriptDocument doc = _document();
                if (doc == null || textView.Document == null) return;
                foreach (int line in doc.Breakpoints)
                    Fill(textView, drawingContext, line, BreakpointFill, dot: true);
                if (doc.ExecutionLine is int exec)
                    Fill(textView, drawingContext, exec, ExecutionFill, dot: false);
            }

            private static void Fill(AvaloniaEdit.Rendering.TextView textView, global::Avalonia.Media.DrawingContext dc, int line,
                global::Avalonia.Media.IBrush brush, bool dot)
            {
                if (line < 1 || line > textView.Document.LineCount) return;
                var docLine = textView.Document.GetLineByNumber(line);
                var visual = textView.GetVisualLine(line);
                if (visual == null) return;
                foreach (var rect in AvaloniaEdit.Rendering.BackgroundGeometryBuilder.GetRectsFromVisualSegment(textView, visual, 0, 1000))
                {
                    dc.FillRectangle(brush, new global::Avalonia.Rect(0, rect.Top, textView.Bounds.Width, rect.Height));
                    if (dot)
                        dc.DrawEllipse(BreakpointDot, null, new global::Avalonia.Point(rect.Height / 2 + 1, rect.Top + rect.Height / 2), rect.Height / 3, rect.Height / 3);
                    break;
                }
            }
        }

        /// <summary>Paints the line of the last compile error, as upstream's error marker does.</summary>
        private sealed class ErrorLineRenderer : AvaloniaEdit.Rendering.IBackgroundRenderer
        {
            private static readonly global::Avalonia.Media.IBrush Fill =
                new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromArgb(70, 230, 60, 60));
            private readonly Func<int?> _line;

            public ErrorLineRenderer(Func<int?> line) { _line = line; }

            public AvaloniaEdit.Rendering.KnownLayer Layer => AvaloniaEdit.Rendering.KnownLayer.Background;

            public void Draw(AvaloniaEdit.Rendering.TextView textView, global::Avalonia.Media.DrawingContext drawingContext)
            {
                int? line = _line();
                if (line == null || textView.Document == null || line > textView.Document.LineCount) return;
                var docLine = textView.Document.GetLineByNumber(line.Value);
                foreach (var rect in AvaloniaEdit.Rendering.BackgroundGeometryBuilder.GetRectsFromVisualSegment(
                             textView, textView.GetOrConstructVisualLine(docLine), 0, 1000))
                {
                    drawingContext.FillRectangle(Fill, new global::Avalonia.Rect(0, rect.Top, textView.Bounds.Width, rect.Height));
                }
            }
        }

        private CompletionWindow _completion;

        internal CompletionWindow CompletionWindow => _completion;

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+Space (as in the upstream editor) or Option+Escape: show completions for the current word.
            if (e.Key == Key.Escape && CallTipWindow != null)
            {
                CloseCallTip();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F9)
            {
                // Toggle Breakpoint (F9 as in the upstream editor)
                _document?.ToggleBreakpoint(Editor.TextArea.Caret.Line);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F12 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Find All Usages (⇧F12, as in Visual Studio; upstream: context menu only)
                _document?.RequestUsages(Editor.Text, Editor.CaretOffset);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F12)
            {
                // Go to Definition (F12 as in the upstream editor)
                _document?.RequestDefinition(Editor.Text, Editor.CaretOffset);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                ShowCompletion(memberAccess: false, force: true);
                e.Handled = true;
            }
        }

        /// <summary>Upstream ScriptEditor context menu (the parts that apply here).</summary>
        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();
            var definition = new MenuItem { Header = "Go to Definition", InputGesture = new KeyGesture(Key.F12) };
            definition.Click += (s, e) => _document?.RequestDefinition(Editor.Text, Editor.CaretOffset);
            var usages = new MenuItem { Header = "Find All Usages", InputGesture = new KeyGesture(Key.F12, KeyModifiers.Shift) };
            usages.Click += (s, e) => _document?.RequestUsages(Editor.Text, Editor.CaretOffset);
            var breakpoint = new MenuItem { Header = "Toggle Breakpoint", InputGesture = new KeyGesture(Key.F9) };
            breakpoint.Click += (s, e) => _document?.ToggleBreakpoint(Editor.TextArea.Caret.Line);
            var cut = new MenuItem { Header = "Cut", InputGesture = new KeyGesture(Key.X, KeyModifiers.Meta) };
            cut.Click += (s, e) => Editor.Cut();
            var copy = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Meta) };
            copy.Click += (s, e) => Editor.Copy();
            var paste = new MenuItem { Header = "Paste", InputGesture = new KeyGesture(Key.V, KeyModifiers.Meta) };
            paste.Click += (s, e) => Editor.Paste();
            menu.Items.Add(definition);
            menu.Items.Add(usages);
            menu.Items.Add(new Separator());
            menu.Items.Add(breakpoint);
            menu.Items.Add(new Separator());
            menu.Items.Add(cut);
            menu.Items.Add(copy);
            menu.Items.Add(paste);
            menu.Opening += (s, e) =>
            {
                string word = ScriptDocument.WordAt(Editor.Text, Editor.CaretOffset);
                definition.IsEnabled = usages.IsEnabled = word != null;
                definition.Header = word == null ? "Go to Definition" : $"Go to Definition of {word}";
                usages.Header = word == null ? "Find All Usages" : $"Find All Usages of {word}";
                cut.IsEnabled = copy.IsEnabled = Editor.SelectionLength > 0;
            };
            return menu;
        }

        private void OnTextEntered(object sender, TextInputEventArgs e)
        {
            if (e.Text == "(" || e.Text == "," || e.Text == ")")
            {
                ShowCallTip();
                return;
            }
            if (e.Text == ".")
                ShowCompletion(memberAccess: true, force: false);
            else if (_completion == null && e.Text?.Length == 1 && (char.IsLetterOrDigit(e.Text[0]) || e.Text[0] == '_'))
                ShowCompletion(memberAccess: false, force: false);
        }

        /// <summary>Opens the completion popup at the caret (members after '.', otherwise global names).</summary>
        internal void ShowCompletion(bool memberAccess, bool force)
        {
            ScriptCompletion completion = _document?.Completion;
            if (completion == null) return;
            int caret = Editor.CaretOffset;
            string text = Editor.Text;

            int wordStart = caret;
            while (wordStart > 0 && (char.IsLetterOrDigit(text[wordStart - 1]) || text[wordStart - 1] == '_'))
                wordStart--;
            string prefix = text.Substring(wordStart, caret - wordStart);

            System.Collections.Generic.IReadOnlyList<CompletionItem> items;
            if (memberAccess)
            {
                completion.Update(_document.Script);
                items = completion.GetMembers(_document.Script, text, caret);
            }
            else
            {
                // Like upstream, start suggesting after a few letters (or immediately with Ctrl+Space),
                // and not after a '.' (member lists handle that) or inside a declaration's type position.
                if (!force && prefix.Length < 3) return;
                if (wordStart > 0 && text[wordStart - 1] == '.') return;
                completion.Update(_document.Script);
                items = completion.GetGlobal(_document.Script, prefix);
            }
            if (items.Count == 0) return;

            _completion?.Close();
            _completion = new CompletionWindow(Editor.TextArea) { StartOffset = memberAccess ? caret : wordStart };
            // Same font as the code, so the popup reads like part of the editor.
            _completion.CompletionList.FontFamily = Editor.FontFamily;
            _completion.CompletionList.FontSize = Editor.FontSize;
            _completion.MinWidth = 260;
            foreach (CompletionItem item in items)
                _completion.CompletionList.CompletionData.Add(new CompletionData(item));
            _completion.Closed += (s, e) => _completion = null;
            _completion.Show();
            if (!memberAccess && prefix.Length > 0)
                _completion.CompletionList.SelectItem(prefix);
        }

        private Popup _callTip => CallTipPopup;
        private TextBlock _callTipText => CallTipText;

        /// <summary>The open calltip popup, or null.</summary>
        internal Popup CallTipWindow => _callTip != null && _callTip.IsOpen ? _callTip : null;

        /// <summary>The calltip shown last (for tests).</summary>
        internal ScriptCompletion.CallTip CurrentCallTip { get; private set; }

        private void CloseCallTip()
        {
            if (_callTip != null) _callTip.IsOpen = false;
            CurrentCallTip = null;
        }

        /// <summary>
        /// Calltip for the call at the caret (upstream ShowFunctionCalltip): the signature with the current
        /// parameter in bold, or — when that parameter is an enum and nothing is typed yet — its values.
        /// </summary>
        internal void ShowCallTip()
        {
            ScriptCompletion completion = _document?.Completion;
            if (completion == null) return;
            ScriptCompletion.CallTip tip = completion.GetCallTip(_document.Script, Editor.Text, Editor.CaretOffset);
            if (tip == null)
            {
                CloseCallTip();
                return;
            }
            // the parameter is still empty: only whitespace since '(' or ','
            int b = Editor.CaretOffset - 1;
            while (b >= 0 && (Editor.Text[b] == ' ' || Editor.Text[b] == '\t')) b--;
            char before = b >= 0 ? Editor.Text[b] : ' ';
            if (tip.EnumValues != null && tip.EnumValues.Count > 0 && (before == '(' || before == ','))
            {
                CloseCallTip();
                _completion?.Close();
                _completion = new CompletionWindow(Editor.TextArea) { StartOffset = Editor.CaretOffset };
                _completion.CompletionList.FontFamily = Editor.FontFamily;
                _completion.CompletionList.FontSize = Editor.FontSize;
                _completion.MinWidth = 260;
                foreach (string value in tip.EnumValues)
                    _completion.CompletionList.CompletionData.Add(new CompletionData(new CompletionItem(value, CompletionKind.EnumValue, tip.Text)));
                _completion.Closed += (s, e) => _completion = null;
                _completion.Show();
                return;
            }

            _callTipText.Inlines.Clear();
            _callTipText.Inlines.Add(new global::Avalonia.Controls.Documents.Run(tip.Prefix));
            for (int i = 0; i < tip.Parameters.Count; i++)
            {
                if (i > 0) _callTipText.Inlines.Add(new global::Avalonia.Controls.Documents.Run(", "));
                var run = new global::Avalonia.Controls.Documents.Run(tip.Parameters[i]);
                if (i == tip.CurrentParameter) run.FontWeight = global::Avalonia.Media.FontWeight.Bold;
                _callTipText.Inlines.Add(run);
            }
            _callTipText.Inlines.Add(new global::Avalonia.Controls.Documents.Run(")"));
            if (!string.IsNullOrEmpty(tip.Description))
                _callTipText.Inlines.Add(new global::Avalonia.Controls.Documents.Run("\n" + tip.Description) { FontWeight = global::Avalonia.Media.FontWeight.Normal });

            // below the caret's line (offsets relative to the editor's top-left)
            var caretRect = Editor.TextArea.Caret.CalculateCaretRectangle();
            var scroll = Editor.TextArea.TextView.ScrollOffset;
            var origin = Editor.TextArea.TextView.TranslatePoint(new Point(0, 0), Editor) ?? new Point(0, 0);
            _callTip.HorizontalOffset = origin.X + caretRect.X - scroll.X;
            _callTip.VerticalOffset = origin.Y + caretRect.Bottom - scroll.Y + 2;
            _callTip.IsOpen = true;
            CurrentCallTip = tip;
        }

        internal TextEditor TextEditor => Editor;

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_document != null)
            {
                _document.NavigationRequested -= Navigate;
                _document.LineRequested -= GoToLine;
                _document.SelectionRequested -= SelectRange;
                _document.Redraw -= RedrawText;
            }
            _document = DataContext as ScriptDocument;
            if (_document != null)
            {
                _document.NavigationRequested += Navigate;
                _document.LineRequested += GoToLine;
                _document.SelectionRequested += SelectRange;
                _document.Redraw += RedrawText;
            }
            _loading = true;
            Editor.Text = _document?.Text ?? string.Empty;
            Editor.SyntaxHighlighting = _document?.Highlighting;
            Editor.IsReadOnly = _document == null;
            _loading = false;
            if (_document?.PendingNavigation != null)
                Navigate(_document.PendingNavigation);
            if (_document?.PendingLine != null)
                GoToLine(_document.PendingLine.Value);
            if (_document?.PendingSelection is (int offset, int length))
                SelectRange(offset, length);
            Editor.TextArea.TextView.Redraw();
        }

        /// <summary>Selects a character range and scrolls to it.</summary>
        internal void SelectRange(int offset, int length)
        {
            if (_document != null)
                _document.PendingSelection = null;
            if (offset < 0 || offset > Editor.Document.TextLength) return;
            length = Math.Clamp(length, 0, Editor.Document.TextLength - offset);
            Editor.Select(offset, length);
            Editor.CaretOffset = offset;
            int line = Editor.Document.GetLineByOffset(offset).LineNumber;
            Editor.ScrollToLine(line);
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Editor.ScrollToLine(line);
                Editor.Focus();
            }, global::Avalonia.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Selects a line (1-based) and scrolls to it; an error line gets a red background.</summary>
        internal void GoToLine(int line)
        {
            if (_document != null)
                _document.PendingLine = null;
            if (line < 1 || line > Editor.Document.LineCount) return;
            var target = Editor.Document.GetLineByNumber(line);
            Editor.Select(target.Offset, target.Length);
            Editor.CaretOffset = target.Offset;
            Editor.TextArea.TextView.Redraw();
            Editor.ScrollToLine(line);
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Editor.ScrollToLine(line);
                Editor.Focus();
            }, global::Avalonia.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Like upstream ZoomToFile with ZoomToLineAfterOpeningBrace: finds the regex and puts the caret
        /// on the line after the next '{'.
        /// </summary>
        internal void Navigate(string regex)
        {
            if (_document != null)
                _document.PendingNavigation = null;
            var match = System.Text.RegularExpressions.Regex.Match(Editor.Text, regex, System.Text.RegularExpressions.RegexOptions.Multiline);
            if (!match.Success) return;
            int offset = match.Index;
            int brace = Editor.Text.IndexOf('{', offset);
            if (brace >= 0)
            {
                var line = Editor.Document.GetLineByOffset(brace);
                offset = line.NextLine != null ? line.NextLine.Offset : line.EndOffset;
            }
            var target = Editor.Document.GetLineByOffset(offset);
            Editor.CaretOffset = target.EndOffset;
            // Scrolling needs a measured editor; when the tab was just created that happens after layout.
            Editor.ScrollToLine(target.LineNumber);
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Editor.ScrollToLine(target.LineNumber);
                Editor.Focus();
            }, global::Avalonia.Threading.DispatcherPriority.Loaded);
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            if (!_loading && _document != null)
            {
                _document.Text = Editor.Text;
                if (_document.ErrorLine != null)
                {
                    _document.ClearError(); // the marker is stale once the code changes
                    Editor.TextArea.TextView.Redraw();
                }
            }
        }
    }
}
