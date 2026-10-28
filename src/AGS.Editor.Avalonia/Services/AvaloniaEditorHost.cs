using System;
using AGS.Editor.Hosting;

namespace AGS.Editor.Avalonia.Services
{
    /// <summary>
    /// Editor host for the desktop app. Messages go to the Output pane; questions from the editor
    /// logic are answered with their default for now (interactive dialogs come with the panes that need them).
    /// </summary>
    public sealed class AvaloniaEditorHost : IEditorHost
    {
        private readonly Action<string> _output;

        public AvaloniaEditorHost(Action<string> output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public void ShowMessage(string message, MessageSeverity severity)
        {
            _output($"[{severity}] {message}");
        }

        public bool AskQuestion(string question, MessageSeverity severity, bool defaultAnswer)
        {
            _output($"[{severity}] {question} -> {(defaultAnswer ? "yes" : "no")}");
            return defaultAnswer;
        }
    }
}
