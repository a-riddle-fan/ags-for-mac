// ags4mac: forked from ags/Editor/AGS.Editor/TextProcessing/SpeechLinesNumbering.cs.
// Changes: the write-access check (source control) is dropped; errors are returned instead of shown through the
// WinForms GUI controller; rooms are processed through TextProcessingHooks (the editor's ProcessAllGameTexts
// event); speechref.txt is written to the project folder.
using AGS.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AGS.Editor
{
    public class SpeechLinesNumbering : BaseTextProcess
    {
        public const string SPEECH_REFERENCE_FILE_NAME = "speechref.txt";

        private Dictionary<string, FunctionCallType> _speechableFunctionCalls;
        private CompileMessages _errors;
        private bool _includeNarrator;
        private bool _combineIdenticalLines;
        private bool _removeNumbering;
        private int? _characterID;
        private StreamWriter _referenceFile;

        public CompileMessages NumberSpeechLines(GameProject project, bool includeNarrator, bool combineIdenticalLines, bool removeNumbering, int? characterID)
        {
            Game game = project.Game;
            _speechableFunctionCalls = GetFunctionCallsToProcessForSpeech(includeNarrator);

            _errors = new CompileMessages();
            _includeNarrator = includeNarrator;
            _combineIdenticalLines = combineIdenticalLines;
            _removeNumbering = removeNumbering;
            _characterID = characterID;

            using (_referenceFile = new StreamWriter(Path.Combine(project.Directory, SPEECH_REFERENCE_FILE_NAME), false))
            {
                _referenceFile.WriteLine("// AGS auto-numbered speech lines output. This file was automatically generated.");
                PerformNumbering(project);
            }

            return _errors;
        }

        private void PerformNumbering(GameProject project)
        {
            Game game = project.Game;
            SpeechLineProcessor processor = new SpeechLineProcessor(game, _includeNarrator,
                _combineIdenticalLines, _removeNumbering, _characterID, _speechableFunctionCalls, _errors, _referenceFile);

            foreach (Dialog dialog in game.RootDialogFolder.AllItemsFlat)
            {
                foreach (DialogOption option in dialog.Options)
                {
                    if (option.Say)
                    {
                        option.Text = processor.ProcessText(GameTextLine.MakeSpeechLine(game.PlayerCharacter.ID, option.Text), GameTextType.DialogOption);
                    }
                }

                dialog.Script = processor.ProcessText(GameTextLine.MakeScript(dialog.Script, dialog.FileName, dialog.Name), GameTextType.DialogScript);
            }

            foreach (ScriptAndHeader script in game.RootScriptFolder.AllItemsFlat)
            {
                script.Script.Text = processor.ProcessText(GameTextLine.MakeScript(script.Script.Text, script.Script.FileName), GameTextType.Script);
            }

            for (int i = 0; i < game.GlobalMessages.Length; i++)
            {
                game.GlobalMessages[i] = processor.ProcessText(GameTextLine.MakeSpeechLine(Character.NARRATOR_CHARACTER_ID, game.GlobalMessages[i]), GameTextType.Message);
            }

            if (_errors.HasErrors)
            {
                throw new AGSEditorException("Errors encountered. Check the output window for details.");
            }

            TextProcessingHooks.ProcessRoomTexts(project, processor, _errors);
        }
    }
}
