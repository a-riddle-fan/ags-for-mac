using System;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// The model behind the custom properties dialog (upstream CustomPropertiesEditor): a
    /// <see cref="CustomPropertyBag"/> (compiled as-is) shows the schema's properties that apply to the
    /// object's kind, each editing the object's <see cref="CustomProperties"/>.
    /// </summary>
    public static class CustomPropertiesEditing
    {
        /// <summary>GUIController.ShowPropertiesEditorFromPropertyGrid: which schema entries apply.</summary>
        public static CustomPropertyAppliesTo AppliesTo(object objectThatHasProperties)
        {
            switch (objectThatHasProperties)
            {
                case Character _: return CustomPropertyAppliesTo.Characters;
                case RoomHotspot _: return CustomPropertyAppliesTo.Hotspots;
                case InventoryItem _: return CustomPropertyAppliesTo.InventoryItems;
                case RoomObject _: return CustomPropertyAppliesTo.Objects;
                case Room _: return CustomPropertyAppliesTo.Rooms;
                default:
                    throw new AGSEditorException("Object not recognised to contain properties: " + objectThatHasProperties);
            }
        }

        /// <summary>An object for a property grid: one row per applicable schema property.</summary>
        public static object CreatePropertyBag(Game game, CustomProperties properties, object objectThatHasProperties)
        {
            return new CustomPropertyBag(game.PropertySchema, properties, AppliesTo(objectThatHasProperties));
        }
    }
}
