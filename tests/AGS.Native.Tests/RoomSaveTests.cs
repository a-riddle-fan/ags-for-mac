using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AGS.Imaging;
using AGS.Types;

namespace AGS.Native.Tests
{
    /// <summary>Saves every reference room through the port of the editor's save path.</summary>
    public class RoomSaveTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ags4mac-rooms-" + Guid.NewGuid().ToString("N"));

        public RoomSaveTests()
        {
            Directory.CreateDirectory(_dir);
            AnsiEncoding.EnsureCodePagesRegistered();
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        private static string RoomFile(int number) => Path.Combine(SpriteFileTests.ReferenceGameDir(), $"room{number}.crm");

        private static string SaveRoundTrip(string source, int number, string target)
        {
            using NativeRoom native = NativeRoom.Load(source);
            Room room = native.ToRoom(new UnloadedRoom(number), AnsiEncoding.Default);
            native.ApplyRoom(room, AnsiEncoding.Default);
            native.Save(target);
            return Dump(room);
        }

        [Theory]
        [MemberData(nameof(RoomTests.RoomNumbers), MemberType = typeof(RoomTests))]
        public void SavedRoomIsStableAndPreservesContent(int number)
        {
            string a = Path.Combine(_dir, $"a{number}.crm");
            string b = Path.Combine(_dir, $"b{number}.crm");

            string original = SaveRoundTrip(RoomFile(number), number, a);
            string afterSave = SaveRoundTrip(a, number, b);

            Assert.Equal(original, afterSave);
            Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));

            using NativeRoom before = NativeRoom.Load(RoomFile(number));
            using NativeRoom after = NativeRoom.Load(a);
            for (int frame = 0; frame < before.BackgroundCount; frame++)
                AssertSamePixels(before.GetBackground(frame), after.GetBackground(frame));
            foreach (RoomMaskType mask in Enum.GetValues<RoomMaskType>())
                AssertSamePixels(before.GetMask(mask), after.GetMask(mask));
        }

        [Fact]
        public void EditsArePersisted()
        {
            string target = Path.Combine(_dir, "edited.crm");
            using (NativeRoom native = NativeRoom.Load(RoomFile(2)))
            {
                Room room = native.ToRoom(new UnloadedRoom(2), AnsiEncoding.Default);
                room.Hotspots[1].Description = "Tür zum Flur";
                room.Hotspots[1].Name = "hFlurtuer";
                room.LeftEdgeX = 17;
                room.Regions[2].UseColourTint = true;
                room.Regions[2].RedTint = 200;
                room.Regions[2].TintSaturation = 40;
                room.Regions[2].TintLuminance = 60;
                room.Messages.Add(new RoomMessage(room.Messages.Count) { Text = "Größe: \\n zwei Zeilen", ShowAsSpeech = true, CharacterID = 3 });
                native.ApplyRoom(room, AnsiEncoding.Default);
                native.Save(target);
            }

            using NativeRoom reloaded = NativeRoom.Load(target);
            Room r = reloaded.ToRoom(new UnloadedRoom(2), AnsiEncoding.Default);
            Assert.Equal("Tür zum Flur", r.Hotspots[1].Description);
            Assert.Equal("hFlurtuer", r.Hotspots[1].Name);
            Assert.Equal(17, r.LeftEdgeX);
            Assert.True(r.Regions[2].UseColourTint);
            Assert.Equal((200, 40, 60), (r.Regions[2].RedTint, r.Regions[2].TintSaturation, r.Regions[2].TintLuminance));
            RoomMessage m = r.Messages.Last();
            // Text properties are unescaped on save, as in the upstream editor: "\n" becomes a newline.
            Assert.Equal("Größe: \n zwei Zeilen", m.Text);
            Assert.True(m.ShowAsSpeech);
            Assert.Equal(3, m.CharacterID);
            Assert.Equal(AnsiEncoding.Default.CodePage, reloaded.TextEncoding.CodePage);
        }

        [Fact]
        public void TransparencyConversionMatchesUpstreamIntegerMath()
        {
            // Upstream's 100 <-> 255 conversion uses integer math, so odd percentages shift once
            // (e.g. 1% -> 247 -> 2%), exactly as in the Windows editor; values are stable after that.
            foreach (int t in new[] { 0, 10, 20, 50, 80, 100 })
                Assert.Equal(t, NativeRoom.LegacyTrans255ToTrans100(NativeRoom.Trans100ToLegacyTrans255(t)));
            Assert.Equal(2, NativeRoom.LegacyTrans255ToTrans100(NativeRoom.Trans100ToLegacyTrans255(1)));
            for (int t = 0; t <= 100; t++)
            {
                int once = NativeRoom.LegacyTrans255ToTrans100(NativeRoom.Trans100ToLegacyTrans255(t));
                Assert.Equal(once, NativeRoom.LegacyTrans255ToTrans100(NativeRoom.Trans100ToLegacyTrans255(once)));
            }
        }

        private static void AssertSamePixels(PixelImage expected, PixelImage actual)
        {
            Assert.Equal((expected.Width, expected.Height, expected.PixelFormat), (actual.Width, actual.Height, actual.PixelFormat));
            for (int y = 0; y < expected.Height; y++)
                Assert.True(expected.GetRow(y).SequenceEqual(actual.GetRow(y)), $"row {y} differs");
        }

        // Dumps the public, readable state of the room model (recursing into AGS.Types objects).
        private static string Dump(object value)
        {
            var sb = new StringBuilder();
            DumpValue(sb, "room", value, 0);
            return sb.ToString();
        }

        private static void DumpValue(StringBuilder sb, string path, object value, int depth)
        {
            if (depth > 6) return;
            // null and "" both mean "no script function"; upstream also writes empty names for unset slots.
            if (value == null) { sb.AppendLine($"{path}="); return; }
            Type t = value.GetType();
            if (t.IsPrimitive || value is string || t.IsEnum || value is decimal || t.Namespace == "System.Drawing")
            {
                sb.AppendLine($"{path}={value}");
                return;
            }
            if (value is IDictionary dict)
            {
                foreach (object key in dict.Keys.Cast<object>().OrderBy(k => k.ToString(), StringComparer.Ordinal))
                    DumpValue(sb, $"{path}[{key}]", dict[key], depth + 1);
                return;
            }
            if (value is IEnumerable list)
            {
                int i = 0;
                foreach (object item in list)
                    DumpValue(sb, $"{path}[{i++}]", item, depth + 1);
                return;
            }
            if (t.Namespace != "AGS.Types") { sb.AppendLine($"{path}=<{t.Name}>"); return; }
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                if (p.Name is "Parent" or "Room" or "Script" or "_roomStructPtr") continue;
                // [Obsolete] settings (e.g. SaveLoadEnabled) are reset on save by upstream as well.
                if (p.IsDefined(typeof(ObsoleteAttribute))) continue;
                DumpValue(sb, $"{path}.{p.Name}", p.GetValue(value), depth + 1);
            }
        }
    }
}
