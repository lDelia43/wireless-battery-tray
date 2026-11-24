using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// User settings, stored in %LOCALAPPDATA%\WirelessBatteryTray\settings.ini
// Global keys first, then one [monitor] section per monitor with its own look and position.
class PosMon
{
    public int OffsetX, Spacing = 12;
    public float TextSize = 13f, IconSize = 16f;
    public bool LeftSide, Locked, ShowIcons = true, ShowPercent = true, ColorByLevel = true, Animate = true;
    public Color TextColor = Color.White;
    public HashSet<string> Hidden = new HashSet<string>();

    public PosMon Copia()
    {
        var c = (PosMon)MemberwiseClone(); c.Hidden = new HashSet<string>(Hidden); c.OffsetX = 0; c.LeftSide = false; c.Locked = false; return c;
    }

    static readonly System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
    public void Set(string k, string v)
    {
        switch (k)
        {
            case "OffsetX": int.TryParse(v, out OffsetX); break;
            case "Spacing": int.TryParse(v, out Spacing); break;
            case "TextSize": float.TryParse(v, System.Globalization.NumberStyles.Float, CI, out TextSize); break;
            case "IconSize": float.TryParse(v, System.Globalization.NumberStyles.Float, CI, out IconSize); break;
            case "LeftSide": LeftSide = v == "1"; break;
            case "Locked": Locked = v == "1"; break;
            case "ShowIcons": ShowIcons = v == "1"; break;
            case "ShowPercent": ShowPercent = v == "1"; break;
            case "ColorByLevel": ColorByLevel = v == "1"; break;
            case "Animate": Animate = v == "1"; break;
            case "TextColor": try { TextColor = ColorTranslator.FromHtml(v); } catch { } break;
            case "Hidden": Hidden = new HashSet<string>(v.Split('|').Where(x => x != "")); break;
        }
    }
    public IEnumerable<string> Lineas()
    {
        yield return "OffsetX=" + OffsetX; yield return "Spacing=" + Spacing;
        yield return "TextSize=" + TextSize.ToString(CI); yield return "IconSize=" + IconSize.ToString(CI);
        yield return "LeftSide=" + (LeftSide ? 1 : 0); yield return "Locked=" + (Locked ? 1 : 0);
        yield return "ShowIcons=" + (ShowIcons ? 1 : 0); yield return "ShowPercent=" + (ShowPercent ? 1 : 0);
        yield return "ColorByLevel=" + (ColorByLevel ? 1 : 0); yield return "Animate=" + (Animate ? 1 : 0);
        yield return "TextColor=" + ColorTranslator.ToHtml(TextColor); yield return "Hidden=" + string.Join("|", Hidden);
    }
}

static class Cfg
{
    public static int LowThreshold = 20, RefreshSeconds = 30;
    public static bool Notifications = true;
    public static HashSet<string> Monitors = new HashSet<string>();   // monitor device names; empty = main monitor only
    public static Dictionary<string, PosMon> Pos = new Dictionary<string, PosMon>();
    static PosMon Base = new PosMon();   // template for monitors seen for the first time (also reads old single-monitor settings)

    public static PosMon PosDe(string monitor)
    {
        PosMon p; if (!Pos.TryGetValue(monitor, out p)) { p = Base.Copia(); Pos[monitor] = p; }
        return p;
    }

