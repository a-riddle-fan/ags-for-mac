using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AGS.Editor.Avalonia.Services;
using AGS.Editor.Build;
using AGS.Imaging.Magick;
using AGS.Native;
using AGS.Types;
using AGS.Editor;

namespace AGS.Editor.Avalonia.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly StringBuilder _output = new StringBuilder();
        private readonly AvaloniaEditorHost _host;
        private string _lastBuiltGame;
        private AvaloniaEdit.Highlighting.IHighlightingDefinition _scriptHighlighting;
        private ScriptCompletion _completion;

        public MainWindowViewModel()
        {
            _host = new AvaloniaEditorHost(AppendOutput);
            PropertyGrid = new PropertyGridViewModel(message => AppendOutput($"[Error] {message}"));
            PropertyGrid.PropertyChangedByUser += (name, oldValue) =>
            {
                IsModified = true;
                if (SelectedDocument is RoomEditorDocument room)
                {
                    room.IsModified = true;
                    room.Render(); // object positions, sprites, edges may have changed
                }
                if (PropertyGrid.Component is AGS.Types.Font changedFont && _guiRendererReady)
                {
                    try { NativeGuiRenderer.UpdateFont(changedFont); } catch (Exception ex) { AppendOutput($"[Warning] {ex.Message}"); }
                    foreach (GuiEditorDocument d in Documents.OfType<GuiEditorDocument>()) d.Refresh();
                    foreach (FontDocument d in Documents.OfType<FontDocument>()) d.Refresh();
                }
                if (SelectedDocument is GuiEditorDocument guiDoc)
                    guiDoc.Refresh();
                if (SelectedDocument is ViewEditorDocument viewDoc)
                    viewDoc.Refresh();
                if (SelectedDocument is TextParserDocument parserDoc && PropertyGrid.Component is TextParserWord word)
                    parserDoc.Rebuild(word);
                if (SelectedDocument is PaletteDocument paletteDoc)
                    paletteDoc.RefreshSlots();
                StatusText = $"Changed {name} (unsaved)";
                RebuildTree(); // labels (names, IDs) may have changed
            };
            KnownBuildTargets.RegisterAll();
            AppendOutput($"AGS Editor for macOS — AGS {AGS.Types.Version.AGS_EDITOR_VERSION}, .NET {Environment.Version}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        }

        /// <summary>Set by the view: shows a file picker for Game.agf and returns the chosen path (or null).</summary>
        public Func<Task<string>> PickProjectFile { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Title), nameof(HasProject))]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(BuildCommand), nameof(DebugCommand), nameof(FindInScriptsCommand), nameof(MakeTemplateCommand), nameof(AutoNumberSpeechCommand), nameof(CreateVoiceActingScriptCommand))]
        private GameProject _project;

        public bool HasProject => Project != null;

        public string Title => Project == null ? "AGS Editor" : $"{Project.Game.Settings.GameName}{(IsModified ? " •" : "")} — AGS Editor";

        public ObservableCollection<ProjectTreeNode> TreeRoots { get; } = new ObservableCollection<ProjectTreeNode>();

        public ObservableCollection<DocumentViewModel> Documents { get; } = new ObservableCollection<DocumentViewModel>();

        /// <summary>Errors and warnings of the last build (double-click to open the script at the line).</summary>
        public ObservableCollection<CompileMessage> Errors { get; } = new ObservableCollection<CompileMessage>();

        public bool HasErrors => Errors.Count > 0;

        /// <summary>Opens the script of a compile message at its line (upstream: double-click in the error list).</summary>
        public void GoToMessage(CompileMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.ScriptName)) return;
            // Dialog scripts are compiled as sections "Dialog N" (agf2dlgasc keeps their line numbers).
            var dialogMatch = System.Text.RegularExpressions.Regex.Match(message.ScriptName, @"^Dialog (\d+)$");
            if (dialogMatch.Success)
            {
                int id = int.Parse(dialogMatch.Groups[1].Value);
                Dialog dialog = Project?.Game.RootDialogFolder.AllItemsFlat.FirstOrDefault(d => d.ID == id);
                if (dialog != null)
                {
                    OpenDialog(dialog);
                    (SelectedDocument as DialogEditorDocument)?.ShowError(message.LineNumber, message.Message);
                    return;
                }
            }
            Script script = FindScript(message.ScriptName);
            if (script == null)
            {
                AppendOutput($"[Error] Script {message.ScriptName} not found.");
                return;
            }
            OpenNode(new ProjectTreeNode(script.FileName, ProjectNodeKind.Item, script));
            (SelectedDocument as ScriptDocument)?.ShowError(message.LineNumber, message.Message);
        }

        [ObservableProperty]
        private DocumentViewModel _selectedDocument;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PropertiesTitle))]
        private ProjectTreeNode _selectedNode;

        public PropertyGridViewModel PropertyGrid { get; }

        partial void OnSelectedNodeChanged(ProjectTreeNode value)
        {
            PropertyGrid.Component = value?.Payload;
        }

        public string PropertiesTitle => SelectedNode?.Payload == null ? "Properties" : $"Properties — {SelectedNode.Name}";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Title))]
        private bool _isModified;

        [ObservableProperty]
        private string _outputText = string.Empty;

        [ObservableProperty]
        private string _statusText = "No project loaded";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(BuildCommand), nameof(RunCommand))]
        private bool _isBuilding;

        public void OpenProject(string gameFile)
        {
            try
            {
                Stopwatch watch = Stopwatch.StartNew();
                CloseProject();
                GameProject project = GameProject.Load(gameFile, _host);
                Project = project;

                RebuildTree();

                Game game = project.Game;
                string summary =
                    $"{game.Rooms.Count} rooms · {game.Characters.Count} characters · {game.RootViewFolder.AllItemsFlat.Count()} views · " +
                    $"{game.RootSpriteFolder.CountSpritesInAllSubFolders()} sprites · {game.GUIs.Count} GUIs · {game.AudioClips.Count} audio clips · " +
                    $"{game.RootScriptFolder.AllItemsFlat.Count()} script modules\n" +
                    $"Resolution {game.Settings.CustomResolution.Width}×{game.Settings.CustomResolution.Height}, {game.Settings.ColorDepth}, text encoding {game.TextEncoding.WebName}\n" +
                    $"Saved by AGS {game.SavedXmlEditorVersion ?? "?"}; project folder {project.Directory}";
                OpenDocument(new GameOverviewDocument(game, summary));

                StartScriptWatcher(project.Directory);
                AppendOutput($"Opened {gameFile} in {watch.ElapsedMilliseconds} ms");
                StatusText = $"{game.Settings.GameName} — {project.Directory}";
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Could not open {gameFile}: {ex.Message}");
                StatusText = "Opening the project failed";
            }
        }

        /// <summary>A Game.agf opened from the Finder: asks before discarding unsaved changes of the open game.</summary>
        public void OpenProjectFromFinder(string gameFile)
        {
            if (Project != null && string.Equals(Path.GetFullPath(Project.GameFilePath), Path.GetFullPath(gameFile), StringComparison.Ordinal))
                return; // already open
            if (IsModified && Confirm != null && !Confirm("Open game", "The open game has unsaved changes that will be lost. Continue?"))
                return;
            OpenProject(gameFile);
        }

        [RelayCommand]
        private async Task OpenProjectAsync()
        {
            if (PickProjectFile == null) return;
            string file = await PickProjectFile();
            if (!string.IsNullOrEmpty(file))
                OpenProject(file);
        }

        [RelayCommand(CanExecute = nameof(HasProject))]
        private void Save()
        {
            try
            {
                Project.Save();
                SaveModifiedRooms();
                IsModified = false;
                AppendOutput($"Saved {Project.GameFilePath}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Saving failed: {ex.Message}");
            }
        }

        private bool CanBuild() => HasProject && !IsBuilding;

        partial void OnIsBuildingChanged(bool value) => BuildMacAppCommand.NotifyCanExecuteChanged();

        [RelayCommand(CanExecute = nameof(CanBuild))]
        private async Task BuildAsync()
        {
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            if (toolchain == null)
            {
                AppendOutput("[Error] agsbuild not found; run scripts/build-upstream-tools.sh or set AGS4MAC_TOOLS.");
                return;
            }

            IsBuilding = true;
            StatusText = "Building…";
            try
            {
                Project.Save(); // like the upstream editor, build what is saved
                SaveModifiedRooms();
                IsModified = false;
                AppendOutput($"Building with {toolchain.AgsBuildPath}");
                string gameFile = Project.GameFilePath;
                Errors.Clear();
                OnPropertyChanged(nameof(HasErrors));
                foreach (ScriptDocument doc in Documents.OfType<ScriptDocument>())
                    doc.ClearError();
                foreach (DialogEditorDocument doc in Documents.OfType<DialogEditorDocument>())
                    doc.ShowError(0, null);
                BuildResult result = await Task.Run(() => toolchain.Build(gameFile));
                foreach (CompileMessage m in result.Messages)
                {
                    AppendOutput($"[{(m is CompileError ? "Error" : "Warning")}] {(string.IsNullOrEmpty(m.ScriptName) ? "" : $"{m.ScriptName}({m.LineNumber}): ")}{m.Message}");
                    Errors.Add(m);
                }
                OnPropertyChanged(nameof(HasErrors));
                if (Errors.FirstOrDefault(m => !string.IsNullOrEmpty(m.ScriptName)) is CompileMessage firstError)
                    GoToMessage(firstError); // like upstream, jump to the first error
                if (result.Succeeded && !HasErrors)
                {
                    _lastBuiltGame = Directory.GetFiles(result.OutputDirectory, "*.ags").FirstOrDefault();
                    AppendOutput($"Build succeeded: {result.OutputDirectory}");
                    StatusText = "Build succeeded";
                }
                else
                {
                    // Script errors are listed above; the full tool log only helps for other failures.
                    if (!Errors.Any(m => !string.IsNullOrEmpty(m.ScriptName)))
                        AppendOutput(result.Log);
                    StatusText = "Build failed";
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Build failed: {ex.Message}");
                StatusText = "Build failed";
            }
            finally
            {
                IsBuilding = false;
                RunCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanRun() => !IsBuilding && _lastBuiltGame != null;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            string engine = toolchain == null ? null : Path.Combine(toolchain.ToolsDirectory, "ags");
            if (engine == null || !File.Exists(engine))
            {
                AppendOutput("[Error] The AGS engine was not found next to agsbuild.");
                return;
            }
            var psi = new ProcessStartInfo(engine) { UseShellExecute = false };
            psi.ArgumentList.Add(_lastBuiltGame);
            Process.Start(psi);
            AppendOutput($"Running {_lastBuiltGame}");
        }

        /// <summary>Set by the view: asks for a folder (title); null when cancelled.</summary>
        public Func<string, Task<string>> PickFolder { get; set; }

        /// <summary>File → New Game from Template… (upstream Start New Game wizard).</summary>
        [RelayCommand]
        private async Task NewGameFromTemplateAsync()
        {
            if (PickOpenFiles == null || PickFolder == null) return;
            IReadOnlyList<string> templates = await PickOpenFiles("Choose a game template (.agt)", new[] { "agt" });
            string template = templates?.FirstOrDefault();
            if (template == null) return;
            string parent = await PickFolder("Choose the folder to create the game in");
            if (parent == null) return;
            string name = PromptText?.Invoke("New game — name", Path.GetFileNameWithoutExtension(template)) ?? Path.GetFileNameWithoutExtension(template);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            string fileName = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            if (fileName.Length == 0) fileName = "game";
            string target = Path.Combine(parent, name);
            if (IsModified && Confirm != null && !Confirm("New game", "The open game has unsaved changes that will be lost. Continue?"))
                return;
            try
            {
                StatusText = "Creating the game…";
                CloseProject();
                GameTemplates.CreateNewGame(template, target, fileName, name, _host);
                OpenProject(Path.Combine(target, GameProject.GAME_FILE_NAME));
                AppendOutput($"Created {name} in {target} from {Path.GetFileName(template)}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] There was an error creating your game: {ex.Message}");
                StatusText = "Ready";
            }
        }

        /// <summary>Build → Build macOS App: builds the game, then packages it with the engine as a .app.</summary>
        [RelayCommand(CanExecute = nameof(CanBuild))]
        private async Task BuildMacAppAsync()
        {
            await BuildCommand.ExecuteAsync(null);
            if (Errors.OfType<CompileError>().Any() || Project == null) return;
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            if (toolchain == null) return;
            try
            {
                StatusText = "Packaging the macOS app…";
                GameProject project = Project;
                string app = await Task.Run(() => MacAppPackager.Package(project, toolchain));
                LastMacApp = app;
                AppendOutput($"macOS app created: {app}");
                StatusText = "macOS app created";
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Packaging the macOS app failed: {ex.Message}");
                StatusText = "Ready";
            }
        }

        /// <summary>The .app created by the last "Build macOS App".</summary>
        public string LastMacApp { get; private set; }

        /// <summary>File → Make Template from This Game… (upstream CreateTemplateFromCurrentGame).</summary>
        [RelayCommand(CanExecute = nameof(HasProject))]
        private async Task MakeTemplateAsync()
        {
            if (PickSaveFile == null) return;
            string file = await PickSaveFile(Project.Game.Settings.GameName + ".agt", new[] { "agt" });
            if (string.IsNullOrEmpty(file)) return;
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            if (toolchain == null)
            {
                AppendOutput("[Error] agspak not found; run scripts/build-upstream-tools.sh or set AGS4MAC_TOOLS.");
                return;
            }
            try
            {
                Project.Save();
                SaveModifiedRooms();
                IsModified = false;
                var files = GameTemplates.MakeTemplate(Project, file, toolchain);
                AppendOutput($"Template created: {file} ({files.Count} files)");
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] {ex.Message}");
            }
        }

        // ---- debugger (upstream DebugController + Tasks.TestGame(withDebugger)) ----

        private EngineDebugger _debugger;
        private ScriptDocument _executionDocument;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StepIntoCommand), nameof(PauseDebuggingCommand), nameof(StopDebuggingCommand))]
        private DebugSessionState _debugState = DebugSessionState.NotRunning;

        public bool IsDebugging => DebugState != DebugSessionState.NotRunning;
        public bool IsPaused => DebugState == DebugSessionState.Paused;

        partial void OnDebugStateChanged(DebugSessionState value)
        {
            OnPropertyChanged(nameof(IsDebugging));
            OnPropertyChanged(nameof(IsPaused));
            StatusText = value switch
            {
                DebugSessionState.Running => "Debugging: running (F5 continue, F11 step, ⇧F5 stop)",
                DebugSessionState.Paused => "Debugging: paused",
                _ => "Ready",
            };
        }

        /// <summary>Breakpoints of all scripts (modules and loaded room scripts) as (script file, line).</summary>
        private IEnumerable<(string, int)> AllBreakpoints()
        {
            foreach (ScriptAndHeader module in Project.Game.RootScriptFolder.AllItemsFlat)
                foreach (int line in module.Script.BreakpointedLines ?? Array.Empty<int>())
                    yield return (module.Script.FileName, line);
            foreach (IRoom room in Project.Game.RootRoomFolder.AllItemsFlat)
                if (room is UnloadedRoom unloaded && unloaded.Script?.BreakpointedLines != null)
                    foreach (int line in unloaded.Script.BreakpointedLines)
                        yield return (unloaded.Script.FileName, line);
        }

        private void OnBreakpointToggled(ScriptDocument document, int line, bool added)
        {
            if (_debugger == null) return;
            if (added) _debugger.AddBreakpoint(document.Script.FileName, line);
            else _debugger.RemoveBreakpoint(document.Script.FileName, line);
        }

        /// <summary>F5: continue when paused, otherwise build and run the game with the debugger.</summary>
        [RelayCommand(CanExecute = nameof(HasProject))]
        private async Task DebugAsync()
        {
            if (DebugState == DebugSessionState.Paused)
            {
                ClearExecutionLine();
                _debugger.Resume();
                return;
            }
            if (DebugState == DebugSessionState.Running) return;
            await BuildCommand.ExecuteAsync(null);
            if (Errors.OfType<CompileError>().Any() || _lastBuiltGame == null) return;
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            string engine = toolchain == null ? null : Path.Combine(toolchain.ToolsDirectory, "ags");
            if (engine == null || !File.Exists(engine))
            {
                AppendOutput("[Error] The AGS engine was not found next to agsbuild.");
                return;
            }
            _debugger?.Dispose();
            _debugger = new EngineDebugger(engine, _lastBuiltGame);
            _debugger.StateChanged += state => Dispatcher.UIThread.Post(() => DebugState = state);
            _debugger.Log += line => Dispatcher.UIThread.Post(() => AppendOutput("[Engine] " + line));
            _debugger.BreakAtLocation += stack => Dispatcher.UIThread.Post(() => OnBreak(stack));
            _debugger.Exited += () => Dispatcher.UIThread.Post(() =>
            {
                ClearExecutionLine();
                AppendOutput("The game has exited.");
            });
            try
            {
                _debugger.Start(AllBreakpoints());
                AppendOutput($"Debugging {_lastBuiltGame}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] The game could not be started with the debugger: {ex.Message}");
            }
        }

        /// <summary>BreakAtLocation: show the script line, the call stack and an error message.</summary>
        private void OnBreak(DebugCallStack stack)
        {
            ClearExecutionLine();
            if (stack.ErrorMessage != null)
                AppendOutput("[Error] " + stack.ErrorMessage);
            AppendOutput("Call stack:" + string.Concat(stack.Lines.Select(l => $"\n    {l.ScriptName}, line {l.LineNumber}")));
            CallStackLine top = stack.Lines.FirstOrDefault();
            if (top == null) return;
            Script script = FindScript(top.ScriptName);
            if (script == null)
            {
                AppendOutput($"[Warning] Script {top.ScriptName} not found.");
                return;
            }
            OpenScriptAt(script, 0, 0);
            if (SelectedDocument is ScriptDocument doc)
            {
                _executionDocument = doc;
                doc.SetExecutionLine(top.LineNumber);
            }
            BringToFront?.Invoke();
        }

        private void ClearExecutionLine()
        {
            _executionDocument?.SetExecutionLine(null);
            _executionDocument = null;
        }

        /// <summary>Set by the view: restores the default arrangement of the dockable panels.</summary>
        public Action ResetLayout { get; set; }

        [RelayCommand]
        private void ResetPanelLayout() => ResetLayout?.Invoke();

        /// <summary>Set by the view: brings the editor window to the front (the engine window had the focus).</summary>
        public Action BringToFront { get; set; }

        [RelayCommand(CanExecute = nameof(IsPaused))]
        private void StepInto()
        {
            ClearExecutionLine();
            _debugger?.StepInto();
        }

        private bool IsRunning() => DebugState == DebugSessionState.Running;

        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void PauseDebugging() => _debugger?.Pause();

        [RelayCommand(CanExecute = nameof(IsDebugging))]
        private void StopDebugging()
        {
            ClearExecutionLine();
            _debugger?.Stop();
        }

        /// <summary>Opens a search result: a script at the match, or a dialog's script at the line.</summary>
        private void OpenSearchResult(ScriptSearchResult result, int length)
        {
            if (result.Dialog != null)
            {
                OpenDialog(result.Dialog);
                (SelectedDocument as DialogEditorDocument)?.GoToLine(result.Line);
                return;
            }
            OpenScriptAt(result.Script, result.Offset, length);
        }

        /// <summary>Opens a script (module or room script) and selects a range.</summary>
        public void OpenScriptAt(Script script, int offset, int length)
        {
            if (script == null) return;
            OpenNode(new ProjectTreeNode(script.FileName, ProjectNodeKind.Item, script));
            (SelectedDocument as ScriptDocument)?.Select(offset, length);
        }

        /// <summary>F12 / ⌘-click in a script: upstream GoToDefinition.</summary>
        private void GoToDefinition(ScriptDocument document, string text, int offset)
        {
            ScriptCompletion completion = document.Completion ?? (_completion ??= new ScriptCompletion(Project.Game));
            ScriptCompletion.Definition def = completion.FindDefinition(document.Script, text, offset);
            if (def == null)
            {
                StatusText = "No definition found";
                return;
            }
            if (def.IsBuiltIn)
            {
                AppendOutput($"{def.Symbol} is defined by AGS ({def.Script.FileName}).");
                StatusText = $"{def.Symbol}: built-in";
                return;
            }
            string name = def.Symbol.Contains('.') ? def.Symbol.Substring(def.Symbol.LastIndexOf('.') + 1) : def.Symbol;
            OpenScriptAt(def.Script, def.Offset, name.Length);
            StatusText = $"{def.Symbol} — {def.Script.FileName}";
        }

        /// <summary>
        /// Upstream FindAllUsages: no usage → message, one → jump there, several → the result list.
        /// </summary>
        public void FindAllUsages(string token) => FindAllUsages(token, null);

        /// <summary>From the script editor: a local variable is only searched in its function (FindAllUsages.GetScriptsToSearch).</summary>
        private void FindAllUsagesInScript(ScriptDocument document, string text, int offset, string token) =>
            FindAllUsages(token, ScriptSearch.FindLocalUsages(document.Script, text, offset, token));

        private void FindAllUsages(string token, IReadOnlyList<ScriptSearchResult> localResults)
        {
            if (Project == null || string.IsNullOrWhiteSpace(token)) return;
            IReadOnlyList<ScriptSearchResult> results = localResults ?? ScriptSearch.FindUsages(ScriptSearch.AllScripts(Project.Game), token)
                .Concat(ScriptSearch.FindUsagesInDialogs(Project.Game, token)).ToList();
            if (results.Count == 0)
            {
                AppendOutput($"No usages of {token} were found!");
                StatusText = $"No usages of {token} were found";
                return;
            }
            if (results.Count == 1)
            {
                OpenSearchResult(results[0], token.Length);
                StatusText = $"This is the only usage of {token}";
                return;
            }
            FindInScripts();
            if (SelectedDocument is FindInScriptsDocument find)
                find.ShowUsages(token, results);
            StatusText = $"{results.Count} usages of {token}";
        }

        [RelayCommand(CanExecute = nameof(HasProject))]
        private void FindInScripts()
        {
            DocumentViewModel open = Documents.FirstOrDefault(d => d is FindInScriptsDocument);
            if (open != null)
            {
                SelectedDocument = open;
                return;
            }
            var doc = new FindInScriptsDocument(Project.Game)
            {
                Confirm = question => Confirm == null || Confirm("Replace all", question),
            };
            doc.Edited += () =>
            {
                IsModified = true;
                StatusText = "Scripts changed by Replace All (unsaved)";
                ReloadOpenScripts(); // open script editors show the new text
                SelectedDocument = Documents.FirstOrDefault(d => d is FindInScriptsDocument);
            };
            doc.OpenResult += r => OpenSearchResult(r, doc.LastSearchLength);
            OpenDocument(doc);
        }

        /// <summary>Opens the document for a tree node (double-click).</summary>
        public void OpenNode(ProjectTreeNode node)
        {
            switch (node?.Payload)
            {
                case Script script:
                    _scriptHighlighting ??= AgsScriptHighlighting.Create(AgsScriptHighlighting.CollectTypeNames(Project?.Game));
                    _completion ??= new ScriptCompletion(Project.Game);
                    var scriptDoc = new ScriptDocument(script, _scriptHighlighting, () =>
                    {
                        IsModified = true;
                        StatusText = $"Edited {script.FileName} (unsaved)";
                    }, _completion);
                    scriptDoc.DefinitionRequested += GoToDefinition;
                    scriptDoc.UsagesRequested += FindAllUsagesInScript;
                    scriptDoc.BreakpointToggled += OnBreakpointToggled;
                    OpenDocument(scriptDoc);
                    break;
                case UnloadedRoom room:
                    OpenRoom(room);
                    break;
                case ISpriteFolder folder:
                    OpenSpriteFolder(folder);
                    break;
                case GUI gui:
                    OpenGui(gui);
                    break;
                case View view:
                    OpenView(view);
                    break;
                case Dialog dialog:
                    OpenDialog(dialog);
                    break;
                case GlobalVariables _:
                    OpenGlobalVariables();
                    break;
                case TextParser parser:
                    OpenSimple(parser, () => new TextParserDocument(parser, item => PropertyGrid.Component = item));
                    break;
                case LipSync lipSync:
                    OpenSimple(lipSync, () => new LipSyncDocument(lipSync));
                    PropertyGrid.Component = lipSync;
                    break;
                case PaletteEntry[] palette:
                    OpenSimple(palette, () => new PaletteDocument(Project.Game, item => PropertyGrid.Component = item));
                    break;
                case AGS.Types.Font font:
                    if (EnsureGuiRenderer())
                        OpenDocument(new FontDocument(font));
                    PropertyGrid.Component = font;
                    break;
            }
        }

        /// <summary>Set by the app: sprite chooser (current sprite → chosen sprite or null).</summary>
        public Func<int?, int?> PickSprite { get; set; }

        /// <summary>Set by the app: the global variable dialog.</summary>
        public Func<GlobalVariable, bool> EditGlobalVariable { get; set; }

        private void OpenGlobalVariables()
        {
            DocumentViewModel open = Documents.FirstOrDefault(d => d is GlobalVariablesDocument);
            if (open != null)
            {
                SelectedDocument = open;
                return;
            }
            var doc = new GlobalVariablesDocument(Project.Game)
            {
                EditVariable = v => EditGlobalVariable != null && EditGlobalVariable(v),
                Confirm = question => Confirm == null || Confirm("Global variables", question),
            };
            doc.Edited += () =>
            {
                IsModified = true;
                StatusText = "Global variables changed (unsaved)";
                _completion?.RefreshGeneratedHeader();
            };
            OpenDocument(doc);
        }

        private void OpenSimple(object key, Func<DocumentViewModel> create)
        {
            DocumentViewModel open = Documents.FirstOrDefault(d => ReferenceEquals(d.Key, key));
            if (open != null)
            {
                SelectedDocument = open;
                return;
            }
            DocumentViewModel doc = create();
            Action edited = () => { IsModified = true; StatusText = $"{doc.Title} changed (unsaved)"; };
            if (doc is TextParserDocument parserDoc) parserDoc.Edited += edited;
            if (doc is LipSyncDocument lipDoc) lipDoc.Edited += edited;
            OpenDocument(doc);
        }

        /// <summary>Text parser toolbar (Add new word / Add synonym / Delete word).</summary>
        public void TextParserAction(string action)
        {
            if (SelectedDocument is not TextParserDocument doc) return;
            switch (action)
            {
                case "add": doc.AddWord(PromptText?.Invoke("Add new word", "") ?? ""); break;
                case "synonym": doc.AddSynonym(PromptText?.Invoke("Add synonym", "") ?? ""); break;
                case "delete":
                    if (doc.CanDelete(doc.Selected)) doc.DeleteSelected();
                    else AppendOutput("[Warning] The last word of this special word group can't be deleted.");
                    break;
            }
        }

        [RelayCommand]
        private void GlobalVariableAction(string action)
        {
            if (SelectedDocument is not GlobalVariablesDocument doc) return;
            switch (action)
            {
                case "add": doc.Add(); break;
                case "edit": doc.Edit(); break;
                case "delete": doc.Delete(); break;
            }
        }

        private void OpenDialog(Dialog dialog)
        {
            DocumentViewModel open = Documents.FirstOrDefault(d => ReferenceEquals(d.Key, dialog));
            if (open != null)
            {
                SelectedDocument = open;
                return;
            }
            _scriptHighlighting ??= AgsScriptHighlighting.Create(AgsScriptHighlighting.CollectTypeNames(Project?.Game));
            var doc = new DialogEditorDocument(dialog, _scriptHighlighting)
            {
                Confirm = question => Confirm == null || Confirm("Dialog", question),
            };
            doc.Edited += () =>
            {
                IsModified = true;
                StatusText = $"Dialog {dialog.Name} changed (unsaved)";
            };
            OpenDocument(doc);
            PropertyGrid.Component = dialog;
        }

        private void OpenView(View view)
        {
            DocumentViewModel open = Documents.FirstOrDefault(d => ReferenceEquals(d.Key, view));
            if (open != null)
            {
                SelectedDocument = open;
                return;
            }
            var doc = new ViewEditorDocument(view, Project.Game, Sprites, item => PropertyGrid.Component = item)
            {
                PickSprite = current => PickSprite?.Invoke(current),
            };
            doc.Edited += () =>
            {
                IsModified = true;
                StatusText = "View changed (unsaved)";
            };
            OpenDocument(doc);
            PropertyGrid.Component = view;
        }

        private bool _guiRendererReady;

        /// <summary>Fonts were added, deleted or replaced: reload them for previews.</summary>
        private void FontsChanged()
        {
            _guiRendererReady = false;
            if (!Documents.Any(d => d is FontDocument || d is GuiEditorDocument)) return;
            bool ready = EnsureGuiRenderer();
            foreach (GuiEditorDocument d in Documents.OfType<GuiEditorDocument>())
            {
                d.UseNativeRendering = ready;
                d.Refresh();
            }
            foreach (FontDocument d in Documents.OfType<FontDocument>())
                d.Refresh();
        }

        /// <summary>Prepares upstream's GUI drawing (fonts, sprites) for the open game, once.</summary>
        private bool EnsureGuiRenderer()
        {
            if (_guiRendererReady) return true;
            try
            {
                NativeGuiRenderer.Init(Project.Game, Project.Directory);
                _guiRendererReady = true;
            }
            catch (Exception ex)
            {
                AppendOutput($"[Warning] GUI preview with AGS fonts unavailable: {ex.Message}");
            }
            return _guiRendererReady;
        }

        private void OpenGui(GUI gui)
        {
            DocumentViewModel open = Documents.FirstOrDefault(d => ReferenceEquals(d.Key, gui));
            if (open != null)
            {
                SelectedDocument = open;
                return;
            }
            var doc = new GuiEditorDocument(gui, Project.Game, Sprites, item => PropertyGrid.Component = item)
            {
                UseNativeRendering = EnsureGuiRenderer(),
            };
            doc.Edited += () =>
            {
                IsModified = true;
                StatusText = "GUI changed (unsaved)";
                PropertyGrid.Rebuild(); // positions, sizes, IDs
            };
            OpenDocument(doc);
            PropertyGrid.Component = gui;
        }

        /// <summary>Set by the app: asks the user a yes/no question (modal). Defaults to "yes" when unset (tests).</summary>
        public Func<string, string, bool> Confirm { get; set; }

        /// <summary>Set by the app: asks for a line of text (title, current value); returns null when cancelled.</summary>
        public Func<string, string, string> PromptText { get; set; }

        /// <summary>
        /// Rebuilds the project tree from the game, keeping expanded nodes and the selection, and attaches
        /// the context-menu actions (ported from the upstream components' context menus).
        /// </summary>
        public void RebuildTree(object select = null)
        {
            if (Project == null) return;
            var expanded = new HashSet<string>(AllNodes().Where(n => n.IsExpanded).Select(n => n.Path));
            object selected = select ?? SelectedNode?.Payload;
            bool firstBuild = TreeRoots.Count == 0;

            ProjectTreeNode root = ProjectTreeBuilder.Build(Project.Game);
            TreeRoots.Clear();
            TreeRoots.Add(root);

            foreach (ProjectTreeNode node in AllNodes())
            {
                if (!firstBuild)
                    node.IsExpanded = expanded.Contains(node.Path);
                AddActions(node);
            }
            ProjectTreeNode toSelect = selected == null ? null : AllNodes().FirstOrDefault(n => ReferenceEquals(n.Payload, selected));
            if (toSelect != null)
            {
                for (ProjectTreeNode p = toSelect.Parent; p != null; p = p.Parent)
                    p.IsExpanded = true;
            }
            SelectedNode = toSelect;
        }

        private IEnumerable<ProjectTreeNode> AllNodes()
        {
            var stack = new Stack<ProjectTreeNode>(TreeRoots);
            while (stack.Count > 0)
            {
                ProjectTreeNode n = stack.Pop();
                yield return n;
                foreach (ProjectTreeNode c in n.Children) stack.Push(c);
            }
        }

        private bool Ask(string question) => Confirm == null || Confirm("Confirm delete", question);

        private void AddActions(ProjectTreeNode node)
        {
            Game game = Project.Game;
            if (node.Kind == ProjectNodeKind.Section && node.Name == "Fonts")
                node.AddAction("New Font", () => { Change(() => FontOperations.AddFont(Project)); FontsChanged(); });
            if (node.Kind == ProjectNodeKind.Section && node.Name == "Translations")
            {
                node.AddAction("Update all", () => UpdateTranslations(game.Translations.ToList()));
                node.AddAction("New translation", () =>
                {
                    string name = PromptText == null ? TranslationUpdater.GetFreeNameForTranslation(game)
                        : PromptText("New translation — name (file name without .trs)", TranslationUpdater.GetFreeNameForTranslation(game));
                    if (string.IsNullOrWhiteSpace(name)) return;
                    Translation created = null;
                    Change(() =>
                    {
                        created = TranslationUpdater.CreateTranslation(Project);
                        TranslationUpdater.RenameTranslation(Project, created, name.Trim());
                        AppendOutput($"Created translation {created.FileName}");
                        return created;
                    });
                    if (created != null && Ask("Would you like to update the new translation file with all the latest game messages? This could take some time, and your game will be saved first. You can do this later yourself by choosing 'Update' on the context menu?"))
                        UpdateTranslations(new List<Translation> { created });
                });
            }
            if (node.Payload is Translation translation)
            {
                node.AddAction("Update", () => UpdateTranslations(new List<Translation> { translation }));
                node.AddAction("Make default language", () =>
                {
                    if (!Ask("This command will replace all the text in your game with the translations in this file. This means that your current default language will be lost in the process.\n\nAdditionally, all your translations will be updated to contain this translation as the source text.\n\nAre you really sure you want to continue?"))
                        return;
                    RunGameTextChange("Game text replaced with the translation text.",
                        () => TranslationUpdater.ReplaceGameTextWithTranslation(Project, translation));
                });
                node.AddAction("Rename...", () =>
                {
                    string name = PromptText?.Invoke("Rename translation", translation.Name);
                    if (!string.IsNullOrWhiteSpace(name))
                        Change(() => { TranslationUpdater.RenameTranslation(Project, translation, name.Trim()); return translation; });
                });
                node.AddAction("Delete", () =>
                {
                    if (Ask("Are you sure you want to delete this translation?"))
                        Change(() => { TranslationUpdater.DeleteTranslation(Project, translation); return null; });
                });
            }
            if (node.Kind == ProjectNodeKind.Section && node.Name == "Mouse Cursors")
                node.AddAction("New Cursor", () => Change(() => ProjectOperations.AddCursor(game)));
            if (node.Payload is MouseCursor cursor && cursor.ID >= ProjectOperations.BuiltInCursors)
                node.AddAction("Delete this cursor", () =>
                {
                    if (Ask("Are you sure you want to delete this cursor? Doing so will break any scripts that refer to cursors by their number."))
                        Change(() => { ProjectOperations.DeleteCursor(game, cursor); return null; });
                });
            switch (node.Folder ?? node.Payload)
            {
                case UnloadedRoomFolder folder:
                    node.AddAction("New room", () => Change(() =>
                    {
                        int suggested = RoomOperations.SuggestRoomNumber(game);
                        string answer = PromptText == null ? suggested.ToString() : PromptText("New room (Blank Room) — room number", suggested.ToString());
                        if (string.IsNullOrWhiteSpace(answer)) return null;
                        if (!int.TryParse(answer.Trim(), out int number) || number < UnloadedRoom.FIRST_ROOM_NUMBER || number > UnloadedRoom.HIGHEST_ROOM_NUMBER_ALLOWED)
                            throw new AGSEditorException($"Room numbers must be between {UnloadedRoom.FIRST_ROOM_NUMBER} and {UnloadedRoom.HIGHEST_ROOM_NUMBER_ALLOWED}.");
                        UnloadedRoom created = RoomOperations.CreateBlankRoom(Project, number, folder,
                            question => Confirm == null || Confirm("New room", question));
                        if (created != null)
                            AppendOutput($"Created room {created.Number} ({created.FileName}, {created.ScriptFileName})");
                        return created;
                    }));
                    node.AddAction("New room from template...", async () =>
                    {
                        if (PickOpenFiles == null) return;
                        IReadOnlyList<string> files;
                        try { files = await PickOpenFiles("Choose a room template (.art)", new[] { "art" }); }
                        catch (Exception ex) { AppendOutput($"[Error] {ex.Message}"); return; }
                        string file = files?.FirstOrDefault();
                        if (file == null) return;
                        int suggested;
                        try { suggested = RoomOperations.SuggestRoomNumber(game); }
                        catch (Exception ex) { AppendOutput($"[Error] {ex.Message}"); return; }
                        string answer = PromptText == null ? suggested.ToString() : PromptText("New room from template — room number", suggested.ToString());
                        if (string.IsNullOrWhiteSpace(answer) || !int.TryParse(answer.Trim(), out int number)) return;
                        Change(() =>
                        {
                            UnloadedRoom created = GameTemplates.CreateRoomFromTemplate(Project, file, number, folder);
                            AppendOutput($"Created room {created.Number} from {Path.GetFileName(file)}");
                            return created;
                        });
                    });
                    node.AddAction("Import existing room...", async () =>
                    {
                        if (PickOpenFiles == null) return;
                        IReadOnlyList<string> files;
                        try { files = await PickOpenFiles("Choose room to import...", new[] { "crm" }); }
                        catch (Exception ex) { AppendOutput($"[Error] {ex.Message}"); return; }
                        string file = files?.FirstOrDefault();
                        if (file == null) return;
                        Change(() =>
                        {
                            UnloadedRoom imported = RoomOperations.ImportRoomFile(Project, file, folder);
                            AppendOutput("Room file imported successfully as room " + imported.Number + ".");
                            return imported;
                        });
                    });
                    break;
                case AGS.Types.Font font:
                    node.AddAction("Import over this font...", async () =>
                    {
                        if (PickOpenFiles == null) return;
                        IReadOnlyList<string> files;
                        try { files = await PickOpenFiles("Select font to import...", new[] { "ttf", "wfn", "*" }); } // "*": SCI fonts are named font.*
                        catch (Exception ex) { AppendOutput($"[Error] {ex.Message}"); return; }
                        string file = files?.FirstOrDefault();
                        if (file == null) return;
                        ImportOverFont(font, file);
                        if (PropertyGrid.Component == font)
                        {
                            PropertyGrid.Component = null;
                            PropertyGrid.Component = font;
                        }
                    });
                    if (font.ID >= FontOperations.BuiltInFonts)
                        node.AddAction("Delete this font", () =>
                        {
                            if (Ask("Are you sure you want to delete this font? Doing so could break any scripts that refer to fonts by number."))
                            {
                                foreach (FontDocument d in Documents.OfType<FontDocument>().Where(d => d.Font == font).ToList())
                                    Documents.Remove(d);
                                Change(() => { FontOperations.DeleteFont(Project, font); return null; });
                                FontsChanged();
                            }
                        });
                    break;
                case AudioClipFolder folder:
                    node.AddAction("Add audio file(s)...", async () =>
                    {
                        if (PickOpenFiles == null) return;
                        IReadOnlyList<string> files;
                        try { files = await PickOpenFiles("Select audio to add", AudioOperations.SupportedExtensions); }
                        catch (Exception ex) { AppendOutput($"[Error] {ex.Message}"); return; }
                        if (files == null || files.Count == 0) return;
                        Change(() =>
                        {
                            IReadOnlyList<AudioClip> clips = AudioOperations.ImportAudioFiles(Project, files, folder, w => AppendOutput("[Warning] " + w));
                            foreach (AudioClip clip in clips)
                                AppendOutput($"Added audio clip {clip.ScriptName} ({clip.SourceFileName})");
                            return clips.LastOrDefault();
                        });
                    });
                    break;
                case UnloadedRoom room:
                    node.AddAction("Delete room", () =>
                    {
                        if (!Ask("Are you sure you want to delete this room? It cannot be recovered.")) return;
                        Change(() =>
                        {
                            foreach (DocumentViewModel doc in Documents.Where(d => Equals(d.Key, room) ||
                                (d is ScriptDocument sd && string.Equals(sd.Script.FileName, room.ScriptFileName, StringComparison.OrdinalIgnoreCase))).ToList())
                            {
                                Documents.Remove(doc);
                                (doc as IDisposable)?.Dispose();
                            }
                            if (RoomOperations.DeleteRoom(Project, room, question => Confirm == null || Confirm("Delete room", question),
                                warning => AppendOutput("[Warning] " + warning)))
                                AppendOutput($"Deleted room {room.Number}");
                            return null;
                        });
                    });
                    break;
                case SpriteFolder spriteFolder:
                    node.AddAction("Create sub-folder", () =>
                    {
                        string name = PromptText?.Invoke("New sprite folder", "New folder");
                        if (string.IsNullOrWhiteSpace(name)) return;
                        var created = new SpriteFolder(name.Trim());
                        spriteFolder.SubFolders.Add(created);
                        IsModified = true;
                        RebuildTree(created);
                    });
                    if (spriteFolder != game.RootSpriteFolder)
                    {
                        node.AddAction("Rename", () =>
                        {
                            string name = PromptText?.Invoke("Rename sprite folder", spriteFolder.Name);
                            if (string.IsNullOrWhiteSpace(name)) return;
                            spriteFolder.Name = name.Trim();
                            IsModified = true;
                            RebuildTree(spriteFolder);
                        });
                        node.AddAction("Delete", () =>
                        {
                            if (!Ask("Are you sure you want to delete the folder '" + spriteFolder.Name + "' and all its subfolders?")) return;
                            try
                            {
                                RetireSpriteFile();
                                SpriteOperations.DeleteSpriteFolder(game, Project.Directory, spriteFolder);
                                foreach (DocumentViewModel d in Documents.Where(d => d is SpriteFolderDocument sd && (sd.Folder == spriteFolder || IsInside(sd.Folder, spriteFolder))).ToList())
                                    Documents.Remove(d);
                                AppendOutput($"Deleted sprite folder {spriteFolder.Name}");
                                SpriteFileReplaced(null);
                            }
                            catch (Exception ex)
                            {
                                EditorMessages.ShowWarning(ex.Message, "Delete folder");
                            }
                        });
                    }
                    node.AddAction("Find sprite by number...", FindSpriteByNumber);
                    if (spriteFolder == game.RootSpriteFolder)
                    {
                        node.AddAction("Replace source paths for sprites...", () => ReplaceSpriteSourcePaths());
                        node.AddAction("Restore all sprites from sources", async () => await RestoreAllSpritesFromSourcesAsync());
                        node.AddAction("Create source files for all sprites with missing / external sources...", CreateMissingSpriteSources);
                    }
                    break;
                case CharacterFolder folder:
                    node.AddAction("New character", () => Change(() => ProjectOperations.AddCharacter(game, folder)));
                    node.AddAction("New folder", () => Change(() => ProjectOperations.AddFolder<Character, CharacterFolder>(folder, n => new CharacterFolder(n))));
                    if (node.Kind == ProjectNodeKind.Folder)
                        node.AddAction("Delete folder", () =>
                        {
                            bool empty = folder.Items.Count == 0 && folder.SubFolders.Count == 0;
                            if (empty || Ask("Are you sure you want to delete this folder and all its characters?" + Environment.NewLine + Environment.NewLine + "If any of the characters are referenced in code by their number it could cause crashes in the game."))
                                Change(() => { ProjectOperations.DeleteFolderWithContents<Character, CharacterFolder>(game.RootCharacterFolder, folder, c => { ProjectOperations.DeleteCharacter(game, c); return true; }); return null; });
                        });
                    break;
                case Character character:
                    node.AddAction("Find All Usages of " + character.ScriptName, () => FindAllUsages(character.ScriptName));
                    node.AddAction("Delete character", () =>
                    {
                        if (Ask("Are you sure you want to delete this character? Doing so could break any scripts that refer to characters by number."))
                            Change(() => { ProjectOperations.DeleteCharacter(game, character); return null; });
                    });
                    break;
                case InventoryItemFolder folder:
                    node.AddAction("New inventory item", () => Change(() => ProjectOperations.AddInventoryItem(game, folder)));
                    node.AddAction("New folder", () => Change(() => ProjectOperations.AddFolder<InventoryItem, InventoryItemFolder>(folder, n => new InventoryItemFolder(n))));
                    if (node.Kind == ProjectNodeKind.Folder)
                        node.AddAction("Delete folder", () =>
                        {
                            bool empty = folder.Items.Count == 0 && folder.SubFolders.Count == 0;
                            if (empty || Ask("Are you sure you want to delete this folder and all its inventory items?" + Environment.NewLine + Environment.NewLine + "If any of the items are referenced in code by their number it could cause crashes in the game."))
                                Change(() => { ProjectOperations.DeleteFolderWithContents<InventoryItem, InventoryItemFolder>(game.RootInventoryItemFolder, folder, i => { ProjectOperations.DeleteInventoryItem(game, i); return true; }); return null; });
                        });
                    break;
                case ViewFolder folder:
                    node.AddAction("New view", () => Change(() => ProjectOperations.AddView(game, folder)));
                    AddFolderActions<View, ViewFolder>(node, folder, game.RootViewFolder, n => new ViewFolder(n),
                        v => { ProjectOperations.DeleteView(game, v); return true; },
                        "Are you sure you want to delete this folder and all its views?" + Environment.NewLine + Environment.NewLine + "If any of the views are used as animations anywhere it could cause crashes in the game.",
                        // ViewsComponent.CanFolderBeDeleted: no view in the folder tree may be in use
                        () => folder.AllItemsFlat.Select(v => ProjectOperations.GetViewUsageReport(game, v.ID)).FirstOrDefault(r => r != null));
                    break;
                case View view:
                    node.AddAction("Find All Usages of " + view.Name, () => FindAllUsages(view.Name));
                    node.AddAction("Delete view", () =>
                    {
                        if (Ask("Are you sure you want to delete view '" + view.Name + "'?\n\nIf it is used as an animation anywhere it could cause crashes in the game."))
                            Change(() => { ProjectOperations.DeleteView(game, view); return null; });
                    });
                    break;
                case DialogFolder folder:
                    node.AddAction("New dialog", () => Change(() => ProjectOperations.AddDialog(game, folder)));
                    AddFolderActions<Dialog, DialogFolder>(node, folder, game.RootDialogFolder, n => new DialogFolder(n),
                        d => { ProjectOperations.DeleteDialog(game, d); return true; },
                        "Are you sure you want to delete this folder and all its dialogs?" + Environment.NewLine + Environment.NewLine + "If any of the dialogs are referenced in code by their number it could cause crashes in the game.");
                    break;
                case Dialog dialog:
                    node.AddAction("Find All Usages of " + dialog.Name, () => FindAllUsages(dialog.Name));
                    node.AddAction("Delete dialog", () =>
                    {
                        if (Ask("Are you sure you want to delete this dialog? Doing so will break any scripts that refer to dialogs by their number."))
                            Change(() => { ProjectOperations.DeleteDialog(game, dialog); return null; });
                    });
                    break;
                case GUIFolder folder:
                    node.AddAction("New GUI", () => Change(() => ProjectOperations.AddGui(game, false, folder)));
                    node.AddAction("New text window GUI", () => Change(() => ProjectOperations.AddGui(game, true, folder)));
                    AddFolderActions<GUI, GUIFolder>(node, folder, game.RootGUIFolder, n => new GUIFolder(n),
                        g => { ProjectOperations.DeleteGui(game, g); return true; },
                        "Are you sure you want to delete this folder and all its GUIs?" + Environment.NewLine + Environment.NewLine + "If any of the GUIs are referenced in code by their number it could cause crashes in the game.");
                    break;
                case GUI gui:
                    node.AddAction("Find All Usages of " + gui.Name, () => FindAllUsages(gui.Name));
                    node.AddAction("Delete GUI", () =>
                    {
                        if (Ask("Are you sure you want to delete this GUI? Doing so could break any scripts that refer to GUIs by number."))
                            Change(() => { ProjectOperations.DeleteGui(game, gui); return null; });
                    });
                    break;
                case ScriptFolder folder:
                    node.AddAction("New script", () => Change(() => ProjectOperations.AddScriptModule(game, folder)));
                    break;
                case ScriptAndHeader module when !ProjectOperations.IsGlobalScript(module):
                    node.AddAction("Rename…", () =>
                    {
                        string name = PromptText?.Invoke("Rename script module", module.Name);
                        if (name != null)
                            Change(() => { ProjectOperations.RenameScriptModule(game, module, name); CloseDocumentsFor(module); return module; });
                    });
                    node.AddAction("Delete", () =>
                    {
                        if (Ask("Are you sure you want to delete this script and its header?"))
                            Change(() => { CloseDocumentsFor(module); ProjectOperations.DeleteScriptModule(game, module); return null; });
                    });
                    break;
                case InventoryItem item:
                    node.AddAction("Find All Usages of " + item.Name, () => FindAllUsages(item.Name));
                    node.AddAction("Delete inventory item", () =>
                    {
                        if (Ask("Are you sure you want to delete this inventory item? Doing so could break any scripts that refer to inventory items by number."))
                            Change(() => { ProjectOperations.DeleteInventoryItem(game, item); return null; });
                    });
                    break;
            }
        }

        /// <summary>TranslationsComponent.UpdateTranslations: saves the game, then merges all game texts in.</summary>
        private void UpdateTranslations(List<Translation> translations)
        {
            if (translations.Count == 0) return;
            if (!Ask("Updating the translation can take some time depending on the size of your game, and will save your game beforehand. Do you want to continue?"))
                return;
            try
            {
                Project.Save();
                SaveModifiedRooms();
                IsModified = false;
                CompileMessages messages = TranslationUpdater.UpdateTranslations(Project, translations);
                foreach (CompileMessage m in messages)
                    AppendOutput($"[{(m is CompileError ? "Error" : "Warning")}] {m.Message}");
                AppendOutput(messages.HasErrors ? "Translation(s) update failed. Check the output window for details."
                    : $"Translation(s) updated: {string.Join(", ", translations.Select(t => t.FileName))}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Translation update failed: {ex.Message}");
            }
        }

        private void AddFolderActions<TItem, TFolder>(ProjectTreeNode node, TFolder folder, TFolder root, Func<string, TFolder> create,
            Func<TItem, bool> deleteItem = null, string confirmation = null, Func<string> blockedBecause = null)
            where TItem : IToXml
            where TFolder : BaseFolderCollection<TItem, TFolder>
        {
            node.AddAction("New folder", () => Change(() => ProjectOperations.AddFolder<TItem, TFolder>(folder, create)));
            if (node.Kind != ProjectNodeKind.Folder) return;
            bool empty = folder.Items.Count == 0 && folder.SubFolders.Count == 0;
            if (empty)
                node.AddAction("Delete folder", () => Change(() => { ProjectOperations.DeleteEmptyFolder<TItem, TFolder>(root, folder); return null; }));
            else if (deleteItem != null)
                // BaseComponentWithFolders COMMAND_DELETE_FOLDER (GetFolderDeleteConfirmationText per component)
                node.AddAction("Delete folder", () =>
                {
                    if (!Ask(confirmation)) return;
                    string blocked = blockedBecause?.Invoke();
                    if (blocked != null)
                    {
                        EditorMessages.ShowWarning(blocked, "Delete folder");
                        return;
                    }
                    Change(() => { ProjectOperations.DeleteFolderWithContents(root, folder, deleteItem); return null; });
                });
        }

        private void CloseDocumentsFor(ScriptAndHeader module)
        {
            foreach (var doc in Documents.OfType<ScriptDocument>().Where(d => d.Script == module.Script || d.Script == module.Header).ToList())
                Documents.Remove(doc);
        }

        /// <summary>Runs a project change, then marks the project modified and refreshes the tree (selecting the result).</summary>
        private void Change(Func<object> change)
        {
            try
            {
                object result = change();
                IsModified = true;
                _completion?.RefreshGeneratedHeader(); // new/renamed characters, GUIs, … become known to scripts
                RebuildTree(result);
                StatusText = "Project changed (unsaved)";
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a script by file name: script modules, or a room script (roomN.asc), loading it if needed.
        /// Port of upstream GUIController.OnGetScript handling.
        /// </summary>
        public Script FindScript(string fileName)
        {
            Game game = Project?.Game;
            if (game == null || string.IsNullOrEmpty(fileName)) return null;
            Script script = game.RootScriptFolder.GetScriptByFileName(fileName, true);
            if (script != null) return script;
            foreach (IRoom room in game.RootRoomFolder.AllItemsFlat)
            {
                if (room is UnloadedRoom unloaded && string.Equals(unloaded.ScriptFileName, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    if (unloaded.Script == null)
                        unloaded.LoadScript();
                    return unloaded.Script;
                }
            }
            return null;
        }

        /// <summary>ScriptFunctionUIEditor.CreateScriptFunction hook (upstream GUIController).</summary>
        public bool CreateScriptFunction(CreateScriptFunctionArgs args)
        {
            Script script = FindScript(args.ScriptName);
            if (script == null)
            {
                AppendOutput($"[Error] Script {args.ScriptName} not found.");
                return false;
            }
            string before = script.Text;
            script.Text = ScriptGeneration.InsertFunction(script.Text, args.FunctionName, args.FunctionParameters);
            if (script.Text != before)
            {
                IsModified = true;
                AppendOutput($"Created function {args.FunctionName}({args.FunctionParameters}) in {args.ScriptName}");
                if (Documents.OfType<ScriptDocument>().FirstOrDefault(d => d.Script == script) is ScriptDocument open)
                {
                    var replacement = new ScriptDocument(script, open.Highlighting, () => IsModified = true, open.Completion);
                    replacement.DefinitionRequested += GoToDefinition;
                    replacement.UsagesRequested += FindAllUsagesInScript;
                    replacement.BreakpointToggled += OnBreakpointToggled;
                    Documents[Documents.IndexOf(open)] = replacement;
                }
            }
            return true;
        }

        /// <summary>ScriptFunctionUIEditor.OpenScriptFunction hook: open the script at the function.</summary>
        public void OpenScriptFunction(OpenScriptFunctionArgs args)
        {
            // Like upstream, run after the property grid finished processing the edit.
            Dispatcher.UIThread.Post(() =>
            {
                Script script = FindScript(args.ScriptName);
                if (script == null) return;
                string pattern = string.Format(ScriptGeneration.SCRIPT_EVENT_FUNCTION_PATTERN, System.Text.RegularExpressions.Regex.Escape(args.FunctionName));
                if (!System.Text.RegularExpressions.Regex.IsMatch(script.Text, pattern, System.Text.RegularExpressions.RegexOptions.Multiline) && args.CreateIfNotExists)
                    CreateScriptFunction(args);
                OpenNode(new ProjectTreeNode(script.FileName, ProjectNodeKind.Item, script));
                (SelectedDocument as ScriptDocument)?.NavigateTo(pattern);
                PropertyGrid.Rebuild();
            });
        }

        /// <summary>Plugs this editor into the AGS.Types script hooks, as upstream GUIController does.</summary>
        /// <summary>Set by the app: ImportTTFDialog (current size → pixel height or not and the value; null = cancel).</summary>
        public Func<int, (bool PixelHeight, int Value)?> AskTtfSize { get; set; }

        /// <summary>Set by the app: a synchronous open panel (title, type name, extensions → path or null).</summary>
        public Func<string, string, string[], string> PickFileSync { get; set; }

        /// <summary>FontsComponent: FontFileUIEditor.FontFileGUI and FontSizeUIEditor.FontSizeGUI.</summary>
        public void InstallFontHooks()
        {
            FontFileUIEditor.FontFileGUI = ImportFontFile;
            FontSizeUIEditor.FontSizeGUI = ReimportFontSize;
        }

        /// <summary>FontsComponent.ImportFontFile: choose a TTF, WFN or SCI ("font.*") file and import it over the font.</summary>
        private string ImportFontFile(AGS.Types.Font font)
        {
            // Upstream's filter has "font.*" for SCI fonts (FONT.000, FONT.001, ...); the macOS panel can only
            // filter by extension, so any file can be chosen and FontOperations.ImportFont tells them apart.
            string file = PickFileSync?.Invoke("Select font to import...", "Fonts (TTF, WFN, SCI font.*)", new[] { "ttf", "wfn", "*" });
            if (file != null)
                ImportOverFont(font, file);
            return font.SourceFilename;
        }

        /// <summary>FontsComponent.ImportFont: the size dialog for TTF fonts, copy, reload previews.</summary>
        private void ImportOverFont(AGS.Types.Font font, string file)
        {
            (bool PixelHeight, int Value)? size = null;
            if (file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            {
                size = AskTtfSize?.Invoke(font.PointSize > 0 ? font.PointSize : FontOperations.DefaultImportedFontSize)
                       ?? (AskTtfSize == null ? (false, FontOperations.DefaultImportedFontSize) : null);
                if (size == null) return;
            }
            try
            {
                FontOperations.ImportFont(Project, font, file, size is { PixelHeight: false } ? size.Value.Value : FontOperations.DefaultImportedFontSize);
                if (size is { PixelHeight: true })
                    font.PointSize = FindTtfSizeForHeight(font.TTFFileName, size.Value.Value);
                IsModified = true;
                AppendOutput($"Imported {file} into font {font.ID}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Unable to import the font: {ex.Message}");
            }
            FontsChanged();
        }

        /// <summary>FontsComponent.ReimportFontSize: a TTF font at another size (point size or closest to a pixel height).</summary>
        private int ReimportFontSize(AGS.Types.Font font)
        {
            if (font.ProjectFilename == null || !font.ProjectFilename.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            {
                EditorMessages.ShowWarning("You can only reimport TTF fonts with different size. For bitmap fonts try adjusting Size Multiplier instead.", "Font size");
                return font.PointSize;
            }
            (bool PixelHeight, int Value)? size = AskTtfSize?.Invoke(font.PointSize > 0 ? font.PointSize : FontOperations.DefaultImportedFontSize);
            if (size == null) return font.PointSize;
            if (!size.Value.PixelHeight) return size.Value.Value;
            try
            {
                return FindTtfSizeForHeight(font.ProjectFilename, size.Value.Value);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] {ex.Message}");
                return font.PointSize;
            }
        }

        private int FindTtfSizeForHeight(string fontFile, int pixelHeight)
        {
            if (!EnsureGuiRenderer())
                throw new AGSEditorException("The font renderer is not available.");
            return NativeGuiRenderer.FindTtfSizeForHeight(fontFile, pixelHeight);
        }

        public void InstallScriptHooks()
        {
            ScriptFunctionUIEditor.CreateScriptFunction = CreateScriptFunction;
            ScriptFunctionUIEditor.OpenScriptFunction = OpenScriptFunction;
            PropertyTabInteractions.UpdateEventName = UpdateEventName;
        }

        /// <summary>PropertyTabInteractions.UpdateEventName hook: "$$01".."$$09" become cursor names.</summary>
        public string UpdateEventName(string eventName)
        {
            Game game = Project?.Game;
            if (game == null) return eventName;
            for (int i = 1; i <= Math.Min(9, game.Cursors.Count - 1); i++)
                eventName = eventName.Replace("$$0" + i, game.Cursors[i].Name);
            return eventName;
        }

        private SpriteFile _spriteFile;

        /// <summary>The game's acsprset.spr, opened on first use (null if it can't be read).</summary>
        private SpriteFile Sprites
        {
            get
            {
                if (_spriteFile == null && Project != null)
                {
                    try { _spriteFile = SpriteFile.OpenInProject(Project.Directory); }
                    catch (Exception ex) { AppendOutput($"[Error] Could not open the sprite file: {ex.Message}"); }
                }
                return _spriteFile;
            }
        }

        private void OpenSpriteFolder(ISpriteFolder folder)
        {
            DocumentViewModel open = Documents.FirstOrDefault(d => ReferenceEquals(d.Key, folder));
            if (open != null)
            {
                SelectedDocument = open;
                return;
            }
            OpenDocument(new SpriteFolderDocument(folder, Project.Game, Sprites, sprite => PropertyGrid.Component = sprite));
        }

        /// <summary>Set by the view: asks where to save a file (suggested name, extensions); null when cancelled.</summary>
        public Func<string, string[], System.Threading.Tasks.Task<string>> PickSaveFile { get; set; }

        /// <summary>Set by the view: asks for image files to open (title, extensions); empty when cancelled.</summary>
        public Func<string, string[], System.Threading.Tasks.Task<IReadOnlyList<string>>> PickOpenFiles { get; set; }

        /// <summary>Set by the app: asks for the import options (current transparency, alpha, file count); null = cancelled.</summary>
        public Func<SpriteImportTransparency, bool, int, (SpriteImportTransparency Transparency, bool ImportAlpha)?> PickSpriteImportOptions { get; set; }

        // Last used import options (upstream keeps them as editor preferences; default "Leave as-is").
        private SpriteImportTransparency _spriteImportTransparency = SpriteImportTransparency.LeaveAsIs;
        private bool _spriteImportAlpha = true;

        // Sprite files replaced by an import; still read by open room editors (the old file stays readable
        // through its open handle), disposed with the project.
        private readonly List<SpriteFile> _retiredSpriteFiles = new List<SpriteFile>();

        private SpriteImporter CreateSpriteImporter()
        {
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            if (toolchain == null)
            {
                AppendOutput("[Error] spriteimport not found; run scripts/build-upstream-tools.sh or set AGS4MAC_TOOLS.");
                return null;
            }
            return new SpriteImporter(Project, toolchain);
        }

        /// <summary>After acsprset.spr was replaced: reopen it and refresh the open sprite folders.</summary>
        private void SpriteFileReplaced(Sprite select)
        {
            _guiRendererReady = false; // the GUI renderer caches sprites
            foreach (GuiEditorDocument d in Documents.OfType<GuiEditorDocument>())
            {
                d.UseNativeRendering = EnsureGuiRenderer();
                d.Refresh();
            }
            if (_spriteFile != null)
                _retiredSpriteFiles.Add(_spriteFile);
            _spriteFile = null;
            for (int i = 0; i < Documents.Count; i++)
            {
                if (Documents[i] is SpriteFolderDocument folderDoc)
                {
                    bool wasSelected = ReferenceEquals(SelectedDocument, folderDoc);
                    var refreshed = new SpriteFolderDocument(folderDoc.Folder, Project.Game, Sprites, sprite => PropertyGrid.Component = sprite);
                    Documents[i] = refreshed;
                    if (wasSelected)
                        SelectedDocument = refreshed;
                    if (select != null)
                        refreshed.Selected = refreshed.Items.FirstOrDefault(t => ReferenceEquals(t.Sprite, select));
                }
            }
            RebuildTree();
            IsModified = true;
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task ImportSpritesAsync(SpriteFolderDocument document)
        {
            if (document == null || Project == null || PickOpenFiles == null) return;
            IReadOnlyList<string> files = await PickOpenFiles("Import sprites", new[] { "png", "bmp", "pcx" });
            await ImportImageFilesAsync((SpriteFolder)document.Folder, files);
        }

        /// <summary>Image file extensions the sprite import accepts (also for files dropped from the Finder).</summary>
        public static readonly string[] SpriteImageExtensions = { "png", "bmp", "pcx", "gif", "jpg", "jpeg", "tif", "tiff" };

        /// <summary>Imports image files as new sprites into a folder (the import button, or files dropped from the Finder).</summary>
        public async System.Threading.Tasks.Task ImportImageFilesAsync(SpriteFolder folder, IReadOnlyList<string> files)
        {
            files = files?.Where(f => SpriteImageExtensions.Contains(Path.GetExtension(f).TrimStart('.').ToLowerInvariant())).ToList();
            if (folder == null || Project == null || files == null || files.Count == 0) return;
            if (PickSpriteImportOptions != null)
            {
                var options = PickSpriteImportOptions(_spriteImportTransparency, _spriteImportAlpha, files.Count);
                if (options == null) return;
                (_spriteImportTransparency, _spriteImportAlpha) = options.Value;
            }
            SpriteImporter importer = CreateSpriteImporter();
            if (importer == null) return;
            SpriteImportTransparency transparency = _spriteImportTransparency;
            bool alpha = _spriteImportAlpha;
            Sprite last = null;
            StatusText = "Importing sprites…";
            foreach (string file in files)
            {
                try
                {
                    last = await System.Threading.Tasks.Task.Run(() => importer.Import(file, folder, transparency, alpha));
                    AppendOutput($"Imported {file} as sprite {last.Number} ({last.Width}×{last.Height})");
                }
                catch (Exception ex)
                {
                    AppendOutput($"[Error] Import of {file} failed: {ex.Message}");
                }
            }
            StatusText = "Ready";
            if (last != null)
                SpriteFileReplaced(last);
        }

        /// <summary>
        /// Project tree drag &amp; drop (BaseComponentWithFolders): moves an item or folder relative to / into the target, if
        /// both belong to the same kind of folder tree. With <paramref name="apply"/> false, only checks whether it can.
        /// </summary>
        public bool DropNode(ProjectTreeNode source, ProjectTreeNode target, TargetDropZone zone, bool apply = true)
        {
            if (Project == null || source == null || target == null || source == target) return false;
            object src = source.Payload ?? source.Folder, dst = target.Payload ?? target.Folder;
            Game game = Project.Game;
            bool? Try<TItem, TFolder>(TFolder root)
                where TItem : class, IToXml
                where TFolder : BaseFolderCollection<TItem, TFolder>
            {
                if (!(src is TItem || src is TFolder) || !(dst is TItem || dst is TFolder)) return null;
                return apply ? FolderDragDrop<TItem, TFolder>.Drop(root, src, dst, zone) : FolderDragDrop<TItem, TFolder>.CanDrop(root, src, dst, zone);
            }
            bool result = Try<Character, CharacterFolder>(game.RootCharacterFolder)
                ?? Try<InventoryItem, InventoryItemFolder>(game.RootInventoryItemFolder)
                ?? Try<View, ViewFolder>(game.RootViewFolder)
                ?? Try<Dialog, DialogFolder>(game.RootDialogFolder)
                ?? Try<GUI, GUIFolder>(game.RootGUIFolder)
                ?? Try<ScriptAndHeader, ScriptFolder>(game.RootScriptFolder)
                ?? Try<IRoom, UnloadedRoomFolder>(game.RootRoomFolder)
                ?? Try<AudioClip, AudioClipFolder>(game.RootAudioClipFolder)
                ?? (src is SpriteFolder sf && dst is SpriteFolder df ? (apply ? MoveSpriteFolder(sf, df) : CanMoveSpriteFolder(sf, df)) : false);
            if (apply && result)
            {
                IsModified = true;
                if (src is ScriptAndHeader || src is ScriptFolder)
                    _completion = null; // the script order (imports) changed
                RebuildTree(src);
                StatusText = $"Moved {source.Name}";
            }
            return result;
        }

        private bool CanMoveSpriteFolder(SpriteFolder folder, SpriteFolder target) =>
            folder != Project.Game.RootSpriteFolder && folder != target && !IsInside(target, folder) &&
            SpriteOperations.FindParent(Project.Game.RootSpriteFolder, folder) != target;

        private bool MoveSpriteFolder(SpriteFolder folder, SpriteFolder target)
        {
            if (!CanMoveSpriteFolder(folder, target)) return false;
            SpriteOperations.FindParent(Project.Game.RootSpriteFolder, folder)?.SubFolders.Remove(folder);
            target.SubFolders.Add(folder);
            return true;
        }

        /// <summary>Sprites dragged from the sprite view onto a sprite folder in the tree.</summary>
        public void DropSpritesOnFolder(IReadOnlyList<Sprite> sprites, SpriteFolder target)
        {
            if (Project == null || sprites == null || sprites.Count == 0 || target == null) return;
            SpriteOperations.MoveSprites(Project.Game, sprites, target);
            AppendOutput($"Moved {sprites.Count} sprite(s) to {target.Name}");
            SpriteFileReplaced(null);
        }

        /// <summary>SpriteSelector "Replace sprite from file...".</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task ReplaceSpriteFromFileAsync(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null || Project == null || PickOpenFiles == null) return;
            IReadOnlyList<string> files = await PickOpenFiles("Replace sprite...", new[] { "png", "bmp", "pcx", "gif", "jpg", "jpeg", "tif", "tiff" });
            string file = files?.FirstOrDefault();
            if (file == null) return;
            if (PickSpriteImportOptions != null)
            {
                var options = PickSpriteImportOptions(_spriteImportTransparency, _spriteImportAlpha, 1);
                if (options == null) return;
                (_spriteImportTransparency, _spriteImportAlpha) = options.Value;
            }
            SpriteImporter importer = CreateSpriteImporter();
            if (importer == null) return;
            Sprite sprite = thumbnail.Sprite;
            SpriteImportTransparency transparency = _spriteImportTransparency;
            bool alpha = _spriteImportAlpha;
            try
            {
                await System.Threading.Tasks.Task.Run(() => importer.Replace(sprite, file, transparency, alpha));
                AppendOutput($"Replaced sprite {sprite.Number} with {file} ({sprite.Width}×{sprite.Height})");
                SpriteFileReplaced(sprite);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] There was an error importing the file: {ex.Message}");
            }
        }

        /// <summary>SpriteSelector "Restore all sprites from sources" (Tasks.RecreateSpriteFileFromSources).</summary>
        public async System.Threading.Tasks.Task RestoreAllSpritesFromSourcesAsync()
        {
            if (Project == null) return;
            if (!Ask("This will recreate game's spritefile using sprite source files if they are available. All sprites will be updated from their sources.\n\nNOTE: sprites that don't have source file references, or which source files are missing, - will remain untouched.\n\nAre you sure you want to do this?"))
                return;
            SpriteImporter importer = CreateSpriteImporter();
            if (importer == null) return;
            var all = Project.Game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().ToList();
            StatusText = "Restoring sprites from their sources…";
            try
            {
                IReadOnlyList<Sprite> skipped = await System.Threading.Tasks.Task.Run(() => importer.RestoreFromSources(all));
                AppendOutput($"Restored {all.Count - skipped.Count} sprites from their sources; {skipped.Count} without (available) source files were left untouched.");
                SpriteFileReplaced(null);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Restoring the sprites failed: {ex.Message}");
            }
            StatusText = "Ready";
        }

        /// <summary>Set by the app: the tiling settings for a sprite sheet image (null = cancel).</summary>
        public Func<string, AGS.Editor.Utils.SpriteSheet> AskSpriteSheet { get; set; }

        /// <summary>Tiled import of SpriteImportWindow: one sprite per tile of a sprite sheet.</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task ImportSpriteSheetAsync(SpriteFolderDocument document)
        {
            if (document == null || Project == null || PickOpenFiles == null || AskSpriteSheet == null) return;
            IReadOnlyList<string> files = await PickOpenFiles("Import sprites from a sprite sheet", new[] { "png", "bmp", "pcx" });
            string file = files?.FirstOrDefault();
            if (file == null) return;
            if (PickSpriteImportOptions != null)
            {
                var options = PickSpriteImportOptions(_spriteImportTransparency, _spriteImportAlpha, 1);
                if (options == null) return;
                (_spriteImportTransparency, _spriteImportAlpha) = options.Value;
            }
            AGS.Editor.Utils.SpriteSheet sheet;
            try { sheet = AskSpriteSheet(file); }
            catch (Exception ex) { AppendOutput($"[Error] {ex.Message}"); return; }
            if (sheet == null) return;
            SpriteImporter importer = CreateSpriteImporter();
            if (importer == null) return;
            var folder = (SpriteFolder)document.Folder;
            SpriteImportTransparency transparency = _spriteImportTransparency;
            bool alpha = _spriteImportAlpha;
            StatusText = "Importing sprites…";
            try
            {
                IReadOnlyList<Sprite> sprites = await System.Threading.Tasks.Task.Run(() => importer.ImportTiles(file, sheet, folder, transparency, alpha));
                AppendOutput($"Imported {sprites.Count} sprites from {file} (sprites {sprites[0].Number}–{sprites[^1].Number})");
                SpriteFileReplaced(sprites[0]);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Import of {file} failed: {ex.Message}");
            }
            StatusText = "Ready";
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task ReimportSpriteAsync(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null || Project == null) return;
            SpriteImporter importer = CreateSpriteImporter();
            if (importer == null) return;
            try
            {
                await System.Threading.Tasks.Task.Run(() => importer.Reimport(thumbnail.Sprite));
                AppendOutput($"Reimported sprite {thumbnail.Number} from {thumbnail.Sprite.SourceFile}");
                SpriteFileReplaced(thumbnail.Sprite);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Reimport failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task ImportBackgroundAsync(RoomEditorDocument room) =>
            await ImportBackgroundIntoAsync(room, room?.BackgroundFrame ?? 0);

        [RelayCommand]
        private async System.Threading.Tasks.Task AddBackgroundAsync(RoomEditorDocument room) =>
            await ImportBackgroundIntoAsync(room, room?.Room.BackgroundCount ?? 0);

        private async System.Threading.Tasks.Task ImportBackgroundIntoAsync(RoomEditorDocument room, int frame)
        {
            if (room == null || PickOpenFiles == null) return;
            if (frame >= 5) // MAX_ROOM_BGFRAMES
            {
                AppendOutput("[Warning] A room can have at most 5 backgrounds.");
                return;
            }
            IReadOnlyList<string> files = await PickOpenFiles("Select background to import...", new[] { "png", "bmp", "pcx" });
            string file = files?.FirstOrDefault();
            if (file == null) return;
            try
            {
                string message = room.ImportBackground(file, frame, question => Confirm == null || Confirm("Import background", question));
                if (message != null)
                    AppendOutput("[Warning] " + message);
                if (room.IsModified)
                {
                    IsModified = true;
                    AppendOutput($"Imported {file} as background {frame} of room {room.Unloaded.Number}");
                    PropertyGrid.Rebuild();
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] The background could not be imported: {ex.Message}");
            }
        }

        /// <summary>BaseAreasEditorFilter "Import mask from file" for the current paint layer.</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task ImportMaskAsync(RoomEditorDocument room)
        {
            if (room == null || PickOpenFiles == null) return;
            IReadOnlyList<string> files = await PickOpenFiles("Select mask to import...", new[] { "png", "bmp", "pcx", "gif" });
            string file = files?.FirstOrDefault();
            if (file == null) return;
            try
            {
                using AGS.Imaging.PixelImage image = new NativeImageCodec().Decode(file).Frames[0];
                string message = room.ImportMask(image);
                if (message != null)
                    EditorMessages.ShowWarning(message, "Import mask");
                if (room.IsModified)
                {
                    IsModified = true;
                    AppendOutput($"Imported {file} as the {room.PaintLayer} mask of room {room.Unloaded.Number}");
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] There was an error importing the area mask: {ex.Message}");
            }
        }

        /// <summary>BaseAreasEditorFilter "Export mask to file" for the current paint layer.</summary>
        [RelayCommand]
        private void ExportMask(RoomEditorDocument room)
        {
            if (room == null) return;
            string suggested = Path.Combine(Project.Directory, $"room{room.Unloaded.Number}-{room.PaintLayer}.png".ToLowerInvariant());
            string file = PickSaveFileSync == null ? suggested : PickSaveFileSync("Export mask to file", suggested, "PNG images", "png");
            if (file == null) return;
            try
            {
                using AGS.Imaging.PixelImage image = room.ExportMask();
                new NativeImageCodec().Encode(image, file);
                AppendOutput($"Exported the {room.PaintLayer} mask of room {room.Unloaded.Number} to {file}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] There was an error exporting the area mask: {ex.Message}");
            }
        }

        [RelayCommand]
        private void DeleteBackground(RoomEditorDocument room)
        {
            if (room == null || !room.CanDeleteBackground) return;
            if (!Ask($"Are you sure you want to delete background {room.BackgroundFrame}?")) return;
            room.DeleteBackground(room.BackgroundFrame);
            IsModified = true;
        }

        [RelayCommand]
        private void DeleteSprite(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null || Project == null) return;
            if (!Ask("Only delete this sprite(s) if you are ABSOLUTELY SURE it is not used AT ALL in your game. AGS cannot automatically detect usage of the sprite within room files or script function calls.\n\nAny parts of your game that do use this sprite will cause the editor and engine to crash if you go ahead. Are you sure?"))
                return;
            try
            {
                // Upstream RoomsComponent's PreDeleteSprite check, for every open room.
                string UsedInOpenRoom(int number)
                {
                    foreach (RoomEditorDocument room in Documents.OfType<RoomEditorDocument>())
                        foreach (RoomObject obj in room.Room.Objects)
                            if (obj.Image == number)
                                return "Sprite " + number + " is in use by object " + obj.ID + " in room " + room.Unloaded.Number + " and cannot be deleted.";
                    return null;
                }
                // The old file stays readable for open documents; a new one is opened afterwards.
                if (_spriteFile != null)
                    _retiredSpriteFiles.Add(_spriteFile);
                _spriteFile = null;
                IReadOnlyList<Sprite> sprites = SpritesFor(thumbnail);
                IReadOnlyList<string> problems = SpriteOperations.DeleteSprites(Project.Game, Project.Directory, sprites, UsedInOpenRoom);
                foreach (string problem in problems)
                    AppendOutput("[Warning] " + problem);
                if (problems.Count < sprites.Count)
                {
                    AppendOutput($"Deleted {sprites.Count - problems.Count} sprite(s)");
                    if (sprites.Contains(PropertyGrid.Component as Sprite))
                        PropertyGrid.Component = null;
                }
                SpriteFileReplaced(null);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Deleting the sprite failed: {ex.Message}");
            }
        }

        /// <summary>Set by the app: choose one entry of a list (title, entries → index or null).</summary>
        public Func<string, IReadOnlyList<string>, int?> ChooseFromList { get; set; }

        /// <summary>Set by the app: the AssignToView dialog (null = cancel).</summary>
        public Func<Game, (int View, int Loop, bool AddToExisting, bool ContinueIntoNextLoop, bool Reverse, bool Flip)?> AskAssignToView { get; set; }

        /// <summary>SpriteSelector "Show usage...".</summary>
        [RelayCommand]
        private void ShowSpriteUsage(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null) return;
            string usage = SpriteOperations.GetSpriteUsageReport(thumbnail.Number, Project.Game);
            string text = usage ?? "No uses of this sprite could be found automatically. HOWEVER, it may be used in scripts or as a room object image; these uses cannot be detected automatically.";
            AppendOutput($"Sprite {thumbnail.Number}: " + text);
            EditorMessages.ShowWarning(text, $"Usage of sprite {thumbnail.Number}");
        }

        /// <summary>SpriteSelector.ChangeSpriteNumber.</summary>
        [RelayCommand]
        private void ChangeSpriteNumber(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null) return;
            Sprite sprite = thumbnail.Sprite;
            string usage = SpriteOperations.GetSpriteUsageReport(sprite.Number, Project.Game);
            if (usage != null)
            {
                EditorMessages.ShowWarning("Cannot change the sprite number because it is in use:" + Environment.NewLine + usage, "Change Sprite Number");
                return;
            }
            string answer = PromptText?.Invoke($"Change Sprite Number — enter the new sprite number (0-{SpriteOperations.MaxStaticSprites - 1}). WARNING: only re-number this sprite if you are ABSOLUTELY SURE it is not used AT ALL in your game.", sprite.Number.ToString());
            if (string.IsNullOrWhiteSpace(answer) || !int.TryParse(answer.Trim(), out int newNumber) || newNumber == sprite.Number || newNumber < 0)
                return;
            try
            {
                int old = sprite.Number;
                RetireSpriteFile();
                SpriteOperations.ChangeSpriteNumbers(Project.Game, Project.Directory, new[] { sprite }, new[] { newNumber });
                AppendOutput($"Sprite {old} is now sprite {newNumber}");
                SpriteFileReplaced(sprite);
            }
            catch (Exception ex)
            {
                EditorMessages.ShowWarning("Unable to change the sprite number. " + ex.Message, "Change Sprite Number");
            }
        }

        /// <summary>SpriteSelector "Assign to view...".</summary>
        [RelayCommand]
        private void AssignSpriteToView(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null) return;
            var options = AskAssignToView?.Invoke(Project.Game);
            if (options == null) return;
            var o = options.Value;
            string message = SpriteOperations.AssignSpritesToView(Project.Game, SpritesFor(thumbnail).Select(sp => sp.Number).ToList(), o.View, o.Loop, o.Reverse, o.AddToExisting, o.ContinueIntoNextLoop, o.Flip);
            AppendOutput(message);
            IsModified = true;
            foreach (ViewEditorDocument d in Documents.OfType<ViewEditorDocument>())
                d.Refresh();
        }

        /// <summary>The sprites a thumbnail command applies to: all selected ones when the thumbnail is part of a multi-selection.</summary>
        private IReadOnlyList<Sprite> SpritesFor(SpriteThumbnail thumbnail)
        {
            if (SelectedDocument is SpriteFolderDocument doc && doc.SelectedItems.Count > 1 && doc.SelectedItems.Contains(thumbnail))
                return doc.SelectedSprites;
            return new[] { thumbnail.Sprite };
        }

        /// <summary>SpriteSelector.AssignSequentialNumbers: the selected sprites get the closest free numbers from a start number on.</summary>
        [RelayCommand]
        private void AssignSequentialSpriteNumbers(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null) return;
            IReadOnlyList<Sprite> sprites = SpritesFor(thumbnail);
            foreach (Sprite sprite in sprites)
            {
                string usage = SpriteOperations.GetSpriteUsageReport(sprite.Number, Project.Game);
                if (usage != null)
                {
                    EditorMessages.ShowWarning("Cannot change the sprite number because it is in use:" + Environment.NewLine + usage, "Assign sequential sprite numbers");
                    return;
                }
            }
            string answer = PromptText?.Invoke($"Assign sequential sprite numbers — enter the new sprite number for the first sprite in sequence (0-{SpriteOperations.MaxStaticSprites - 1}). This will assign closest found free numbers to all selected sprites in a sequence. WARNING: only re-number sprites if you are ABSOLUTELY SURE they are not used AT ALL in your game.", sprites[0].Number.ToString());
            if (string.IsNullOrWhiteSpace(answer) || !int.TryParse(answer.Trim(), out int first) || first < 0) return;
            int[] numbers = SpriteOperations.SequentialNumbers(Project.Game, sprites, first);
            if (numbers == null)
            {
                EditorMessages.ShowWarning("Unable to find enough free sprite slots for the sequence.", "Assign sequential sprite numbers");
                return;
            }
            try
            {
                RetireSpriteFile();
                SpriteOperations.ChangeSpriteNumbers(Project.Game, Project.Directory, sprites, numbers);
                AppendOutput($"Renumbered {sprites.Count} sprite(s) from {numbers[0]} on");
                SpriteFileReplaced(sprites[0]);
            }
            catch (Exception ex)
            {
                EditorMessages.ShowWarning("Unable to change the sprite numbers. " + ex.Message, "Assign sequential sprite numbers");
            }
        }

        [RelayCommand]
        private void CropSpriteEdges(SpriteThumbnail thumbnail) => CropSprites(thumbnail, false);

        [RelayCommand]
        private void CropSpriteEdgesSymmetric(SpriteThumbnail thumbnail) => CropSprites(thumbnail, true);

        /// <summary>SpriteSelector "Crop sprite edges" / "Crop sprite edges (symmetric)".</summary>
        private void CropSprites(SpriteThumbnail thumbnail, bool symmetric)
        {
            if (thumbnail == null) return;
            if (!Ask(symmetric
                ? "Cropping the selected sprites will trim off the edges to reduce all the selected sprites to the size required by the largest, but ensuring that the central pivot point of the sprites remains unchanged. Are you sure you want to proceed?"
                : "Cropping the selected sprites will trim off the edges to reduce all the selected sprites to the size required by the largest. Are you sure you want to proceed?"))
                return;
            IReadOnlyList<Sprite> sprites = SpritesFor(thumbnail);
            try
            {
                RetireSpriteFile();
                string message = SpriteOperations.CropSpriteEdges(Project.Directory, sprites, symmetric);
                if (message != null)
                {
                    EditorMessages.ShowWarning(message, "Crop sprite edges");
                    SpriteFileReplaced(null);
                    return;
                }
                AppendOutput("The selected sprites were cropped down to " + sprites[0].Width + " x " + sprites[0].Height + " successfully.");
                SpriteFileReplaced(sprites[0]);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Cropping failed: {ex.Message}");
            }
        }

        /// <summary>Set by the view: puts an image on the clipboard.</summary>
        public Func<AGS.Imaging.PixelImage, System.Threading.Tasks.Task> CopyImageToClipboard { get; set; }

        /// <summary>Set by the view: the clipboard image (32-bit BGRA) or null.</summary>
        public Func<System.Threading.Tasks.Task<AGS.Imaging.PixelImage>> GetClipboardImage { get; set; }

        /// <summary>SpriteSelector "Copy sprite to clipboard".</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task CopySpriteToClipboardAsync(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null || CopyImageToClipboard == null) return;
            var palette = Project.Game.Palette.Select(p => p.Colour).ToArray();
            using AGS.Imaging.PixelImage image = Sprites.LoadSprite(thumbnail.Number, palette);
            if (image == null) return;
            await CopyImageToClipboard(image);
            StatusText = $"Sprite {thumbnail.Number} copied to the clipboard";
        }

        /// <summary>
        /// SpriteSelector "Paste new sprite from clipboard...": the image is saved as a PNG in the game's
        /// "ClipboardSprites" folder and imported from there (so it can be reimported later; upstream keeps no source).
        /// </summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task PasteSpriteFromClipboardAsync(SpriteFolderDocument document) =>
            await PasteFromClipboardAsync(document, null);

        /// <summary>SpriteSelector "Replace sprite from clipboard...".</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task ReplaceSpriteFromClipboardAsync(SpriteThumbnail thumbnail) =>
            await PasteFromClipboardAsync(null, thumbnail?.Sprite);

        private async System.Threading.Tasks.Task PasteFromClipboardAsync(SpriteFolderDocument document, Sprite replace)
        {
            if (Project == null || GetClipboardImage == null || (document == null && replace == null)) return;
            AGS.Imaging.PixelImage image = await GetClipboardImage();
            if (image == null)
            {
                EditorMessages.ShowWarning("The clipboard does not contain an image.", "Paste sprite");
                return;
            }
            if (PickSpriteImportOptions != null)
            {
                var options = PickSpriteImportOptions(_spriteImportTransparency, _spriteImportAlpha, 1);
                if (options == null) { image.Dispose(); return; }
                (_spriteImportTransparency, _spriteImportAlpha) = options.Value;
            }
            string folder = Path.Combine(Project.Directory, "ClipboardSprites");
            Directory.CreateDirectory(folder);
            string file;
            int n = 1;
            do file = Path.Combine(folder, $"clipboard{n++}.png"); while (File.Exists(file));
            using (image)
                new NativeImageCodec().Encode(image, file);
            SpriteImporter importer = CreateSpriteImporter();
            if (importer == null) return;
            SpriteImportTransparency transparency = _spriteImportTransparency;
            bool alpha = _spriteImportAlpha;
            try
            {
                Sprite sprite;
                if (replace != null)
                {
                    await System.Threading.Tasks.Task.Run(() => importer.Replace(replace, file, transparency, alpha));
                    sprite = replace;
                    AppendOutput($"Replaced sprite {sprite.Number} from the clipboard ({sprite.Width}×{sprite.Height})");
                }
                else
                {
                    var target = (SpriteFolder)document.Folder;
                    sprite = await System.Threading.Tasks.Task.Run(() => importer.Import(file, target, transparency, alpha));
                    AppendOutput($"Pasted sprite {sprite.Number} from the clipboard ({sprite.Width}×{sprite.Height}), source {sprite.SourceFile}");
                }
                SpriteFileReplaced(sprite);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] There was an error importing the clipboard image: {ex.Message}");
            }
        }

        /// <summary>Set by the app: the ReplaceFolderDialog (title, info, old, new → (old, new) or null).</summary>
        public Func<string, string, string, string, (string Old, string New)?> AskReplaceFolder { get; set; }

        /// <summary>SpriteSelector "Replace source paths for sprites...".</summary>
        public void ReplaceSpriteSourcePaths(IReadOnlyList<Sprite> selected = null)
        {
            if (Project == null || AskReplaceFolder == null) return;
            string first = selected?.FirstOrDefault(sp => !string.IsNullOrEmpty(sp.SourceFile))?.SourceFile
                ?? Project.Game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().FirstOrDefault(sp => !string.IsNullOrEmpty(sp.SourceFile))?.SourceFile;
            if (string.IsNullOrEmpty(first))
            {
                EditorMessages.ShowWarning("None of the sprites has a source filename.", "Replace source paths");
                return;
            }
            string normalized = first.Replace('\\', '/');
            string parent = normalized.Contains('/') ? first.Substring(0, normalized.LastIndexOf('/')) : string.Empty;
            var dirs = AskReplaceFolder("Replace sprite(s) source path",
                "Please choose which part of the parent path should be replaced and provide a replacement. Relative paths will be assumed relative to your game's project folder.",
                parent, parent);
            if (dirs == null || dirs.Value.Old == dirs.Value.New) return;
            int count = SpriteOperations.ReplaceSourcePaths(Project.Game, dirs.Value.Old, dirs.Value.New);
            if (count > 0)
            {
                IsModified = true;
                PropertyGrid.Rebuild();
                AppendOutput($"{count} sprite(s) had their source paths updated.");
            }
            else
            {
                EditorMessages.ShowWarning("No sprites with the matching old paths found, no changes were made.", "Replace source paths");
            }
        }

        [RelayCommand]
        private void ReplaceSourcePathsOfSprite(SpriteThumbnail thumbnail) =>
            ReplaceSpriteSourcePaths(thumbnail == null ? null : SpritesFor(thumbnail));

        /// <summary>"Open File Explorer at sprite source": reveals the source file in the Finder.</summary>
        [RelayCommand]
        private void ShowSpriteSourceInFinder(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null) return;
            string source = string.IsNullOrEmpty(thumbnail.Sprite.SourceFile) ? null
                : Path.GetFullPath(Path.Combine(Project.Directory, ProjectPaths.ToLocal(thumbnail.Sprite.SourceFile)));
            if (source == null || !File.Exists(source))
            {
                EditorMessages.ShowWarning($"The source file of sprite {thumbnail.Number} ('{thumbnail.Sprite.SourceFile}') was not found.", "Show in Finder");
                return;
            }
            Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", source }, UseShellExecute = false });
        }

        // ---- "Edit in default image editor" (SpriteSelector.LaunchImageEditorForSprite) ----
        private FileSystemWatcher _externalSpriteWatcher;
        private readonly Dictionary<string, (Sprite Sprite, DateTime Written)> _externalSprites = new Dictionary<string, (Sprite, DateTime)>(StringComparer.Ordinal);

        /// <summary>Directory of the temporary files edited in the image editor.</summary>
        public static string ExternalEditFolder { get; } = Path.Combine(Path.GetTempPath(), "ags4mac-sprite-edit");

        /// <summary>
        /// Exports the sprite to a temporary PNG and opens it with the default image application. Each time that file is
        /// saved, the sprite image is replaced from it (the sprite's source settings stay as they are, like upstream's
        /// ReplaceSpriteWithBitmap). macOS editors usually keep running, so the file is watched instead of waiting.
        /// </summary>
        [RelayCommand]
        private void EditSpriteExternally(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null) return;
            Directory.CreateDirectory(ExternalEditFolder);
            string file = Path.Combine(ExternalEditFolder, $"{Path.GetFileName(Project.Directory)}-sprite{thumbnail.Number}.png");
            var palette = Project.Game.Palette.Select(p => p.Colour).ToArray();
            using (AGS.Imaging.PixelImage image = Sprites.LoadSprite(thumbnail.Number, palette))
                new NativeImageCodec().Encode(image, file);
            _externalSprites[file] = (thumbnail.Sprite, File.GetLastWriteTimeUtc(file));
            if (_externalSpriteWatcher == null)
            {
                _externalSpriteWatcher = new FileSystemWatcher(ExternalEditFolder, "*.png")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                FileSystemEventHandler changed = (s, e) => Dispatcher.UIThread.Post(() => OnExternalSpriteFileChanged(e.FullPath));
                _externalSpriteWatcher.Changed += changed;
                _externalSpriteWatcher.Created += changed;
                _externalSpriteWatcher.Renamed += (s, e) => changed(s, e);
                _externalSpriteWatcher.EnableRaisingEvents = true;
            }
            if (OpenWithDefaultApplication != null)
                OpenWithDefaultApplication(file);
            else
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { file }, UseShellExecute = false });
            AppendOutput($"Editing sprite {thumbnail.Number} in your image editor ({file}); saving the image updates the sprite.");
        }

        /// <summary>Set by tests: replaces "open file with the default application".</summary>
        public Action<string> OpenWithDefaultApplication { get; set; }

        /// <summary>A temporary sprite image was saved by the image editor: replace the sprite from it.</summary>
        public void OnExternalSpriteFileChanged(string file)
        {
            if (Project == null || !_externalSprites.TryGetValue(file, out var entry) || !File.Exists(file)) return;
            DateTime written = File.GetLastWriteTimeUtc(file);
            if (written == entry.Written) return;
            AGS.Imaging.PixelImage image;
            try { image = new NativeImageCodec().Decode(file).Frames[0]; }
            catch (Exception) { return; } // still being written; another event follows
            image.Dispose();
            _externalSprites[file] = (entry.Sprite, written);
            Sprite sprite = entry.Sprite;
            SpriteImporter importer = CreateSpriteImporter();
            if (importer == null) return;
            var keep = (sprite.SourceFile, sprite.ImportAsTile, sprite.OffsetX, sprite.OffsetY, sprite.ImportWidth, sprite.ImportHeight);
            try
            {
                RetireSpriteFile();
                importer.Replace(sprite, file, sprite.TransparentColour, sprite.ImportAlphaChannel);
                (sprite.SourceFile, sprite.ImportAsTile, sprite.OffsetX, sprite.OffsetY, sprite.ImportWidth, sprite.ImportHeight) = keep;
                AppendOutput($"Sprite {sprite.Number} updated from your image editor ({sprite.Width}×{sprite.Height})");
                SpriteFileReplaced(sprite);
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] The edited image could not be imported: {ex.Message}");
            }
        }

        /// <summary>Set by the app: the SpriteExportDialog (null = cancel).</summary>
        public Func<string, (string ExportPath, bool UseRootFolder, bool Recurse, SpriteExport.SkipIf SkipIf, bool UpdateSource, bool ResetTiles)?> AskSpriteExport { get; set; }

        /// <summary>Set by the app: a synchronous folder panel (title, start folder → path or null).</summary>
        public Func<string, string, string> PickFolderSync { get; set; }

        /// <summary>SpriteSelector "Export all sprites...".</summary>
        [RelayCommand]
        private void ExportAllSprites(SpriteFolderDocument document)
        {
            if (document == null || AskSpriteExport == null) return;
            var o = AskSpriteExport(Project.Directory);
            if (o == null) return;
            var opts = o.Value;
            SpriteFolder start = opts.UseRootFolder ? Project.Game.RootSpriteFolder : (SpriteFolder)document.Folder;
            RunSpriteExport(start, new SpriteExport.ExportSpritesOptions(opts.ExportPath, opts.Recurse, opts.SkipIf, opts.UpdateSource, opts.ResetTiles));
        }

        /// <summary>SpriteSelector "Create source files for all sprites with missing / external sources..." (ExportFixupSources).</summary>
        public void CreateMissingSpriteSources()
        {
            string folder = PickFolderSync?.Invoke("Create sprite source in folder...", Project.Directory);
            if (folder == null) return;
            RunSpriteExport(Project.Game.RootSpriteFolder, new SpriteExport.ExportSpritesOptions(
                Path.Combine(folder, "%Number%"), recurse: true, skipIf: SpriteExport.SkipIf.SourceLocal, updateSourcePath: true, resetTileSettings: true));
        }

        private void RunSpriteExport(SpriteFolder start, SpriteExport.ExportSpritesOptions options)
        {
            try
            {
                int count = SpriteExport.ExportSprites(Project, Sprites, start, options);
                AppendOutput($"Exported {count} sprite(s) to {Path.GetDirectoryName(options.ExportPath)}");
                if (options.UpdateSourcePath || options.ResetTileSettings)
                {
                    IsModified = true;
                    PropertyGrid.Rebuild();
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] There was an error exporting the sprites: {ex.Message}");
            }
        }

        /// <summary>SpriteSelector "Find sprite by number...".</summary>
        public void FindSpriteByNumber()
        {
            string answer = PromptText?.Invoke("Find sprite — type the number of the sprite that you want to find:", "");
            if (string.IsNullOrWhiteSpace(answer) || !int.TryParse(answer.Trim(), out int number)) return;
            SpriteFolder folder = Project.Game.RootSpriteFolder.FindFolderThatContainsSprite(number);
            if (folder == null)
            {
                EditorMessages.ShowWarning("Unable to display sprite " + number + ". Could not find a sprite with that number.", "Find sprite");
                return;
            }
            OpenSpriteFolder(folder);
            if (SelectedDocument is SpriteFolderDocument doc)
                doc.Selected = doc.Items.FirstOrDefault(t => t.Number == number);
        }

        /// <summary>Moves a sprite to another folder (instead of upstream's drag &amp; drop).</summary>
        [RelayCommand]
        private void MoveSpriteToFolder(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null) return;
            var folders = new List<(string Path, SpriteFolder Folder)>();
            void Collect(SpriteFolder f, string path)
            {
                folders.Add((path, f));
                foreach (SpriteFolder sub in f.SubFolders) Collect(sub, path + "/" + sub.Name);
            }
            Collect(Project.Game.RootSpriteFolder, Project.Game.RootSpriteFolder.Name);
            int index;
            if (ChooseFromList != null)
            {
                int? chosen = ChooseFromList("Move sprite to folder", folders.Select(f => f.Path).ToList());
                if (chosen == null) return;
                index = chosen.Value;
            }
            else
            {
                string answer = PromptText?.Invoke("Move sprite to folder — " + string.Join(", ", folders.Select((f, i) => $"{i}: {f.Path}")), "0");
                if (string.IsNullOrWhiteSpace(answer) || !int.TryParse(answer.Trim(), out index)) return;
            }
            if (index < 0 || index >= folders.Count) return;
            SpriteOperations.MoveSprites(Project.Game, new[] { thumbnail.Sprite }, folders[index].Folder);
            AppendOutput($"Moved sprite {thumbnail.Number} to {folders[index].Path}");
            SpriteFileReplaced(null);
        }

        private static bool IsInside(ISpriteFolder folder, ISpriteFolder ancestor) =>
            ancestor.SubFolders.Any(sub => sub == folder || IsInside(folder, sub));

        private void RetireSpriteFile()
        {
            if (_spriteFile != null)
                _retiredSpriteFiles.Add(_spriteFile);
            _spriteFile = null;
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task ExportSpriteAsync(SpriteThumbnail thumbnail)
        {
            if (thumbnail == null || Sprites == null || PickSaveFile == null) return;
            string file = await PickSaveFile($"sprite{thumbnail.Number}.png", new[] { "png", "bmp" });
            if (string.IsNullOrEmpty(file)) return;
            try
            {
                var palette = Project.Game.Palette.Select(p => p.Colour).ToArray();
                AGS.Imaging.ImageCodecs.Save(Sprites.LoadSprite(thumbnail.Number, palette), file);
                AppendOutput($"Exported sprite {thumbnail.Number} to {file}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] Export failed: {ex.Message}");
            }
        }

        private void OpenRoom(UnloadedRoom room)
        {
            DocumentViewModel open = Documents.FirstOrDefault(d => Equals(d.Key, room));
            if (open != null)
            {
                SelectedDocument = open;
                return;
            }
            NativeRoom native = null;
            try
            {
                string file = Path.Combine(Project.Directory, ProjectPaths.ToLocal(room.FileName));
                native = NativeRoom.Load(file);
                Room loaded = native.ToRoom(room, Project.Game.TextEncoding);
                var doc = new RoomEditorDocument(room, native, loaded, Project.Game, Sprites, item => PropertyGrid.Component = item);
                doc.MaskChanged += () =>
                {
                    IsModified = true;
                    StatusText = "Area painted (unsaved)";
                };
                doc.ItemMoved += item =>
                {
                    IsModified = true;
                    StatusText = "Moved (unsaved)";
                    PropertyGrid.Rebuild(); // show the new StartX/StartY
                };
                OpenDocument(doc);
                PropertyGrid.Component = loaded;
            }
            catch (Exception ex)
            {
                native?.Dispose();
                AppendOutput($"[Error] Could not open room {room.Number}: {ex.Message}");
            }
        }

        private void OpenDocument(DocumentViewModel document)
        {
            DocumentViewModel open = Documents.FirstOrDefault(d => Equals(d.Key, document.Key) && d.GetType() == document.GetType());
            if (open == null)
            {
                Documents.Add(document);
                open = document;
            }
            SelectedDocument = open;
        }

        /// <summary>
        /// A text processor that changes game text everywhere (make default language, speech numbering): saves the
        /// game, closes room and dialog editors (rooms are rewritten on disk), runs it, saves again and reloads open
        /// scripts — as upstream (SaveGameFiles, BusyDialog, OnRefreshAllComponentsFromGame, SaveGameFiles).
        /// </summary>
        private void RunGameTextChange(string successMessage, Func<CompileMessages> process)
        {
            try
            {
                Project.Save();
                SaveModifiedRooms();
                foreach (DocumentViewModel doc in Documents.Where(d => d is RoomEditorDocument || d is DialogEditorDocument).ToList())
                {
                    Documents.Remove(doc);
                    (doc as IDisposable)?.Dispose();
                }
                PropertyGrid.Component = null;
                CompileMessages messages = process();
                foreach (CompileMessage m in messages)
                    AppendOutput($"[{(m is CompileError ? "Error" : "Warning")}] {m.Message}");
                Project.Save();
                IsModified = false;
                ReloadOpenScripts();
                _completion?.RefreshGeneratedHeader();
                RebuildTree();
                AppendOutput(messages.HasErrors ? "[Error] The operation finished with errors; see above." : successMessage);
                StatusText = messages.HasErrors ? "Finished with errors" : successMessage;
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] {ex.Message}");
                StatusText = "Failed";
            }
        }

        /// <summary>
        /// Schema editor OK with renamed properties (CustomPropertiesComponent.ShowSchemaEditor): open rooms are saved
        /// and closed, then every character, inventory item and room file gets the new property names.
        /// </summary>
        public void ApplySchemaRenames(CustomPropertySchema newSchema, List<CustomPropertyDefChange> changes)
        {
            try
            {
                SaveModifiedRooms();
                foreach (DocumentViewModel doc in Documents.Where(d => d is RoomEditorDocument).ToList())
                {
                    Documents.Remove(doc);
                    (doc as IDisposable)?.Dispose();
                }
                CompileMessages errors = CustomPropertySchemaChanges.Apply(Project, newSchema, changes);
                foreach (CompileMessage m in errors)
                    AppendOutput($"[Error] {m.Message}");
                IsModified = true;
                AppendOutput("Custom properties renamed in all characters, inventory items and rooms.");
                object component = PropertyGrid.Component;
                PropertyGrid.Component = null;
                PropertyGrid.Component = component is Room ? null : component;
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] {ex.Message}");
            }
        }

        /// <summary>Open script editors show the scripts' current text (after a change outside the editor).</summary>
        private void ReloadOpenScripts()
        {
            foreach (ScriptDocument sd in Documents.OfType<ScriptDocument>().ToList())
            {
                var fresh = new ScriptDocument(sd.Script, sd.Highlighting, () => IsModified = true, sd.Completion);
                fresh.DefinitionRequested += GoToDefinition;
                fresh.UsagesRequested += FindAllUsagesInScript;
                fresh.BreakpointToggled += OnBreakpointToggled;
                bool selected = SelectedDocument == sd;
                Documents[Documents.IndexOf(sd)] = fresh;
                if (selected) SelectedDocument = fresh;
            }
        }

        /// <summary>Set by the app: the "Number Speech Lines" options (null = cancel).</summary>
        public Func<Game, (bool DoNarrator, bool CombineIdenticalLines, bool RemoveNumbering, int? CharacterID)?> AskSpeechNumbering { get; set; }

        /// <summary>Set by the app: a synchronous save panel (title, suggested path, type name, extension → path or null).</summary>
        public Func<string, string, string, string, string> PickSaveFileSync { get; set; }

        /// <summary>File → Auto-number speech lines… (FileCommandsComponent + ShowAutoNumberSpeechWizard).</summary>
        [RelayCommand(CanExecute = nameof(HasProject))]
        private void AutoNumberSpeech()
        {
            if (Project.Game.Translations.Count > 0 &&
                !Ask("Your game has one or more translations. If you proceed with re-numbering speech lines, this could break your translations of those lines. Are you sure you want to continue?"))
                return;
            var options = AskSpeechNumbering?.Invoke(Project.Game);
            if (options == null) return;
            var o = options.Value;
            RunGameTextChange("Speech lines numbered successfully.",
                () => SpeechTools.NumberSpeechLines(Project, o.DoNarrator, o.CombineIdenticalLines, o.RemoveNumbering, o.CharacterID));
        }

        /// <summary>File → Create voice acting script… (ShowCreateVoiceActingScriptWizard).</summary>
        [RelayCommand(CanExecute = nameof(HasProject))]
        private void CreateVoiceActingScript()
        {
            string suggested = Path.Combine(Project.Directory, "VoiceSpeech.txt");
            string file = PickSaveFileSync == null ? suggested : PickSaveFileSync("Select where you want to save the voice acting script.", suggested, "Text files", "txt");
            if (file == null) return;
            try
            {
                Project.Save();
                SaveModifiedRooms();
                IsModified = false;
                CompileMessages results = SpeechTools.CreateVoiceActingScript(Project, file);
                foreach (CompileMessage m in results)
                    AppendOutput($"[{(m is CompileError ? "Error" : "Warning")}] {m.Message}");
                AppendOutput(results.HasErrors
                    ? "[Error] There were errors creating the voice acting script."
                    : $"Voice acting script created successfully: {file}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[Error] There was an error creating the voice acting script: {ex.Message}");
            }
        }

        private void SaveModifiedRooms()
        {
            foreach (RoomEditorDocument room in Documents.OfType<RoomEditorDocument>().Where(r => r.IsModified))
            {
                string file = Path.Combine(Project.Directory, ProjectPaths.ToLocal(room.Unloaded.FileName));
                room.Save(file, Project.Game.TextEncoding);
                AppendOutput($"Saved {file}");
            }
        }

        [RelayCommand]
        private void CloseDocument(DocumentViewModel document)
        {
            if (document is RoomEditorDocument room && room.IsModified && Confirm != null &&
                Confirm("Unsaved room", $"Save changes to room {room.Unloaded.Number}?"))
            {
                SaveModifiedRooms();
            }
            Documents.Remove(document);
            (document as IDisposable)?.Dispose();
        }

        private void CloseProject()
        {
            _scriptWatcher?.Dispose();
            _scriptWatcher = null;
            _externalSpriteWatcher?.Dispose();
            _externalSpriteWatcher = null;
            _externalSprites.Clear();
            _externallyChanged.Clear();
            Project = null;
            TreeRoots.Clear();
            foreach (IDisposable d in Documents.OfType<IDisposable>().ToList())
                d.Dispose();
            Documents.Clear();
            SelectedNode = null;
            IsModified = false;
            _lastBuiltGame = null;
            _guiRendererReady = false;
            _spriteFile?.Dispose();
            _spriteFile = null;
            foreach (SpriteFile retired in _retiredSpriteFiles)
                retired.Dispose();
            _retiredSpriteFiles.Clear();
            _scriptHighlighting = null;
            _completion = null;
        }

        // ---- scripts changed outside the editor (AGSEditor._fileWatcher, ScriptEditor.Events_FileChangedInGameFolder) ----

        private FileSystemWatcher _scriptWatcher;
        private readonly List<Script> _externallyChanged = new List<Script>();

        /// <summary>Set by the view: whether the main window is active (prompts wait until it is, as upstream).</summary>
        public bool IsWindowActive { get; set; } = true;

        private void StartScriptWatcher(string directory)
        {
            try
            {
                _scriptWatcher = new FileSystemWatcher(directory)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };
                _scriptWatcher.Filters.Add("*.asc");
                _scriptWatcher.Filters.Add("*.ash");
                FileSystemEventHandler changed = (s, e) =>
                    Dispatcher.UIThread.Post(() => OnScriptFileChanged(Path.GetFileName(e.FullPath)));
                _scriptWatcher.Changed += changed;
                _scriptWatcher.Created += changed;
                _scriptWatcher.Renamed += (s, e) => changed(s, e); // editors that save through a temporary file
                _scriptWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                AppendOutput($"[Warning] Changes to scripts outside the editor won't be noticed: {ex.Message}");
            }
        }

        /// <summary>
        /// A script file in the game folder changed. Unless it is the editor's own save (same text on disk), an
        /// unedited script that isn't open is reloaded silently; an open one is offered for reloading once the
        /// window is active (ReloadScriptOnExternalChange = Prompt, the upstream default).
        /// </summary>
        public void OnScriptFileChanged(string fileName)
        {
            if (Project == null || string.IsNullOrEmpty(fileName)) return;
            Script script = ProjectScripts().FirstOrDefault(sc => string.Equals(sc.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            if (script == null || script.IsBeingSaved) return;
            string onDisk;
            try { onDisk = Script.TextEncoding.GetString(File.ReadAllBytes(Path.Combine(Project.Directory, script.FileName))); }
            catch (IOException) { return; } // still being written; another event follows
            if (onDisk == script.Text) return;
            bool open = Documents.OfType<ScriptDocument>().Any(d => d.Script == script);
            if (!open)
            {
                if (!script.Modified)
                {
                    script.LoadFromDisk();
                    AppendOutput($"Reloaded {script.FileName} (changed outside the editor)");
                }
                return;
            }
            if (!_externallyChanged.Contains(script))
                _externallyChanged.Add(script);
            if (IsWindowActive)
                PromptForExternallyChangedScripts();
        }

        /// <summary>The view calls this when the main window is activated (GUIController_OnMainWindowActivated).</summary>
        public void OnMainWindowActivated()
        {
            IsWindowActive = true;
            PromptForExternallyChangedScripts();
        }

        private void PromptForExternallyChangedScripts()
        {
            foreach (Script script in _externallyChanged.ToList())
            {
                _externallyChanged.Remove(script);
                if (!Ask($"The file '{script.FileName}' has been modified externally. Do you want to reload it?"))
                    continue;
                script.LoadFromDisk();
                foreach (ScriptDocument sd in Documents.OfType<ScriptDocument>().Where(d => d.Script == script).ToList())
                {
                    var fresh = new ScriptDocument(sd.Script, sd.Highlighting, () => IsModified = true, sd.Completion);
                    fresh.DefinitionRequested += GoToDefinition;
                    fresh.UsagesRequested += FindAllUsagesInScript;
                    fresh.BreakpointToggled += OnBreakpointToggled;
                    bool selected = SelectedDocument == sd;
                    Documents[Documents.IndexOf(sd)] = fresh;
                    if (selected) SelectedDocument = fresh;
                }
                AppendOutput($"Reloaded {script.FileName}");
            }
        }

        /// <summary>Script modules and headers, and room scripts that are loaded.</summary>
        private IEnumerable<Script> ProjectScripts()
        {
            foreach (ScriptAndHeader module in Project.Game.RootScriptFolder.AllItemsFlat)
            {
                yield return module.Header;
                yield return module.Script;
            }
            foreach (IRoom room in Project.Game.RootRoomFolder.AllItemsFlat)
                if (room is UnloadedRoom unloaded && unloaded.Script != null)
                    yield return unloaded.Script;
        }

        private void AppendOutput(string line)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => AppendOutput(line));
                return;
            }
            _output.AppendLine(line);
            OutputText = _output.ToString();
        }

        static MainWindowViewModel()
        {
            // Image formats for sprite import/export: Magick.NET first, upstream's native codecs override BMP/PCX/PNG.
            MagickImageCodec.RegisterAll();
            try
            {
                NativeImageCodec.RegisterAll();
            }
            catch (Exception)
            {
                // libagsnative missing: rooms and native codecs won't work; reported when used.
            }
        }
    }
}
