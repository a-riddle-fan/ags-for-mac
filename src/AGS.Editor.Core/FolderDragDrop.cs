using System;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>Where a dragged node is dropped on the target node (upstream TargetDropZone).</summary>
    public enum TargetDropZone { Top, MiddleTop, MiddleBottom, Bottom }

    /// <summary>
    /// Drag &amp; drop in the project tree for items in folders (BaseComponentWithFolders.ProjectTreeItem_CanDropHere /
    /// ProjectTreeItem_DropHere and the Drag* helpers, logic kept): items are moved before/after another item or into a
    /// folder, folders before/after another folder or into one. Folder change notifications are suppressed as upstream
    /// (moving doesn't renumber IDs). Source and target are each an item or a folder of the same tree.
    /// </summary>
    public static class FolderDragDrop<TItem, TFolder>
        where TItem : class, IToXml
        where TFolder : BaseFolderCollection<TItem, TFolder>
    {
        public static bool CanDrop(TFolder root, object source, object target, TargetDropZone dropZone)
        {
            TFolder sourceFolder = source as TFolder, targetFolder = target as TFolder;
            TItem sourceItem = source as TItem, targetItem = target as TItem;
            if ((sourceFolder == null && sourceItem == null) || (targetItem == null && targetFolder == null))
                return false;
            if (sourceFolder != null && targetFolder == null)
                return false;
            if (sourceFolder != null && !IsValidFolderMove(sourceFolder, targetFolder))
                return false;
            if (sourceItem != null && targetItem != null && sourceItem.Equals(targetItem))
                return false;
            if (sourceFolder != null && targetFolder != null)
            {
                if (FindParentFolder(root, sourceFolder) == null)
                    return false; // the root folder itself can't be moved
                bool intoFolder = dropZone == TargetDropZone.MiddleTop || dropZone == TargetDropZone.MiddleBottom;
                if (!intoFolder && FindParentFolder(root, targetFolder) == null)
                    return false; // nothing goes beside the root folder
            }
            if (sourceItem != null && targetFolder != null && FindFolderThatContainsItem(root, sourceItem) == targetFolder)
                return false;
            return true;
        }

        /// <summary>ProjectTreeItem_DropHere; returns false when nothing was moved (or the move is refused).</summary>
        public static bool Drop(TFolder root, object source, object target, TargetDropZone dropZone)
        {
            if (!CanDrop(root, source, target, dropZone)) return false;
            TFolder sourceFolder = source as TFolder, targetFolder = target as TFolder;
            TItem sourceItem = source as TItem, targetItem = target as TItem;
            if (sourceFolder != null)
            {
                if (dropZone == TargetDropZone.Bottom)
                    return DragFolderRelativeToFolderImpl(root, sourceFolder, targetFolder, true);
                if (dropZone == TargetDropZone.Top)
                    return DragFolderRelativeToFolderImpl(root, sourceFolder, targetFolder, false);
                return DropFolderToFolder(root, sourceFolder, targetFolder);
            }
            if (targetFolder == null)
            {
                DragItemRelativeToItemImpl(root, sourceItem, targetItem,
                    after: dropZone == TargetDropZone.Bottom || dropZone == TargetDropZone.MiddleBottom);
            }
            else
            {
                DragItemToFolder(root, sourceItem, targetFolder);
            }
            return true;
        }

        private static bool IsValidFolderMove(TFolder source, TFolder target)
        {
            foreach (TFolder subFolder in source.SubFolders)
            {
                if (!IsValidFolderMove(subFolder, target))
                    return false;
            }
            return source != target;
        }

        private static void PerformActionWithoutNotification(TFolder folder, Action<TFolder> action)
        {
            bool skipNotification = folder.ShouldSkipChangeNotifications;
            folder.ShouldSkipChangeNotifications = true;
            action(folder);
            folder.ShouldSkipChangeNotifications = skipNotification;
        }

        private static bool DragFolderRelativeToFolderImpl(TFolder root, TFolder folderToMove, TFolder targetFolder, bool after)
        {
            if (!IsValidFolderMove(folderToMove, targetFolder))
                return false;
            TFolder parentOfSource = FindParentFolder(root, folderToMove)
                ?? throw new AGSEditorException("Folder being moved has no parent");
            TFolder parentOfTarget = FindParentFolder(root, targetFolder)
                ?? throw new AGSEditorException("Target folder has no parent");
            if (parentOfTarget == folderToMove)
                return false;

            PerformActionWithoutNotification(parentOfSource, folder => folder.SubFolders.Remove(folderToMove));
            int targetIndex = parentOfTarget.SubFolders.IndexOf(targetFolder);  // removal may have changed index
            if (after) targetIndex++;                                           // inserts before if 'after' is false
            PerformActionWithoutNotification(parentOfTarget, folder => folder.SubFolders.Insert(targetIndex, folderToMove));
            return true;
        }

        private static void DragItemRelativeToItemImpl(TFolder root, TItem itemToMove, TItem targetItem, bool after)
        {
            TFolder sourceFolder = FindFolderThatContainsItem(root, itemToMove)
                ?? throw new AGSEditorException("Source item was not in a folder");
            TFolder targetFolder = FindFolderThatContainsItem(root, targetItem)
                ?? throw new AGSEditorException("Target item was not in a folder");

            PerformActionWithoutNotification(sourceFolder, folder => folder.Items.Remove(itemToMove));
            int targetIndex = targetFolder.Items.IndexOf(targetItem);  // removal may have changed index
            if (after) targetIndex++;                                 // inserts before if 'after' is false
            PerformActionWithoutNotification(targetFolder, folder => folder.Items.Insert(targetIndex, itemToMove));
        }

        private static void DragItemToFolder(TFolder root, TItem itemToMove, TFolder targetFolder)
        {
            TFolder sourceFolder = FindFolderThatContainsItem(root, itemToMove)
                ?? throw new AGSEditorException("Source item was not in a folder");
            PerformActionWithoutNotification(sourceFolder, folder => folder.Items.Remove(itemToMove));
            PerformActionWithoutNotification(targetFolder, folder => folder.Items.Add(itemToMove));
        }

        private static bool DropFolderToFolder(TFolder root, TFolder folderToMove, TFolder targetFolder)
        {
            if (!IsValidFolderMove(folderToMove, targetFolder))
                return false;
            TFolder parentOfSource = FindParentFolder(root, folderToMove)
                ?? throw new AGSEditorException("Folder being moved has no parent?");
            if (parentOfSource == targetFolder)
                return false;
            PerformActionWithoutNotification(parentOfSource, folder => folder.SubFolders.Remove(folderToMove));
            PerformActionWithoutNotification(targetFolder, folder => folder.SubFolders.Add(folderToMove));
            return true;
        }

        public static TFolder FindFolderThatContainsItem(TFolder rootFolder, TItem item)
        {
            foreach (TFolder subFolder in rootFolder.SubFolders)
            {
                TFolder result = FindFolderThatContainsItem(subFolder, item);
                if (result != null)
                    return result;
            }
            return rootFolder.Items.Contains(item) ? rootFolder : null;
        }

        public static TFolder FindParentFolder(TFolder rootFolder, TFolder folder)
        {
            foreach (TFolder subFolder in rootFolder.SubFolders)
            {
                if (subFolder == folder)
                    return rootFolder;
                TFolder result = FindParentFolder(subFolder, folder);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}