    static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WirelessBatteryTray"); } }
    static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            PosMon actual = null;
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) { actual = PosDe(line.Substring(1, line.Length - 2)); continue; }
                int i = line.IndexOf('='); if (i < 1) continue;
                string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                if (actual != null) { actual.Set(k, v); continue; }
                switch (k)
                {
                    case "LowThreshold": int.TryParse(v, out LowThreshold); break;
                    case "RefreshSeconds": int.TryParse(v, out RefreshSeconds); break;
                    case "Notifications": Notifications = v == "1"; break;
                    case "Monitors": Monitors = new HashSet<string>(v.Split('|').Where(x => x != "")); break;
                    default: Base.Set(k, v); break;
                }
            }
            if (RefreshSeconds < 5) RefreshSeconds = 5;
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var l = new List<string> { "LowThreshold=" + LowThreshold, "RefreshSeconds=" + RefreshSeconds, "Notifications=" + (Notifications ? 1 : 0), "Monitors=" + string.Join("|", Monitors) };
            foreach (var kv in Pos) { l.Add(""); l.Add("[" + kv.Key + "]"); l.AddRange(kv.Value.Lineas()); }
            File.WriteAllLines(FilePath, l);
        }
        catch { }
    }

    // Start with Windows: shortcut in the Startup folder created by the installer
    static string StartupLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Wireless Battery Tray.lnk"); } }
    static string StartupLinkOff { get { return Path.Combine(Dir, "Wireless Battery Tray.lnk.disabled"); } }
    public static bool StartWithWindows { get { return File.Exists(StartupLink); } }
    public static void SetStartWithWindows(bool on)
    {
        try
        {
            if (on && !File.Exists(StartupLink) && File.Exists(StartupLinkOff)) File.Move(StartupLinkOff, StartupLink);
            else if (!on && File.Exists(StartupLink)) { if (File.Exists(StartupLinkOff)) File.Delete(StartupLinkOff); File.Move(StartupLink, StartupLinkOff); }
        }
        catch { }
    }
}

class SettingsForm : Form
{
    public event Action Applied;
    static readonly Color Bg = Color.FromArgb(32, 32, 32), Card = Color.FromArgb(43, 43, 43), Line = Color.FromArgb(60, 60, 60), Dim = Color.FromArgb(170, 170, 170), Accent = Color.FromArgb(0, 120, 212);

    TableLayoutPanel tabla;

    public SettingsForm(IEnumerable<string> devices, PosMon mc, string monitor)
    {
        var pant = Screen.AllScreens.OrderBy(x => x.Bounds.Left).ToList();
        int nMon = pant.FindIndex(x => x.DeviceName == monitor) + 1;
        Text = "Wireless Battery Tray - Settings (Monitor " + nMon + ")";
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9.5f); BackColor = Bg; ForeColor = Color.White;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; ShowInTaskbar = true; TopMost = true;
        Padding = new Padding(20, 12, 20, 16);

