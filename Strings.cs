// NJ-Flush strings, English and Arabic (Nader 2026-11-27: "add an Arabic version, the app itself, fully, right-to-left").
// One table: key -> (English, Arabic). Every word the user can read goes through Tx.T(key) / Tx.F(key, args); nothing else holds copy.
//
// Language: settings.ini `language=auto|en|ar` (auto = Arabic when the Windows display language is Arabic, else English), chosen on the
//   Settings page ("Language / اللغة"). The environment variable NJFLUSH_LANG=ar|en|auto wins over settings.ini; it exists only so
//   off-screen renders (/render, /render-ui) can show a language without touching the owner's live settings file.
// Numbers: always Western digits (0-9), formatted with the invariant culture, with Arabic text around them. Units stay Latin (GB, MB, %).
// Counts: Arabic needs one/two/few/many forms, so a counted noun goes through Tx.Plural(noun, n); elsewhere copy uses "label: N".
// Glossary (the Arabic side; one word per thing, used everywhere):
//   RAM = ذاكرة RAM (short labels: RAM)          CPU = المعالج (short labels: CPU)       GPU = بطاقة الرسوميات (short labels: GPU)
//   VRAM = ذاكرة الرسوميات (short labels: VRAM)  flush = تفريغ                          deep flush = تفريغ عميق
//   calm (CPU) = تهدئة                           restart (GPU) = إعادة تشغيل            efficiency mode = وضع الكفاءة
//   efficiency guard = حارس الكفاءة              kept out = مستبعد                      app = تطبيق      process = عملية
//   widget = الودجت                              panel = اللوحة                         shortcut = اختصار    cache = التخزين المؤقت
// Product name NJ-Flush and key caps (Ctrl+Alt+F) stay Latin.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using W = System.Windows;
using WF = System.Windows.Forms;

static class Tx {
    public const string EnvVar = "NJFLUSH_LANG";
    public static bool Ar;   // the language in force, set by Resolve()

    // "auto" | "en" | "ar", or null when the text is none of them
    public static string ParseLang(string v) {
        v = (v ?? "").Trim().ToLowerInvariant();
        return v == "auto" || v == "en" || v == "ar" ? v : null;
    }
    public static bool ChoosesArabic(string lang, bool windowsIsArabic) { return lang == "ar" || (lang == "auto" && windowsIsArabic); }
    // the environment variable first, then settings.ini; call after Settings.Load() and whenever the setting changes
    public static void Resolve() {
        bool ui = false;
        try { ui = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "ar"; } catch (Exception) { }
        Ar = ChoosesArabic(ParseLang(Environment.GetEnvironmentVariable(EnvVar)) ?? Settings.Language, ui);
    }

    static Dictionary<string, string[]> map;
    static Dictionary<string, string[]> Map { get { return map ?? (map = Rows.ToDictionary(r => r[0], r => r)); } }
    public static IEnumerable<string[]> All { get { return Rows; } }

    public static string T(string key) {
        string[] r;
        if (!Map.TryGetValue(key, out r)) return key;
        return Ar ? (r[2].IndexOf('{') < 0 ? Bidi(r[2]) : r[2]) : r[1];
    }
    public static string F(string key, params object[] a) { return Bidi(string.Format(CultureInfo.InvariantCulture, Map.ContainsKey(key) ? (Ar ? Map[key][2] : Map[key][1]) : key, a)); }
    public static string Pct(double v) { return Bidi(string.Format(CultureInfo.InvariantCulture, "{0:0}%", v)); }
    public static string Join(IEnumerable<string> parts) { return string.Join(T("sep"), parts); }
    public static string Upper(string s) { return Ar ? s : s.ToUpperInvariant(); }   // pill captions are upper case in English only

    // a counted noun: "1 app" / "3 apps"; Arabic one, two, 3-10, 11+
    public static string Plural(string noun, int n) {
        string[] r;
        if (!PluralMap.TryGetValue(noun, out r)) return n + " " + noun;
        string f;
        if (!Ar) f = n == 1 ? r[1] : r[2];
        else { int m = n % 100; f = n == 1 ? r[3] : n == 2 ? r[4] : (m >= 3 && m <= 10) || n == 0 ? r[5] : r[6]; }
        return Bidi(string.Format(CultureInfo.InvariantCulture, f, n));
    }
    // Arabic text keeps every Latin word, number and unit as one left-to-right run (2.61 GB, 45%, Ctrl+Alt+F, 2026-11-27 22:34), whatever sits next to it:
    // a bare right-to-left paragraph would show "GB 2.61" and "%45". LRM on both sides (WPF ignores the embedding and isolate marks, but every engine honours a strong left-to-right mark; the mark on both sides is what keeps a trailing % or unit with its number). Idempotent.
    static readonly System.Text.RegularExpressions.Regex LatinRun = new System.Text.RegularExpressions.Regex(@"[A-Za-z0-9](?:(?: ?[A-Za-z0-9.,:/+%\-])*[A-Za-z0-9%])?");
    public static string Bidi(string s) {
        if (!Ar || string.IsNullOrEmpty(s) || s.IndexOf('\u200E') >= 0) return s;
        return LatinRun.Replace(s, "\u200E$0\u200E");
    }
    static Dictionary<string, string[]> pmap;
    static Dictionary<string, string[]> PluralMap { get { return pmap ?? (pmap = PluralRows.ToDictionary(r => r[0], r => r)); } }
    public static IEnumerable<string[]> AllPlural { get { return PluralRows; } }

