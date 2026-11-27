// The decisions NJ-Flush makes, tested without touching the real install folder, real processes or the network.
//   dotnet test tests/NJFlush.Tests
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]   // Settings is static: tests that change it must not overlap

public class OrphanRule {
    static readonly DateTime Now = new DateTime(2026, 11, 24, 12, 0, 0);
    const string Mcp = @"node C:\Users\x\AppData\npm\node_modules\@playwright\mcp\cli.js";

    [Fact] public void A_parentless_old_helper_is_a_leftover() {
        Assert.True(Orphans.IsLeftover("node.exe", false, Now.AddHours(-7), Now, Mcp));
        Assert.True(Orphans.IsLeftover("CMD.EXE", false, Now.AddHours(-7), Now, "cmd /c npx-cli.js context7-mcp"));
    }
    [Fact] public void Exactly_six_hours_is_old_enough_just_under_is_not() {
        Assert.True(Orphans.IsLeftover("node.exe", false, Now.AddHours(-6), Now, Mcp));
        Assert.False(Orphans.IsLeftover("node.exe", false, Now.AddHours(-6).AddSeconds(1), Now, Mcp));
    }
    [Fact] public void A_live_parent_never() { Assert.False(Orphans.IsLeftover("node.exe", true, Now.AddDays(-3), Now, Mcp)); }
    [Fact] public void Unknown_start_time_never() { Assert.False(Orphans.IsLeftover("node.exe", false, null, Now, Mcp)); }
    [Fact] public void Another_program_never() {
        Assert.False(Orphans.IsLeftover("chrome.exe", false, Now.AddDays(-3), Now, Mcp));
        Assert.False(Orphans.IsLeftover("powershell.exe", false, Now.AddDays(-3), Now, Mcp));
        Assert.False(Orphans.IsLeftover(null, false, Now.AddDays(-3), Now, Mcp));
    }
    [Fact] public void A_node_or_cmd_that_is_not_a_known_helper_never() {
        Assert.False(Orphans.IsLeftover("node.exe", false, Now.AddDays(-3), Now, @"node C:\work\my-server.js"));
        Assert.False(Orphans.IsLeftover("node.exe", false, Now.AddDays(-3), Now, null));
    }
    [Fact] public void Every_listed_helper_is_recognised_in_any_case() {
        foreach (var h in new[] { "npx-cli.js", @"@playwright\mcp", "@playwright/mcp", "context7-mcp", @"@knip\mcp", "@knip/mcp", "flint-chart-mcp",
                                  @"business-wrap\mcp", "business-wrap/mcp", @"NJ-QA\mcp", "NJ-QA/mcp", "test-server -c", "mcp-server" })
            Assert.True(Orphans.IsLeftover("node.exe", false, Now.AddHours(-8), Now, "node " + h.ToUpperInvariant() + " --stdio"), h);
    }
}

