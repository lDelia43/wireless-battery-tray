using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// User settings, stored as key=value in %LOCALAPPDATA%\BateriaEnBandeja\settings.ini
static class Cfg
{
    public static int LowThreshold = 20, RefreshSeconds = 30, OffsetX = 0, OffsetY = 0, Spacing = 18;
    public static float TextSize = 16f, IconSize = 20f;
    public static bool ShowIcons = true, ShowPercent = true, Notifications = true, ColorByLevel = true, Animate = true;
    public static Color TextColor = Color.White;
    public static HashSet<string> Hidden = new HashSet<string>();

    static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BateriaEnBandeja"); } }
    static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int i = line.IndexOf('='); if (i < 1) continue;
                string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                switch (k)
                {
                    case "LowThreshold": int.TryParse(v, out LowThreshold); break;
                    case "RefreshSeconds": int.TryParse(v, out RefreshSeconds); break;
                    case "OffsetX": int.TryParse(v, out OffsetX); break;
                    case "OffsetY": int.TryParse(v, out OffsetY); break;
                    case "Spacing": int.TryParse(v, out Spacing); break;
                    case "TextSize": float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out TextSize); break;
                    case "IconSize": float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out IconSize); break;
                    case "ShowIcons": ShowIcons = v == "1"; break;
                    case "ShowPercent": ShowPercent = v == "1"; break;
                    case "Notifications": Notifications = v == "1"; break;
                    case "ColorByLevel": ColorByLevel = v == "1"; break;
                    case "Animate": Animate = v == "1"; break;
                    case "TextColor": try { TextColor = ColorTranslator.FromHtml(v); } catch { } break;
                    case "Hidden": Hidden = new HashSet<string>(v.Split('|').Where(x => x != "")); break;
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
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            File.WriteAllLines(FilePath, new[] {
                "LowThreshold=" + LowThreshold, "RefreshSeconds=" + RefreshSeconds, "OffsetX=" + OffsetX, "OffsetY=" + OffsetY,
                "Spacing=" + Spacing, "TextSize=" + TextSize.ToString(ci), "IconSize=" + IconSize.ToString(ci),
                "ShowIcons=" + (ShowIcons ? 1 : 0), "ShowPercent=" + (ShowPercent ? 1 : 0), "Notifications=" + (Notifications ? 1 : 0),
                "ColorByLevel=" + (ColorByLevel ? 1 : 0), "Animate=" + (Animate ? 1 : 0),
                "TextColor=" + ColorTranslator.ToHtml(TextColor), "Hidden=" + string.Join("|", Hidden) });
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

    public SettingsForm(IEnumerable<string> devices)
    {
        Text = "Wireless Battery Tray - Settings";
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9.5f); BackColor = Bg; ForeColor = Color.White;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; ShowInTaskbar = true; TopMost = true;
        Padding = new Padding(20, 12, 20, 16);

        tabla = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
        tabla.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        tabla.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        Controls.Add(tabla);

        Seccion("Display");
        var cIcons = Check("Show device icons", Cfg.ShowIcons);
        var cPct = Check("Show % sign", Cfg.ShowPercent);
        var cColor = Check("Red text when battery is low", Cfg.ColorByLevel);
        var cAnim = Check("Animate number changes", Cfg.Animate);
        var bColor = new Button { Size = new Size(96, 26), BackColor = Cfg.TextColor, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 0, 4) };
        bColor.FlatAppearance.BorderColor = Line;
        bColor.Click += (s, e) => { using (var cd = new ColorDialog { Color = bColor.BackColor, FullOpen = true }) if (cd.ShowDialog(this) == DialogResult.OK) bColor.BackColor = cd.Color; };
        Fila("Text color", bColor);
        var nText = Num("Text size", 10, 30, (decimal)Cfg.TextSize);
        var nIcon = Num("Icon size", 10, 32, (decimal)Cfg.IconSize);
        var nSpace = Num("Space between devices", 4, 60, Cfg.Spacing);

        Seccion("Position");
        var nX = Num("Move left (px)", -500, 1500, Cfg.OffsetX);
        var nY = Num("Move down (px)", -20, 20, Cfg.OffsetY);

        Seccion("Behavior");
        var nLow = Num("Low battery warning at (%)", 0, 100, Cfg.LowThreshold);
        var nRef = Num("Refresh every (seconds)", 5, 600, Cfg.RefreshSeconds);
        var cNot = Check("Low battery notifications", Cfg.Notifications);
        var cStart = Check("Start with Windows", Cfg.StartWithWindows);

        var devs = devices.Concat(Cfg.Hidden).Distinct().ToList();
        var checksDev = new List<CheckBox>();
        if (devs.Count > 0)
        {
            Seccion("Devices");
            foreach (var d in devs) checksDev.Add(Check(d, !Cfg.Hidden.Contains(d)));
        }

        // buttons
        var barra = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 18, 0, 0) };
        var ok = Boton("Save", true); var cancel = Boton("Cancel", false); var reset = Boton("Reset", false);
        barra.Controls.AddRange(new Control[] { ok, cancel, reset });
        tabla.Controls.Add(barra); tabla.SetColumnSpan(barra, 2);
        AcceptButton = ok; CancelButton = cancel;

        cancel.Click += (s, e) => Close();
        reset.Click += (s, e) =>
        {
            cIcons.Checked = cPct.Checked = cColor.Checked = cAnim.Checked = cNot.Checked = true;
            bColor.BackColor = Color.White; nText.Value = 16; nIcon.Value = 20; nSpace.Value = 18; nX.Value = 0; nY.Value = 0; nLow.Value = 20; nRef.Value = 30;
            foreach (var c in checksDev) c.Checked = true;
        };
        ok.Click += (s, e) =>
        {
            Cfg.ShowIcons = cIcons.Checked; Cfg.ShowPercent = cPct.Checked; Cfg.ColorByLevel = cColor.Checked; Cfg.Animate = cAnim.Checked;
            Cfg.TextColor = bColor.BackColor; Cfg.TextSize = (float)nText.Value; Cfg.IconSize = (float)nIcon.Value; Cfg.Spacing = (int)nSpace.Value;
            Cfg.OffsetX = (int)nX.Value; Cfg.OffsetY = (int)nY.Value; Cfg.LowThreshold = (int)nLow.Value; Cfg.RefreshSeconds = (int)nRef.Value;
            Cfg.Notifications = cNot.Checked; Cfg.SetStartWithWindows(cStart.Checked);
            Cfg.Hidden.Clear(); foreach (var c in checksDev) if (!c.Checked) Cfg.Hidden.Add(c.Text);
            Cfg.Save();
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
