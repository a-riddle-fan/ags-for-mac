using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AGS.Types;

namespace AGS.Editor.Avalonia.Services
{
    /// <summary>
    /// Syntax highlighting for AGS script, matching the upstream editor's Scintilla setup: the keywords
    /// of Constants.SCRIPT_KEY_WORDS, plus struct and enum names from the script headers ("global classes").
    /// </summary>
    public static class AgsScriptHighlighting
    {
        private static readonly Regex TypeDeclaration = new Regex(@"^\s*(?:(?:managed|builtin|internalstring|autoptr)\s+)*(?:struct|enum)\s+([A-Za-z_]\w*)", RegexOptions.Multiline);

        /// <summary>Struct and enum names declared in script header text.</summary>
        public static IEnumerable<string> FindTypeNames(string headerText)
        {
            if (string.IsNullOrEmpty(headerText)) yield break;
            foreach (Match m in TypeDeclaration.Matches(headerText))
                yield return m.Groups[1].Value;
        }

        /// <summary>Type names of the built-in script API (agsdefns.sh) and the game's own headers.</summary>
        public static IReadOnlyCollection<string> CollectTypeNames(Game game)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            using (var reader = new StreamReader(EditorResources.Open("agsdefns.sh")))
                names.UnionWith(FindTypeNames(reader.ReadToEnd()));
            if (game != null)
                foreach (ScriptAndHeader s in game.RootScriptFolder.AllItemsFlat)
                    names.UnionWith(FindTypeNames(s.Header.Text));
            return names;
        }

        public static IHighlightingDefinition Create(IEnumerable<string> typeNames)
        {
            string Words(IEnumerable<string> words) => string.Concat(words.Distinct().Select(w => $"<Word>{SecurityElement.Escape(w)}</Word>"));

            string xshd = $@"<?xml version=""1.0""?>
<SyntaxDefinition name=""AGS Script"" extensions="".asc;.ash"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
  <Color name=""Comment"" foreground=""#3A9A3A"" />
  <Color name=""String"" foreground=""#C0504D"" />
  <Color name=""Char"" foreground=""#C0504D"" />
  <Color name=""Keyword"" foreground=""#3B6FD8"" fontWeight=""bold"" />
  <Color name=""Type"" foreground=""#2B9AA0"" />
  <Color name=""Number"" foreground=""#B0682A"" />
  <Color name=""Preprocessor"" foreground=""#8E5BB8"" />
  <RuleSet>
    <Span color=""Comment"" begin=""//"" />
    <Span color=""Comment"" multiline=""true"" begin=""/\*"" end=""\*/"" />
    <Span color=""Preprocessor"" begin=""^\s*\#"" />
    <Span color=""String""><Begin>""</Begin><End>""</End><RuleSet><Span begin=""\\"" end=""."" /></RuleSet></Span>
    <Span color=""Char""><Begin>'</Begin><End>'</End><RuleSet><Span begin=""\\"" end=""."" /></RuleSet></Span>
    <Keywords color=""Keyword"">{Words(Constants.SCRIPT_KEY_WORDS.Split(' ', StringSplitOptions.RemoveEmptyEntries))}</Keywords>
    <Keywords color=""Type"">{Words(typeNames)}</Keywords>
    <Rule color=""Number"">\b0[xX][0-9a-fA-F]+|\b\d+(\.\d+)?</Rule>
  </RuleSet>
</SyntaxDefinition>";
            using var reader = XmlReader.Create(new StringReader(xshd));
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
    }
}
