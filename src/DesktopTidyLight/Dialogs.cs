using System.Windows.Forms;

namespace DesktopTidyLight
{
    internal static class Dialogs
    {
        // Always use an unowned window from our process. Inferring the owner can
        // disable Explorer's Progman through a desktop-owned group window.
        internal static DialogResult Show(Form dialog)
        {
            using (var owner = new Form { ShowInTaskbar = false })
            {
                var handle = owner.Handle;
                return dialog.ShowDialog(owner);
            }
        }

        internal static DialogResult Message(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            using (var owner = new Form { ShowInTaskbar = false })
            {
                var handle = owner.Handle;
                return MessageBox.Show(owner, text, title, buttons, icon);
            }
        }
    }
}
