using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// "Ctrl+Alt+F" <-> (modifiers, key). Modifiers: 1 Alt, 2 Ctrl, 4 Shift, 8 Win (RegisterHotKey's bits). Empty = no shortcut.
static class Hotkeys {
    public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;
    public static bool Parse(string s, out uint mods, out uint key) {
        mods = 0; key = 0;
        if (string.IsNullOrEmpty(s) || s.Trim().Length == 0) return true;   // none
        foreach (string raw in s.Split('+')) {
            string p = raw.Trim().ToLowerInvariant();
            if (p == "ctrl" || p == "control") mods |= Ctrl; else if (p == "alt") mods |= Alt; else if (p == "shift") mods |= Shift; else if (p == "win") mods |= Win;
            else {
                if (key != 0) return false;
                System.Windows.Forms.Keys k;
                if (p.Length == 1 && char.IsDigit(p[0])) p = "d" + p;
                if (!Enum.TryParse(p, true, out k) || k == System.Windows.Forms.Keys.None) return false;
                key = (uint)k;
            }
        }
        return key != 0 && mods != 0;   // a global shortcut needs a modifier, or it would steal a plain key from every app
    }
    public static string Format(uint mods, uint key) {
        if (key == 0) return "";
        var parts = new List<string>();
        if ((mods & Ctrl) != 0) parts.Add("Ctrl"); if ((mods & Alt) != 0) parts.Add("Alt"); if ((mods & Shift) != 0) parts.Add("Shift"); if ((mods & Win) != 0) parts.Add("Win");
        var k = (System.Windows.Forms.Keys)key; string n = k.ToString();
        if (k >= System.Windows.Forms.Keys.D0 && k <= System.Windows.Forms.Keys.D9) n = n.Substring(1);
        parts.Add(n);
        return string.Join("+", parts);
    }
    public static string Normal(string s) { uint m, k; return Parse(s, out m, out k) ? Format(m, k) : null; }
}
