using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>One match of a script search.</summary>
    public sealed class ScriptSearchResult
    {
        public ScriptSearchResult(Script script, int line, int column, int offset, string lineText)
        {
            Script = script;
            Line = line;
            Column = column;
            Offset = offset;
            LineText = lineText;
        }

        public Script Script { get; }
        /// <summary>The dialog whose script this is (Find All Usages also searches dialog scripts), else null.</summary>
        public Dialog Dialog { get; init; }
        /// <summary>1-based line and column.</summary>
        public int Line { get; }
        public int Column { get; }
        /// <summary>0-based character offset in the script text.</summary>
        public int Offset { get; }
        public string LineText { get; }
        public string FileName => Script.FileName;
        public override string ToString() => $"{FileName}({Line}): {LineText.Trim()}";
    }

    /// <summary>
    /// Find / replace in the project's scripts (upstream FindReplace with "Look in: Current project"): script
    /// modules and headers and all room scripts (loaded on demand), with match case and whole word.
    /// </summary>
    public static class ScriptSearch
    {
        /// <summary>The scripts searched: module headers and scripts in compile order, then room scripts.</summary>
        public static IEnumerable<Script> AllScripts(Game game)
        {
            foreach (ScriptAndHeader module in game.RootScriptFolder.AllItemsFlat)
            {
                yield return module.Header;
                yield return module.Script;
            }
            foreach (IRoom room in game.RootRoomFolder.AllItemsFlat)
            {
                if (room is not UnloadedRoom unloaded) continue;
                if (unloaded.Script == null)
                {
                    try { unloaded.LoadScript(); }
                    catch (FileNotFoundException) { continue; }
                }
                yield return unloaded.Script;
            }
        }

        private static Regex BuildRegex(string text, bool matchCase, bool wholeWord)
        {
            string pattern = Regex.Escape(text);
            if (wholeWord)
                pattern = @"(?<![A-Za-z0-9_])" + pattern + @"(?![A-Za-z0-9_])";
            return new Regex(pattern, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static IReadOnlyList<ScriptSearchResult> FindAll(IEnumerable<Script> scripts, string text, bool matchCase = false, bool wholeWord = false)
        {
            var results = new List<ScriptSearchResult>();
            if (string.IsNullOrEmpty(text)) return results;
            Regex regex = BuildRegex(text, matchCase, wholeWord);
            foreach (Script script in scripts)
            {
                string content = script.Text ?? string.Empty;
                MatchCollection matches = regex.Matches(content);
                if (matches.Count == 0) continue;
                var lineStarts = new List<int> { 0 };
                for (int i = 0; i < content.Length; i++)
                    if (content[i] == '\n') lineStarts.Add(i + 1);
                foreach (Match m in matches)
                {
                    int index = lineStarts.BinarySearch(m.Index);
                    if (index < 0) index = ~index - 1;
                    int lineStart = lineStarts[index];
                    int lineEnd = content.IndexOf('\n', m.Index);
                    if (lineEnd < 0) lineEnd = content.Length;
                    results.Add(new ScriptSearchResult(script, index + 1, m.Index - lineStart + 1, m.Index,
                        content.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r')));
                }
            }
            return results;
        }

        /// <summary>
        /// Upstream FindAllUsages: the whole-word, case-sensitive occurrences of a script name in all scripts,
        /// except inside strings and comments.
        /// </summary>
        public static IReadOnlyList<ScriptSearchResult> FindUsages(IEnumerable<Script> scripts, string token)
        {
            if (string.IsNullOrEmpty(token)) return Array.Empty<ScriptSearchResult>();
            return FindAll(scripts, token, matchCase: true, wholeWord: true)
                .Where(r => !IsInsideStringOrComment(r.Script.Text ?? string.Empty, r.Offset))
                .ToList();
        }

        /// <summary>
        /// FindAllUsages.GetScriptsToSearch for a local variable: when <paramref name="token"/> is declared in the function
        /// around <paramref name="offset"/> (a parameter or a local), only that function is searched. Returns null when the
        /// position is outside a function or the token isn't local there.
        /// </summary>
        public static IReadOnlyList<ScriptSearchResult> FindLocalUsages(Script script, string text, int offset, string token)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(text)) return null;
            (int Start, int End)? function = FindFunctionAround(text, offset);
            if (function == null) return null;
            var (start, end) = function.Value;
            string body = text.Substring(start, end - start);
            var declaration = new Regex(@"\b[A-Za-z_]\w*\s*\*?\s*\b" + Regex.Escape(token) + @"\b\s*(\[\s*\])?\s*[=;,)\[]");
            bool local = declaration.Matches(body).Cast<Match>()
                .Any(m => !IsInsideStringOrComment(text, start + m.Index) && !Regex.IsMatch(m.Value, @"^(return|new|else|import|export)\b"));
            if (!local) return null;
            var copy = new Script(script.FileName, text, script.IsHeader);
            return FindUsages(new[] { copy }, token)
                .Where(r => r.Offset >= start && r.Offset < end)
                .Select(r => new ScriptSearchResult(script, r.Line, r.Column, r.Offset, r.LineText))
                .ToList();
        }

        /// <summary>
        /// The function around <paramref name="offset"/>: from the start of its header (after the previous top-level
        /// ';' or '}') to its closing brace; braces in strings and comments are ignored. Null outside functions.
        /// </summary>
        public static (int Start, int End)? FindFunctionAround(string text, int offset)
        {
            int depth = 0, bodyOpen = -1, headerStart = 0, candidateHeader = 0;
            int i = 0;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '/' && next == '/') { int e = text.IndexOf('\n', i); i = e < 0 ? text.Length : e; continue; }
                if (c == '/' && next == '*') { int e = text.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? text.Length : e + 1; continue; }
                if (c == '"' || c == '\'')
                {
                    int e = i + 1;
                    while (e < text.Length && text[e] != c && text[e] != '\n') e += text[e] == '\\' ? 2 : 1;
                    i = e;
                    continue;
                }
                if (c == '{')
                {
                    if (depth == 0)
                    {
                        if (i > offset) return null; // the offset is between functions
                        bodyOpen = i;
                        headerStart = candidateHeader;
                        while (headerStart < i && char.IsWhiteSpace(text[headerStart])) headerStart++;
                    }
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        if (bodyOpen >= 0 && offset > bodyOpen && offset <= i)
                            return (headerStart, i + 1);
                        candidateHeader = i + 1;
                        bodyOpen = -1;
                    }
                    if (depth < 0) depth = 0;
                }
                else if (c == ';' && depth == 0)
                {
                    candidateHeader = i + 1;
                }
            }
            // unterminated function body at the end of the text
            return bodyOpen >= 0 && offset > bodyOpen && depth > 0 ? (headerStart, text.Length) : null;
        }

        /// <summary>FindAllUsages over the dialog scripts (AGSEditor.GetAllScripts includes the dialogs).</summary>
        public static IReadOnlyList<ScriptSearchResult> FindUsagesInDialogs(Game game, string token)
        {
            var results = new List<ScriptSearchResult>();
            foreach (Dialog dialog in game.RootDialogFolder.AllItemsFlat)
            {
                var script = new Script(dialog.FileName, dialog.Script ?? string.Empty, false);
                foreach (ScriptSearchResult r in FindUsages(new[] { script }, token))
                    results.Add(new ScriptSearchResult(script, r.Line, r.Column, r.Offset, r.LineText) { Dialog = dialog });
            }
            return results;
        }

        /// <summary>Whether <paramref name="offset"/> is inside a string or character literal or a comment.</summary>
        public static bool IsInsideStringOrComment(string text, int offset)
        {
            int i = 0;
            while (i < offset && i < text.Length)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                int end;
                if (c == '/' && next == '/')
                {
                    end = text.IndexOf('\n', i);
                    if (end < 0) end = text.Length;
                }
                else if (c == '/' && next == '*')
                {
                    end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? text.Length : end + 2;
                }
                else if (c == '"' || c == '\'')
                {
                    end = i + 1;
                    while (end < text.Length && text[end] != c && text[end] != '\n')
                        end += text[end] == '\\' ? 2 : 1;
                    end = Math.Min(end + 1, text.Length);
                }
                else
                {
                    i++;
                    continue;
                }
                if (offset < end) return true;
                i = end;
            }
            return false;
        }

        /// <summary>Replaces every match; returns the number of replacements. Changed scripts are saved with the project.</summary>
        public static int ReplaceAll(IEnumerable<Script> scripts, string text, string replacement, bool matchCase = false, bool wholeWord = false)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            Regex regex = BuildRegex(text, matchCase, wholeWord);
            int count = 0;
            foreach (Script script in scripts.ToList())
            {
                string content = script.Text ?? string.Empty;
                int matches = regex.Matches(content).Count;
                if (matches == 0) continue;
                script.Text = regex.Replace(content, _ => replacement ?? string.Empty);
                count += matches;
            }
            return count;
        }
    }
}