public class SettingsRules {
    static void Defaults() {
        Settings.Animation = Settings.Sound = Settings.RestartExplorer = Settings.Hotkey = Settings.AutoUpdate = true; Settings.AutoFlush = false;
        Settings.Volume = 100; Settings.IdleOpacity = 40; Settings.AutoAt = 90; Settings.Cooldown = 60;
    }
    [Fact] public void Every_key_is_read_and_only_1_or_true_is_on() {
        Defaults();
        Settings.Parse(new[] { "animation=0", "sound=false", "RestartExplorer = TRUE", "autoflush=1", "hotkey=no", "autoupdate=0",
                               "autoat=75", "cooldown=120", "volume=0", "idleopacity=20" });
        Assert.False(Settings.Animation); Assert.False(Settings.Sound); Assert.True(Settings.RestartExplorer); Assert.True(Settings.AutoFlush);
        Assert.False(Settings.Hotkey); Assert.False(Settings.AutoUpdate);
        Assert.Equal(75, Settings.AutoAt); Assert.Equal(120, Settings.Cooldown); Assert.Equal(0, Settings.Volume); Assert.Equal(20, Settings.IdleOpacity);
    }
    [Fact] public void Numbers_outside_their_range_keep_the_current_value() {
        Defaults();
        Settings.Parse(new[] { "autoat=49", "cooldown=9", "volume=101", "idleopacity=19" });
        Assert.Equal(90, Settings.AutoAt); Assert.Equal(60, Settings.Cooldown); Assert.Equal(100, Settings.Volume); Assert.Equal(40, Settings.IdleOpacity);
        Settings.Parse(new[] { "autoat=100", "cooldown=601", "volume=-1", "idleopacity=101", "autoat=x" });
        Assert.Equal(90, Settings.AutoAt); Assert.Equal(60, Settings.Cooldown); Assert.Equal(100, Settings.Volume); Assert.Equal(40, Settings.IdleOpacity);
    }
    [Fact] public void Range_edges_are_accepted() {
        Defaults();
        Settings.Parse(new[] { "autoat=50", "cooldown=10", "idleopacity=100", "volume=100" });
        Assert.Equal(50, Settings.AutoAt); Assert.Equal(10, Settings.Cooldown); Assert.Equal(100, Settings.IdleOpacity);
        Settings.Parse(new[] { "autoat=99", "cooldown=600", "volume=0" });
        Assert.Equal(99, Settings.AutoAt); Assert.Equal(600, Settings.Cooldown); Assert.Equal(0, Settings.Volume);
    }
    [Fact] public void Lines_without_equals_and_unknown_keys_change_nothing() {
        Defaults();
        Settings.Parse(new[] { "animation", "", "colour=red", "=1" });
        Assert.True(Settings.Animation); Assert.Equal(90, Settings.AutoAt);
    }
}

public class CooldownRules {
    static readonly DateTime T0 = new DateTime(2026, 11, 24, 12, 0, 0);
    [Fact] public void Counts_down_in_whole_seconds_rounded_up() {
        Assert.Equal(60, Settings.CooldownLeft(T0.Ticks, T0, 60));
        Assert.Equal(1, Settings.CooldownLeft(T0.Ticks, T0.AddSeconds(59.5), 60));
        Assert.Equal(30, Settings.CooldownLeft(T0.Ticks, T0.AddSeconds(30), 60));
    }
    [Fact] public void Is_over_at_the_end_and_after() {
        Assert.Equal(0, Settings.CooldownLeft(T0.Ticks, T0.AddSeconds(60), 60));
        Assert.Equal(0, Settings.CooldownLeft(T0.Ticks, T0.AddHours(1), 60));
    }
    [Fact] public void A_clock_moved_back_never_locks() { Assert.Equal(0, Settings.CooldownLeft(T0.Ticks, T0.AddSeconds(-5), 60)); }
}

public class Tiers {
    [Theory]
    [InlineData(0, 0)] [InlineData(79.9, 0)] [InlineData(80, 1)] [InlineData(89.9, 1)] [InlineData(90, 2)] [InlineData(95.9, 2)] [InlineData(96, 3)] [InlineData(100, 3)]
    public void Tier_by_RAM_in_use(double pct, int tier) { Assert.Equal(tier, Overlay.TierOf(pct)); }
}

