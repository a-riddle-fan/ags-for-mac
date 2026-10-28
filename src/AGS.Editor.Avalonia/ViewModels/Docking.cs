using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace AGS.Editor.Avalonia.ViewModels
{
    /// <summary>A fixed panel of the main window (project tree, properties, output) hosted in the dock layout.</summary>
    public abstract class EditorTool : Tool, IDeferredContentPresentation
    {
        /// <summary>Present at once (no deferred realization): the panels are always needed.</summary>
        public bool DeferContentPresentation => false;

        protected EditorTool(string id, string title, MainWindowViewModel main)
        {
            Id = id;
            Title = title;
            Main = main;
            CanClose = false;  // the panels stay; "Reset Layout" brings them back to their places
        }

        public MainWindowViewModel Main { get; }
    }

    public sealed class ProjectTool : EditorTool
    {
        public ProjectTool(MainWindowViewModel main) : base("Project", "Project", main) { }
    }

    public sealed class PropertiesTool : EditorTool
    {
        public PropertiesTool(MainWindowViewModel main) : base("Properties", "Properties", main) { }
    }

    public sealed class OutputTool : EditorTool
    {
        public OutputTool(MainWindowViewModel main) : base("Output", "Output", main) { }
    }

    /// <summary>
    /// A document tab of the dock: wraps one of the editor's <see cref="DocumentViewModel"/>s (shown through the existing
    /// data templates). Closing goes through the view model's CloseDocument command (asks about unsaved rooms); the
    /// dock tab is removed when the view model drops the document.
    /// </summary>
    public sealed class EditorDockDocument : Document, IDeferredContentPresentation
    {
        /// <summary>
        /// Present at once: callers open a document and then select a line or a range in its editor, which needs the
        /// view (deferred realization would create it a few frames later).
        /// </summary>
        public bool DeferContentPresentation => false;

        private readonly MainWindowViewModel _main;

        public EditorDockDocument(DocumentViewModel model, MainWindowViewModel main)
        {
            Model = model;
            _main = main;
            Id = "Document";
            Title = model.Title;
            model.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DocumentViewModel.Title)) Title = model.Title;
            };
        }

        public DocumentViewModel Model { get; }

        public override bool OnClose()
        {
            _main.CloseDocumentCommand.Execute(Model);
            return false; // removed by the main window when the view model's document list changes
        }
    }

    /// <summary>
    /// The main window's dock layout (Dock.Avalonia): project tree on the left, documents in the middle, properties on
    /// the right and output at the bottom — like the upstream editor's default DockPanel layout. Panels can be moved,
    /// tabbed together and resized; documents can be split.
    /// </summary>
    public sealed class EditorDockFactory : Factory
    {
        private readonly MainWindowViewModel _main;

        public EditorDockFactory(MainWindowViewModel main)
        {
            _main = main;
        }

        public IDocumentDock Documents { get; private set; }

        public override IRootDock CreateLayout()
        {
            var project = new ProjectTool(_main);
            var properties = new PropertiesTool(_main);
            var output = new OutputTool(_main);

            Documents = new DocumentDock
            {
                Id = "Documents",
                Title = "Documents",
                IsCollapsable = false,
                CanCreateDocument = false,
                Proportion = double.NaN,
                VisibleDockables = CreateList<IDockable>(),
            };
            var left = new ToolDock
            {
                Id = "LeftPane", Title = "Left", Proportion = 0.2,
                ActiveDockable = project, VisibleDockables = CreateList<IDockable>(project), Alignment = Alignment.Left,
            };
            var right = new ToolDock
            {
                Id = "RightPane", Title = "Right", Proportion = 0.24,
                ActiveDockable = properties, VisibleDockables = CreateList<IDockable>(properties), Alignment = Alignment.Right,
            };
            var bottom = new ToolDock
            {
                Id = "BottomPane", Title = "Bottom", Proportion = 0.2,
                ActiveDockable = output, VisibleDockables = CreateList<IDockable>(output), Alignment = Alignment.Bottom,
            };
            var top = new ProportionalDock
            {
                Id = "Top", Orientation = Orientation.Horizontal, Proportion = double.NaN,
                VisibleDockables = CreateList<IDockable>(left, new ProportionalDockSplitter(), Documents, new ProportionalDockSplitter(), right),
            };
            var main = new ProportionalDock
            {
                Id = "Main", Orientation = Orientation.Vertical, IsCollapsable = false,
                VisibleDockables = CreateList<IDockable>(top, new ProportionalDockSplitter(), bottom),
            };
            var root = CreateRootDock();
            root.Id = "Root";
            root.IsCollapsable = false;
            root.ActiveDockable = main;
            root.DefaultDockable = main;
            root.VisibleDockables = CreateList<IDockable>(main);
            return root;
        }

        // ---- layout persistence: the arrangement of the panels (not the open documents) ----

        /// <summary>The arrangement of <paramref name="root"/> as JSON: docks, sizes, which panel is where, pinned panels.</summary>
        public JsonObject SaveLayout(IRootDock root)
        {
            IDockable main = root.VisibleDockables?.FirstOrDefault();
            var json = new JsonObject
            {
                ["version"] = 1,
                ["root"] = main == null ? null : SaveNode(main),
                ["pinned"] = new JsonObject
                {
                    ["left"] = Ids(root.LeftPinnedDockables),
                    ["right"] = Ids(root.RightPinnedDockables),
                    ["top"] = Ids(root.TopPinnedDockables),
                    ["bottom"] = Ids(root.BottomPinnedDockables),
                },
            };
            return json;
        }

        private static JsonArray Ids(IList<IDockable> dockables) =>
            new JsonArray((dockables ?? new List<IDockable>()).OfType<EditorTool>().Select(t => (JsonNode)JsonValue.Create(t.Id)).ToArray());

        private static JsonNode Proportion(double value) => double.IsNaN(value) || double.IsInfinity(value) ? null : JsonValue.Create(value);

        private static JsonObject SaveNode(IDockable dockable)
        {
            switch (dockable)
            {
                case IProportionalDockSplitter _:
                    return new JsonObject { ["kind"] = "splitter" };
                case IDocumentDock d:
                    return new JsonObject { ["kind"] = "documents", ["proportion"] = Proportion(d.Proportion) };
                case IToolDock t:
                    return new JsonObject
                    {
                        ["kind"] = "tools",
                        ["proportion"] = Proportion(t.Proportion),
                        ["alignment"] = t.Alignment.ToString(),
                        ["active"] = (t.ActiveDockable as EditorTool)?.Id,
                        ["tools"] = Ids(t.VisibleDockables),
                    };
                case IProportionalDock p:
                    return new JsonObject
                    {
                        ["kind"] = "proportional",
                        ["proportion"] = Proportion(p.Proportion),
                        ["orientation"] = p.Orientation.ToString(),
                        ["children"] = new JsonArray((p.VisibleDockables ?? new List<IDockable>()).Select(c => (JsonNode)SaveNode(c)).ToArray()),
                    };
                default:
                    return new JsonObject { ["kind"] = "unknown" };
            }
        }

        /// <summary>
        /// A layout saved by <see cref="SaveLayout"/>, with fresh panels; null when it is unusable (unknown version,
        /// a panel missing or twice, no document area) — the caller then uses the default layout.
        /// </summary>
        public IRootDock LoadLayout(JsonObject json)
        {
            try
            {
                if (json?["version"]?.GetValue<int>() != 1 || json["root"] is not JsonObject rootNode) return null;
                var tools = new Dictionary<string, EditorTool>
                {
                    ["Project"] = new ProjectTool(_main),
                    ["Properties"] = new PropertiesTool(_main),
                    ["Output"] = new OutputTool(_main),
                };
                var used = new HashSet<string>();
                EditorTool Tool(string id) => id != null && tools.TryGetValue(id, out EditorTool tool) && used.Add(id) ? tool : null;
                Documents = null;
                IDockable main = LoadNode(rootNode, Tool);
                if (main == null || Documents == null) return null;

                var pinned = json["pinned"] as JsonObject;
                IList<IDockable> Pinned(string side) => CreateList<IDockable>(
                    ((pinned?[side] as JsonArray) ?? new JsonArray()).Select(n => (IDockable)Tool(n?.GetValue<string>())).Where(t => t != null).ToArray());
                var left = Pinned("left");
                var right = Pinned("right");
                var top = Pinned("top");
                var bottom = Pinned("bottom");
                // panels that were in floating windows (not saved) come back docked on the right
                var missing = tools.Values.Where(t => !used.Contains(t.Id)).ToList();
                if (missing.Count > 0)
                {
                    var dock = new ToolDock
                    {
                        Proportion = 0.24, Alignment = Alignment.Right, ActiveDockable = missing[0],
                        VisibleDockables = CreateList(missing.Cast<IDockable>().ToArray()),
                    };
                    main = main is IProportionalDock { Orientation: Orientation.Horizontal } horizontal
                        ? AppendTo(horizontal, dock)
                        : new ProportionalDock { Orientation = Orientation.Horizontal, VisibleDockables = CreateList(main, new ProportionalDockSplitter(), dock) };
                }

                if (main is not IProportionalDock mainDock)
                    mainDock = new ProportionalDock { Orientation = Orientation.Horizontal, VisibleDockables = CreateList(main) };
                mainDock.Id = "Main";
                mainDock.IsCollapsable = false;
                var root = CreateRootDock();
                root.Id = "Root";
                root.IsCollapsable = false;
                root.ActiveDockable = mainDock;
                root.DefaultDockable = mainDock;
                root.VisibleDockables = CreateList<IDockable>(mainDock);
                root.LeftPinnedDockables = left;
                root.RightPinnedDockables = right;
                root.TopPinnedDockables = top;
                root.BottomPinnedDockables = bottom;
                return root;
            }
            catch (Exception)
            {
                return null; // a damaged file: the default layout is used
            }
        }

        private IProportionalDock AppendTo(IProportionalDock dock, IDockable dockable)
        {
            dock.VisibleDockables.Add(new ProportionalDockSplitter());
            dock.VisibleDockables.Add(dockable);
            return dock;
        }

        private IDockable LoadNode(JsonObject node, Func<string, EditorTool> tool)
        {
            double proportion = node["proportion"]?.GetValue<double>() ?? double.NaN;
            switch (node["kind"]?.GetValue<string>())
            {
                case "splitter":
                    return new ProportionalDockSplitter();
                case "documents":
                    if (Documents != null) return null; // only one document area (split ones are merged)
                    Documents = new DocumentDock
                    {
                        Id = "Documents", Title = "Documents", IsCollapsable = false, CanCreateDocument = false,
                        Proportion = proportion, VisibleDockables = CreateList<IDockable>(),
                    };
                    return Documents;
                case "tools":
                    var list = ((node["tools"] as JsonArray) ?? new JsonArray()).Select(n => (IDockable)tool(n?.GetValue<string>())).Where(t => t != null).ToList();
                    if (list.Count == 0) return null;
                    string active = node["active"]?.GetValue<string>();
                    return new ToolDock
                    {
                        Proportion = proportion,
                        Alignment = Enum.TryParse(node["alignment"]?.GetValue<string>(), out Alignment alignment) ? alignment : Alignment.Unset,
                        ActiveDockable = list.FirstOrDefault(t => ((EditorTool)t).Id == active) ?? list[0],
                        VisibleDockables = CreateList(list.ToArray()),
                    };
                case "proportional":
                    var children = ((node["children"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>()
                        .Select(c => LoadNode(c, tool)).Where(c => c != null).ToList();
                    // no splitters at the ends or next to each other (left over from docks that were dropped)
                    var clean = new List<IDockable>();
                    foreach (IDockable child in children)
                    {
                        bool splitter = child is IProportionalDockSplitter;
                        if (splitter && (clean.Count == 0 || clean[^1] is IProportionalDockSplitter)) continue;
                        clean.Add(child);
                    }
                    while (clean.Count > 0 && clean[^1] is IProportionalDockSplitter) clean.RemoveAt(clean.Count - 1);
                    if (clean.Count == 0) return null;
                    if (clean.Count == 1 && clean[0] is not IProportionalDockSplitter)
                    {
                        if (!double.IsNaN(proportion)) clean[0].Proportion = proportion;
                        return clean[0];
                    }
                    return new ProportionalDock
                    {
                        Proportion = proportion,
                        Orientation = Enum.TryParse(node["orientation"]?.GetValue<string>(), out Orientation orientation) ? orientation : Orientation.Horizontal,
                        VisibleDockables = CreateList(clean.ToArray()),
                    };
                default:
                    return null;
            }
        }

        public override void InitLayout(IDockable layout)
        {
            ContextLocator = new Dictionary<string, System.Func<object>>();
            DockableLocator = new Dictionary<string, System.Func<IDockable>>();
            HostWindowLocator = new Dictionary<string, System.Func<IHostWindow>>();
            base.InitLayout(layout);
        }
    }
}
