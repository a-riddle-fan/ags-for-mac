using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using AGS.Imaging;
using AGS.Types;

namespace AGS.Native
{
    /// <summary>Room area masks, same values as AGS::Common::RoomAreaMask.</summary>
    public enum RoomMaskType
    {
        Hotspots = NativeMethods.AGSN_MASK_HOTSPOTS,
        WalkBehinds = NativeMethods.AGSN_MASK_WALKBEHINDS,
        WalkableAreas = NativeMethods.AGSN_MASK_WALKABLE,
        Regions = NativeMethods.AGSN_MASK_REGIONS,
    }

    /// <summary>
    /// A room file loaded by upstream's native code (the equivalent of the native RoomStruct the
    /// upstream editor keeps behind Room._roomStructPtr). Holds the binary content (backgrounds, masks,
    /// compiled script); <see cref="ToRoom"/> converts the editable fields to the AGS.Types model.
    /// </summary>
    public sealed class NativeRoom : IDisposable
    {
        // ags/Common/game/room_version.h
        private const int RoomVersion300a = 26;
        // ags/Common/ac/common_defines.h, ags/Common/game/roomdata.h
        private const int OBJF_NOINTERACT = 0x01;
        private const int OBJF_USEREGIONTINTS = 0x08;
        private const int OBJF_USEROOMSCALING = 0x10;
        private const int OBJF_SOLID = 0x20;
        private const int MSG_DISPLAYNEXT = 0x01;
        private const int MSG_TIMELIMIT = 0x02;
        private const int NOT_VECTOR_SCALED = -10000;


        private IntPtr _handle;
        private NativeMethods.RoomInfo _info;

        private NativeRoom(IntPtr handle)
        {
            _handle = handle;
            NativeImage.Check(NativeMethods.RoomGetInfo(handle, out _info));
        }

        /// <summary>
        /// Loads a room file as the upstream editor does. <paramref name="gameIsHires"/> and
        /// <paramref name="sprites"/> are only needed to upgrade rooms from legacy high-resolution games.
        /// </summary>
        public static unsafe NativeRoom Load(string fileName, bool gameIsHires = false, IReadOnlyList<(int Width, int Height, uint Flags)> sprites = null)
        {
            NativeLibraryInfo.EnsureCompatible();
            NativeMethods.SpriteMetric[] metrics = sprites?.Select(s => new NativeMethods.SpriteMetric { Width = s.Width, Height = s.Height, Flags = s.Flags }).ToArray();
            IntPtr handle;
            fixed (NativeMethods.SpriteMetric* p = metrics)
                handle = NativeMethods.RoomLoad(fileName, gameIsHires ? 1 : 0, p, metrics?.Length ?? 0);
            if (handle == IntPtr.Zero)
                throw new AGSEditorException(NativeMethods.GetLastError());
            return new NativeRoom(handle);
        }

        /// <summary>
        /// Creates a room file for <paramref name="room"/> with black backgrounds and empty masks, as the upstream
        /// editor's SaveDefaultRoom (save_default_crm_file) for "Blank Room".
        /// </summary>
        public static NativeRoom CreateBlank(Room room, Encoding gameTextEncoding)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            NativeLibraryInfo.EnsureCompatible();
            IntPtr handle = NativeMethods.RoomCreate(room.Width, room.Height, Math.Max(1, room.MaskResolution), room.ColorDepth,
                Math.Max(1, room.BackgroundCount));
            if (handle == IntPtr.Zero)
                throw new AGSEditorException(NativeMethods.GetLastError());
            var native = new NativeRoom(handle);
            native.ApplyRoom(room, gameTextEncoding);
            return native;
        }

        /// <summary>
        /// The script source embedded in an old (pre-3.0) room file (upstream load_room_script), or null if it has
        /// none. Old games store it in ANSI.
        /// </summary>
        public static unsafe string LoadRoomScript(string fileName)
        {
            NativeLibraryInfo.EnsureCompatible();
            NativeImage.Check(NativeMethods.RoomExtractScriptText(fileName, null, 0, out nuint length, out int found));
            if (found == 0) return null;
            var bytes = new byte[(int)length];
            fixed (byte* p = bytes)
                NativeImage.Check(NativeMethods.RoomExtractScriptText(fileName, p, (nuint)bytes.Length, out length, out found));
            return AnsiEncoding.Default.GetString(bytes, 0, (int)length);
        }