public class UpdateRules {
    static string Fixture() { return File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", "release-v1.2.2.json")); }
    static string Sha(byte[] b) { using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }

    [Fact] public void The_real_GitHub_reply_gives_tag_url_and_checksum() {
        var i = Updater.Parse(Fixture(), "1.2.1");
        Assert.Null(i.Error);
        Assert.Equal("v1.2.2", i.Tag);
        Assert.EndsWith("/v1.2.2/NJ-Flush.exe", i.Url);
        Assert.Equal("ed3f75f9e2ccc7b4f4ba3cd77c92db8c99bf55129203f644ffcf3df772719cab", i.Sha);  // the published v1.2.2 exe
        Assert.True(i.Newer);
    }
    [Fact] public void The_same_or_an_older_release_is_not_newer() {
        Assert.False(Updater.Parse(Fixture(), "1.2.2").Newer);
        Assert.False(Updater.Parse(Fixture(), "1.10.0").Newer);  // compared as versions, not as text
    }
    [Fact] public void A_release_without_the_exe_is_refused() {
        var i = Updater.Parse("{\"tag_name\":\"v9.9.9\",\"assets\":[{\"name\":\"other.zip\",\"browser_download_url\":\"https://x/other.zip\"}]}", "1.0.0");
        Assert.NotNull(i.Error); Assert.False(i.Newer); Assert.Null(i.Url);
    }
    [Fact] public void No_digest_means_no_checksum_and_Apply_refuses_before_downloading() {
        var i = Updater.Parse("{\"tag_name\":\"v9.9.9\",\"assets\":[{\"name\":\"NJ-Flush.exe\",\"browser_download_url\":\"https://x/NJ-Flush.exe\"}]}", "1.0.0");
        Assert.True(i.Newer); Assert.Null(i.Sha);
        Assert.Contains("checksum", Updater.Apply(i));
    }
    [Fact] public void Apply_refuses_when_nothing_is_newer() {
        Assert.Equal("No newer version to install.", Updater.Apply(null));
        Assert.Equal("No newer version to install.", Updater.Apply(new Updater.Info { Newer = false, Url = "https://x", Sha = new string('a', 64) }));
        Assert.Equal("No newer version to install.", Updater.Apply(new Updater.Info { Newer = true, Url = null, Sha = new string('a', 64) }));
    }
    [Fact] public void A_download_installs_only_when_it_is_an_exe_with_the_published_checksum() {
        byte[] exe = { (byte)'M', (byte)'Z', 1, 2, 3 };
        Assert.True(Updater.ShaMatches(exe, Sha(exe)));
        Assert.False(Updater.ShaMatches(exe, Sha(exe).ToUpperInvariant()));  // GitHub's digest is lower-case and Parse lower-cases it
        Assert.False(Updater.ShaMatches(exe, new string('0', 64)));
        byte[] notExe = { 1, 2, 3 };
        Assert.False(Updater.ShaMatches(notExe, Sha(notExe)));                // the right checksum, but not a Windows program
        Assert.False(Updater.ShaMatches(new byte[] { (byte)'M' }, Sha(new byte[] { (byte)'M' })));
        Assert.False(Updater.ShaMatches(new byte[] { (byte)'M', 0 }, Sha(new byte[] { (byte)'M', 0 })));
        Assert.False(Updater.ShaMatches(exe, null));
        Assert.False(Updater.ShaMatches(null, Sha(exe)));
    }
}

// 1.3 (2026-11-27): the rules behind the CPU, GPU and gentle RAM flushes, the request file, shortcuts and the guard settings
public class BusyRule {
    [Fact] public void Busy_means_every_slice_of_the_minute_at_ninety_percent_of_a_core() {
        Assert.True(Busy.Sustained(Enumerable.Repeat(0.9, 20), 20));
        Assert.True(Busy.Sustained(Enumerable.Repeat(3.0, 20), 20));
        Assert.False(Busy.Sustained(Enumerable.Repeat(0.89, 20), 20));
        Assert.False(Busy.Sustained(Enumerable.Repeat(1.0, 19), 20));                       // under a minute of history
        Assert.False(Busy.Sustained(Enumerable.Repeat(1.0, 19).Concat(new[] { 0.1 }), 20)); // one quiet slice clears it
    }
    [Fact] public void Never_calms_the_app_in_front_its_tree_its_namesakes_guarded_apps_services_or_the_system() {
        var none = new int[0]; var noGuard = new string[0];
        Assert.True(Busy.MayCalm("node.exe", 1, 1, 500, 99, "Code", none, noGuard));
        Assert.False(Busy.MayCalm("node.exe", 0, 1, 500, 99, "Code", none, noGuard));          // another session: a service
        Assert.False(Busy.MayCalm("Code.exe", 1, 1, 500, 99, "Code", none, noGuard));          // same name as the app in front
        Assert.False(Busy.MayCalm("node.exe", 1, 1, 500, 99, "Code", new[] { 500 }, noGuard)); // under the app in front
        Assert.False(Busy.MayCalm("opera.exe", 1, 1, 500, 99, null, none, new[] { "opera" }));
        Assert.False(Busy.MayCalm("dwm.exe", 1, 1, 500, 99, null, none, noGuard));
        Assert.False(Busy.MayCalm("NJ-Flush.exe", 1, 1, 500, 99, null, none, noGuard));
        Assert.False(Busy.MayCalm("x.exe", 1, 1, 99, 99, null, none, noGuard));                // itself
    }
    [Fact] public void The_tree_follows_children_and_grandchildren_only() {
        var rows = new List<ProcRow> { new ProcRow { Pid = 10, Parent = 1 }, new ProcRow { Pid = 11, Parent = 10 }, new ProcRow { Pid = 12, Parent = 11 }, new ProcRow { Pid = 20, Parent = 1 } };
        Assert.Equal(new[] { 10, 11, 12 }, Busy.Tree(rows, 10).OrderBy(x => x).ToArray());
        Assert.Empty(Busy.Tree(rows, 0));
    }
    [Fact] public void Busy_file_round_trips_and_skips_bad_lines() {
        var list = new List<BusyApp> { new BusyApp { Pid = 5, Created = 123, Name = "node.exe", Cores = 1.25 } };
        var back = Busy.Parse(Busy.Format(list).Split('\n').Concat(new[] { "garbage", "1|x|n|1" }));
        Assert.Single(back); Assert.Equal(5, back[0].Pid); Assert.Equal(123, back[0].Created); Assert.Equal("node.exe", back[0].Name); Assert.Equal(1.25, back[0].Cores);
    }
}

public class GentleRam {
    [Fact] public void Trims_only_idle_background_apps_of_this_session_with_64_MB_or_more() {
        long mb = 1L << 20;
        Assert.True(Flush.MayTrim("chrome.exe", 1, 1, 0.0, 200 * mb, false));
        Assert.False(Flush.MayTrim("chrome.exe", 1, 1, 0.05, 200 * mb, false));   // working
        Assert.False(Flush.MayTrim("chrome.exe", 1, 1, 0.0, 63 * mb, false));     // too small to matter
        Assert.False(Flush.MayTrim("chrome.exe", 1, 1, 0.0, 200 * mb, true));     // in front
        Assert.False(Flush.MayTrim("chrome.exe", 0, 1, 0.0, 200 * mb, false));    // a service
        foreach (var n in new[] { "explorer.exe", "dwm.exe", "csrss.exe", "audiodg.exe", "NJ-Flush.exe" }) Assert.False(Flush.MayTrim(n, 1, 1, 0, 900 * mb, false), n);
    }
    [Fact] public void A_RAM_flush_below_the_threshold_does_nothing() {
        Assert.False(Flush.RamNeededAt(47, 75)); Assert.True(Flush.RamNeededAt(75, 75)); Assert.True(Flush.RamNeededAt(96, 75));
    }
}

public class GpuRule {
    static FlushRequest Q(string k, bool auto = false) { return new FlushRequest { Kind = k, Auto = auto }; }
    [Fact] public void Reset_GPU_by_name_always_runs_when_there_is_a_card() {
        Assert.Null(Flush.GpuSkipFor(Q("gpu"), false, true, 0, 5, true));
        Assert.NotNull(Flush.GpuSkipFor(Q("gpu"), true, false, 3, 99, false));
    }
    [Fact] public void Flush_all_restarts_only_on_heavy_clogged_or_full_VRAM() {
        Assert.Null(Flush.GpuSkipFor(Q("all"), true, true, 2, 10, false));
        Assert.Null(Flush.GpuSkipFor(Q("all"), true, true, 0, 85, false));
        Assert.Equal("not needed", Flush.GpuSkipFor(Q("all"), true, true, 1, 84.9, false));
    }
    [Fact] public void Flush_all_never_restarts_on_auto_under_full_screen_or_when_off() {
        Assert.Equal("never on an auto-flush", Flush.GpuSkipFor(Q("all", true), true, true, 3, 99, false));
        Assert.Equal("a full-screen app is in front", Flush.GpuSkipFor(Q("all"), true, true, 3, 99, true));
        Assert.Equal("off in Settings", Flush.GpuSkipFor(Q("all"), false, true, 3, 99, false));
        Assert.Equal("not asked", Flush.GpuSkipFor(Q("ram"), true, true, 3, 99, false));
    }
    [Fact] public void PDH_instance_names_give_pid_adapter_and_engine() {
        const string i = "pid_19344_luid_0x00000000_0x00012f7e_phys_0_eng_0_engtype_3D";
        Assert.Equal(19344, Gpu.PidOf(i)); Assert.Equal(-1, Gpu.PidOf("luid_0x00000000_0x00012f7e_phys_0")); Assert.Equal(-1, Gpu.PidOf(null));
        Assert.True(Gpu.OnAdapter(i, Gpu.LuidName(0, 0x12f7e))); Assert.False(Gpu.OnAdapter(i, Gpu.LuidName(0, 0x158ea)));
        Assert.Equal("3D", Gpu.EngType(i)); Assert.Equal("", Gpu.EngType("pid_1_luid_x"));
    }
    [Fact] public void The_card_with_the_most_dedicated_memory_wins_and_virtual_ones_never() {
        var list = new List<Gpu.Adapter> { new Gpu.Adapter { Name = "Parsec", TotalGB = 0 }, new Gpu.Adapter { Name = "iGPU", TotalGB = 0.5 }, new Gpu.Adapter { Name = "RTX", TotalGB = 6 } };
        Assert.Equal("RTX", Gpu.Pick(list).Name);
        Assert.Null(Gpu.Pick(new List<Gpu.Adapter> { new Gpu.Adapter { Name = "Parsec", TotalGB = 0 } }));
    }
}

public class Requests {
    [Fact] public void Kind_auto_and_deep_are_read_anything_else_is_All() {
        var r = Flush.ParseRequest("cpu auto"); Assert.Equal("cpu", r.Kind); Assert.True(r.Auto); Assert.False(r.Deep);
        r = Flush.ParseRequest("RAM deep\r\n"); Assert.Equal("ram", r.Kind); Assert.True(r.Deep);
        Assert.True(Flush.ParseRequest("ram force").Force); Assert.False(Flush.ParseRequest("ram").Force);
        Assert.Equal("all", Flush.ParseRequest("").Kind); Assert.Equal("all", Flush.ParseRequest(null).Kind); Assert.Equal("all", Flush.ParseRequest("rm -rf").Kind);
    }
    [Fact] public void All_waits_for_all_three_clocks() {
        Assert.Equal(new[] { "ram", "cpu", "gpu" }, Settings.Parts("all")); Assert.Equal(new[] { "gpu" }, Settings.Parts("gpu"));
    }
    [Fact] public void CPU_percent_from_system_times() {
        Assert.Equal(25, CpuMeter.Pct(100, 75)); Assert.Equal(0, CpuMeter.Pct(0, 0)); Assert.Equal(100, CpuMeter.Pct(100, -5));
    }
}

public class ShortcutsAndGuard {
    [Fact] public void Shortcuts_round_trip_and_need_a_modifier() {
        uint m, k;
        Assert.True(Hotkeys.Parse("ctrl + alt + f", out m, out k)); Assert.Equal("Ctrl+Alt+F", Hotkeys.Format(m, k));
        Assert.Equal("Ctrl+Shift+5", Hotkeys.Normal("Shift+Ctrl+5"));
        Assert.Equal("Alt+F12", Hotkeys.Normal("alt+f12"));
        Assert.Null(Hotkeys.Normal("F"));            // no modifier: it would steal a key from every app
        Assert.Null(Hotkeys.Normal("Ctrl+Alt"));     // no key
        Assert.Null(Hotkeys.Normal("Ctrl+F+G"));     // two keys
        Assert.Null(Hotkeys.Normal("Ctrl+Banana"));
        Assert.Equal("", Hotkeys.Normal(""));        // none is allowed
    }
    [Fact] public void Settings_read_shortcuts_guard_list_and_the_new_switches() {
        Settings.Defaults();
        Settings.Parse(new[] { "key.cpu=Alt+Shift+C", "key.gpu=", "key.all=nonsense", "key.nope=Ctrl+X", "guardapps=Opera.exe, chrome ,,opera",
                               "gpureset=0", "calmcpu=0", "widgetbars=0", "guard=0", "guardbattery=0", "minram=80", "minram=20" });
        Assert.Equal("Alt+Shift+C", Settings.Keys_[2]); Assert.Equal("", Settings.Keys_[3]); Assert.Equal("Ctrl+Alt+F", Settings.Keys_[0]);
        Assert.Equal(new[] { "opera", "chrome" }, Settings.GuardApps.ToArray());
        Assert.False(Settings.GpuReset); Assert.False(Settings.CalmCpu); Assert.False(Settings.WidgetBars); Assert.False(Settings.Guard); Assert.False(Settings.GuardBattery);
        Assert.Equal(80, Settings.MinRam);
        Settings.Defaults();
        Assert.False(Settings.RestartExplorer);   // off by default since 1.3 (freeze report)
        Assert.Equal(75, Settings.MinRam); Assert.Contains("opera", Settings.GuardApps);
    }
    [Fact] public void Efficiency_mode_is_on_only_when_controlled_and_set() {
        Assert.True(Eco.On(1, 1)); Assert.True(Eco.On(5, 5)); Assert.False(Eco.On(1, 0)); Assert.False(Eco.On(0, 1)); Assert.False(Eco.On(4, 0));
    }
}

// 1.3 round 2: which variant plays, preview names, the widget's spot
public class Variants {
    [Theory]
    [InlineData(10, 0, 0)] [InlineData(39.9, 0, 0)] [InlineData(40, 0, 1)] [InlineData(70, 0, 2)] [InlineData(90, 0, 3)]
    [InlineData(10, 1, 1)] [InlineData(10, 2, 2)] [InlineData(10, 5, 3)] [InlineData(95, 1, 3)]
    public void CPU_takes_the_higher_of_load_and_busy_apps(double pct, int busy, int tier) { Assert.Equal(tier, Scenes.CpuTier(pct, busy)); }
    [Theory]
    [InlineData(0, 0)] [InlineData(59.9, 0)] [InlineData(60, 1)] [InlineData(85, 2)] [InlineData(95, 3)] [InlineData(100, 3)]
    public void GPU_by_VRAM(double pct, int tier) { Assert.Equal(tier, Scenes.GpuTier(pct)); }
    [Fact] public void All_takes_the_worst_part() { Assert.Equal(3, Scenes.AllTier(0, 3, 1)); Assert.Equal(2, Scenes.AllTier(2, 0, 0)); Assert.Equal(0, Scenes.AllTier(0, 0, 0)); }
    [Fact] public void Preview_names_and_numbers() {
        string k; int v;
        Assert.True(Scenes.ParsePreview("cpu-meltdown", out k, out v)); Assert.Equal("cpu", k); Assert.Equal(3, v);
        Assert.True(Scenes.ParsePreview("GPU-2", out k, out v)); Assert.Equal("gpu", k); Assert.Equal(2, v);
        Assert.True(Scenes.ParsePreview("all", out k, out v)); Assert.Equal("all", k); Assert.Equal(1, v);
        Assert.True(Scenes.ParsePreview("all-9", out k, out v)); Assert.Equal(1, v);
        Assert.False(Scenes.ParsePreview("clogged", out k, out v)); Assert.False(Scenes.ParsePreview(null, out k, out v));
    }
}

public class WidgetSpot {
    static readonly System.Drawing.Rectangle Wa = new System.Drawing.Rectangle(0, 0, 1920, 1040);
    static readonly System.Drawing.Size Sz = new System.Drawing.Size(212, 62);
    [Fact] public void Stays_inside_the_working_area() {
        Assert.Equal(new System.Drawing.Point(0, 0), Widget.Snap(new System.Drawing.Point(-50, -50), Sz, Wa, 0));
        Assert.Equal(new System.Drawing.Point(1920 - 212, 1040 - 62), Widget.Snap(new System.Drawing.Point(5000, 5000), Sz, Wa, 0));
    }
    [Fact] public void Snaps_to_an_edge_within_the_distance_only() {
        Assert.Equal(new System.Drawing.Point(0, 500), Widget.Snap(new System.Drawing.Point(11, 500), Sz, Wa, 12));
        Assert.Equal(new System.Drawing.Point(12, 500), Widget.Snap(new System.Drawing.Point(12, 500), Sz, Wa, 12));
        Assert.Equal(new System.Drawing.Point(1920 - 212, 1040 - 62), Widget.Snap(new System.Drawing.Point(1920 - 212 - 5, 1040 - 62 - 5), Sz, Wa, 12));
    }
    [Fact] public void The_spot_is_read_and_reset() {
        Settings.Defaults();
        Settings.Parse(new[] { "widgetx=300", "widgety=40" }); Assert.Equal(300, Settings.WidgetX); Assert.Equal(40, Settings.WidgetY);
        Settings.Parse(new[] { "widgetx=-5" }); Assert.Equal(300, Settings.WidgetX);
        Settings.Defaults(); Assert.Equal(-1, Settings.WidgetX); Assert.Equal(-1, Settings.WidgetY);
    }
}

// Arabic (Strings.cs): the table, the language setting and its resolution
public class Strings {
    static System.Collections.Generic.List<string> Slots(string s) {
        return System.Text.RegularExpressions.Regex.Matches(s, @"\{(\d+)(?::[^}]*)?\}").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
    }
    static string SrcDir([System.Runtime.CompilerServices.CallerFilePath] string p = "") { return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(p), "..", "..")); }

    [Fact] public void Every_key_has_an_English_and_an_Arabic_text() {
        foreach (var r in Tx.All) { Assert.False(string.IsNullOrWhiteSpace(r[1]), r[0] + " en"); Assert.False(string.IsNullOrWhiteSpace(r[2]), r[0] + " ar"); }
        foreach (var r in Tx.AllPlural) for (int i = 1; i < r.Length; i++) Assert.False(string.IsNullOrWhiteSpace(r[i]), r[0] + " " + i);
        Assert.Equal(Tx.All.Count(), Tx.All.Select(r => r[0]).Distinct().Count());   // no key twice
    }
    [Fact] public void Placeholders_match_between_the_two_languages() {
        foreach (var r in Tx.All) Assert.Equal(Slots(r[1]), Slots(r[2]));
        foreach (var r in Tx.AllPlural) { Assert.Equal(new[] { "0" }, Slots(r[1]).ToArray()); for (int i = 2; i < r.Length; i++) if (i == 2 || i >= 5) Assert.Equal(new[] { "0" }, Slots(r[i]).ToArray()); }
    }
    [Fact] public void Arabic_text_has_no_tatweel_no_Persian_letters_no_hidden_marks() {
        foreach (var r in Tx.All) foreach (char c in r[2]) {
            Assert.NotEqual('\u0640', c); Assert.NotEqual('\u06A9', c); Assert.NotEqual('\u06CC', c);
            Assert.False(c >= '\u200B' && c <= '\u200F' || c >= '\u202A' && c <= '\u202E' || c >= '\u2066' && c <= '\u2069', r[0]);
        }
    }
    [Fact] public void Every_key_used_in_the_source_is_in_the_table() {
        var keys = new System.Collections.Generic.HashSet<string>(Tx.All.Select(r => r[0]));
        var used = new System.Collections.Generic.List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(SrcDir(), "src"), "*.cs", SearchOption.AllDirectories)) {
            if (Path.GetFileName(f) == "Strings.cs") continue;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"Tx\.(?:T|F)\(""([a-z0-9_.]+)""[,)]")) used.Add(m.Groups[1].Value);
        }
        Assert.True(used.Count > 100);
        foreach (string k in used) Assert.True(keys.Contains(k), "missing key " + k);
        for (int i = 0; i < 4; i++) { Assert.True(keys.Contains("tier." + i)); Assert.True(keys.Contains("pill." + i)); foreach (var kd in new[] { "cpu", "gpu", "all" }) Assert.True(keys.Contains("scene." + kd + "." + i)); }
        foreach (var a in Settings.Actions) Assert.True(keys.Contains("act." + a));
    }
    [Fact] public void The_language_setting_parses_and_a_bad_value_keeps_the_current_one() {
        Settings.Defaults(); Assert.Equal("auto", Settings.Language);
        Settings.Parse(new[] { "language=ar" }); Assert.Equal("ar", Settings.Language);
        Settings.Parse(new[] { "language = EN " }); Assert.Equal("en", Settings.Language);
        Settings.Parse(new[] { "language=fr" }); Assert.Equal("en", Settings.Language);
        Settings.Parse(new[] { "language=" }); Assert.Equal("en", Settings.Language);
        Settings.Parse(new[] { "language=Auto" }); Assert.Equal("auto", Settings.Language);
        Settings.Defaults();
    }
    [Fact] public void Auto_follows_Windows_and_a_named_language_wins() {
        Assert.True(Tx.ChoosesArabic("auto", true)); Assert.False(Tx.ChoosesArabic("auto", false));
        Assert.True(Tx.ChoosesArabic("ar", false)); Assert.False(Tx.ChoosesArabic("en", true));
        Assert.Null(Tx.ParseLang("xx")); Assert.Equal("ar", Tx.ParseLang(" AR "));
    }
    [Fact] public void The_environment_variable_wins_over_the_setting() {
        string old = Environment.GetEnvironmentVariable(Tx.EnvVar);
        try {
            Settings.Defaults(); Settings.Language = "en";
            Environment.SetEnvironmentVariable(Tx.EnvVar, "ar"); Tx.Resolve(); Assert.True(Tx.Ar);
            Environment.SetEnvironmentVariable(Tx.EnvVar, "en"); Settings.Language = "ar"; Tx.Resolve(); Assert.False(Tx.Ar);
            Environment.SetEnvironmentVariable(Tx.EnvVar, null); Tx.Resolve(); Assert.True(Tx.Ar);
        } finally { Environment.SetEnvironmentVariable(Tx.EnvVar, old); Settings.Defaults(); Tx.Resolve(); }
    }
    [Fact] public void Arabic_plural_forms_and_Latin_runs() {
        try {
            Tx.Ar = false; Assert.Equal("1 app", Tx.Plural("app", 1)); Assert.Equal("3 apps", Tx.Plural("app", 3)); Assert.Equal("2.61 GB", Tx.Bidi("2.61 GB"));
            Tx.Ar = true;
            Assert.Equal("تطبيق واحد", Tx.Plural("app", 1)); Assert.Equal("تطبيقان", Tx.Plural("app", 2));
            Assert.Contains("3 تطبيقات", Tx.Plural("app", 3)); Assert.Contains("11 تطبيقًا", Tx.Plural("app", 11)); Assert.Contains("12 تطبيقًا", Tx.Plural("app", 112));
            Assert.Equal("\u200E2.61 GB\u200E", Tx.Bidi("2.61 GB"));
            Assert.Equal("\u200E45%\u200E", Tx.Pct(45));
            Assert.Equal("من \u200ENJ-Flush\u200E.", Tx.Bidi("من NJ-Flush."));   // the closing full stop stays outside the Latin run
            Assert.Equal(Tx.Bidi("2.61 GB"), Tx.Bidi(Tx.Bidi("2.61 GB")));
        } finally { Tx.Ar = false; }
    }
}
