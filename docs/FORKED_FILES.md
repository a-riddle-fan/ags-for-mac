# Forked and replaced upstream files

The `src/` projects compile upstream sources from `ags/` in place. This file lists every upstream file
that is **not** compiled as-is, and why. When merging a new upstream version, re-check each entry:
diff the upstream file against the version it was forked from and carry changes over.

Forked from upstream `master` @ `b99b05272` (AGS 3.6.4 dev).

## Mechanisms

| Mechanism | Where | Use for |
|---|---|---|
| **Fork** | `src/<Project>/Forked/<same relative path>` + `<AgsTypesForked Include=…>` in the csproj | Real API changes (WinForms/GDI types in signatures, UI behaviour). Every fork starts with a `// ags4mac: forked from …` header naming the change. |
| **Exclude** | `Exclude=` on the linked `<Compile>` | Pure WinForms UI with no replacement in that project (rebuilt in Avalonia, Phase 2). |
| **Source rewrite** | `<AgsSourceRewrite>` in the csproj, task in `Directory.Build.targets` | Small mechanical one-line fixes that would otherwise force forking many files. Rewritten copies go to `obj/…/ags-rewritten/`, with `#line` mapping back to upstream. A rule that matches nothing produces a build warning. |
| **Compat shim** | `src/<Project>/Compat/` | Neutral stand-ins for framework types that don't exist on macOS. |

## AGS.Types → `src/AGS.Types.Core`

### Forks
| Upstream file | Change |
|---|---|
| `EditorFeatures/EditorContentPanel.cs` | No longer derives from WinForms `UserControl`; holds the toolkit control in `View`, focus via `HasFocus`, first-show via `NotifyShownAtFullSize()`. |
| `EditorFeatures/ContentDocument.cs` | Unused WinForms `using` removed. |
| `EditorFeatures/MenuCommand.cs` | `Keys` resolves to `AGS.Types.Keys`. |
| `EditorFeatures/BaseTemplate.cs`, `GameTemplate.cs`, `RoomTemplate.cs` | `System.Drawing.Icon` → `AGS.Imaging.IImage`. |
| `Interfaces/Docking/IDockingContainer.cs` | `Icon` → `IImage`; `ContextMenuStrip` → `object`. |
| `Interfaces/IScriptEditorControl.cs` | `Control` → `object`. |
| `Interfaces/IGUIController.cs` | `Icon` → `IImage`; `Graphics` → `AGS.Imaging.IDrawingContext`. |
| `Interfaces/IRoomController.cs` | `Graphics` → `IDrawingContext`. |
| `Plugins/IAGSEditor.cs` | `Bitmap` → `IImage`. |
| `PropertyGridExtras/ColorUIEditor.cs` | GDI+ `PaintValue` → `GetSwatchColor(value)`. |
| `PropertyGridExtras/ScriptFunctionUIEditor.cs` | `MessageBox` → `EditorMessages.ShowWarning`. |
| `PropertyGridExtras/StringListUIEditor.cs` | WinForms drop-down → `StringListGUI` hook + `CanUncheck`. |
| `PropertyGridExtras/BuildTargetUIEditor.cs` | `ItemCheck` handler → `CanUncheck` override. |
| `PropertyGridExtras/SpriteFileNameEditor.cs` | WinForms `FileNameEditor` → `SpriteFileNameGUI` hook. |
| `PropertyGridExtras/PropertyTabEvents.cs`, `PropertyTabInteractions.cs` | WinForms `PropertyTab` → `AgsPropertyTab`; tab bitmap dropped. |

### Excluded
| Upstream file | Reason |
|---|---|
| `PropertyGridExtras/StringListUIEditorControl.cs` (+ `.Designer.cs`) | WinForms drop-down control; Avalonia version in Phase 2. |
| `PropertyGridExtras/event.bmp` (resource) | Only used by the WinForms property tabs. |

