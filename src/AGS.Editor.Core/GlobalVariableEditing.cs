using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AGS.Types;

namespace AGS.Editor
{
    /// <summary>
    /// Global variables editor logic (upstream Panes/GlobalVariablesEditor + GUI/GlobalVariableDialog): the
    /// type list (built-in types and the managed types of the script API) and the dialog's validation.
    /// </summary>
    public static class GlobalVariableEditing
    {
        private const string VARIABLE_NAME_REGEX = @"^[a-zA-Z_][a-zA-Z0-9_]*$";

        /// <summary>A variable type and how its default value is validated (GlobalVariableDialog.GlobalVariableType).</summary>
        public sealed class VariableType
        {
            private readonly string _validationRegEx;

            public VariableType(string typeName, string validationRegEx)
            {
                TypeName = typeName;
                _validationRegEx = validationRegEx;
            }

            public string TypeName { get; }
            public bool CanHaveDefaultValue => _validationRegEx != null;

            public bool IsDefaultValueValid(string defaultValue) =>
                Regex.IsMatch(defaultValue, _validationRegEx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            public override string ToString() => TypeName;
        }

        /// <summary>GlobalVariablesEditor.PopulateScriptTypeList: struct types declared in the script API.</summary>
        public static IReadOnlyList<APITypeDef> ApiTypes()
        {
            var types = new List<APITypeDef>();
            string api = EditorResources.ReadText("agsdefns.sh");
            var regex = new Regex(@"\b(?:(internalstring|autoptr|builtin|managed)\s+)+struct\s+(\w+)", RegexOptions.Compiled);
            for (Match m = regex.Match(api); m.Success; m = m.NextMatch())
            {
                bool is_string = false, is_managed = false, is_autoptr = false;
                foreach (Capture c in m.Groups[1].Captures)
                {
                    is_string |= c.Value == "internalstring";
                    is_managed |= c.Value == "managed";
                    is_autoptr |= c.Value == "autoptr";
                }
                types.Add(new APITypeDef(m.Groups[2].Captures[0].Value, is_autoptr, is_managed, is_string));
            }
            return types;
        }

        /// <summary>GlobalVariableDialog.PopulateTypeList.</summary>
        public static IReadOnlyList<VariableType> VariableTypes(IEnumerable<APITypeDef> apiTypes)
        {
            var list = new List<VariableType>
            {
                new VariableType("int", @"^\-?[0-9]+$"),
                new VariableType("String", @"^.*$"),
                new VariableType("float", @"^\-?[0-9]+(\.[0-9]+)?$"),
                new VariableType("bool", @"^true$|^false$"),
            };
            if (apiTypes != null)
            {
                foreach (var def in apiTypes)
                {
                    if (!def.Managed || def.String) continue;
                    list.Add(new VariableType(def.Autoptr ? def.Name : string.Format("{0}*", def.Name), null));
                }
            }
            return list;
        }

        /// <summary>
        /// GlobalVariableDialog.btnOK_Click: validates and, if valid, applies the values to <paramref name="variable"/>
        /// (renaming it in <paramref name="variables"/> when it is already listed). Returns an error message or null.
        /// </summary>
        public static string Apply(Game game, GlobalVariable variable, string name, VariableType type, string defaultValue,
            GlobalVariableArrayType arrayType, int arraySize)
        {
            defaultValue ??= string.Empty;
            name ??= string.Empty;
            if (!string.IsNullOrEmpty(defaultValue) && (type == null || !type.CanHaveDefaultValue || !type.IsDefaultValueValid(defaultValue)))
                return "The default value '" + defaultValue + "' is not valid for this type of variable. Please try again.";
            if (name == string.Empty)
                return "You must type in a name for the new variable.";
            if (!Regex.IsMatch(name, VARIABLE_NAME_REGEX))
                return "The variable name '" + name + "' is not a valid variable name. It can contain only letters, numbers and the underscore character; and cannot begin with a number.";
            if (game.IsScriptNameAlreadyUsed(name, variable))
                return "The variable name '" + name + "' is already being used by something else in the game. Please choose another name.";

            string nameWas = variable.Name;
            variable.Name = name;
            variable.DefaultValue = defaultValue;
            variable.Type = type?.TypeName ?? "int";
            variable.ArrayType = arrayType;
            variable.ArraySize = arrayType == GlobalVariableArrayType.Array ? Math.Max(1, arraySize) : 0;
            if (nameWas != null && nameWas != name && game.GlobalVariables.ToList().Contains(variable))
                game.GlobalVariables.VariableRenamed(variable, nameWas); // EditSelectedVariable
            return null;
        }
    }
}
