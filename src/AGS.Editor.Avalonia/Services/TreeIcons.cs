using System;
using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media.Imaging;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Types;

namespace AGS.Editor.Avalonia.Services
{
    /// <summary>
    /// Project tree icons: the upstream editor's .ico files (embedded unchanged), chosen as the upstream
    /// components register them (RegisterIcon in Components/*.cs).
    /// </summary>
    public static class TreeIcons
    {
        private static readonly ConcurrentDictionary<string, Bitmap> Cache = new ConcurrentDictionary<string, Bitmap>();

        public static Bitmap For(ProjectTreeNode node)
        {
            string name = IconName(node);
            return name == null ? null : Load(name);
        }

        public static Bitmap Load(string iconFile)
        {
            return Cache.GetOrAdd(iconFile, file =>
            {
                try
                {
                    using Stream stream = typeof(TreeIcons).Assembly.GetManifestResourceStream("AGSEditor.Icons." + file);
                    return stream == null ? null : new Bitmap(stream);
                }
                catch (Exception)
                {
                    return null; // unsupported icon format: no icon
                }
            });
        }

        private static string IconName(ProjectTreeNode node)
        {
            switch (node.Payload)
            {
                case Game _: return "game.ico";
                case Settings _: return "iconsett.ico";
                case RuntimeSetup _: return "menu_build_gamesetup.ico";
                case PaletteEntry[] _: return "iconpal.ico";
                case TextParser _: return "textparser.ico";
                case LipSync _: return "lips.ico";
                case GlobalVariables _: return "globalvars.ico";
                case Character _: return "charactr-item.ico";
                case InventoryItem _: return "iconinv-item.ico";
                case MouseCursor _: return "cursor-item.ico";
                case Dialog _: return "dialog-item.ico";
                case GUI _: return "icongui-item.ico";
                case Font _: return "font-item.ico";
                case View _: return "view-item.ico";
                case AudioClipType _: return "tree_audio_generic.ico";
                case AudioClip clip:
                    switch (clip.FileType)
                    {
                        case AudioClipFileType.OGG: return "audio-ogg.ico";
                        case AudioClipFileType.MP3: return "audio-mp3.ico";
                        case AudioClipFileType.WAV: return "audio-wav.ico";
                        case AudioClipFileType.VOC: return "audio-voc.ico";
                        case AudioClipFileType.MIDI: return "audio-midi.ico";
                        case AudioClipFileType.MOD: return "audio-mod.ico";
                        default: return "tree_audio_generic.ico";
                    }
                case Script _: return "script.ico";
                case UnloadedRoom _: return "room-item.ico";
                case Translation _: return "translations.ico";
                case ISpriteFolder _ when node.Kind != ProjectNodeKind.Section: return "folder.ico";
            }
            if (node.Kind == ProjectNodeKind.Folder)
                return "folder.ico";
            switch (node.Name)
            {
                case "Sprites": return "iconspr.ico";
                case "Mouse Cursors": return "cursor.ico";
                case "Characters": return "charactr.ico";
                case "Inventory Items": return "iconinv.ico";
                case "Dialogs": return "dialog.ico";
                case "GUIs": return "icongui.ico";
                case "Fonts": return "font.ico";
                case "Views": return "view.ico";
                case "Audio": return "audio.ico";
                case "Scripts": return "scripts.ico";
                case "Rooms": return "room.ico";
                case "Translations": return "translations.ico";
            }
            return null;
        }
    }
}