    // the five shortcut actions (Settings.Actions order)
    public static string Action(int i) { return T("act." + Settings.Actions[i]); }
    // the four RAM tiers, 0 light .. 3 clogged
    public static string TierName(int i) { return T("tier." + i); }
    public static string TierPill(int i) { return T("pill." + i); }
    // the animation variant names, kind = cpu | gpu | all, v = 0..3
    public static string SceneName(string kind, int v) { return T("scene." + kind + "." + v); }
    public static string[] TierNames() { return new[] { TierName(0), TierName(1), TierName(2), TierName(3) }; }
    public static string[] SceneNames(string kind) { return new[] { SceneName(kind, 0), SceneName(kind, 1), SceneName(kind, 2), SceneName(kind, 3) }; }

    public static W.FlowDirection Flow { get { return Ar ? W.FlowDirection.RightToLeft : W.FlowDirection.LeftToRight; } }
    public static bool HasArabic(string s) { return s != null && s.Any(c => c >= 0x0600 && c <= 0x06FF); }

    // message boxes read from the right in Arabic
    public static WF.DialogResult Box(WF.IWin32Window owner, string text, string caption, WF.MessageBoxButtons b, WF.MessageBoxIcon icon) {
        var opt = Ar ? (WF.MessageBoxOptions.RtlReading | WF.MessageBoxOptions.RightAlign) : (WF.MessageBoxOptions)0;
        return WF.MessageBox.Show(owner, text, caption, b, icon, WF.MessageBoxDefaultButton.Button1, opt);
    }
    public static W.MessageBoxResult WBox(W.Window owner, string text, string caption, W.MessageBoxButton b, W.MessageBoxImage icon) {
        return W.MessageBox.Show(owner, text, caption, b, icon, W.MessageBoxResult.None, Ar ? (W.MessageBoxOptions.RtlReading | W.MessageBoxOptions.RightAlign) : W.MessageBoxOptions.None);
    }

    static string[] R(string key, string en, string ar) { return new[] { key, en, ar }; }
    // noun, English one, English other, Arabic one / two / 3-10 / 11+ ({0} is the number)
    static string[] P(string noun, string en1, string enN, string ar1, string ar2, string arFew, string arMany) { return new[] { noun, en1, enN, ar1, ar2, arFew, arMany }; }

    static readonly string[][] PluralRows = {
        P("app", "{0} app", "{0} apps", "تطبيق واحد", "تطبيقان", "{0} تطبيقات", "{0} تطبيقًا"),
        P("process", "{0} process", "{0} processes", "عملية واحدة", "عمليتان", "{0} عمليات", "{0} عملية"),
        P("busyapp", "{0} busy app", "{0} busy apps", "تطبيق مشغول واحد", "تطبيقان مشغولان", "{0} تطبيقات مشغولة", "{0} تطبيقًا مشغولًا"),
    };

