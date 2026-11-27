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

// Updates (Nader 2026-11-23, "like my other tools"): reads the latest GitHub release, compares it with this version, and on request
// downloads NJ-Flush.exe over HTTPS, checks its SHA-256 against the digest GitHub publishes, swaps it in and restarts the widget.
static class Updater {
    public const string Version = "1.3.0", Repo = "NJ-Burrito/NJ-Flush";
    public class Info { public string Tag, Url, Sha, Error; public bool Newer; }

    static string Get(string url) {
        System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;  // TLS 1.2 on .NET 4
        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        req.UserAgent = "NJ-Flush/" + Version; req.Accept = "application/vnd.github+json"; req.Timeout = 10000;
        using (var r = new StreamReader(req.GetResponse().GetResponseStream())) return r.ReadToEnd();
    }

    public static Info Check() {
        var i = new Info();
        try {
            return Parse(Get("https://api.github.com/repos/" + Repo + "/releases/latest"), Version);
        } catch (Exception e) { i.Error = Tx.F("upd.unreachable", e.Message); }
        return i;
    }

    // a release reply -> tag, exe URL, its SHA-256 (from GitHub's digest) and whether it is newer than `current`
    public static Info Parse(string json, string current) {
        var i = new Info();
        try {
            var tag = System.Text.RegularExpressions.Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            var asset = System.Text.RegularExpressions.Regex.Match(json, "\"name\"\\s*:\\s*\"NJ-Flush\\.exe\"[\\s\\S]*?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
            if (!tag.Success || !asset.Success) { i.Error = Tx.T("upd.no_exe"); return i; }
            i.Tag = tag.Groups[1].Value; i.Url = asset.Groups[1].Value;
            var sha = System.Text.RegularExpressions.Regex.Match(asset.Value, "\"digest\"\\s*:\\s*\"sha256:([0-9a-fA-F]{64})\"");
            if (sha.Success) i.Sha = sha.Groups[1].Value.ToLowerInvariant();
            i.Newer = new Version(i.Tag.TrimStart('v', 'V')) > new Version(current);
        } catch (Exception e) { i.Error = Tx.F("upd.unreadable", e.Message); }
        return i;
    }
    public static bool ShaMatches(byte[] data, string sha) {
        if (data == null || sha == null || data.Length < 2 || data[0] != 'M' || data[1] != 'Z') return false;
        using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant() == sha;
    }

    // returns null when the new version is in place and the widget restart is queued; otherwise the reason it stopped
    public static string Apply(Info i) {
        if (i == null || !i.Newer || i.Url == null) return Tx.T("upd.no_newer");
        if (i.Sha == null) return Tx.T("upd.no_sha");
        string tmp = App.Exe + ".new";
        try {
            Directory.CreateDirectory(App.Dir);
            System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;
            using (var wc = new System.Net.WebClient()) { wc.Headers.Add("User-Agent", "NJ-Flush/" + Version); wc.DownloadFile(i.Url, tmp); }
            if (!ShaMatches(File.ReadAllBytes(tmp), i.Sha)) { File.Delete(tmp); return Tx.T("upd.bad_sha"); }
            string old = App.Exe + ".old";
            try { File.Delete(old); } catch (Exception) { }
            if (File.Exists(App.Exe)) File.Move(App.Exe, old);  // a running exe can be renamed, not overwritten
            File.Move(tmp, App.Exe);
        } catch (Exception e) { try { File.Delete(tmp); } catch (Exception) { } return Tx.F("upd.failed", e.Message); }
        foreach (var p in Process.GetProcessesByName("NJ-Flush"))   // the old widget holds the one-widget lock: stop it (the caller exits itself)
            if (p.Id != Process.GetCurrentProcess().Id) try { p.Kill(); } catch (Exception) { }
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & start \"\" \"" + App.Exe + "\" /widget") { CreateNoWindow = true, UseShellExecute = false });
        return null;
    }

    // the menu / settings action: check, ask, install; exits the caller's app when the swap is done
    public static void Interactive(IWin32Window owner) {
        Info i = null;
        Cursor.Current = Cursors.WaitCursor; i = Check(); Cursor.Current = Cursors.Default;
        string title = Tx.T("upd.title");
        if (i.Error != null) { Tx.Box(owner, i.Error, title, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!i.Newer) { Tx.Box(owner, Tx.F("upd.latest", Version), title, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (Tx.Box(owner, Tx.F("upd.available", i.Tag.TrimStart('v'), Version), title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        string err = Apply(i);
        if (err != null) { Tx.Box(owner, err, title, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Application.Exit();
    }
}
