using System.Collections.Generic;
using System.IO;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// The speech tools of upstream's GUIController: "Auto-number speech lines" (AutoNumberSpeechLinesProcess) and
    /// "Create voice acting script" (CreateVoiceActingScriptProcess, WriteLinesForCharacter, WriteLineIfItHasVoiceFile).
    /// The game must be saved beforehand and no room open in an editor; after numbering the caller saves the game.
    /// </summary>
    public static class SpeechTools
    {
        public static CompileMessages NumberSpeechLines(GameProject project, bool doNarrator, bool combineIdenticalLines,
            bool removeNumbering, int? characterID)
        {
            return new SpeechLinesNumbering().NumberSpeechLines(project, doNarrator, combineIdenticalLines, removeNumbering, characterID);
        }

        public static CompileMessages CreateVoiceActingScript(GameProject project, string outputFile)
        {
            Game game = project.Game;
            VoiceActorScriptGenerator generator = new VoiceActorScriptGenerator();
            CompileMessages results;
            using (TextProcessingHooks.Install(project))
                results = generator.CreateVoiceActingScript(game);
            if (!results.HasErrors)
            {
                using (StreamWriter sw = new StreamWriter(outputFile, false))
                {
                    foreach (int charID in generator.LinesByCharacter.Keys)
                    {
                        WriteLinesForCharacter(game, charID, sw, generator.LinesByCharacter[charID]);
                    }

                    sw.WriteLine();
                    sw.WriteLine("*** All text lines, in order of appearance in the scripts ***");
                    sw.WriteLine();

                    foreach (GameTextLine line in generator.LinesInOrder)
                    {
                        Character character = game.FindCharacterByID(line.CharacterID);
                        string characterName = "NARRATOR";
                        if (character != null)
                        {
                            characterName = character.ScriptName;
                        }
                        WriteLineIfItHasVoiceFile(characterName, line.Text, sw);
                    }
                }
            }
            return results;
        }

        private static void WriteLinesForCharacter(Game game, int charID, StreamWriter sw, Dictionary<string, string> lines)
        {
            Character character = game.FindCharacterByID(charID);
            if (character != null)
            {
                sw.WriteLine();
                sw.WriteLine("*** Lines for character " + character.RealName + " (ID " + charID + ") ***");
                sw.WriteLine();
            }
            else
            {
                sw.WriteLine();
                sw.WriteLine("*** Lines for narrator ***");
                sw.WriteLine();
            }

            foreach (string line in lines.Keys)
            {
                WriteLineIfItHasVoiceFile(string.Empty, line, sw);
            }
        }

        private static void WriteLineIfItHasVoiceFile(string prefix, string line, StreamWriter sw)
        {
            if (line.StartsWith("&"))
            {
                sw.WriteLine(prefix + ((prefix.Length > 0) ? ": " : string.Empty) + line);
            }
        }
    }
}
