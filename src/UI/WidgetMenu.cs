// NJ-Flush: the widget's right-click menu (also the tray icon's).
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using W = System.Windows;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;

partial class Widget {
    // the right-click menu (and the tray icon's): built again when the language changes
    ContextMenuStrip BuildMenu() {
        var menu = new ContextMenuStrip();
        menu.RightToLeft = Tx.Ar ? RightToLeft.Yes : RightToLeft.No;
        if (Tx.Ar) menu.Font = new Font(face, 9f);
        // seven top-level choices or fewer before the separator (L15); flushes and previews in submenus
        menu.Items.Add(Tx.T("menu.open"), null, delegate { Panel.Open(); }).Font = new Font(menu.Font, FontStyle.Bold);
        allItem = (ToolStripMenuItem)menu.Items.Add(Tx.T("menu.all"), null, delegate { TryFlush("all"); });
        var one = new ToolStripMenuItem(Tx.T("menu.one"));
        for (int i = 1; i <= 3; i++) { string kind = Settings.Actions[i]; oneItems[i - 1] = (ToolStripMenuItem)one.DropDownItems.Add(Tx.Action(i), null, delegate { TryFlush(kind); }); }
        one.DropDownItems.Add(new ToolStripSeparator());
        one.DropDownItems.Add(Tx.T("menu.deep"), null, delegate { TryFlush("ram", true); });
        menu.Items.Add(one);
        var guard = new ToolStripMenuItem(Tx.T("menu.guard")) { Checked = Settings.Guard };
        guard.Click += delegate { Settings.Guard = !Settings.Guard; Settings.Save(); guard.Checked = Settings.Guard; };
        menu.Items.Add(guard);
        // previews grouped by flush, each with its variants (Nader, round 2)
        var preview = new ToolStripMenuItem(Tx.T("menu.preview"));
        foreach (var grp in new[] {
            Tuple.Create(Tx.T("grp.ram"), new[] { "light", "medium", "heavy", "clogged" }, Tx.TierNames()),
            Tuple.Create(Tx.T("grp.cpu"), new[] { "cpu-0", "cpu-1", "cpu-2", "cpu-3" }, Tx.SceneNames("cpu")),
            Tuple.Create(Tx.T("grp.gpu"), new[] { "gpu-0", "gpu-1", "gpu-2", "gpu-3" }, Tx.SceneNames("gpu")),
            Tuple.Create(Tx.T("grp.all"), new[] { "all-0", "all-1", "all-2", "all-3" }, Tx.SceneNames("all")) }) {
            var sub = new ToolStripMenuItem(grp.Item1);
            for (int k = 0; k < 4; k++) {
                string arg = grp.Item2[k];
                sub.DropDownItems.Add(grp.Item3[k], null, delegate { Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "/preview " + arg) { UseShellExecute = false }); });
            }
            preview.DropDownItems.Add(sub);
        }
        menu.Items.Add(preview);
        menu.Items.Add(Tx.T("menu.settings"), null, delegate { Panel.Open("settings"); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Tx.T("menu.reset_pos"), null, delegate { Settings.WidgetX = Settings.WidgetY = -1; Settings.Save(); Pin(); });
        updateItem = (ToolStripMenuItem)menu.Items.Add(Tx.T("menu.check"), null, delegate { Updater.Interactive(this); });
        if (updateTag != null) { updateItem.Text = Tx.F("menu.update_to", updateTag); updateItem.Font = new Font(updateItem.Font, FontStyle.Bold); }
        menu.Items.Add(Tx.T("menu.uninstall"), null, delegate {
            if (Tx.Box(null, Tx.T("menu.uninstall_q"), "NJ-Flush", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) { App.Uninstall(); Close(); }
        });
        menu.Items.Add(Tx.T("menu.close"), null, delegate { Close(); });
        menu.Opening += delegate {   // shortcuts shown next to their actions, as they are set now
            allItem.ShortcutKeyDisplayString = Settings.Hotkey ? Settings.Keys_[0] : "";
            for (int i = 0; i < 3; i++) oneItems[i].ShortcutKeyDisplayString = Settings.Hotkey ? Settings.Keys_[i + 1] : "";
            guard.Checked = Settings.Guard;
        };
        return menu;
    }
    string updateTag;   // a newer release found by the daily check

    // the language changed (panel Settings): the menu, the right-to-left reading of tips and the painted text follow at once
}