        tabla = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(20, 12) };
        tabla.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        tabla.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        Controls.Add(tabla);

        Seccion("Display - Monitor " + nMon);
        var cIcons = Check("Show device icons", mc.ShowIcons);
        var cPct = Check("Show % sign", mc.ShowPercent);
        var cColor = Check("Red text when battery is low", mc.ColorByLevel);
        var cAnim = Check("Animate number changes", mc.Animate);
        var nText = Num("Text size", 10, 30, (decimal)mc.TextSize);
        var nIcon = Num("Icon size", 10, 32, (decimal)mc.IconSize);
        var nSpace = Num("Space between devices", 4, 60, mc.Spacing);

        Seccion("Behavior");
        var nLow = Num("Low battery warning at (%)", 0, 100, Cfg.LowThreshold);
        var nRef = Num("Refresh every (seconds)", 5, 600, Cfg.RefreshSeconds);
        var cNot = Check("Low battery notifications", Cfg.Notifications);
        var cStart = Check("Start with Windows", Cfg.StartWithWindows);

        Seccion("Monitors");
        var pantallas = Screen.AllScreens.OrderBy(x => x.Bounds.Left).ToList();
        var checksMon = new List<Tuple<CheckBox, string>>();
        for (int i = 0; i < pantallas.Count; i++)
        {
            var sc = pantallas[i];
            bool marcado = Cfg.Monitors.Count == 0 ? sc.Primary : Cfg.Monitors.Contains(sc.DeviceName);
            checksMon.Add(Tuple.Create(Check("Monitor " + (i + 1) + (sc.Primary ? " (main)" : "") + "  -  " + sc.Bounds.Width + "x" + sc.Bounds.Height, marcado), sc.DeviceName));
        }

        var devs = devices.Concat(mc.Hidden).Distinct().ToList();
        var checksDev = new List<CheckBox>();
        if (devs.Count > 0)
        {
            Seccion("Devices");
            foreach (var d in devs) checksDev.Add(Check(d, !mc.Hidden.Contains(d)));
        }

        // buttons
        var barra = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = false, Size = new Size(370, 50), Anchor = AnchorStyles.Right, Margin = new Padding(0, 18, 0, 0) };
        var ok = Boton("Save", true); var cancel = Boton("Cancel", false); var reset = Boton("Reset", false);
        barra.Controls.AddRange(new Control[] { ok, cancel, reset });
        tabla.Controls.Add(barra); tabla.SetColumnSpan(barra, 2);
        AcceptButton = ok; CancelButton = cancel;

        cancel.Click += (s, e) => Close();
        reset.Click += (s, e) =>
        {
            cIcons.Checked = cPct.Checked = cColor.Checked = cAnim.Checked = cNot.Checked = true;
             nText.Value = 13; nIcon.Value = 16; nSpace.Value = 12; nLow.Value = 20; nRef.Value = 30;
            foreach (var c in checksDev) c.Checked = true;
            foreach (var t in checksMon) t.Item1.Checked = Screen.AllScreens.First(x => x.DeviceName == t.Item2).Primary;
        };
        ok.Click += (s, e) =>
        {
            mc.ShowIcons = cIcons.Checked; mc.ShowPercent = cPct.Checked; mc.ColorByLevel = cColor.Checked; mc.Animate = cAnim.Checked;
            mc.TextSize = (float)nText.Value; mc.IconSize = (float)nIcon.Value; mc.Spacing = (int)nSpace.Value;
            Cfg.LowThreshold = (int)nLow.Value; Cfg.RefreshSeconds = (int)nRef.Value;
            Cfg.Notifications = cNot.Checked; Cfg.SetStartWithWindows(cStart.Checked);
            mc.Hidden.Clear(); foreach (var c in checksDev) if (!c.Checked) mc.Hidden.Add(c.Text);
            var monAntes = string.Join("|", Cfg.Monitors.OrderBy(x => x));
            Cfg.Monitors = new HashSet<string>(checksMon.Where(t => t.Item1.Checked).Select(t => t.Item2));
            if (Cfg.Monitors.Count == 0) Cfg.Monitors.Add(Screen.PrimaryScreen.DeviceName);
            bool reiniciar = monAntes != string.Join("|", Cfg.Monitors.OrderBy(x => x));
            Cfg.Save();
            if (reiniciar) { Application.Restart(); return; }
            if (Applied != null) Applied();
            Close();
        };
    }

    void Seccion(string t)
    {
        var l = new Label { Text = t.ToUpperInvariant(), AutoSize = true, ForeColor = Dim, Font = new Font("Segoe UI Semibold", 8.5f), Margin = new Padding(0, tabla.Controls.Count == 0 ? 4 : 16, 0, 4) };
        tabla.Controls.Add(l); tabla.SetColumnSpan(l, 2);
    }

    void Fila(string t, Control c)
    {
        tabla.Controls.Add(new Label { Text = t, AutoSize = false, Size = new Size(260, 34), TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty });
        c.Anchor = AnchorStyles.Right; tabla.Controls.Add(c);
    }

    NumericUpDown Num(string t, decimal min, decimal max, decimal val)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, val)), Width = 96, BackColor = Card, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center, Margin = new Padding(0, 4, 0, 4) };
        Fila(t, n); return n;
    }

    CheckBox Check(string t, bool v)
    {
        var c = new CheckBox { Text = t, Checked = v, AutoSize = true, Margin = new Padding(0, 6, 0, 6), Cursor = Cursors.Hand };
        tabla.Controls.Add(c); tabla.SetColumnSpan(c, 2); return c;
    }

    static Button Boton(string t, bool principal)
    {
        var b = new Button { Text = t, Size = new Size(92, 32), FlatStyle = FlatStyle.Flat, BackColor = principal ? Accent : Color.FromArgb(55, 55, 55), ForeColor = Color.White, Cursor = Cursors.Hand, Margin = new Padding(8, 0, 0, 0) };
        b.FlatAppearance.BorderColor = principal ? Accent : Line;
        return b;
    }
}
