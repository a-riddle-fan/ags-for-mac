using System;
using System.IO;
using System.Linq;
using AGS.Editor.Hosting;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    [Collection(CurrentDirectoryCollection.Name)]
    public class ScriptSearchTests : IDisposable
    {
        private readonly string _originalCwd = Directory.GetCurrentDirectory();
        public void Dispose() => Directory.SetCurrentDirectory(_originalCwd);

        private static GameProject Load() =>
            GameProject.Load(Path.Combine(ReferenceGame.Directory, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));

        [Fact]
        public void FindsInModulesAndRoomScripts()
        {
            GameProject project = Load();
            var scripts = ScriptSearch.AllScripts(project.Game).ToList();
            Assert.Contains(scripts, s => s.FileName == "GlobalScript.asc");
            Assert.Contains(scripts, s => s.FileName.StartsWith("room", StringComparison.OrdinalIgnoreCase));

            var results = ScriptSearch.FindAll(scripts, "player.changeroom");
            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Contains("player.ChangeRoom", r.LineText, StringComparison.OrdinalIgnoreCase));
            ScriptSearchResult first = results[0];
            string[] lines = first.Script.Text.Split('\n');
            Assert.Equal(lines[first.Line - 1].TrimEnd('\r'), first.LineText);
            Assert.Equal(first.Offset, first.Script.Text.IndexOf(first.LineText) + first.Column - 1);

            // Match case and whole word narrow the results.
            Assert.True(ScriptSearch.FindAll(scripts, "player.changeroom", matchCase: true).Count < results.Count);
            int partial = ScriptSearch.FindAll(scripts, "Room").Count;
            Assert.True(ScriptSearch.FindAll(scripts, "Room", wholeWord: true).Count < partial);
        }

        [Fact]
        public void FindUsagesSkipsStringsCommentsAndPartialWords()
        {
            var script = new Script("test.asc", "// cEgo in a comment\n" +
                "cEgo.Walk(1, 2); /* cEgo */ Display(\"cEgo says \\\"hi\\\" cEgo\");\n" +
                "cEgoX = 'c'; cEgo.Say(\"x\"); // cEgo\n", false);
            var results = ScriptSearch.FindUsages(new[] { script }, "cEgo");
            Assert.Equal(2, results.Count);
            Assert.Equal((2, 1), (results[0].Line, results[0].Column));
            Assert.Equal(3, results[1].Line);
            Assert.Equal("cEgo.Say", script.Text.Substring(results[1].Offset, 8));
            Assert.Empty(ScriptSearch.FindUsages(new[] { script }, "cego")); // case-sensitive as upstream

            // In the reference game: the player character is used outside strings and comments.
            GameProject project = Load();
            string player = project.Game.PlayerCharacter.ScriptName;
            var usages = ScriptSearch.FindUsages(ScriptSearch.AllScripts(project.Game), player);
            Assert.NotEmpty(usages);
            Assert.All(usages, u => Assert.False(ScriptSearch.IsInsideStringOrComment(u.Script.Text, u.Offset)));
            Assert.True(usages.Count <= ScriptSearch.FindAll(ScriptSearch.AllScripts(project.Game), player, true, true).Count);
        }

        [Fact]
        public void LocalVariablesAreOnlySearchedInTheirFunction()
        {
            string text = "int i;\nfunction a(int count)\n{\n  int i = 0; // \"}\"\n  i += count;\n  String s = \"i}\";\n}\n" +
                          "function b()\n{\n  i = 5;\n}\n";
            var script = new Script("test.asc", text, false);
            int inA = text.IndexOf("i += count");
            int inB = text.IndexOf("i = 5");

            var (start, end) = ScriptSearch.FindFunctionAround(text, inA).Value;
            Assert.Equal(text.IndexOf("function a"), start);
            Assert.Equal(text.IndexOf("function b") - 1, end); // braces in strings and comments are ignored
            Assert.Null(ScriptSearch.FindFunctionAround(text, 2));   // outside functions

            var local = ScriptSearch.FindLocalUsages(script, text, inA, "i");
            Assert.Equal(new[] { 4, 5 }, local.Select(r => r.Line));
            Assert.Null(ScriptSearch.FindLocalUsages(script, text, inB, "i"));          // the global i in b()
            Assert.Equal(2, ScriptSearch.FindLocalUsages(script, text, inA, "count").Count); // parameter + use
            Assert.Equal(4, ScriptSearch.FindUsages(new[] { script }, "i").Count);       // everywhere, except in strings and comments
        }

        [Fact]
        public void ReplaceAllChangesTheScripts()
        {
            GameProject project = Load();
            var scripts = ScriptSearch.AllScripts(project.Game).ToList();
            int count = ScriptSearch.FindAll(scripts, "player.ChangeRoom", matchCase: true).Count;
            Assert.Equal(count, ScriptSearch.ReplaceAll(scripts, "player.ChangeRoom", "cBernard.ChangeRoom", matchCase: true));
            Assert.Empty(ScriptSearch.FindAll(scripts, "player.ChangeRoom", matchCase: true));
            Assert.Contains(scripts, s => s.Modified);
        }

        [Fact]
        public void GoToDefinitionFindsModuleFunctionsMembersAndLocals()
        {
            GameProject project = Load();
            var completion = new ScriptCompletion(project.Game);
            Script global = project.Game.RootScriptFolder.GetScriptByFileName("GlobalScript.asc", true);
            ScriptAndHeader doors = project.Game.RootScriptFolder.AllItemsFlat.Single(m => m.Name == "Doors");
            AutoComplete.ConstructCache(doors.Header, null);
            string function = doors.Header.AutoCompleteData.Functions.First().FunctionName;

            // A module function used elsewhere: its implementation in Doors.asc (preferred to the header import).
            string text = "function test() {\n  " + function + "(1);\n}";
            var def = completion.FindDefinition(global, text, text.IndexOf(function) + 2);
            Assert.NotNull(def);
            Assert.Equal("Doors.asc", def.Script.FileName);
            Assert.Equal(function, doors.Script.Text.Substring(def.Offset, function.Length));

            // A built-in member: Character.Say is declared by the script API.
            text = "function t() {\n  player.Say(\"hi\");\n}";
            def = completion.FindDefinition(global, text, text.IndexOf("Say"));
            Assert.NotNull(def);
            Assert.True(def.IsBuiltIn);
            Assert.Equal("Character.Say", def.Symbol);

            // A local variable.
            text = "function t() {\n  int coins = 3;\n  coins++;\n}";
            def = completion.FindDefinition(global, text, text.LastIndexOf("coins"));
            Assert.Equal(text.IndexOf("coins"), def.Offset);
        }

        [Fact]
        public void CallTipsShowTheParameterBeingTyped()
        {
            GameProject project = Load();
            var completion = new ScriptCompletion(project.Game);
            Script global = project.Game.RootScriptFolder.GetScriptByFileName("GlobalScript.asc", true);

            // A member function: Character.Say(const string message, ...)
            string text = "function t() {\n  player.Say(\"Hello, world\", ";
            var tip = completion.GetCallTip(global, text, text.Length);
            Assert.NotNull(tip);
            Assert.StartsWith("void Character.Say(", tip.Prefix);
            Assert.Equal(1, tip.CurrentParameter); // the comma inside the string doesn't count

            // A global function with an enum parameter: the enum's values are offered (upstream).
            text = "function t() {\n  player.Walk(10, 20, ";
            tip = completion.GetCallTip(global, text, text.Length);
            Assert.NotNull(tip);
            Assert.Equal(2, tip.CurrentParameter);
            Assert.Contains("eBlock", tip.EnumValues);
            Assert.StartsWith("optional BlockingStyle", tip.Parameters[2]);

            // Nested calls: the innermost open call wins; outside calls there is no tip.
            text = "function t() {\n  player.Say(String.Format(\"%d\", ";
            tip = completion.GetCallTip(global, text, text.Length);
            Assert.Contains("Format", tip.Prefix);
            Assert.Equal(1, tip.CurrentParameter);
            text = "function t() {\n  player.Say(\"x\");\n  ";
            Assert.Null(completion.GetCallTip(global, text, text.Length));
        }
    }
}
