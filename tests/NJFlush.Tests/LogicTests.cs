// The decisions NJ-Flush makes, tested without touching the real install folder, real processes or the network.
//   dotnet test tests/NJFlush.Tests
using System;
using System.IO;
using Xunit;

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
