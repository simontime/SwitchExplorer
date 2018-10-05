using LibHac;
using System.Windows.Forms;

namespace SwitchExplorer
{
    internal class IO
    {
        // Thanks to Alex Barney for providing this code!
        public static void PopulateTreeView(TreeNodeCollection nodes, RomfsDir root)
        {
            RomfsFile fileNode = root.FirstFile;

            while (fileNode != null)
            {
                nodes.Add(fileNode.FullPath, fileNode.Name);
                fileNode = fileNode.NextSibling;
            }

            RomfsDir dirNode = root.FirstChild;

            while (dirNode != null)
            {
                TreeNode newNode = nodes.Add(dirNode.FullPath, dirNode.Name);
                PopulateTreeView(newNode.Nodes, dirNode);
                dirNode = dirNode.NextSibling;
            }
        }
    }
}