### Source rewrites
| Rule | Effect | Files hit |
|---|---|---|
| `ansi-encoding` | `Encoding.Default` → `AGS.Types.AnsiEncoding.Default` (Windows-1252 by default instead of .NET's UTF-8). | `Utilities.cs`, `Translation.cs` |

### Compat shims (`src/AGS.Types.Core/Compat`)
| File | Provides |
|---|---|
| `DesignTimeEditors.cs` | `System.Drawing.Design.UITypeEditor`, `UITypeEditorEditStyle` (UI-neutral). |
| `NamespacePlaceholders.cs` | Empty `System.Windows.Forms[.Design]` namespaces for leftover unused `using`s. They declare no API. |
| `Keys.cs` | `AGS.Types.Keys` with WinForms-compatible values. |
| `AgsPropertyTab.cs` | Base class for property-grid tabs. |
| `EditorMessages.cs` | `ShowWarning` hook replacing `MessageBox`. |
| `AnsiEncoding.cs` | ANSI code page + code-page provider registration. |

### Source rewrites in AGS.Editor.Core
| Rule | Effect |
|---|---|
| `portable-invalid-filename-chars` | `Path.GetInvalidFileNameChars()` → `PortablePaths.InvalidFileNameChars` (Windows set) |
| `portable-invalid-path-chars` | `Path.GetInvalidPathChars()` → `PortablePaths.InvalidPathChars` (Windows set) |
| `process-all-game-texts-hook` | `Factory.AGSEditor.RunProcessAllGameTextsEvent` → `TextProcessingHooks.RunProcessAllGameTextsEvent` (room texts) |
| `resource-text` | `Resources.ResourceManager.GetResourceAsString` → `EditorResources.ReadText` |
| `no-remoting-using` | drops the unused `using System.Runtime.Remoting.Contexts;` (not in .NET Core) |
| `spritesheet-image` | `SpriteSheet.SelectionFitsWithinBitmap(Rectangle, Bitmap)` takes an `AGS.Imaging.IImage` (no GDI+). |
| `speech-processor-reparse` | **Upstream bug fix** in `GameSpeechProcessor.ProcessScript`: after a string was replaced, parsing continued on the old text (ParserState keeps the original script), so changing processors (make default language, speech numbering) threw or looped forever. Now re-creates the state via `ParserStateFix.Rebuild`. |

## AGS.CScript.Compiler → `src/AGS.CScript.Compiler`

Compiled entirely as-is (no forks).

## AGS.Editor → `src/AGS.Editor.Core`

Upstream `AGS.Editor` is a WinForms application organised around singletons (`Factory.AGSEditor`,
`Factory.GUIController`, `Factory.NativeProxy`, `Factory.Events`). Editor.Core does not recreate those
singletons; it ports the UI-free logic into explicit services and replaces UI calls with `IEditorHost`.

### Compiled as-is
| Upstream file | Used for |
|---|---|
| `Utils/ScriptGeneration.cs` | `InsertFunction` (post-load script upgrade, event handler creation) |
| `Utils/Validation.cs` (with source rewrites, below) | file name checks |
| `AutoComplete.cs`, `Enums/BackgroundAutoCompleteStatus.cs`, `Entities/AutoCompleteParserState.cs` | script parser for autocomplete (`ScriptAutoCompleteData`) |
| `GUI/CustomPropertyBag.cs` | property grid object of the custom properties dialog |
| `Utils/IncludeUtils.cs` | template.files patterns |
| `Entities/DebugCallStack.cs` | debugger call stacks |
| `TextProcessing/APITypeDef.cs` | global variable types |
| `TextProcessing/TextImporter.cs`, `TextImportProcessor.cs`, `BaseTextProcess.cs`, `SpeechOnlyProcessor.cs`, `SpeechLineProcessor.cs`, `VoiceActorScriptGenerator.cs`, `VoiceActorScriptProcessor.cs`, `Enums/FunctionCallType.cs` | make default language, speech numbering, voice acting script |
| `TextProcessing/TextProcessingHelper.cs`, `GameSpeechProcessor.cs`, `TranslationSourceProcessor.cs`, `TranslationGenerator.cs`, `GameTextLine.cs`, `IGameTextProcessor.cs`, `Utils/ScriptParsing.cs`, `Utils/DialogScriptConverter.cs` (+ `Resources/__DialogScripts.asc`) | translation source generation |
| `Resources/agsdefns.sh` (embedded) | built-in script API for highlighting and autocomplete |
| `Resources/AGSFNT0..2.WFN` (embedded, same resource names) | default fonts added to projects that lack them |

### Forks
| Upstream source | Fork | Change |
|---|---|---|
| `Tasks.cs` lines 302–942 (`HandleObjectIDErrors*`, `SetDefaultGameContentIfMissing`, `SetDefaultValuesForNewFeatures`, helpers) | `Forked/GameUpgrader.cs` | Instance-free (explicit game + directory), `GUIController.ShowMessage` → `IEditorHost`, `GetFirstAvailableScriptName` → `ScriptNames`, `Utilities.FixupSpriteResolution`/`AGSEditor.BaseGameFileName` evaluated for the loaded game, fonts from embedded resources. |
| `Utils/ColorMapper.cs` | `Forked/ColorMapper.cs` | `AGSEditor` dependency → `Func<Game>`. |
| `Tasks.cs` `RegenerateScriptHeader` + `Append*ToHeader` | `Forked/AutoGeneratedHeader.cs` | Unchanged code in its own class (builds `_AutoGenerated.ash`). |
| `TextProcessing/SpeechLinesNumbering.cs` | `Forked/SpeechLinesNumbering.cs` | Takes a `GameProject`; no write-access check; errors thrown instead of shown through the GUI controller; rooms via `TextProcessingHooks`; speechref.txt in the project folder. |

### Ports (rewritten from upstream methods, upstream structure kept)
| Upstream | Port |
|---|---|
| `AGSEditor.LoadGameFile`, `LoadUserDataFile`, `VerifyGameNotAlreadyOpenInAnotherEditor`, `SaveGameFilesProcess` (project file part), `SaveUserDataFile`, `WriteMainGameFile`; `Tasks.LoadGameFromDisk`, `InitSpritesAfterGameLoad` (check only); `GamePostLoad` handlers of `ApplicationController` and `ScriptsComponent`; `ComponentController` save/load of "Component" sections (kept verbatim) | `GameProject.cs` |
| `AGSEditor.GetFirstAvailableScriptName` | `ScriptNames.cs` |
| `Utilities.GetRelativeToBasePath`, `AudioComponent.GetCacheFileName` | `ProjectPaths.cs` |
| `Resources/ResourceManager.CopyFileFromResourcesToDisk` | `EditorResources.cs` |
| `BuildTargets/*` (names only) | `Build/KnownBuildTargets.cs` |
| `AGSEditor.GetImportedScriptHeaders`, `Panes/ScintillaWrapper.cs` completion lists, `ShouldShowThis`, `ParsePreviousExpression` (member chains) | `ScriptCompletion.cs` |
| `Components/CharactersComponent`, `InventoryComponent`, `ScriptsComponent` (new/rename/delete module), `BaseComponentWithFolders` (add/delete items, folders, ID renumbering) | `ProjectOperations.cs` |
| Component calls of `*TypeConverter.Set*List` | `TypeConverterLists.cs` |
| `Utils/SpriteTools.cs` import of new sprites / reimport / tiled ImportNewSprites (conversion delegated to upstream's `spriteimport` tool; `Utils/SpriteSheet.cs` compiled as-is), `SpriteImportWindow` tiling options | `SpriteImporter.cs`, `EditorDialogs.AskSpriteSheet` |
| `AGSEditor.DeleteSprite`, `SpriteSelector.DeleteSelectedSprites`, `SpriteTools.GetSpriteUsageReport` (copied unchanged), `RoomsComponent.AGSEditor_PreDeleteSprite` | `SpriteOperations.cs` (+ room check in the Avalonia view model) |
| `AGS.Native/agsnative.cpp` `ImportBackground`/`DeleteBackground`/`CreateBlockFromBitmap`/`Convert8BitToHiColor`/`remap_background`; `RoomSettingsEditor.ImportBackground`/`ExtendBitmapIfSmallerThanScreen`/`OnRoomSizeChanged` | `native/shim/agsn_room_background.cpp`, `NativeRoom.ImportBackground`, `RoomEditorDocument.ImportBackground` |
| `RoomsComponent.CreateNewRoom`/`CreateEmptyRoom`/`PromptForAndDeleteAnyExistingRoomFile`, `NewRoomDialog` number suggestion, `agsnative.cpp` `save_default_crm_file` | `RoomOperations.cs`, `NativeRoom.CreateBlank` (+ `agsn_room_create`) |
| `RoomsComponent.DeleteRoom`/`EnsureNoCharactersStartInRoom` | `RoomOperations.DeleteRoom` |
| `RoomsComponent.ImportExistingRoomFile`/`CopyScriptOutOfOldRoomFile`, `agsnative.cpp` `load_room_script` | `RoomOperations.ImportRoomFile`, `NativeRoom.LoadRoomScript` (+ `agsn_room_extract_script_text`) |
| `AudioComponent.ImportAudioFiles`/`CreateAudioClipForFile`/`SetAudioClipSourceFile`/`EnsureScriptNameIsUnique` | `AudioOperations.cs` |
| `FontsComponent` new/delete/`ImportFont`/`ImportTTFFont`/`ImportWFNFont`, `Utilities.CopyFont`; `RefreshDataFromGame` ProjectFilename fixup | `FontOperations.cs`; fixup in `GameProject.Load` |
| `GUIController.ShowPropertiesEditorFromPropertyGrid` | `CustomPropertiesEditing.cs` |
| `TranslationsComponent` (UpdateSingleTranslationProcess and UpdateAllTranslationsWithNewDefaultText unchanged; new/rename/delete/update, ReplaceGameTextWithTranslation), `RoomsComponent.AGSEditor_ProcessAllGameTexts` (also for changing processors: rooms saved through libagsnative) | `Forked/TranslationUpdater.cs` |
| `GUIController.AutoNumberSpeechLinesProcess`/`CreateVoiceActingScriptProcess`/`WriteLinesForCharacter`/`WriteLineIfItHasVoiceFile`, `AutoNumberSpeechWizardPage(2)`, `CreateVoiceActingScriptPage` | `SpeechTools.cs`, `EditorDialogs.AskSpeechNumbering`, File menu commands |
| `FontsComponent.ReimportFontSize`/`ImportFontFile`, `ImportTTFDialog`, `NativeMethods.FindTTFSizeForHeight` | view model font hooks, `EditorDialogs.AskTtfSize`, `agsn_font_find_ttf_size` |
| `GUI/RoomMessagesEditor` | `EditorDialogs.EditRoomMessages` |
| `Panes/SpriteSelector` folder menu (create/rename/delete folder, VerifySpriteFolderTreeCanBeDeleted), Find by number, Show usage, ChangeSpriteNumber/AssignSequentialNumbers, AssignSpritesToView (+ `GUI/AssignToView`), `NativeProxy.ChangeSpriteNumbers`/`FindNearestFreeSpriteNumber` | `SpriteOperations.cs`, `EditorDialogs.AskAssignToView`/`ChooseFromList`, view model commands, sprite context menu |
| `AGSEditor._fileWatcher`, `ScriptEditor.Events_FileChangedInGameFolder`/`PromptUserThatFileHasChangedExternally`/`GUIController_OnMainWindowActivated` | `MainWindowViewModel.OnScriptFileChanged`/`OnMainWindowActivated` |
| `Utils/SpriteTools.cs` ExportSprite/ExportSprites/ExpandExportPath/ExportSpritesOptions/SkipIf, `GUI/SpriteExportDialog`, SpriteSelector ExportFixupSources/AssignSequentialNumbers | `SpriteExport.cs`, `EditorDialogs.AskSpriteExport`, view model commands |
| SpriteSelector Copy to clipboard / Paste new / Replace from clipboard, LaunchImageEditorForSprite, Open File Explorer, ReplaceSourceFolderForSprites (+ `Utilities.ReplacePathBase*`, `GUI/ReplaceFolderDialog`) | view model commands, `ImageConversion.FromBitmap`, `SpriteOperations.ReplaceSourcePaths`, `EditorDialogs.AskReplaceFolder` |
| `BaseAreasEditorFilter.ImportMaskFromFile`/`ExportMaskFromFile`, `agsnative.cpp` `import_area_mask`/`validate_mask`/`export_area_mask` | `RoomEditorDocument.ImportMask`/`ExportMask`, view model commands |
| `CustomPropertiesComponent.ShowSchemaEditor` (rename part), `Tasks.HandleCustomPropertySchemaChanges`/`ApplyCustomPropertySchemaChanges(InRoom)` (copied), `CustomPropertyDefChange(Type)` (copied), `CustomPropertySchemaItemEditor.btnRename_Click` | `CustomPropertySchemaChanges.cs`, `EditorDialogs.EditPropertySchema`, `MainWindowViewModel.ApplySchemaRenames` |
| `AGS.Native/agsnative.cpp` GUI preview (globals, draw_gui_sprite & co., GameFontUpdated, ConvertGUIToBinaryFormat, drawGUI/drawGUIAt); `Editor/Native/acgui_agsnative.cpp` compiled as-is | `native/shim/agsn_gui.cpp`, `NativeGuiRenderer.cs` |
| `Panes/GlobalVariablesEditor`, `GUI/GlobalVariableDialog`, `GlobalVariablesComponent.UpdateScriptHeader` | `GlobalVariableEditing.cs`, `GlobalVariablesDocument.cs`, `EditorDialogs.EditGlobalVariable`, `ScriptCompletion.GlobalVariablesHeader` |
| `TextProcessing/FindReplace` (Current project scope, Find All / Replace All), `ScriptEditorBase.GoToDefinition`/`FindTokenInScript` | `ScriptSearch.cs`, `ScriptCompletion.FindDefinition`, `FindInScriptsDocument.cs` |
| `Panes/TextParserEditor`, `LipSyncEditor`, `PaletteEditor` (colour finder) | `ViewModels/SmallEditorDocuments.cs` |
| `Panes/DialogEditor` (btnNewOption_Click, btnDeleteOption_Click), `DialogOptionEditor` | `DialogOperations.cs`, `DialogEditorDocument.cs`, `Views/DialogEditorView` |
| `Panes/ViewEditor`, `ViewLoopEditor` (frame insert/delete/flip/reverse, loops, NewFrameAdded), `ViewPreview` | `ViewOperations.cs`, `ViewEditorDocument.cs`, `Views/ViewEditorView` |
| `Panes/FontEditor` preview, `agsnative.cpp` DrawTextUsingFontAt/GetFontMetrics | `FontDocument.cs`, `agsn_font_draw_text`/`agsn_font_get_metrics` |
| `FontsComponent.ImportWFNFont` (SCI part), `agsnative.cpp` `import_sci_font` (writes to a given file instead of the current folder) | `FontOperations.ImportFont`, `agsn_font_import_sci` in `native/shim/agsn_gui.cpp`, `NativeGuiRenderer.ImportSciFont` |
| `Panes/GUIEditor` (selection, move/resize, CreateNewControl, DeleteControlClick, z-order) | `GuiEditorDocument.cs`, `Views/GuiCanvas.cs` (Avalonia) |
| `Tasks.CreateNewGameFromTemplate`/`CreateTemplateFromCurrentGame`/`ConstructBasicFileListForTemplate`, `GUIController.CreateNewGame`, `agsnative.cpp` `extract_template_files*`, `Utilities.EnsureStandardSubFoldersExist` | `GameTemplates.cs`, `native/shim/agsn_template.cpp` (packing with agspak instead of DataFileWriter) |
| `Debugger/DebugController`, `FileBasedEngineCommunication`, `Tasks.TestGame(withDebugger)` | `Build/EngineDebugger.cs` (+ engine patch 0007), debug commands in the view model |
| `CursorsComponent` new/delete | `ProjectOperations.AddCursor`/`DeleteCursor` |
| `AudioComponent.ReplaceAudioClipSource` | `EditorDialogs.ReplaceAudioClipSource` (+ `AudioOperations.SetAudioClipSourceFile`) |
| `CustomPropertiesComponent.SyncCustomPropertiesWithSchema` | `GameProject.SyncCustomPropertiesWithSchema` |
| `GUI/CustomPropertiesEditor`, `CustomPropertySchemaEditor`, `CustomPropertySchemaItemEditor` (WinForms) | `EditorDialogs.EditCustomProperties`/`EditPropertySchema` (Avalonia) |
| `AGS.Native/agsnative.cpp` `convert_room_from_native`/`convert_room_to_native`/`save_room_file`/`CopyInteractions`/`CompileCustomProperties` | `src/AGS.Native.Interop/NativeRoom.cs` (+ `native/shim/agsn_room.cpp`) |

### Compatibility rules established by the port
- **Current directory = game folder** while a project is open (upstream relies on it, e.g. `Script.LoadFromDisk`).
- **Stored paths are never rewritten.** Windows projects store `music\music1.mid`; use `ProjectPaths.ToLocal` only when accessing files.
- **XML encoding name** is written with .NET Framework casing (`Windows-1252`, not .NET's `windows-1252`) so files saved on macOS and Windows don't differ.
- **ANSI** means `AnsiEncoding.Default` (Windows-1252 unless configured), never .NET's UTF-8 `Encoding.Default`.
- **All upstream build target names are registered**, so `Settings.BuildTargets` is preserved.
- **Unknown component sections are preserved verbatim** (upstream would drop them with a warning).
- **Line breaks of new texts are CR LF** where upstream uses `Environment.NewLine` for data stored in Game.agf (new dialog scripts, dialog option entry points follow the script's existing line breaks), so files match the Windows editor.
- **New stored paths use `\`** (`ProjectPaths.ToStored`): relative source paths of new sprites/audio clips are written as the Windows editor writes them (`Sounds\door.ogg`), so a project edited on both platforms doesn't change back and forth. Absolute paths stay as they are.
- **File names must be valid on Windows too.** .NET on macOS only rejects `/` and NUL; the source rewrites `portable-invalid-filename-chars`/`portable-invalid-path-chars` make upstream checks use Windows' sets (`PortablePaths`), so e.g. `Foo:Bar.asc` is rejected on macOS as well.
