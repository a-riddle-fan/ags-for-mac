namespace AGS.Editor.Hosting
{
    public enum MessageSeverity
    {
        Information,
        Warning,
        Error,
    }

    /// <summary>
    /// What the editor logic needs from its front end (Avalonia UI or command line): replaces the
    /// upstream editor's Factory.GUIController message boxes and prompts.
    /// </summary>
    public interface IEditorHost
    {
        void ShowMessage(string message, MessageSeverity severity);

        /// <summary>Asks a yes/no question; non-interactive hosts return <paramref name="defaultAnswer"/>.</summary>
        bool AskQuestion(string question, MessageSeverity severity, bool defaultAnswer);
    }
}
