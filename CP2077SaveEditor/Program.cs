using System;
using System.Windows.Forms;
using CP2077SaveEditor.Utils;
using CP2077SaveEditor.Views;

namespace CP2077SaveEditor
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Scrollbars, spin buttons, the tab strip and the common dialogs are
            // drawn by Windows, not by us -- this is the only way they go dark.
            Theme.InitProcess();
            Application.Run(new Form2());
        }
    }
}
