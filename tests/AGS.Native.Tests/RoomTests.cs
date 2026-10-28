using System;
using System.IO;
using System.Linq;
using System.Text;
using AGS.Imaging;
using AGS.Types;

namespace AGS.Native.Tests
{
    /// <summary>Loads every room of the reference game through upstream's room reader.</summary>
    public class RoomTests
    {
        private static readonly Encoding GameEncoding;

        static RoomTests()
        {
            AnsiEncoding.EnsureCodePagesRegistered();
            GameEncoding = AnsiEncoding.Default; // the reference game uses "ANSI"
        }

        public static TheoryData<int> RoomNumbers()
        {
            var data = new TheoryData<int>();
            foreach (int n in Enumerable.Range(1, 21)) data.Add(n);
            return data;
        }

        private static string RoomFile(int number) => Path.Combine(SpriteFileTests.ReferenceGameDir(), $"room{number}.crm");

        [Theory]
        [MemberData(nameof(RoomNumbers))]
        public void RoomLoadsAndConverts(int number)
        {
            using NativeRoom native = NativeRoom.Load(RoomFile(number));
            Room room = native.ToRoom(new UnloadedRoom(number), GameEncoding);

            Assert.Equal(number, room.Number);
            Assert.True(room.Width >= 320 && room.Height >= 200, $"unexpected size {room.Width}x{room.Height}");
            // A 16-bit game, but rooms keep their own depth (room 4 has an 8-bit, all-black background).
            Assert.Contains(room.ColorDepth, new[] { 8, 16 });
            Assert.InRange(room.BackgroundCount, 1, 5);
            Assert.Equal($"room{number}.asc", room.Interactions.ScriptModule);
            Assert.All(room.Objects, o => Assert.Equal($"room{number}.asc", o.Interactions.ScriptModule));

            for (int frame = 0; frame < room.BackgroundCount; frame++)
            {
                PixelImage bg = native.GetBackground(frame);
                Assert.Equal((room.Width, room.Height), (bg.Width, bg.Height));
                Assert.Equal(room.ColorDepth == 8 ? ImagePixelFormat.Indexed8 : ImagePixelFormat.Rgb565, bg.PixelFormat);
            }

            foreach (RoomMaskType type in Enum.GetValues<RoomMaskType>())
            {
                PixelImage mask = native.GetMask(type);
                Assert.Equal(ImagePixelFormat.Indexed8, mask.PixelFormat);
                // Walk-behinds are always full size; the other masks use the mask resolution.
                int scale = type == RoomMaskType.WalkBehinds ? 1 : room.MaskResolution;
                Assert.Equal((room.Width / scale, room.Height / scale), (mask.Width, mask.Height));
            }
        }

        [Fact]
        public void RoomsOfThreePointXHaveNoEmbeddedScript()
        {
            // 3.x rooms keep their script in roomN.asc; only pre-3.0 rooms embed the source (upstream's
            // ExtractScriptText yields empty text for them).
            Assert.True(string.IsNullOrEmpty(NativeRoom.LoadRoomScript(RoomFile(1))));
            Assert.Throws<System.IO.InvalidDataException>(() => NativeRoom.LoadRoomScript(Path.Combine(SpriteFileTests.ReferenceGameDir(), "Game.agf")));
        }

        [Fact]
        public void ObjectSpritesExistInSpriteFile()
        {
            using SpriteFile sprites = SpriteFile.OpenInProject(SpriteFileTests.ReferenceGameDir());
            int objects = 0;
            foreach (int number in Enumerable.Range(1, 21))
            {
                using NativeRoom native = NativeRoom.Load(RoomFile(number));
                Room room = native.ToRoom(new UnloadedRoom(number), GameEncoding);
                foreach (RoomObject obj in room.Objects)
                {
                    objects++;
                    Assert.True(sprites.GetInfo(obj.Image).Exists, $"room {number} object {obj.ID} uses missing sprite {obj.Image}");
                }
            }
            Assert.True(objects > 0);
        }

        [Fact]
        public void TextsDecodeWithoutReplacementCharacters()
        {
            var texts = new StringBuilder();
            foreach (int number in Enumerable.Range(1, 21))
            {
                using NativeRoom native = NativeRoom.Load(RoomFile(number));
                Room room = native.ToRoom(new UnloadedRoom(number), GameEncoding);
                foreach (RoomHotspot h in room.Hotspots) texts.AppendLine(h.Description);
                foreach (RoomObject o in room.Objects) texts.AppendLine(o.Description);
                foreach (RoomMessage m in room.Messages) texts.AppendLine(m.Text);
            }
            string all = texts.ToString();
            Assert.DoesNotContain('�', all);
            Assert.Contains("Hotspot", all); // default hotspot descriptions are present
        }

        [Fact]
        public void LegacyTransparencyConversionMatchesUpstream()
        {
            Assert.Equal(0, NativeRoom.LegacyTrans255ToTrans100(0));
            Assert.Equal(100, NativeRoom.LegacyTrans255ToTrans100(255));
            Assert.Equal(0, NativeRoom.LegacyTrans255ToTrans100(250));
            Assert.Equal(50, NativeRoom.LegacyTrans255ToTrans100(125));
        }

        [Fact]
        public void MissingRoomThrowsEditorException()
        {
            var ex = Assert.Throws<AGSEditorException>(() => NativeRoom.Load("/nonexistent/room99.crm"));
            Assert.Contains("room99.crm", ex.Message);
        }
    }
}
