namespace AGS.Editor
{
    /// <summary>
    /// Used by the "speech-processor-reparse" source rewrite of upstream GameSpeechProcessor.ProcessScript: after a
    /// string was replaced, parsing continues on the new script text instead of the original one.
    /// </summary>
    internal static class ParserStateFix
    {
        public static ScriptParsing.ParserState Rebuild(string script, int baseLine, int continueAt)
        {
            var state = new ScriptParsing.ParserState(script, baseLine);
            ScriptParsing.FillLines(state);
            state.Reset();
            state.SetAt(continueAt);
            return state;
        }
    }
}
