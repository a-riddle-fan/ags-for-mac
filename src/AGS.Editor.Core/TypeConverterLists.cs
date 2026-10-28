using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// Feeds the AGS.Types property-grid TypeConverters the game's current lists (scripts, fonts, GUIs,
    /// audio clips, rooms, translations), as the upstream editor's components do after loading and after
    /// changes (ScriptsComponent, FontsComponent, GuiComponent, AudioComponent, RoomsComponent, ...).
    /// </summary>
    public static class TypeConverterLists
    {
        public static void Refresh(Game game)
        {
            ScriptListTypeConverter.SetScriptList(game.ScriptsAndHeaders);
            FontTypeConverter.SetFontList(game.Fonts);
            GUIIndexTypeConverter.SetGUIList(game.GUIs);
            AudioClipTypeConverter.SetAudioClipList(game.RootAudioClipFolder.GetAllAudioClipsFromAllSubFolders());
            AudioClipTypeTypeConverter.SetAudioClipTypeList(game.AudioClipTypes);
            RoomListTypeConverter.SetRoomList(game.Rooms);
            TranslationListTypeConverter.SetTranslationsList(game.Translations);
            RoomMaskResolutionTypeConverter.SetResolutionRange(1, 4);
        }
    }
}
