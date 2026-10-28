using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AGS.Editor.Avalonia.ViewModels;
using AGS.Types;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>
    /// Drag &amp; drop: project tree nodes onto other nodes (move items/folders, BaseComponentWithFolders), sprites from
    /// the sprite view onto sprite folders in the tree, and image files from the Finder into the sprite view.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly DataFormat<ProjectTreeNode> NodeFormat = DataFormat.CreateInProcessFormat<ProjectTreeNode>("ags4mac.tree-node");
        private static readonly DataFormat<Sprite[]> SpritesFormat = DataFormat.CreateInProcessFormat<Sprite[]>("ags4mac.sprites");

        private PointerPressedEventArgs _dragPress;
        private Point _dragStart;
        private ProjectTreeNode _dragNode;
        private SpriteThumbnail _dragThumbnail;
        private SpriteFolderDocument _dragSpriteDocument;
        private bool _dragging;

        private void InstallDragDrop()
        {
            DragDrop.SetAllowDrop(this, true);
            AddHandler(PointerPressedEvent, OnDragPointerPressed, global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(PointerMovedEvent, OnDragPointerMoved, global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(PointerReleasedEvent, (s, e) => _dragPress = null, global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        private void OnDragPointerPressed(object sender, PointerPressedEventArgs e)
        {
            _dragPress = null;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Visual source) return;
            _dragNode = null;
            _dragThumbnail = null;
            _dragSpriteDocument = null;
            if (source.FindAncestorOfType<TreeViewItem>(includeSelf: true) is TreeViewItem treeItem && treeItem.DataContext is ProjectTreeNode node)
                _dragNode = node;
            else if (source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is ListBoxItem listItem && listItem.DataContext is SpriteThumbnail thumbnail
                && listItem.FindAncestorOfType<ListBox>()?.DataContext is SpriteFolderDocument doc)
            {
                _dragThumbnail = thumbnail;
                _dragSpriteDocument = doc;
            }
            else
                return;
            _dragPress = e;
            _dragStart = e.GetPosition(this);
        }

        private async void OnDragPointerMoved(object sender, PointerEventArgs e)
        {
            if (_dragPress == null || _dragging) return;
            Vector moved = e.GetPosition(this) - _dragStart;
            if (Math.Abs(moved.X) < 6 && Math.Abs(moved.Y) < 6) return;
            var item = new DataTransferItem();
            if (_dragNode != null)
                item.Set(NodeFormat, _dragNode);
            else if (_dragThumbnail != null)
            {
                // the selection, or just the pressed sprite when it isn't part of it
                IReadOnlyList<Sprite> selected = _dragSpriteDocument.SelectedSprites;
                Sprite[] sprites = selected.Contains(_dragThumbnail.Sprite) ? selected.ToArray() : new[] { _dragThumbnail.Sprite };
                item.Set(SpritesFormat, sprites);
            }
            else return;
            var data = new DataTransfer();
            data.Add(item);
            PointerPressedEventArgs press = _dragPress;
            _dragPress = null;
            _dragging = true;
            try
            {
                await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
            }
            finally
            {
                _dragging = false;
            }
        }

        /// <summary>The tree node under the pointer and where on it (top quarter = before, bottom quarter = after).</summary>
        private (ProjectTreeNode Node, TargetDropZone Zone)? TreeTarget(DragEventArgs e)
        {
            if (e.Source is not Visual source || source.FindAncestorOfType<TreeViewItem>(includeSelf: true) is not TreeViewItem item
                || item.DataContext is not ProjectTreeNode node)
                return null;
            Control header = item.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(c => c.Name == "PART_Header" || c.Name == "PART_LayoutRoot" || c.Name == "PART_HeaderPresenter");
            double height = header?.Bounds.Height > 0 ? header.Bounds.Height : Math.Min(item.Bounds.Height, 28);
            double y = e.GetPosition(item).Y;
            TargetDropZone zone = y < height * 0.25 ? TargetDropZone.Top
                : y < height * 0.5 ? TargetDropZone.MiddleTop
                : y < height * 0.75 ? TargetDropZone.MiddleBottom
                : TargetDropZone.Bottom;
            return (node, zone);
        }

        /// <summary>The sprite folder files are dropped into: the sprite view under the pointer, or a sprite folder node.</summary>
        private SpriteFolder FileDropFolder(DragEventArgs e)
        {
            if (e.Source is Visual source && source.FindAncestorOfType<ListBox>(includeSelf: true)?.DataContext is SpriteFolderDocument doc)
                return doc.Folder as SpriteFolder;
            var target = TreeTarget(e);
            return (target?.Node.Payload ?? target?.Node.Folder) as SpriteFolder;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.None;
            if (DataContext is not MainWindowViewModel vm) return;
            if (e.DataTransfer.TryGetValue(NodeFormat) is ProjectTreeNode node)
            {
                var target = TreeTarget(e);
                if (target != null && vm.DropNode(node, target.Value.Node, target.Value.Zone, apply: false))
                    e.DragEffects = DragDropEffects.Move;
            }
            else if (e.DataTransfer.TryGetValue(SpritesFormat) is Sprite[])
            {
                var target = TreeTarget(e);
                if ((target?.Node.Payload ?? target?.Node.Folder) is SpriteFolder)
                    e.DragEffects = DragDropEffects.Move;
            }
            else if (e.DataTransfer.Contains(DataFormat.File) && FileDropFolder(e) != null)
            {
                e.DragEffects = DragDropEffects.Copy;
            }
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if (e.DataTransfer.TryGetValue(NodeFormat) is ProjectTreeNode node)
            {
                var target = TreeTarget(e);
                if (target != null)
                    vm.DropNode(node, target.Value.Node, target.Value.Zone);
            }
            else if (e.DataTransfer.TryGetValue(SpritesFormat) is Sprite[] sprites)
            {
                var target = TreeTarget(e);
                if ((target?.Node.Payload ?? target?.Node.Folder) is SpriteFolder folder)
                    vm.DropSpritesOnFolder(sprites, folder);
            }
            else if (FileDropFolder(e) is SpriteFolder folder && e.DataTransfer.TryGetFiles() is IStorageItem[] items)
            {
                var files = items.Select(i => i.TryGetLocalPath()).Where(p => p != null).ToList();
                await vm.ImportImageFilesAsync(folder, files);
            }
        }
    }
}
