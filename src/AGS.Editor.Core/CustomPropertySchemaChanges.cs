using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor
{
    // Copied from upstream GUI/CustomPropertySchemaEditor.cs (that file is a WinForms form).
    public enum CustomPropertyDefChangeType
    {
        None,
        Add,
        Edit,
        Remove
    }

    public struct CustomPropertyDefChange
    {
        public CustomPropertyDefChangeType Type;
        public string OriginalName;
        public string NewName;

        public CustomPropertyDefChange(CustomPropertyDefChangeType type, string originalName, string newName)
        {
            Type = type;
            OriginalName = originalName;
            NewName = newName;
        }
    }

    /// <summary>
    /// CustomPropertiesComponent.ShowSchemaEditor (after the dialog) + Tasks.HandleCustomPropertySchemaChanges /
    /// ApplyCustomPropertySchemaChanges(InRoom): renamed and removed schema properties are applied to the values of
    /// every character, inventory item and room (room, objects, hotspots); each room file is loaded and resaved
    /// through libagsnative (RoomsComponent.ModifyAllRooms). Rooms must not be open in an editor.
    /// </summary>
    public static class CustomPropertySchemaChanges
    {
        /// <summary>Only name changes need the rooms to be rewritten (as upstream).</summary>
        public static bool HasRenames(IEnumerable<CustomPropertyDefChange> changes) =>
            changes.Any(sc => sc.Type == CustomPropertyDefChangeType.Edit && sc.OriginalName != sc.NewName);

        /// <summary>Applies <paramref name="newSchema"/> with its changes; returns room errors.</summary>
        public static CompileMessages Apply(GameProject project, CustomPropertySchema newSchema, List<CustomPropertyDefChange> schemaChanges)
        {
            var errors = new CompileMessages();
            Game game = project.Game;
            if (!HasRenames(schemaChanges))
            {
                game.PropertySchema.CopyFrom(newSchema);
                return errors;
            }
            // As upstream: keep the old names while updating, add the new ones, then copy the final schema.
            foreach (var change in schemaChanges)
            {
                switch (change.Type)
                {
                    case CustomPropertyDefChangeType.Edit:
                    {
                        var oldItem = game.PropertySchema.PropertyDefinitions.Find(p => p.Name == change.OriginalName);
                        if (oldItem == null) break;
                        CustomPropertySchemaItem newItem = oldItem.Clone() as CustomPropertySchemaItem;
                        newItem.Name = change.NewName;
                        game.PropertySchema.PropertyDefinitions.Add(newItem);
                    }
                    break;
                    case CustomPropertyDefChangeType.Remove:
                        game.PropertySchema.PropertyDefinitions.RemoveAll(p => p.Name == change.OriginalName);
                        break;
                }
            }

            var allProps = new List<CustomProperties>();
            foreach (var c in game.Characters)
                allProps.Add(c.Properties);
            foreach (var i in game.InventoryItems)
                allProps.Add(i.Properties);
            ApplyCustomPropertySchemaChanges(allProps, schemaChanges);

            foreach (UnloadedRoom unloadedRoom in game.RootRoomFolder.AllItemsFlat)
            {
                string roomFile = Path.Combine(project.Directory, ProjectPaths.ToLocal(unloadedRoom.FileName));
                try
                {
                    using NativeRoom native = NativeRoom.Load(roomFile);
                    Room room = native.ToRoom(unloadedRoom, game.TextEncoding);
                    ApplyCustomPropertySchemaChangesInRoom(room, schemaChanges);
                    native.ApplyRoom(room, game.TextEncoding);
                    native.Save(roomFile);
                }
                catch (Exception ex)
                {
                    errors.Add(new CompileError($"Failed to update room {unloadedRoom.Number}: {ex.Message}"));
                }
            }

            game.PropertySchema.CopyFrom(newSchema);
            return errors;
        }

        // Tasks.ApplyCustomPropertySchemaChanges (copied)
        private static void ApplyCustomPropertySchemaChanges(List<CustomProperties> allProps, List<CustomPropertyDefChange> schemaChanges)
        {
            if (allProps.Count == 0 || schemaChanges.Count == 0)
                return;

            foreach (var props in allProps)
            {
                if (props.PropertyValues.Count == 0)
                    continue;

                foreach (var change in schemaChanges)
                {
                    if (change.Type == CustomPropertyDefChangeType.Remove)
                    {
                        props.PropertyValues.Remove(change.OriginalName);
                    }
                    else if (change.Type == CustomPropertyDefChangeType.Edit &&
                        change.NewName.ToLowerInvariant() != change.OriginalName.ToLowerInvariant())
                    {
                        if (props.PropertyValues.ContainsKey(change.OriginalName))
                        {
                            var oldProp = props.PropertyValues[change.OriginalName];
                            props.PropertyValues.Remove(change.OriginalName);
                            oldProp.Name = change.NewName;
                            props.PropertyValues.Add(change.NewName, oldProp);
                        }
                    }
                }
            }
        }

        // Tasks.ApplyCustomPropertySchemaChangesInRoom (copied)
        private static void ApplyCustomPropertySchemaChangesInRoom(Room room, List<CustomPropertyDefChange> schemaChanges)
        {
            List<CustomProperties> allProps = new List<CustomProperties>();
            allProps.Add(room.Properties);
            foreach (var o in room.Objects)
                allProps.Add(o.Properties);
            foreach (var h in room.Hotspots)
                allProps.Add(h.Properties);
            ApplyCustomPropertySchemaChanges(allProps, schemaChanges);
            room.Modified = true;
        }
    }
}
