using System;
using System.Windows.Forms;

namespace SwitchExplorer
{
    static class Program
    {
        public static string FileArg { get; internal set; }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0) FileArg = args[0];
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
