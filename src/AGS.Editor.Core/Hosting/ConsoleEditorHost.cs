using System;
using System.IO;

namespace AGS.Editor.Hosting
{
    /// <summary>Non-interactive host for command-line tools and tests: prints messages, answers questions with the default.</summary>
    public sealed class ConsoleEditorHost : IEditorHost
    {
        private readonly TextWriter _out;

        public ConsoleEditorHost(TextWriter output = null)
        {
            _out = output ?? Console.Error;
        }

        public void ShowMessage(string message, MessageSeverity severity)
        {
            _out.WriteLine($"{severity}: {message}");
        }

        public bool AskQuestion(string question, MessageSeverity severity, bool defaultAnswer)
        {
            _out.WriteLine($"{severity}: {question} -> {(defaultAnswer ? "yes" : "no")} (non-interactive)");
            return defaultAnswer;
        }
    }
}
