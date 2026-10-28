using System;
using System.Collections.Generic;
using System.Linq;
using AGS.Types;

namespace AGS.Editor.Avalonia.ViewModels
{
    /// <summary>
    /// Builds the project explorer for a game, with the sections in the order the upstream editor's
    /// components add them (ApplicationController.CreateComponents).
    /// </summary>
    public static class ProjectTreeBuilder
    {
        public static ProjectTreeNode Build(Game game)
        {
            var root = new ProjectTreeNode(game.Settings.GameName, ProjectNodeKind.Section, game) { IsExpanded = true };

            root.Add(new ProjectTreeNode("General Settings", ProjectNodeKind.Item, game.Settings));
            root.Add(new ProjectTreeNode("Default Setup", ProjectNodeKind.Item, game.DefaultSetup));
            root.Add(new ProjectTreeNode("Colours", ProjectNodeKind.Item, game.Palette));

            var sprites = root.Add(new ProjectTreeNode("Sprites", ProjectNodeKind.Section, game.RootSpriteFolder));
            AddSpriteFolders(sprites, game.RootSpriteFolder);

            root.Add(new ProjectTreeNode("Text Parser", ProjectNodeKind.Item, game.TextParser));
            root.Add(new ProjectTreeNode("Lip Sync", ProjectNodeKind.Item, game.LipSync));

            var cursors = root.Add(new ProjectTreeNode("Mouse Cursors", ProjectNodeKind.Section));
            foreach (MouseCursor c in game.Cursors)
                cursors.Add(new ProjectTreeNode($"{c.ID}: {c.Name}", ProjectNodeKind.Item, c));

            var characters = root.Add(new ProjectTreeNode("Characters", ProjectNodeKind.Section, null, game.RootCharacterFolder));
            AddFolder(characters, game.RootCharacterFolder, (Character c) => $"{c.ID}: {c.ScriptName}");

            var inventory = root.Add(new ProjectTreeNode("Inventory Items", ProjectNodeKind.Section, null, game.RootInventoryItemFolder));
            AddFolder(inventory, game.RootInventoryItemFolder, (InventoryItem i) => $"{i.ID}: {i.Name}");

            root.Add(new ProjectTreeNode("Global Variables", ProjectNodeKind.Item, game.GlobalVariables));

            var dialogs = root.Add(new ProjectTreeNode("Dialogs", ProjectNodeKind.Section, null, game.RootDialogFolder));
            AddFolder(dialogs, game.RootDialogFolder, (Dialog d) => $"{d.ID}: {d.Name}");

            var guis = root.Add(new ProjectTreeNode("GUIs", ProjectNodeKind.Section, null, game.RootGUIFolder));
            AddFolder(guis, game.RootGUIFolder, (GUI g) => $"{g.ID}: {g.Name}");

            var fonts = root.Add(new ProjectTreeNode("Fonts", ProjectNodeKind.Section));
            foreach (AGS.Types.Font f in game.Fonts)
                fonts.Add(new ProjectTreeNode($"{f.ID}: {f.Name}", ProjectNodeKind.Item, f));

            var views = root.Add(new ProjectTreeNode("Views", ProjectNodeKind.Section, null, game.RootViewFolder));
            AddFolder(views, game.RootViewFolder, (View v) => $"{v.ID}: {v.Name}");

            var audio = root.Add(new ProjectTreeNode("Audio", ProjectNodeKind.Section, null, game.RootAudioClipFolder));
            var types = audio.Add(new ProjectTreeNode("Audio Types", ProjectNodeKind.Folder));
            foreach (AudioClipType t in game.AudioClipTypes)
                types.Add(new ProjectTreeNode($"{t.TypeID}: {t.Name}", ProjectNodeKind.Item, t));
            AddFolder(audio, game.RootAudioClipFolder, (AudioClip a) => a.ScriptName);

            var scripts = root.Add(new ProjectTreeNode("Scripts", ProjectNodeKind.Section, null, game.RootScriptFolder) { IsExpanded = true });
            AddFolder(scripts, game.RootScriptFolder, (ScriptAndHeader s) => s.Name, AddScriptPair);

            var rooms = root.Add(new ProjectTreeNode("Rooms", ProjectNodeKind.Section, null, game.RootRoomFolder) { IsExpanded = true });
            AddFolder(rooms, game.RootRoomFolder, (IRoom r) => $"{r.Number}: {r.Description}");

            var translations = root.Add(new ProjectTreeNode("Translations", ProjectNodeKind.Section));
            foreach (Translation t in game.Translations)
                translations.Add(new ProjectTreeNode(t.Name, ProjectNodeKind.Item, t));

            return root;
        }

        // Like upstream: one node per module, with its header and script below.
        private static void AddScriptPair(ProjectTreeNode parent, ScriptAndHeader pair)
        {
            var module = parent.Add(new ProjectTreeNode(pair.Name, ProjectNodeKind.Folder, pair));
            module.Add(new ProjectTreeNode(pair.Header.FileName, ProjectNodeKind.Item, pair.Header));
            module.Add(new ProjectTreeNode(pair.Script.FileName, ProjectNodeKind.Item, pair.Script));
        }

        private static void AddFolder<TItem, TFolder>(ProjectTreeNode parent, BaseFolderCollection<TItem, TFolder> folder,
            Func<TItem, string> label, Action<ProjectTreeNode, TItem> addItem = null)
            where TFolder : BaseFolderCollection<TItem, TFolder>
            where TItem : class, IToXml
        {
            foreach (TFolder sub in folder.SubFolders)
            {
                var node = parent.Add(new ProjectTreeNode(sub.Name, ProjectNodeKind.Folder, sub, sub));
                AddFolder(node, sub, label, addItem);
            }
            foreach (TItem item in folder.Items)
            {
                if (addItem != null)
                    addItem(parent, item);
                else
                    parent.Add(new ProjectTreeNode(label(item), ProjectNodeKind.Item, item));
            }
        }

        private static void AddSpriteFolders(ProjectTreeNode parent, ISpriteFolder folder)
        {
            foreach (ISpriteFolder sub in folder.SubFolders)
            {
                var node = parent.Add(new ProjectTreeNode($"{sub.Name} ({sub.Sprites.Count})", ProjectNodeKind.Folder, sub));
                AddSpriteFolders(node, sub);
            }
        }
    }
}
