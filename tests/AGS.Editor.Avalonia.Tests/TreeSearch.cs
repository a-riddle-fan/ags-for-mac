using System.Collections.Generic;
using System.Linq;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Tests
{
    internal static class TreeSearch
    {
        public static IEnumerable<ProjectTreeNode> Descendants(this ProjectTreeNode node)
        {
            foreach (ProjectTreeNode child in node.Children)
            {
                yield return child;
                foreach (ProjectTreeNode d in child.Descendants())
                    yield return d;
            }
        }

        public static ProjectTreeNode Find(this ProjectTreeNode node, string name) => node.Descendants().Single(n => n.Name == name);
    }
}