        public int DataVersion => _info.DataVersion;
        public int Width => _info.Width;
        public int Height => _info.Height;
        public int MaskResolution => _info.MaskResolution;
        public int BackgroundCount => _info.BgFrameCount;
        /// <summary>Color depth of the background in bits per pixel (8, 16, 32).</summary>
        public int ColorDepth => _info.BackgroundBpp * 8;
        public bool HasLegacyInteractions => _info.HasLegacyInteractions != 0;

        /// <summary>
        /// The room's own text encoding (a "textencoding" option, set when the room was imported from
        /// another game), or null to use the game's.
        /// </summary>
        public Encoding TextEncoding
        {
            get
            {
                byte[] value = NativeMethods.GetBytes(NativeMethods.RoomGetStrOption(Handle, "textencoding"));
                if (value == null || !int.TryParse(Encoding.ASCII.GetString(value), out int codePage))
                    return null;
                try
                {
                    AnsiEncoding.EnsureCodePagesRegistered();
                    return Encoding.GetEncoding(codePage);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }
        }

        /// <summary>Background frame; 8-bit backgrounds carry their palette.</summary>
        public PixelImage GetBackground(int frame)
        {
            return TakeImage(NativeMethods.RoomGetBackground(Handle, frame));
        }

        /// <summary>Area mask as 8-bit area numbers (no palette; colors are up to the UI).</summary>
        public PixelImage GetMask(RoomMaskType mask)
        {
            return TakeImage(NativeMethods.RoomGetMask(Handle, (int)mask));
        }

        /// <summary>
        /// Builds the editable room model; port of convert_room_from_native() in the upstream editor's
        /// AGS.Native/agsnative.cpp. Texts are decoded with the room's own encoding if it has one,
        /// otherwise with <paramref name="gameTextEncoding"/>; custom property values always use the game's.
        /// </summary>
        public Room ToRoom(UnloadedRoom unloadedRoom, Encoding gameTextEncoding)
        {
            if (unloadedRoom == null) throw new ArgumentNullException(nameof(unloadedRoom));
            if (gameTextEncoding == null) throw new ArgumentNullException(nameof(gameTextEncoding));
            if (HasLegacyInteractions)
            {
                // TODO(ags4mac): port ConvertInteractions() (pre-3.0 interaction editor commands → script).
                throw new NotSupportedException($"Room {unloadedRoom.Number} uses pre-3.0 interactions, which the macOS editor can't convert yet.");
            }

            Encoding textEncoding = TextEncoding ?? gameTextEncoding;
            var room = new Room(unloadedRoom.Number);
            string roomScriptName = $"room{room.Number}.asc";
            NativeMethods.RoomInfo rs = _info;

            room.GameID = rs.GameId;
            room.BottomEdgeY = rs.EdgeBottom;
            room.LeftEdgeX = rs.EdgeLeft;
            room.MusicVolumeAdjustment = (RoomVolumeAdjustment)rs.MusicVolume;
            room.PlayerCharacterView = rs.PlayerView;
#pragma warning disable CS0612 // obsolete, but upstream still carries the stored values over
            room.PlayMusicOnRoomLoad = rs.StartupMusic;
            room.RightEdgeX = rs.EdgeRight;
            room.SaveLoadEnabled = rs.SaveLoadDisabled == 0;
#pragma warning restore CS0612
            room.ShowPlayerCharacter = rs.PlayerCharOff == 0;
            room.TopEdgeY = rs.EdgeTop;
            room.Width = rs.Width;
            room.Height = rs.Height;
            room.ColorDepth = rs.BgFrameCount > 0 ? rs.BackgroundBpp * 8 : 0;
            room.BackgroundAnimationDelay = rs.BgAnimSpeed;
            room.BackgroundAnimationEnabled = (rs.Flags & 0x01 /* kRoomFlag_BkgFrameLocked */) == 0;
            room.BackgroundCount = rs.BgFrameCount;
            room.Resolution = (RoomResolution)rs.LegacyResolution;
            room.MaskResolution = rs.MaskResolution;

            for (int i = 0; i < rs.LocalVarCount; i++)
            {
                NativeImage.Check(NativeMethods.RoomGetLocalVarValue(Handle, i, out int value));
                room.OldInteractionVariables.Add(new OldInteractionVariable(Ascii(NativeMethods.AGSN_TEXT_LOCALVAR_NAME, i), value));
            }

            for (int i = 0; i < rs.MessageCount; i++)
            {
                NativeImage.Check(NativeMethods.RoomGetMessageInfo(Handle, i, out NativeMethods.RoomMessageInfo mi));
                var message = new RoomMessage(i);
                message.Text = Text(NativeMethods.AGSN_TEXT_MESSAGE, i, textEncoding);
                message.ShowAsSpeech = mi.DisplayAs > 0;
                message.CharacterID = mi.DisplayAs - 1;
                message.DisplayNextMessageAfter = (mi.Flags & MSG_DISPLAYNEXT) != 0;
                message.AutoRemoveAfterTime = (mi.Flags & MSG_TIMELIMIT) != 0;
                room.Messages.Add(message);
            }

            for (int i = 0; i < rs.ObjectCount; i++)
            {
                NativeImage.Check(NativeMethods.RoomGetObject(Handle, i, out NativeMethods.RoomObject robj));
                var obj = new RoomObject(room);
                obj.ID = i;
                obj.Image = robj.Sprite;
                obj.Transparency = LegacyTrans255ToTrans100(robj.Transparency);
                obj.StartX = robj.X;
                obj.StartY = robj.Y;
                obj.Visible = robj.IsOn != 0;
                obj.Clickable = (robj.Flags & OBJF_NOINTERACT) == 0;
                obj.Baseline = robj.Baseline;
                obj.Solid = (robj.Flags & OBJF_SOLID) != 0;
                obj.BlockingRectangle = new Rectangle(robj.BlockX, robj.BlockY, robj.BlockWidth, robj.BlockHeight);
                obj.Name = Ascii(NativeMethods.AGSN_TEXT_OBJECT_SCRIPTNAME, i);
                obj.Description = Text(NativeMethods.AGSN_TEXT_OBJECT_NAME, i, textEncoding);
                obj.UseRoomAreaScaling = (robj.Flags & OBJF_USEROOMSCALING) != 0;
                obj.UseRoomAreaLighting = (robj.Flags & OBJF_USEREGIONTINTS) != 0;
                ConvertCustomProperties(obj.Properties, NativeMethods.AGSN_OWNER_OBJECT, i, gameTextEncoding);
                CopyInteractions(obj.Interactions, NativeMethods.AGSN_OWNER_OBJECT, i);
                obj.Interactions.ScriptModule = roomScriptName;
                room.Objects.Add(obj);
            }

            for (int i = 0; i < rs.HotspotCount; i++)
            {
                NativeImage.Check(NativeMethods.RoomGetHotspot(Handle, i, out NativeMethods.RoomHotspot rh));
                RoomHotspot hotspot = room.Hotspots[i];
                hotspot.ID = i;
                hotspot.Description = Text(NativeMethods.AGSN_TEXT_HOTSPOT_NAME, i, textEncoding);
                hotspot.Name = Ascii(NativeMethods.AGSN_TEXT_HOTSPOT_SCRIPTNAME, i);
                hotspot.WalkToPoint = new Point(rh.WalkToX, rh.WalkToY);
                ConvertCustomProperties(hotspot.Properties, NativeMethods.AGSN_OWNER_HOTSPOT, i, gameTextEncoding);
                CopyInteractions(hotspot.Interactions, NativeMethods.AGSN_OWNER_HOTSPOT, i);
                hotspot.Interactions.ScriptModule = roomScriptName;
            }

            for (int i = 0; i < Room.MAX_WALKABLE_AREAS; i++)
            {
                NativeImage.Check(NativeMethods.RoomGetWalkArea(Handle, i, out NativeMethods.RoomWalkArea wa));
                RoomWalkableArea area = room.WalkableAreas[i];
                area.ID = i;
                area.AreaSpecificView = wa.PlayerView;
                area.UseContinuousScaling = wa.ScalingNear != NOT_VECTOR_SCALED;
                area.ScalingLevel = wa.ScalingFar + 100;
                area.MinScalingLevel = wa.ScalingFar + 100;
                area.MaxScalingLevel = area.UseContinuousScaling ? wa.ScalingNear + 100 : area.MinScalingLevel;
            }

            for (int i = 0; i < Room.MAX_WALK_BEHINDS; i++)
            {
                NativeImage.Check(NativeMethods.RoomGetWalkBehindBaseline(Handle, i, out int baseline));
                RoomWalkBehind area = room.WalkBehinds[i];
                area.ID = i;
                area.Baseline = baseline;
            }

            for (int i = 0; i < Room.MAX_REGIONS; i++)
            {
                NativeImage.Check(NativeMethods.RoomGetRegion(Handle, i, out NativeMethods.RoomRegion rr));
                RoomRegion area = room.Regions[i];
                area.ID = i;
                // Region light level is shown 100 units higher in the editor, as upstream does.
                area.LightLevel = rr.LightLevel + 100;
                area.UseColourTint = rr.HasTint != 0;
                area.BlueTint = (rr.TintRaw >> 16) & 0xff;
                area.GreenTint = (rr.TintRaw >> 8) & 0xff;
                area.RedTint = rr.TintRaw & 0xff;
                int saturation = (rr.TintRaw >> 24) & 0xff;
                area.TintSaturation = (saturation > 0 && area.UseColourTint) ? saturation :
                    Utilities.GetDefaultValue(area.GetType(), "TintSaturation", 0);
                area.TintLuminance = area.UseColourTint ? rr.TintLuminance :
                    Utilities.GetDefaultValue(area.GetType(), "TintLuminance", 0);
                CopyInteractions(area.Interactions, NativeMethods.AGSN_OWNER_REGION, i);
                area.Interactions.ScriptModule = roomScriptName;
            }

            ConvertCustomProperties(room.Properties, NativeMethods.AGSN_OWNER_ROOM, 0, gameTextEncoding);
            CopyInteractions(room.Interactions, NativeMethods.AGSN_OWNER_ROOM, 0);
            room.Interactions.ScriptModule = roomScriptName;

            room._roomStructPtr = Handle;
            room.Description = unloadedRoom.Description;
            room.Script = unloadedRoom.Script;
            return room;
        }

        /// <summary>
        /// Pushes the editable fields of <paramref name="room"/> into this native room; port of
        /// convert_room_to_native() in the upstream editor. Texts are encoded with the game's encoding,
        /// which is also recorded as the room's "textencoding" option.
        /// </summary>
        public unsafe void ApplyRoom(Room room, Encoding gameTextEncoding)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (gameTextEncoding == null) throw new ArgumentNullException(nameof(gameTextEncoding));

            var rs = new NativeMethods.RoomInfo
            {
                LegacyResolution = (int)room.Resolution,
                MaskResolution = room.MaskResolution,
                GameId = room.GameID,
                EdgeBottom = room.BottomEdgeY,
                EdgeLeft = room.LeftEdgeX,
                MusicVolume = (int)room.MusicVolumeAdjustment,
                PlayerView = room.PlayerCharacterView,
                StartupMusic = 0, // [OBSOLETE]
                EdgeRight = room.RightEdgeX,
                SaveLoadDisabled = 0, // [OBSOLETE]
                PlayerCharOff = room.ShowPlayerCharacter ? 0 : 1,
                EdgeTop = room.TopEdgeY,
                Width = room.Width,
                Height = room.Height,
                BgAnimSpeed = room.BackgroundAnimationDelay,
                Flags = room.BackgroundAnimationEnabled ? 0 : 0x01 /* kRoomFlag_BkgFrameLocked */,
                MessageCount = room.Messages.Count,
                ObjectCount = room.Objects.Count,
                HotspotCount = room.Hotspots.Count,
                WalkAreaCount = room.WalkableAreas.Count,
                WalkBehindCount = room.WalkBehinds.Count,
                RegionCount = room.Regions.Count,
            };
            NativeImage.Check(NativeMethods.RoomSetInfo(Handle, rs));

            for (int i = 0; i < room.Messages.Count; i++)
            {
                RoomMessage message = room.Messages[i];
                SetText(NativeMethods.AGSN_TEXT_MESSAGE, i, message.Text, gameTextEncoding, textProperty: true);
                var info = new NativeMethods.RoomMessageInfo
                {
                    DisplayAs = message.ShowAsSpeech ? message.CharacterID + 1 : 0,
                    Flags = (message.DisplayNextMessageAfter ? MSG_DISPLAYNEXT : 0) | (message.AutoRemoveAfterTime ? MSG_TIMELIMIT : 0),
                };
                NativeImage.Check(NativeMethods.RoomSetMessageInfo(Handle, i, info));
            }

            for (int i = 0; i < room.Objects.Count; i++)
            {
                RoomObject obj = room.Objects[i];
                SetText(NativeMethods.AGSN_TEXT_OBJECT_SCRIPTNAME, i, obj.Name, AnsiEncoding.Default, textProperty: false);
                SetText(NativeMethods.AGSN_TEXT_OBJECT_NAME, i, obj.Description, gameTextEncoding, textProperty: true);
                int flags = 0;
                if (obj.UseRoomAreaScaling) flags |= OBJF_USEROOMSCALING;
                if (obj.UseRoomAreaLighting) flags |= OBJF_USEREGIONTINTS;
                if (!obj.Clickable) flags |= OBJF_NOINTERACT;
                if (obj.Solid) flags |= OBJF_SOLID;
                var robj = new NativeMethods.RoomObject
                {
                    Sprite = obj.Image,
                    Transparency = Trans100ToLegacyTrans255(obj.Transparency),
                    X = obj.StartX,
                    Y = obj.StartY,
                    IsOn = obj.Visible ? 1 : 0,
                    Baseline = obj.Baseline,
                    Flags = flags,
                    BlockX = obj.BlockingRectangle.Left,
                    BlockY = obj.BlockingRectangle.Top,
                    BlockWidth = obj.BlockingRectangle.Width,
                    BlockHeight = obj.BlockingRectangle.Height,
                };
                NativeImage.Check(NativeMethods.RoomSetObject(Handle, i, robj));
                CompileCustomProperties(obj.Properties, NativeMethods.AGSN_OWNER_OBJECT, i, gameTextEncoding);
            }

            for (int i = 0; i < room.Hotspots.Count; i++)
            {
                RoomHotspot hotspot = room.Hotspots[i];
                SetText(NativeMethods.AGSN_TEXT_HOTSPOT_NAME, i, hotspot.Description, gameTextEncoding, textProperty: true);
                SetText(NativeMethods.AGSN_TEXT_HOTSPOT_SCRIPTNAME, i, hotspot.Name, AnsiEncoding.Default, textProperty: false);
                var rh = new NativeMethods.RoomHotspot { WalkToX = hotspot.WalkToPoint.X, WalkToY = hotspot.WalkToPoint.Y };
                NativeImage.Check(NativeMethods.RoomSetHotspot(Handle, i, rh));
                CompileCustomProperties(hotspot.Properties, NativeMethods.AGSN_OWNER_HOTSPOT, i, gameTextEncoding);
            }

            for (int i = 0; i < room.WalkableAreas.Count; i++)
            {
                RoomWalkableArea area = room.WalkableAreas[i];
                var wa = new NativeMethods.RoomWalkArea { PlayerView = area.AreaSpecificView };
                if (area.UseContinuousScaling)
                {
                    wa.ScalingFar = area.MinScalingLevel - 100;
                    wa.ScalingNear = area.MaxScalingLevel - 100;
                }
                else
                {
                    wa.ScalingFar = area.ScalingLevel - 100;
                    wa.ScalingNear = NOT_VECTOR_SCALED;
                }
                NativeImage.Check(NativeMethods.RoomSetWalkArea(Handle, i, wa));
            }

            for (int i = 0; i < room.WalkBehinds.Count; i++)
                NativeImage.Check(NativeMethods.RoomSetWalkBehindBaseline(Handle, i, room.WalkBehinds[i].Baseline));

            for (int i = 0; i < room.Regions.Count; i++)
            {
                RoomRegion area = room.Regions[i];
                var rr = new NativeMethods.RoomRegion();
                if (area.UseColourTint)
                {
                    rr.TintRaw = area.RedTint | (area.GreenTint << 8) | (area.BlueTint << 16) | (area.TintSaturation << 24);
                    rr.LightRaw = (area.TintLuminance * 25) / 10;
                }
                else
                {
                    rr.TintRaw = 0;
                    // Region light level is shown 100 units higher in the editor.
                    rr.LightRaw = area.LightLevel - 100;
                }
                NativeImage.Check(NativeMethods.RoomSetRegion(Handle, i, rr));
            }

            CompileCustomProperties(room.Properties, NativeMethods.AGSN_OWNER_ROOM, 0, gameTextEncoding);

            // convert_room_interactions_to_native()
            SetEvents(room.Interactions, NativeMethods.AGSN_OWNER_ROOM, 0);
            for (int i = 0; i < room.Hotspots.Count; i++)
                SetEvents(room.Hotspots[i].Interactions, NativeMethods.AGSN_OWNER_HOTSPOT, i);
            for (int i = 0; i < room.Objects.Count; i++)
                SetEvents(room.Objects[i].Interactions, NativeMethods.AGSN_OWNER_OBJECT, i);
            for (int i = 0; i < room.Regions.Count; i++)
                SetEvents(room.Regions[i].Interactions, NativeMethods.AGSN_OWNER_REGION, i);

            // Encoding hint
            NativeImage.Check(NativeMethods.RoomSetStrOption(Handle, "textencoding", gameTextEncoding.CodePage.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>Sets the compiled room script (a serialized ccScript, as agscc writes it); null clears it.</summary>
        public unsafe void SetCompiledScript(byte[] compiledScript)
        {
            fixed (byte* p = compiledScript)
                NativeImage.Check(NativeMethods.RoomSetCompiledScript(Handle, p, (nuint)(compiledScript?.Length ?? 0)));
        }

        /// <summary>Replaces an area mask (8-bit area numbers, same size as the current mask).</summary>
        public unsafe void SetMask(RoomMaskType mask, PixelImage image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (image.PixelFormat != ImagePixelFormat.Indexed8)
                throw new ArgumentException("Masks are 8-bit images of area numbers.", nameof(image));
            fixed (byte* p = image.Pixels)
                NativeImage.Check(NativeMethods.RoomSetMask(Handle, (int)mask, image.Width, image.Height, p, (nuint)image.Stride));
        }

        /// <summary>
        /// Imports an image file as background <paramref name="frame"/> (upstream ImportBackground): converted to
        /// the game's colour depth, padded to the game resolution, 8-bit images remapped into the background
        /// palette slots unless <paramref name="useExactPalette"/>. Frame 0 sets the room size (masks are cleared
        /// when it changes); <paramref name="room"/>'s size, resolution and background count are updated.
        /// Returns the number of colours that did not fit into the background palette slots (8-bit games).
        /// </summary>
        public unsafe int ImportBackground(Room room, int frame, string imageFile, Game game, bool useExactPalette = false)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (game == null) throw new ArgumentNullException(nameof(game));
            var palette = new NativeMethods.Rgba[256];
            var palUses = new byte[256];
            for (int i = 0; i < 256 && i < game.Palette.Length; i++)
            {
                Color c = game.Palette[i].Colour;
                palette[i] = new NativeMethods.Rgba { R = c.R, G = c.G, B = c.B, A = 255 };
                palUses[i] = (byte)game.Palette[i].ColourType;
            }
            int lost;
            fixed (NativeMethods.Rgba* pal = palette)
            fixed (byte* uses = palUses)
            {
                NativeImage.Check(NativeMethods.RoomImportBackground(Handle, frame, imageFile, (int)game.Settings.ColorDepth * 8,
                    game.MinRoomWidth, game.MinRoomHeight, pal, uses, useExactPalette ? 1 : 0, out lost));
            }
            RefreshInfo(room);
            return lost;
        }

        /// <summary>Removes background <paramref name="frame"/> (upstream DeleteBackground).</summary>
        public void DeleteBackground(Room room, int frame)
        {
            NativeImage.Check(NativeMethods.RoomDeleteBackground(Handle, frame));
            RefreshInfo(room);
        }

        private void RefreshInfo(Room room)
        {
            NativeImage.Check(NativeMethods.RoomGetInfo(Handle, out _info));
            if (room == null) return;
            room.Width = _info.Width;
            room.Height = _info.Height;
            room.BackgroundCount = _info.BgFrameCount;
            room.ColorDepth = _info.BgFrameCount > 0 ? _info.BackgroundBpp * 8 : 0;
            room.Resolution = (RoomResolution)_info.LegacyResolution;
            room.MaskResolution = _info.MaskResolution;
        }

        /// <summary>Saves the room in the current .crm format (upstream save_room_file).</summary>
        public void Save(string fileName)
        {
            int result = NativeMethods.RoomSave(Handle, fileName);
            if (result != NativeMethods.AGSN_OK)
                throw new AGSEditorException(NativeMethods.GetLastError());
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.RoomFree(_handle);
                _handle = IntPtr.Zero;
            }
        }

        // GfxDef::LegacyTrans255ToTrans100 (ags/Common/gfx/gfx_def.h)
        internal static int LegacyTrans255ToTrans100(int legacyTransparency)
        {
            switch (legacyTransparency)
            {
                case 0: return 0;
                case 255: return 100;
                default: return 100 - ((legacyTransparency * 10) / 25);
            }
        }

        // GfxDef::Trans100ToLegacyTrans255 (ags/Common/gfx/gfx_def.h)
        internal static int Trans100ToLegacyTrans255(int transparency)
        {
            switch (transparency)
            {
                case 0: return 0;
                case 100: return 255;
                default: return ((100 - transparency) * 25) / 10;
            }
        }

        private unsafe void SetText(int kind, int index, string text, Encoding encoding, bool textProperty)
        {
            byte[] bytes = encoding.GetBytes(text ?? string.Empty);
            fixed (byte* p = bytes)
                NativeImage.Check(NativeMethods.RoomSetText(Handle, kind, index, p, (nuint)bytes.Length, textProperty ? 1 : 0));
        }

        // CompileCustomProperties() in agsnative.cpp
        private unsafe void CompileCustomProperties(CustomProperties convertFrom, int owner, int index, Encoding gameTextEncoding)
        {
            NativeImage.Check(NativeMethods.RoomClearProperties(Handle, owner, index));
            foreach (CustomProperty prop in convertFrom.PropertyValues.Values)
            {
                byte[] value = gameTextEncoding.GetBytes(prop.Value ?? string.Empty);
                fixed (byte* p = value)
                    NativeImage.Check(NativeMethods.RoomSetProperty(Handle, owner, index, prop.Name, p, (nuint)value.Length));
            }
        }

        // convert_interaction_scripts() in agsnative.cpp
        private void SetEvents(Interactions interactions, int owner, int index)
        {
            string[] functions = interactions.ScriptFunctionNames.Select(f => f ?? string.Empty).ToArray();
            NativeImage.Check(NativeMethods.RoomSetEvents(Handle, owner, index, interactions.ScriptModule, functions, functions.Length));
        }

        private void ConvertCustomProperties(CustomProperties insertInto, int owner, int index, Encoding gameTextEncoding)
        {
            int count = NativeMethods.RoomGetPropertyCount(Handle, owner, index);
            for (int p = 0; p < count; p++)
            {
                NativeImage.Check(NativeMethods.RoomGetProperty(Handle, owner, index, p, out IntPtr name, out IntPtr value));
                var prop = new CustomProperty();
                prop.Name = AnsiEncoding.Default.GetString(NativeMethods.GetBytes(name)); // property names are always ASCII
                prop.Value = gameTextEncoding.GetString(NativeMethods.GetBytes(value));
                insertInto.PropertyValues.Add(prop.Name, prop);
            }
        }

        // CopyInteractions() in agsnative.cpp
        private void CopyInteractions(Interactions destination, int owner, int index)
        {
            byte[] module = NativeMethods.GetBytes(NativeMethods.RoomGetScriptModule(Handle, owner, index));
            if (module != null)
                destination.ScriptModule = AnsiEncoding.Default.GetString(module);
            int count = Math.Min(NativeMethods.RoomGetEventCount(Handle, owner, index), destination.ScriptFunctionNames.Length);
            for (int e = 0; e < count; e++)
                destination.ScriptFunctionNames[e] = AnsiEncoding.Default.GetString(NativeMethods.GetBytes(NativeMethods.RoomGetEventFunction(Handle, owner, index, e)) ?? Array.Empty<byte>());
        }

        private string Text(int kind, int index, Encoding encoding)
        {
            byte[] bytes = NativeMethods.GetBytes(NativeMethods.RoomGetText(Handle, kind, index));
            return bytes == null ? string.Empty : encoding.GetString(bytes);
        }

        // TextHelper::ConvertASCII: upstream marshals these through the system ANSI code page.
        private string Ascii(int kind, int index)
        {
            return Text(kind, index, AnsiEncoding.Default);
        }

        private static PixelImage TakeImage(IntPtr image)
        {
            if (image == IntPtr.Zero)
                throw new System.IO.InvalidDataException(NativeMethods.GetLastError());
            try
            {
                return NativeImage.ToPixelImage(image, usePalette: true, fallbackPalette: null);
            }
            finally
            {
                NativeMethods.ImageFree(image);
            }
        }

        private IntPtr Handle
        {
            get
            {
                if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(NativeRoom));
                return _handle;
            }
        }
    }
}
