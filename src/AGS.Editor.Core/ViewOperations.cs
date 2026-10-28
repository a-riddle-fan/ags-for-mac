using System.Collections.Generic;
using System.Linq;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// View editing of upstream Panes/ViewEditor and ViewLoopEditor (frames are kept with ID == index).
    /// </summary>
    public static class ViewOperations
    {
        /// <summary>btnNewLoop_Click: appends a loop.</summary>
        public static ViewLoop AddLoop(View view) => view.AddNewLoop();

        /// <summary>btnDeleteLastLoop_Click.</summary>
        public static void DeleteLastLoop(View view)
        {
            if (view.Loops.Count > 0)
                view.Loops.RemoveAt(view.Loops.Count - 1);
        }

        /// <summary>
        /// InsertNewFrames: inserts <paramref name="count"/> blank frames after <paramref name="afterIndex"/> (-1: at
        /// the start); a blank frame gets the sprite that follows the previous frame's sprite in its sprite
        /// folder (ViewEditor.loopPane_NewFrameAdded). Returns the index of the first new frame.
        /// </summary>
        public static int InsertNewFrames(Game game, ViewLoop loop, int afterIndex, int count = 1)
        {
            var frames = Enumerable.Range(0, count).Select(_ => new ViewFrame()).ToArray();
            int first = InsertFrames(loop, afterIndex, frames);
            for (int i = first; i < first + count; i++)
                InitializeNewFrame(game, loop, i);
            return first;
        }

        /// <summary>InsertNewFrames with given frames (e.g. pasted copies); returns the index of the first one.</summary>
        public static int InsertFrames(ViewLoop loop, int afterIndex, IReadOnlyList<ViewFrame> newFrames)
        {
            if (afterIndex < 0) afterIndex = -1;
            if (afterIndex >= loop.Frames.Count) afterIndex = loop.Frames.Count - 1;

            // Shift later frames IDs
            foreach (ViewFrame frame in loop.Frames)
            {
                if (frame.ID > afterIndex)
                    frame.ID += newFrames.Count;
            }
            // Insert new frames, assigning new IDs
            for (int i = 0; i < newFrames.Count; ++i)
            {
                ViewFrame frame = newFrames[i];
                frame.ID = afterIndex + 1 + i;
                loop.Frames.Insert(afterIndex + 1 + i, frame);
            }
            return afterIndex + 1;
        }

        /// <summary>ViewEditor.loopPane_NewFrameAdded.</summary>
        private static void InitializeNewFrame(Game game, ViewLoop loop, int newSelectedFrame)
        {
            if (newSelectedFrame <= 0)
                return;
            // Attempt to initialize the new frame to the sprite that
            // comes after the previous frame in the sprite manager
            int previousFrameImage = loop.Frames[newSelectedFrame - 1].Image;
            SpriteFolder parent = game.RootSpriteFolder.FindFolderThatContainsSprite(previousFrameImage);
            if ((parent != null) && (previousFrameImage > 0))
            {
                for (int i = 0; i < parent.Sprites.Count; i++)
                {
                    if ((parent.Sprites[i].Number == previousFrameImage) && (i < parent.Sprites.Count - 1))
                    {
                        loop.Frames[newSelectedFrame].Image = parent.Sprites[i + 1].Number;
                        break;
                    }
                }
            }
        }

        /// <summary>DeleteSelectedFrames: removes the frames and renumbers the rest.</summary>
        public static void DeleteFrames(ViewLoop loop, IEnumerable<int> selectedFrames)
        {
            var selected = selectedFrames.Distinct().OrderBy(i => i).ToList();
            int removedCount = 0;
            foreach (int sel in selected)
            {
                int removeIndex = sel - (removedCount++);
                if (removeIndex < 0 || removeIndex >= loop.Frames.Count) continue;
                loop.Frames.RemoveAt(removeIndex);
                for (int frame = removeIndex; frame < loop.Frames.Count; ++frame)
                    loop.Frames[frame].ID--;
            }
        }

        /// <summary>FlipSelectedFrames.</summary>
        public static void FlipFrames(ViewLoop loop, IEnumerable<int> selectedFrames)
        {
            foreach (int sel in selectedFrames.Distinct())
                loop.Frames[sel].Flipped = !loop.Frames[sel].Flipped;
        }

        /// <summary>ReverseSelectedFrames: swaps frame contents (not IDs).</summary>
        public static void ReverseFrames(ViewLoop loop, IEnumerable<int> selectedFrames)
        {
            var selected = selectedFrames.Distinct().OrderBy(i => i).ToList();
            var swap = new ViewFrame();
            for (int fw = 0, bw = selected.Count - 1; fw < selected.Count / 2; ++fw, --bw)
            {
                ViewFrame f1 = loop.Frames[selected[fw]];
                ViewFrame f2 = loop.Frames[selected[bw]];
                f1.CopyTo(swap);
                f2.CopyTo(f1);
                swap.CopyTo(f2);
            }
        }
    }
}
