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

    public SettingsForm(IEnumerable<string> devices)
    {
        Text = "Wireless Battery Tray - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f); BackColor = Color.FromArgb(32, 32, 32); ForeColor = Color.White;
        ClientSize = new Size(440, 560); ShowInTaskbar = true; TopMost = true;

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
        Controls.Add(flow);

        Func<string, Label> titulo = t => new Label { Text = t, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(0, 10, 0, 4) };
        Func<string, decimal, decimal, decimal, NumericUpDown> num = (t, min, max, val) =>
        {
            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            row.Controls.Add(new Label { Text = t, Width = 230, TextAlign = ContentAlignment.MiddleLeft, Height = 26 });
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, val)), Width = 80, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };
            row.Controls.Add(n); flow.Controls.Add(row); return n;
        };
        Func<string, bool, CheckBox> chk = (t, v) => { var c = new CheckBox { Text = t, Checked = v, AutoSize = true, Margin = new Padding(0, 3, 0, 3) }; flow.Controls.Add(c); return c; };

        flow.Controls.Add(titulo("Display"));
        var cIcons = chk("Show device icons", Cfg.ShowIcons);
        var cPct = chk("Show % sign", Cfg.ShowPercent);
        var cColor = chk("Color by level (red when low)", Cfg.ColorByLevel);
        var cAnim = chk("Animate number changes", Cfg.Animate);
        var colorRow = new FlowLayoutPanel { AutoSize = true };
        colorRow.Controls.Add(new Label { Text = "Text color", Width = 230, Height = 26, TextAlign = ContentAlignment.MiddleLeft });
        var bColor = new Button { Width = 80, Height = 26, BackColor = Cfg.TextColor, FlatStyle = FlatStyle.Flat };
        bColor.Click += (s, e) => { using (var cd = new ColorDialog { Color = bColor.BackColor }) if (cd.ShowDialog(this) == DialogResult.OK) bColor.BackColor = cd.Color; };
        colorRow.Controls.Add(bColor); flow.Controls.Add(colorRow);
        var nText = num("Text size", 10, 30, (decimal)Cfg.TextSize);
        var nIcon = num("Icon size", 10, 32, (decimal)Cfg.IconSize);
        var nSpace = num("Space between devices", 4, 60, Cfg.Spacing);

        flow.Controls.Add(titulo("Position"));
        var nX = num("Move left (px)", -500, 1500, Cfg.OffsetX);
        var nY = num("Move down (px)", -20, 20, Cfg.OffsetY);

        flow.Controls.Add(titulo("Behavior"));
        var nLow = num("Low battery warning at (%)", 0, 100, Cfg.LowThreshold);
        var nRef = num("Refresh every (seconds)", 5, 600, Cfg.RefreshSeconds);
        var cNot = chk("Show low battery notifications", Cfg.Notifications);
        var cStart = chk("Start with Windows", Cfg.StartWithWindows);

        var devs = devices.Concat(Cfg.Hidden).Distinct().ToList();
        CheckedListBox lista = null;
        if (devs.Count > 0)
        {
            flow.Controls.Add(titulo("Devices (uncheck to hide)"));
            lista = new CheckedListBox { Width = 390, Height = Math.Min(140, 22 * devs.Count + 6), CheckOnClick = true, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, BorderStyle = BorderStyle.None };
            foreach (var d in devs) lista.Items.Add(d, !Cfg.Hidden.Contains(d));
            flow.Controls.Add(lista);
        }

        var botones = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Width = 390, Margin = new Padding(0, 16, 0, 0) };
        var ok = new Button { Text = "Save", Width = 90, Height = 30, BackColor = Color.FromArgb(0, 120, 212), FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Cancel", Width = 90, Height = 30, FlatStyle = FlatStyle.Flat };
        var reset = new Button { Text = "Reset", Width = 90, Height = 30, FlatStyle = FlatStyle.Flat };
        botones.Controls.AddRange(new Control[] { ok, cancel, reset });
        flow.Controls.Add(botones);
        AcceptButton = ok; CancelButton = cancel;

        cancel.Click += (s, e) => Close();
        reset.Click += (s, e) =>
        {
            cIcons.Checked = cPct.Checked = cColor.Checked = cAnim.Checked = cNot.Checked = true;
            bColor.BackColor = Color.White; nText.Value = 16; nIcon.Value = 20; nSpace.Value = 18; nX.Value = 0; nY.Value = 0; nLow.Value = 20; nRef.Value = 30;
            if (lista != null) for (int i = 0; i < lista.Items.Count; i++) lista.SetItemChecked(i, true);
        };
        ok.Click += (s, e) =>
        {
            Cfg.ShowIcons = cIcons.Checked; Cfg.ShowPercent = cPct.Checked; Cfg.ColorByLevel = cColor.Checked; Cfg.Animate = cAnim.Checked;
            Cfg.TextColor = bColor.BackColor; Cfg.TextSize = (float)nText.Value; Cfg.IconSize = (float)nIcon.Value; Cfg.Spacing = (int)nSpace.Value;
            Cfg.OffsetX = (int)nX.Value; Cfg.OffsetY = (int)nY.Value; Cfg.LowThreshold = (int)nLow.Value; Cfg.RefreshSeconds = (int)nRef.Value;
            Cfg.Notifications = cNot.Checked; Cfg.SetStartWithWindows(cStart.Checked);
            if (lista != null) { Cfg.Hidden.Clear(); for (int i = 0; i < lista.Items.Count; i++) if (!lista.GetItemChecked(i)) Cfg.Hidden.Add((string)lista.Items[i]); }
            Cfg.Save();
            if (Applied != null) Applied();
            Close();
        };
    }
}