    static readonly string[][] Rows = {
        // ---- language ----
        R("lang.label", "Language / اللغة", "اللغة / Language"),
        R("lang.auto", "Auto", "تلقائي"),
        R("lang.en", "English", "English"),
        R("lang.ar", "العربية", "العربية"),
        R("lang.note", "Auto follows the Windows display language.", "الخيار التلقائي يتبع لغة العرض في Windows."),
        R("sep", ", ", "، "),

        // ---- first run and install ----
        R("app.task_fail", "NJ-Flush could not register its task.", "تعذّر تسجيل مهمة NJ-Flush المجدولة."),
        R("app.admin_once", "NJ-Flush needs admin approval once, the first time, to install.", "يحتاج NJ-Flush إلى موافقة المسؤول مرة واحدة عند أول تشغيل لإتمام التثبيت."),

        // ---- panel: tiles, bar, titles ----
        R("title.ram", "RAM", "ذاكرة RAM"),
        R("title.cpu", "CPU", "المعالج"),
        R("title.gpu", "GPU", "بطاقة الرسوميات"),
        R("title.guard", "Efficiency guard", "حارس الكفاءة"),
        R("title.settings", "Settings", "الإعدادات"),
        R("tile.ram_sub", "{0:0.0} of {1:0.0} GB", "{0:0.0} من {1:0.0} GB"),
        R("threads", "{0} threads", "الخيوط: {0}"),
        R("tile.gpu_sub", "VRAM {0:0.0} of {1:0.0} GB", "VRAM {0:0.0} من {1:0.0} GB"),
        R("tile.gpu_wait", "looking for the graphics card...", "جارٍ البحث عن بطاقة الرسوميات..."),
        R("tile.guard_paused", "paused", "متوقف مؤقتًا"),
        R("tile.guard_on", "on", "يعمل"),
        R("tile.guard_off", "off", "متوقف"),
        R("tile.guard_apps", "{0} apps kept out", "التطبيقات المستبعدة: {0}"),
        R("tile.guard_slow", "Windows may slow your apps", "قد يُبطئ Windows تطبيقاتك"),
        R("tile.settings_sub", "Animations, sound, shortcuts", "الرسوم والصوت والاختصارات"),
        R("last.line", "Last flush: {0}", "آخر تفريغ: {0}"),
        R("last.none", "no flush yet", "لا يوجد تفريغ بعد"),
        R("demo.last", "RAM flushed, {0} GB in use before, {1} GB after", "تفريغ ذاكرة RAM: {0} GB قيد الاستخدام قبل، و{1} GB بعده"),
        R("ui.flush_all", "Flush all", "تفريغ الكل"),
        R("ui.refreshed", "Refreshed.", "تم التحديث."),

        // ---- panel: answers in the bar ----
        R("do.all", "Flushing everything...", "جارٍ تفريغ كل شيء..."),
        R("do.deep", "Deep flush of RAM...", "جارٍ التفريغ العميق لذاكرة RAM..."),
        R("do.ram", "Flushing RAM...", "جارٍ تفريغ ذاكرة RAM..."),
        R("do.cpu", "Calming the CPU...", "جارٍ تهدئة المعالج..."),
        R("do.gpu", "Restarting the GPU...", "جارٍ إعادة تشغيل بطاقة الرسوميات..."),
        R("refusal.cooldown", "Just flushed. Again in {0} s.", "تم التفريغ للتو. حاول مجددًا بعد {0} ث."),
        R("refusal.ram_fine", "RAM is fine ({0:0}% in use). A flush does nothing below {1}%.", "ذاكرة RAM بحالة جيدة ({0:0}% قيد الاستخدام). لا يفيد التفريغ تحت {1}%."),

        // ---- lists ----
        R("proc.system", "Windows system process", "عملية نظام في Windows"),
        R("proc.pid", "process {0}", "عملية رقم {0}"),
        R("proc.end", "End", "إنهاء"),
        R("proc.end_q", "End {0} (process {1})?\n\nAnything unsaved in it is lost.", "هل تريد إنهاء {0} (العملية {1})؟\n\nسيضيع كل ما لم يُحفظ فيه."),
        R("proc.ended", "{0} ended.", "تم إنهاء {0}."),
        R("proc.end_fail", "{0} could not be ended (it may need admin rights).", "تعذّر إنهاء {0}؛ قد يحتاج إلى صلاحيات المسؤول."),
        R("list.measuring", "Measuring... the list fills in a few seconds.", "جارٍ القياس... تمتلئ القائمة خلال ثوانٍ."),

        // ---- RAM page ----
        R("ram.flush", "Flush RAM", "تفريغ ذاكرة RAM"),
        R("ram.deep", "Deep flush", "تفريغ عميق"),
        R("ram.deep_note", "Deep flush also empties the file cache and writes changed pages to disk, as 1.2 did. Slower for minutes after; use it rarely.",
                           "يفرّغ التفريغ العميق ذاكرة التخزين المؤقت للملفات أيضًا، ويكتب الصفحات المعدّلة على القرص كما كان يفعل الإصدار 1.2. يبطئ الجهاز لدقائق بعده، فاستخدمه نادرًا."),
        R("ram.graph", "RAM in use, last 60 seconds", "ذاكرة RAM المستخدمة، آخر 60 ثانية"),
        R("ram.top", "Using the most memory", "الأكثر استخدامًا للذاكرة"),
        R("ram.sub_use", "{0:0.0} of {1:0.0} GB in use", "{0:0.0} من {1:0.0} GB قيد الاستخدام"),
        R("ram.sub_fine", "fine: a flush does nothing under {0}%", "الوضع جيد: لا يفيد التفريغ تحت {0}%"),
        R("ram.sub_play", "a flush would play: {0}", "الرسم المتحرك عند التفريغ: {0}"),
        R("tier.0", "Light", "خفيف"),
        R("tier.1", "Medium", "متوسط"),
        R("tier.2", "Heavy", "ثقيل"),
        R("tier.3", "Clogged", "مسدود"),

        // ---- CPU page ----
        R("cpu.calm", "Calm CPU", "تهدئة المعالج"),
        R("cpu.calm_note", "Efficiency mode for apps that kept a core busy for a minute. Never the app you are using.",
                           "يضع في وضع الكفاءة التطبيقات التي شغلت نواة كاملة لمدة دقيقة. ولا يمسّ التطبيق الذي تستخدمه."),
        R("cpu.busy_none", "No app has kept a core busy for the last minute.", "لا يوجد تطبيق شغل نواة كاملة خلال الدقيقة الأخيرة."),
        R("cpu.busy_list", "Busy for a minute: {0}", "شغلت نواة كاملة لدقيقة: {0}"),
        R("cpu.graph", "CPU load, last 60 seconds", "حمل المعالج، آخر 60 ثانية"),
        R("cpu.top", "Using the most CPU now", "الأكثر استخدامًا للمعالج الآن"),
        R("cpu.eco", "in efficiency mode", "في وضع الكفاءة"),
        R("cpu.busy", "busy for a minute", "مشغول منذ دقيقة"),
        R("cpu.restore", "Restore", "استعادة"),
        R("cpu.calm_btn", "Calm", "تهدئة"),
        R("cpu.restored", "{0} runs at full speed again.", "{0} يعمل الآن بكامل سرعته."),
        R("cpu.calmed", "{0} is in efficiency mode.", "{0} في وضع الكفاءة الآن."),

        // ---- GPU page ----
        R("gpu.looking", "Looking for the graphics card...", "جارٍ البحث عن بطاقة الرسوميات..."),
        R("gpu.restart", "Restart GPU", "إعادة تشغيل بطاقة الرسوميات"),
        R("gpu.restart_note", "Restarts the graphics card. The screen is black for a few seconds; apps that use CUDA may close.",
                              "تعيد تشغيل بطاقة الرسوميات. تبقى الشاشة سوداء لبضع ثوانٍ، وقد تُغلق التطبيقات التي تستخدم CUDA."),
        R("gpu.graph", "Green: VRAM in use. Grey: GPU load. Last 60 seconds.", "الأخضر: ذاكرة الرسوميات المستخدمة. الرمادي: حمل بطاقة الرسوميات. آخر 60 ثانية."),
        R("gpu.top", "Holding the most VRAM", "الأكثر استخدامًا لذاكرة الرسوميات"),
        R("gpu.row", "GPU {0:0}%   ·   {1}", "الحمل {0:0}%   ·   {1}"),
        R("gpu.facts", "VRAM {0:0.00} of {1:0.0} GB   ·   GPU load {2:0}%   ·   shared {3:0.00} GB (system RAM the GPU borrows)",
                       "ذاكرة الرسوميات {0:0.00} من {1:0.0} GB   ·   حمل البطاقة {2:0}%   ·   المشتركة {3:0.00} GB (تستعيرها البطاقة من ذاكرة RAM)"),

        // ---- efficiency guard page ----
        R("guard.intro", "Windows 11 puts apps it thinks are in the background in efficiency mode: lowest priority, slow cores. Browsers and editors then stutter and load slowly when you go back to them. Switch an app on below and NJ-Flush keeps it out, every 3 seconds, with no admin rights.",
                         "يضع Windows 11 التطبيقات التي يظنها في الخلفية في وضع الكفاءة: أقل أولوية ونوى بطيئة. عندها تتقطّع المتصفحات والمحررات وتتأخر في التحميل عند العودة إليها. فعّل أي تطبيق من القائمة أدناه ليُبقيه NJ-Flush خارج هذا الوضع، ويراجعه كل 3 ثوانٍ دون صلاحيات المسؤول."),
        R("guard.keep", "Keep the chosen apps out of efficiency mode", "أبقِ التطبيقات المختارة خارج وضع الكفاءة"),
        R("guard.battery", "Pause while on battery (efficiency mode saves battery)", "أوقف الحارس أثناء العمل على البطارية (وضع الكفاءة يوفّر البطارية)"),
        R("guard.hint", "Type to find any running app", "اكتب للبحث عن أي تطبيق قيد التشغيل"),
        R("guard.running", "Apps running now", "التطبيقات قيد التشغيل الآن"),
        R("guard.running_note", "In efficiency mode first, then the apps you keep out. One row per app; the switch covers all its processes.",
                                "تظهر أولًا التطبيقات في وضع الكفاءة، ثم التي أبقيتها خارجه. صف لكل تطبيق، والمفتاح يشمل كل عملياته."),
        R("guard.gone", "Kept out, not running now", "مستبعدة وغير قيد التشغيل الآن"),
        R("guard.tip_keep", "Keep {0} out of efficiency mode", "أبقِ {0} خارج وضع الكفاءة"),
        R("guard.said_keep", "{0} is kept out of efficiency mode.", "{0} خارج وضع الكفاءة الآن."),
        R("guard.said_free", "{0} is no longer kept out. Windows may slow it again.", "لم يعد {0} مستبعدًا، وقد يُبطئه Windows من جديد."),
        R("chip.eco", "in efficiency mode", "في وضع الكفاءة"),
        R("chip.eco_n", "{0} of {1} in efficiency mode", "{0} من {1} في وضع الكفاءة"),
        R("chip.kept", "kept out", "مستبعد"),
        R("guard.none_q", "No running app matches.", "لا يوجد تطبيق قيد التشغيل يطابق بحثك."),
        R("guard.none", "No app is in efficiency mode right now, and none is kept out. Type a name above to add one.",
                        "لا يوجد تطبيق في وضع الكفاءة الآن، ولا تطبيق مستبعد. اكتب اسمًا في الأعلى لإضافة تطبيق."),
        R("guard.remove_tip", "Remove {0}", "إزالة {0}"),
        R("guard.removed", "{0} removed.", "تمت إزالة {0}."),
        R("guard.off", "The guard is off.", "الحارس متوقف."),
        R("guard.paused", "Paused: this PC is on battery.", "متوقف مؤقتًا: الجهاز يعمل على البطارية."),
        R("guard.status", "{0} in efficiency mode now   ·   {1} kept out   ·   {2} taken out so far",
                          "{0} في وضع الكفاءة الآن   ·   المستبعدة: {1}   ·   {2} أُخرجت منه حتى الآن"),

        // ---- settings page ----
        R("set.sub", "Every change is saved and applied at once.", "يُحفظ كل تغيير ويُطبَّق فورًا."),
        R("set.anim", "Animations and sound", "الرسوم المتحركة والصوت"),
        R("set.play_anim", "Play the flush animations", "شغّل رسوم التفريغ المتحركة"),
        R("set.play_sound", "Play sound effects", "شغّل المؤثرات الصوتية"),
        R("set.volume", "Volume", "مستوى الصوت"),
        R("fmt.pct", "{0}%", "{0}%"),
        R("set.preview", "Preview the animations", "معاينة الرسوم المتحركة"),
        R("set.preview_note", "Plays one with your sound settings. Nothing is flushed.", "تُعرض الرسوم وفق إعدادات الصوت لديك دون تفريغ أي شيء."),
        R("grp.ram", "RAM", "RAM"),
        R("grp.cpu", "CPU", "CPU"),
        R("grp.gpu", "GPU", "GPU"),
        R("grp.all", "All", "الكل"),
        R("set.ram", "RAM flush", "تفريغ ذاكرة RAM"),
        R("set.ram_from", "Flush RAM from", "ابدأ تفريغ ذاكرة RAM عند"),
        R("fmt.in_use", "{0}% in use", "{0}% قيد الاستخدام"),
        R("set.ram_from_note", "Below this the cache is free memory already, so a flush does nothing.", "تحت هذه النسبة تكون الذاكرة المؤقتة مساحة حرة أصلًا، فلا يفيد التفريغ."),
        R("set.explorer", "Restart File Explorer on flush", "أعد تشغيل مستكشف الملفات عند التفريغ"),
        R("set.explorer_note", "Off by default since 1.3. When on, Explorer is closed gently and comes back.", "متوقف افتراضيًا منذ الإصدار 1.3. عند تفعيله يُغلق المستكشف بهدوء ثم يعود."),
        R("set.wait", "Wait between flushes", "المدة بين كل تفريغين"),
        R("fmt.sec", "{0} s", "{0} ث"),
        R("set.auto", "Auto-flush RAM when it stays high", "فرّغ ذاكرة RAM تلقائيًا عند بقائها مرتفعة"),
        R("set.auto_at", "Auto-flush at", "التفريغ التلقائي عند"),
        R("fmt.ram_pct", "{0}% RAM", "{0}% من ذاكرة RAM"),
        R("set.all", "Flush all also", "تفريغ الكل يشمل أيضًا"),
        R("set.gpu_reset", "Restarts the GPU when it helps", "إعادة تشغيل بطاقة الرسوميات عند الحاجة"),
        R("set.gpu_note", "On a Heavy or Clogged flush, or with VRAM 85% full. Never on an auto-flush or over a full-screen app.",
                          "عند التفريغ الثقيل أو المسدود، أو إذا امتلأت ذاكرة الرسوميات بنسبة 85%. لا يحدث أبدًا في التفريغ التلقائي أو فوق تطبيق بملء الشاشة."),
        R("set.calm", "Calms apps that keep the CPU busy", "تهدئة التطبيقات التي تُشغل المعالج"),
        R("set.widget", "Widget", "الودجت"),
        R("set.bars", "Show CPU and VRAM under RAM", "أظهر المعالج وذاكرة الرسوميات تحت ذاكرة RAM"),
        R("set.bright", "Brightness when idle", "السطوع عند عدم الاستخدام"),
        R("set.autostart", "Start the widget with Windows", "شغّل الودجت مع Windows"),
        R("set.reset_pos", "Reset position", "إعادة الموضع"),
        R("set.pos_done", "The widget is back above the clock.", "عاد الودجت إلى موضعه فوق الساعة."),
        R("set.drag_note", "Drag the widget anywhere; it stays there after restarts and flushes.", "اسحب الودجت إلى أي مكان؛ يبقى فيه بعد إعادة التشغيل والتفريغ."),
        R("set.keys", "Shortcuts", "الاختصارات"),
        R("set.keys_note", "Click a shortcut and press the new keys. × removes it. A shortcut another app already uses shows in red.",
                           "انقر على الاختصار ثم اضغط المفاتيح الجديدة. يحذفه الزر ×. يظهر الاختصار الذي يستخدمه تطبيق آخر باللون الأحمر."),
        R("set.keys_any", "Shortcuts work from anywhere", "تعمل الاختصارات من أي مكان"),
        R("set.key_none", "none", "بلا"),
        R("set.key_press", "Press the keys...", "اضغط المفاتيح..."),
        R("set.key_remove", "Remove this shortcut", "حذف هذا الاختصار"),
        R("set.key_cleared", "{0} has no shortcut now.", "تم حذف اختصار {0}."),
        R("set.key_moved", "{0} moved from {1}.", "نُقل الاختصار {0} من {1}."),
        R("set.key_need_mod", "Add Ctrl or Alt: a plain key would be taken from every app.", "أضف Ctrl أو Alt؛ فالمفتاح المنفرد سيُسحب من كل التطبيقات."),
        R("hotkey.taken", "taken by another app", "يستخدمه تطبيق آخر"),
        R("act.all", "Flush all", "تفريغ الكل"),
        R("act.ram", "Flush RAM", "تفريغ ذاكرة RAM"),
        R("act.cpu", "Calm CPU", "تهدئة المعالج"),
        R("act.gpu", "Reset GPU", "إعادة تشغيل بطاقة الرسوميات"),
        R("act.panel", "Open the panel", "فتح اللوحة"),
        R("set.updates", "Updates", "التحديثات"),
        R("set.auto_update", "Check for updates once a day", "تحقق من التحديثات مرة في اليوم"),
        R("set.check_now", "Check now", "تحقق الآن"),
        R("set.version", "You have version {0}", "الإصدار الحالي: {0}"),
        R("set.reset_all", "Reset everything to defaults", "إعادة كل شيء إلى الإعدادات الافتراضية"),
        R("set.reset_q", "Reset every setting to its default? Your kept-out apps stay.", "هل تريد إعادة كل الإعدادات إلى قيمها الافتراضية؟ تبقى تطبيقاتك المستبعدة كما هي."),
        R("set.reset_done", "Settings are back to their defaults.", "عادت الإعدادات إلى قيمها الافتراضية."),

        // ---- RAM breakdown (RAM page) ----
        R("bd.title", "Where your RAM is", "توزيع ذاكرة RAM"),
        R("bd.sub", "{0:0.00} GB in use of {1:0.00} GB", "{0:0.00} GB قيد الاستخدام من أصل {1:0.00} GB"),
        R("bd.apps", "Apps", "التطبيقات"),
        R("bd.apps_d", "Programs you are running", "البرامج التي تشغّلها"),
        R("bd.kernel", "Kernel", "النواة"),
        R("bd.kernel_d", "Windows' own memory pools", "مجمّعات الذاكرة الخاصة بنظام Windows"),
        R("bd.modified", "Waiting to be written", "بانتظار الكتابة"),
        R("bd.modified_d", "Changed pages on their way to disk; a flush writes them", "صفحات معدّلة في طريقها إلى القرص؛ يكتبها التفريغ"),
        R("bd.hidden", "Hidden", "المخفية"),
        R("bd.hidden_d", "Drivers, hypervisor, Memory Integrity, page tables; no flush reaches it",
                         "برامج التشغيل، والمشرف الافتراضي (Hypervisor)، وتكامل الذاكرة، وجداول الصفحات؛ لا يصل إليها أي تفريغ"),
        R("bd.cache", "Cache", "التخزين المؤقت"),
        R("bd.cache_d", "Recently used files, handed back the moment an app needs it", "ملفات استُخدمت مؤخرًا، تُعاد فورًا حين يحتاج إليها تطبيق"),
        R("bd.free", "Free", "الحرة"),
        R("bd.free_d", "Not used at all", "غير مستخدمة إطلاقًا"),
        R("bd.no_admin", "Cache and free memory need admin rights to read. They show after a flush has run once.", "تحتاج قراءة التخزين المؤقت والذاكرة الحرة إلى صلاحيات المسؤول. تظهر بعد تشغيل تفريغ واحد."),
        R("bd.reserved", "Reserved memory", "الذاكرة المحجوزة"),
        R("bd.reserved_near", "Near the limit: apps may fail to start. Close some, or flush.", "قريبة من الحد: قد تفشل بعض التطبيقات في البدء. أغلق بعضها أو نفّذ تفريغًا."),
        R("bd.reserved_ok", "What apps have asked Windows to set aside", "ما طلبت التطبيقات من Windows حجزه"),
        R("bd.procs", "Processes", "العمليات"),
        R("bd.left", "Leftover helpers", "عمليات مساعدة متبقية"),
        R("bd.left_yes", "Helper processes from closed sessions. A flush stops them.", "عمليات مساعدة من جلسات أُغلقت. يوقفها التفريغ."),
        R("bd.left_none", "None found", "لا توجد"),
        R("bd.mi", "Memory Integrity", "تكامل الذاكرة"),
        R("bd.mi_on", "On", "مفعّل"),
        R("bd.mi_off", "Off", "متوقف"),
        R("bd.mi_note", "Holds memory no tool can see. It is part of Hidden.", "يحجز ذاكرة لا تراها أي أداة، وهي جزء من الذاكرة المخفية."),
        R("bd.tip_node", "Many node processes are running. Each VS Code window and Claude session starts its own helpers; close the ones you do not use.",
                         "عمليات node كثيرة قيد التشغيل. تُنشئ كل نافذة VS Code وكل جلسة Claude عملياتها المساعدة؛ أغلق ما لا تستخدمه."),
        R("bd.tip_hidden", "A large Hidden share is memory no flush can reach. Restarting Windows clears it; turning off Memory Integrity frees part of it, at some cost to security.",
                           "حصة كبيرة من الذاكرة المخفية لا يصل إليها أي تفريغ. إعادة تشغيل Windows تفرّغها، وإيقاف تكامل الذاكرة يحرّر جزءًا منها لكن على حساب بعض الأمان."),

        // ---- widget: menu, tooltip, answers ----
        R("menu.open", "Open the panel", "فتح اللوحة"),
        R("menu.all", "Flush all", "تفريغ الكل"),
        R("menu.one", "Flush one", "تفريغ واحد"),
        R("menu.deep", "Deep flush RAM (the 1.2 flush)", "تفريغ عميق لذاكرة RAM (تفريغ الإصدار 1.2)"),
        R("menu.guard", "Keep apps out of efficiency mode", "أبقِ التطبيقات خارج وضع الكفاءة"),
        R("menu.preview", "Preview animations", "معاينة الرسوم المتحركة"),
        R("menu.settings", "Settings...", "الإعدادات..."),
        R("menu.reset_pos", "Reset widget position", "إعادة موضع الودجت"),
        R("menu.check", "Check for updates...", "التحقق من التحديثات..."),
        R("menu.update_to", "Update to {0}...", "التحديث إلى {0}..."),
        R("menu.uninstall", "Uninstall NJ-Flush...", "إزالة NJ-Flush..."),
        R("menu.close", "Close", "إغلاق"),
        R("menu.uninstall_q", "Remove NJ-Flush from this PC?\n\nThe task, the autostart entry, the settings and the installed copy are removed.",
                              "هل تريد إزالة NJ-Flush من هذا الجهاز؟\n\nستُحذف المهمة المجدولة وإدخال التشغيل التلقائي والإعدادات والنسخة المثبّتة."),
        R("tip.ram", "RAM: {0:0.00} GB free", "ذاكرة RAM: المتاح {0:0.00} GB"),
        R("tip.cpu", "CPU: {0:0}%", "المعالج: {0:0}%"),
        R("tip.vram", "VRAM: {0:0.0} of {1:0.0} GB ({2})", "ذاكرة الرسوميات: {0:0.0} من {1:0.0} GB ({2})"),
        R("tip.guard", "Efficiency guard: {0}", "حارس الكفاءة: {0}"),
        R("tip.guard_paused", "paused on battery", "متوقف مؤقتًا على البطارية"),
        R("tip.guard_kept", "kept {0} out", "أُبقيت خارج وضع الكفاءة: {0}"),
        R("tip.dbl", "Double-click: the panel   Drag: move it", "انقر مرتين: اللوحة   اسحب: لتحريكه"),
        R("tip.key", "{0}: flush all", "{0}: تفريغ الكل"),
        R("tip.update", "Update {0} is available (right-click)", "التحديث {0} متاح (انقر بزر الفأرة الأيمن)"),
        R("tray.base", "NJ-Flush   RAM {0}   CPU {1:0}%", "NJ-Flush   RAM {0}   المعالج {1:0}%"),
        R("tray.vram", "   VRAM {0:0}%", "   VRAM {0:0}%"),
        R("w.next", "next flush {0}s", "التفريغ التالي بعد {0} ث"),
        R("say.cooldown_all", "Just flushed. Next flush in {0} s.", "تم التفريغ للتو. التفريغ التالي بعد {0} ث."),
        R("say.cooldown_kind", "Just flushed. Next {0} flush in {1} s.", "تم التفريغ للتو. التفريغ التالي ({0}) بعد {1} ث."),
        R("say.ram_fine", "RAM is fine ({0:0}% in use). Nothing to flush.", "ذاكرة RAM بحالة جيدة ({0:0}% قيد الاستخدام). لا حاجة إلى التفريغ."),
        R("say.all", "Flushing...", "جارٍ التفريغ..."),
        R("say.deep", "Deep flush...", "تفريغ عميق..."),
        R("say.ram", "Flushing RAM...", "جارٍ تفريغ ذاكرة RAM..."),
        R("say.cpu", "Calming the CPU...", "جارٍ تهدئة المعالج..."),
        R("say.gpu", "Restarting the GPU...", "جارٍ إعادة تشغيل بطاقة الرسوميات..."),

        // ---- overlays: pills, busy lines, scene names ----
        R("ov.pipes", "Checking the pipes...", "نفحص المواسير..."),
        R("ov.deep", "Deep flush...", "تفريغ عميق..."),
        R("ov.ram", "Flushing RAM...", "جارٍ تفريغ ذاكرة RAM..."),
        R("ov.clogged", "It's clogged.", "المجرى مسدود."),
        R("ov.plunge", "Plunging...", "نفتح السدّة..."),
        R("ov.finishing", "Finishing in the background...", "يكتمل العمل في الخلفية..."),
        R("ov.preview", "Preview: nothing was flushed", "معاينة: لم يُفرَّغ أي شيء"),
        R("ov.checking", "Checking...", "جارٍ الفحص..."),
        R("ov.working", "Working...", "جارٍ العمل..."),
        R("pill.0", "LIGHT FLUSH", "تفريغ خفيف"),
        R("pill.1", "MEDIUM FLUSH", "تفريغ متوسط"),
        R("pill.2", "HEAVY FLUSH", "تفريغ ثقيل"),
        R("pill.3", "CLOGGED", "مسدود"),
        R("pill.tier", "{0}   ·   {1:0}% RAM", "{0}   ·   ذاكرة RAM {1:0}%"),
        R("pill.fine", "RAM IS FINE", "ذاكرة RAM بحالة جيدة"),
        R("pill.fine_use", "RAM IS FINE   ·   {0:0}% in use", "ذاكرة RAM بحالة جيدة   ·   {0:0}% قيد الاستخدام"),
        R("pill.cpu", "CALM CPU   ·   {0}   ·   {1}", "تهدئة المعالج   ·   {0}   ·   {1}"),
        R("pill.gpu", "RESET GPU   ·   {0}   ·   {1}", "إعادة تشغيل بطاقة الرسوميات   ·   {0}   ·   {1}"),
        R("pill.all", "FLUSH ALL   ·   {0}   ·   {1}", "تفريغ الكل   ·   {0}   ·   {1}"),
        R("ms.cpu", "{0:0}% CPU", "المعالج {0:0}%"),
        R("ms.vram", "{0:0}% VRAM", "ذاكرة الرسوميات {0:0}%"),
        R("ms.gpu", "GPU", "بطاقة الرسوميات"),
        R("ms.ram", "{0:0}% RAM", "ذاكرة RAM {0:0}%"),
        R("ms.preview", "preview", "معاينة"),
        R("scene.cpu.0", "Breeze", "نسمة"),
        R("scene.cpu.1", "Vent", "تهوية"),
        R("scene.cpu.2", "Overheat", "سخونة زائدة"),
        R("scene.cpu.3", "Meltdown", "انصهار"),
        R("scene.gpu.0", "Smudge", "بقعة"),
        R("scene.gpu.1", "Steamy", "بخار"),
        R("scene.gpu.2", "Fogged", "ضباب"),
        R("scene.gpu.3", "Frozen", "تجمّد"),
        R("scene.all.0", "Rinse", "شطف"),
        R("scene.all.1", "Shower", "دش"),
        R("scene.all.2", "Scrub", "فرك"),
        R("scene.all.3", "Flood", "فيضان"),
        R("busy.cpu.0", "A little fresh air...", "قليل من الهواء النقي..."),
        R("busy.cpu.1", "Venting the heat...", "نُخرج الحرارة..."),
        R("busy.cpu.2", "Cooling it down...", "نبرّد المعالج..."),
        R("busy.cpu.3", "Emergency cooling...", "تبريد طارئ..."),
        R("busy.gpu", "Restarting the GPU...", "جارٍ إعادة تشغيل بطاقة الرسوميات..."),
        R("busy.all.0", "A quick rinse...", "شطفة سريعة..."),
        R("busy.all.1", "Washing everything...", "نغسل كل شيء..."),
        R("busy.all.2", "Scrubbing everything...", "نفرك كل شيء..."),
        R("busy.all.3", "Draining the flood...", "نصرّف الفيضان..."),

        // ---- flush results shown in the overlay (Work.cs) ----
        R("res.gpu_not", "GPU not restarted: {0}", "لم يُعد تشغيل بطاقة الرسوميات: {0}"),
        R("res.gpu_ok", "GPU restarted: VRAM {0:0.0} -> {1:0.0} GB", "أُعيد تشغيل بطاقة الرسوميات: ذاكرة الرسوميات من {0:0.0} إلى {1:0.0} GB"),
        R("res.gpu_ok2", "GPU restarted", "أُعيد تشغيل بطاقة الرسوميات"),
        R("res.cpu_nodata", "CPU: no data yet, the widget measures it", "المعالج: لا توجد بيانات بعد، يقيسها الودجت"),
        R("res.cpu_none", "CPU: nothing busy to calm", "المعالج: لا يوجد ما يستدعي التهدئة"),
        R("res.cpu_calmed", "Calmed {0}: {1}", "تمت تهدئة {0}: {1}"),
        R("res.ram_fine", "RAM is fine ({0:0}% in use, {1:0.0} GB free): nothing to flush", "ذاكرة RAM بحالة جيدة ({0:0}% قيد الاستخدام، المتاح {1:0.0} GB): لا حاجة إلى التفريغ"),
        R("res.stopped", "Stopped: {0}", "توقف العمل: {0}"),
        R("res.freed", "Freed {0:0.00} GB RAM   ({1:0.0} -> {2:0.0} GB)", "تم تحرير {0:0.00} GB من ذاكرة RAM   (من {1:0.0} إلى {2:0.0} GB)"),
        R("res.leftover", "Stopped {0} leftover helper processes", "أُوقفت عمليات مساعدة متبقية: {0}"),
        R("reason.no_gpu", "no graphics card with its own memory", "لا توجد بطاقة رسوميات بذاكرة خاصة"),
        R("reason.no_device", "the graphics card's device was not found", "لم يُعثر على جهاز بطاقة الرسوميات"),
        R("reason.pnp", "pnputil refused ({0})", "رفض pnputil الطلب ({0})"),
        R("reason.pnp_admin", "pnputil refused ({0}): admin rights needed", "رفض pnputil الطلب ({0}): يلزم صلاحيات المسؤول"),
        R("reason.not_asked", "not asked", "لم يُطلب"),
        R("reason.off", "off in Settings", "متوقف في الإعدادات"),
        R("reason.auto", "never on an auto-flush", "لا يحدث في التفريغ التلقائي"),
        R("reason.fullscreen", "a full-screen app is in front", "تطبيق بملء الشاشة في المقدمة"),
        R("reason.not_needed", "not needed", "غير لازم"),
        // the one line kept in last.txt (shown as "Last flush"), written in the language in force when the flush ran
        R("last.gpu_skipped", "GPU skipped: {0}", "تم تخطي بطاقة الرسوميات: {0}"),
        R("last.cpu_off", "CPU skipped: off in Settings", "تم تخطي المعالج: متوقف في الإعدادات"),
        R("last.ram_skipped", "RAM skipped: {0:0}% in use, under {1}%", "تم تخطي ذاكرة RAM: {0:0}% قيد الاستخدام، أقل من {1}%"),
        R("last.gpu_failed", "GPU restart failed: {0}", "تعذّرت إعادة تشغيل بطاقة الرسوميات: {0}"),
        R("last.gpu_ok", "GPU restarted, VRAM {0:0.00} -> {1:0.00} GB", "أُعيد تشغيل بطاقة الرسوميات، ذاكرة الرسوميات من {0:0.00} إلى {1:0.00} GB"),
        R("last.gpu_ok2", "GPU restarted", "أُعيد تشغيل بطاقة الرسوميات"),
        R("last.cpu_nodata", "CPU: no data yet", "المعالج: لا توجد بيانات بعد"),
        R("last.cpu_none", "CPU: none calmed", "المعالج: لم تتم تهدئة أي تطبيق"),
        R("last.cpu_calmed", "CPU calmed {0}", "تمت تهدئة المعالج: {0}"),
        R("last.ram", "RAM {0:0.0} -> {1:0.0} GB", "ذاكرة RAM من {0:0.0} إلى {1:0.0} GB"),
        R("last.ram_deep", "RAM deep {0:0.0} -> {1:0.0} GB", "ذاكرة RAM، تفريغ عميق من {0:0.0} إلى {1:0.0} GB"),
        R("last.trimmed", "trimmed {0} idle apps", "قُلّصت التطبيقات الخاملة: {0}"),
        R("last.explorer_ok", "Explorer restarted", "أُعيد تشغيل المستكشف"),
        R("last.explorer_no", "Explorer not restarted", "لم يُعد تشغيل المستكشف"),
        R("last.leftover", "stopped {0} leftover helpers", "أُوقفت عمليات مساعدة متبقية: {0}"),
        R("last.error", "error {0}", "خطأ: {0}"),

        // ---- updates ----
        R("upd.unreachable", "Could not reach GitHub ({0}).", "تعذّر الوصول إلى GitHub ({0})."),
        R("upd.no_exe", "The latest release has no NJ-Flush.exe.", "الإصدار الأحدث لا يحتوي على ملف NJ-Flush.exe."),
        R("upd.unreadable", "The release could not be read ({0}).", "تعذّرت قراءة بيانات الإصدار ({0})."),
        R("upd.no_newer", "No newer version to install.", "لا يوجد إصدار أحدث للتثبيت."),
        R("upd.no_sha", "GitHub did not publish a checksum for this file, so it was not installed.", "لم ينشر GitHub بصمة تحقق لهذا الملف، لذلك لم يُثبَّت."),
        R("upd.bad_sha", "The download did not match its checksum, so it was not installed.", "لم يطابق الملف المنزَّل بصمة التحقق، لذلك لم يُثبَّت."),
        R("upd.failed", "Update failed: {0}", "فشل التحديث: {0}"),
        R("upd.title", "NJ-Flush updates", "تحديثات NJ-Flush"),
        R("upd.latest", "You have the latest version ({0}).", "لديك أحدث إصدار ({0})."),
        R("upd.available", "NJ-Flush {0} is available (you have {1}).\n\nUpdate now? The widget restarts by itself.", "الإصدار {0} من NJ-Flush متاح (إصدارك {1}).\n\nهل تريد التحديث الآن؟ يُعاد تشغيل الودجت تلقائيًا."),
    };
}
