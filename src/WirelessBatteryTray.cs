// Wireless Battery Tray - shows wireless peripheral battery levels on the Windows taskbar
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class Dispositivo { public string Nombre, Tipo, Etiqueta; public int Nivel; }

class Fila { public string Nombre, Glifo, Etiqueta; public int Mostrado, Objetivo; }

class Overlay : Form
{
    const int SeparacionPx = 6;
    static int UmbralAviso { get { return Cfg.LowThreshold; } }
    List<Dispositivo> ultimos = new List<Dispositivo>();
    System.Windows.Forms.Timer refresco;
    SettingsForm ajustes;

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string n);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("xinput1_4.dll")] static extern int XInputGetBatteryInformation(int user, byte devType, out XBat info);
    struct XBat { public byte Tipo, Nivel; }
    // SetupAPI para leer la bateria que Windows guarda de dispositivos Bluetooth
    [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(IntPtr g, string e, IntPtr h, uint f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint i, ref SP_DEVINFO_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA d, ref DEVPROPKEY k, out uint t, byte[] buf, uint size, out uint req, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA d, StringBuilder id, int size, out int req);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pt, ref SIZE sz, IntPtr src, ref POINT ps, int key, ref BLEND b, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    struct POINT { public int x, y; } struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte op, flags, alpha, format; }
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    struct RECT { public int Left, Top, Right, Bottom; }

    static readonly Color Fondo = Color.FromArgb(28, 28, 28);
    readonly List<Fila> orden = new List<Fila>();
    readonly Dictionary<string, Fila> filas = new Dictionary<string, Fila>();
    readonly Dictionary<string, bool> avisado = new Dictionary<string, bool>();
    readonly ToolTip tip = new ToolTip();
    readonly NotifyIcon aviso = new NotifyIcon { Icon = SystemIcons.Warning };
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly string fuenteIconos;
    bool consultando;

    [STAThread]
    static void Main()
    {
        // cerrar cualquier otra version que este corriendo
        var yo = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
            try { if (p.Id != yo.Id && (p.ProcessName.StartsWith("WirelessBatteryTray", StringComparison.OrdinalIgnoreCase) || p.ProcessName.StartsWith("BateriaEnBandeja", StringComparison.OrdinalIgnoreCase)) && p.ProcessName.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) < 0) { p.Kill(); p.WaitForExit(3000); } } catch { }
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.ThreadException += (s, e) => Fallo(e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Fallo(e.ExceptionObject as Exception);
            Cfg.Load();
            // one overlay per selected monitor's taskbar (main monitor if none selected)
            var barras = Barras();
            var elegidas = barras.Where(b => Cfg.Monitors.Count == 0 ? b.Item3 : Cfg.Monitors.Contains(b.Item2)).ToList();
            if (elegidas.Count == 0) elegidas.Add(barras.First(b => b.Item3));
            Overlay lector = null;
            foreach (var b in elegidas)
            {
                var o = new Overlay(b.Item1, lector == null, b.Item3);
                if (lector == null) lector = o; else o.Show();
            }
            Application.Run(lector);
        }
    }

    static void Fallo(Exception ex)
    {
        try { MessageBox.Show("Wireless Battery Tray " + Version + " crashed:\n\n" + ex, "Wireless Battery Tray", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
    }
    static string Version { get { var n = System.IO.Path.GetFileNameWithoutExtension(Application.ExecutablePath); return n.StartsWith("WirelessBatteryTray-") ? n.Substring(20) : n; } }

    // taskbars: (handle, monitor device name, is main taskbar)
    public static List<Tuple<IntPtr, string, bool>> Barras()
    {
        var res = new List<Tuple<IntPtr, string, bool>>();
        var p = FindWindow("Shell_TrayWnd", null);
        if (p != IntPtr.Zero) res.Add(Tuple.Create(p, Screen.FromHandle(p).DeviceName, true));
        for (var h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null); h != IntPtr.Zero; h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null))
            res.Add(Tuple.Create(h, Screen.FromHandle(h).DeviceName, false));
        return res;
    }

    // one overlay per taskbar; only the main one reads batteries and shows notifications
    static readonly List<Overlay> todas = new List<Overlay>();
    readonly PosMon pos;
    readonly IntPtr barra; readonly bool principal, barraPrincipal;   // principal = reads batteries; barraPrincipal = sits on the main taskbar

    Overlay(IntPtr barra, bool principal, bool barraPrincipal)
    {
        this.barra = barra; this.principal = principal; this.barraPrincipal = barraPrincipal; pos = Cfg.PosDe(Screen.FromHandle(barra).DeviceName); todas.Add(this);
        fuenteIconos = new System.Drawing.Text.InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons")
            ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(1, 1); Size = new Size(1, 1);
        

        Cfg.Load();
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColores());
        menu.ForeColor = Color.White; menu.ShowImageMargin = false; menu.Font = new Font("Segoe UI", 9.5f);
        menu.Items.Add(new ToolStripMenuItem("Wireless Battery Tray " + Version) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh now", null, (s, e) => Consultar());
        menu.Items.Add("Settings...", null, (s, e) => AbrirAjustes());
        menu.Items.Add("Reset position", null, (s, e) => { pos.OffsetX = 0; Cfg.Save(); Posicionar(); Dibujar(); });
        var lado = new ToolStripMenuItem("Move to left side");
        lado.Click += (s, e) => { pos.LeftSide = !pos.LeftSide; pos.OffsetX = 0; Cfg.Save(); Posicionar(); Dibujar(); };
        menu.Items.Add(lado);
        var bloqueo = new ToolStripMenuItem("Lock position");
        bloqueo.Click += (s, e) => { pos.Locked = !pos.Locked; Cfg.Save(); };
        menu.Items.Add(bloqueo);
        var inicio = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        inicio.Click += (s, e) => Cfg.SetStartWithWindows(inicio.Checked);
        menu.Items.Add(inicio);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => { aviso.Dispose(); Application.Exit(); });
        menu.Opening += (s, e) => { inicio.Checked = Cfg.StartWithWindows; bloqueo.Text = pos.Locked ? "Unlock position" : "Lock position"; lado.Text = pos.LeftSide ? "Move to right side" : "Move to left side"; };
        menu.Closed += (s, e) => { abierto = false; menuCerrado = DateTime.Now; Dibujar(); };
        cierreMenu = new System.Windows.Forms.Timer { Interval = 30 };
        cierreMenu.Tick += (s, e) => VigilarClicFuera();

        var anim = new System.Windows.Forms.Timer { Interval = 60 };
        anim.Tick += (s, e) => { foreach (var f in filas.Values) if (f.Mostrado >= 0 && f.Mostrado != f.Objetivo) { f.Mostrado += Math.Sign(f.Objetivo - f.Mostrado); Pintar(f); } };
        anim.Start();

        // revisa cada 50 ms si la barra quedo encima; solo en ese caso vuelve a subir (sin redibujar de mas)
        var top = new System.Windows.Forms.Timer { Interval = 50 };
        top.Tick += (s, e) =>
        {
            Duenio();
            bool tapada = BarraOculta();
            if (tapada != oculto) { oculto = tapada; ShowWindow(Handle, tapada ? 0 : 8); if (!tapada) { Posicionar(); Dibujar(); } }   // SW_HIDE / SW_SHOWNA
            if (!oculto && BarraEncima()) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
        };
        top.Start();

        refresco = new System.Windows.Forms.Timer { Interval = Cfg.RefreshSeconds * 1000 };
        refresco.Tick += (s, e) => Consultar();
        if (principal) refresco.Start();

        MouseEnter += (s, e) => { hover = true; Dibujar(); };
        MouseLeave += (s, e) => { hover = false; Dibujar(); };
        Cursor = Cursors.Hand;
        MouseDown += (s, e) => { if (e.Button != MouseButtons.Left) return; presionado = true; arrastrando = false; inicioArrastre = Cursor.Position; offsetInicial = pos.OffsetX; Dibujar(); };
        MouseMove += (s, e) =>
        {
            if (!presionado || pos.Locked) return;
            var c = Cursor.Position;
            int d = vertical ? c.Y - inicioArrastre.Y : c.X - inicioArrastre.X;
            if (!arrastrando && Math.Abs(d) < 5) return;
            arrastrando = true; Cursor = vertical ? Cursors.SizeNS : Cursors.SizeWE;
            pos.OffsetX = Math.Max(0, (pos.LeftSide && !vertical) ? offsetInicial + d : offsetInicial - d); Posicionar(); Dibujar();
        };
        MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) { if (menu.Visible) menu.Close(); else AbrirMenu(); return; }
            if (e.Button != MouseButtons.Left) return; presionado = false;
            if (arrastrando) { arrastrando = false; Cursor = Cursors.Hand; Cfg.Save(); Posicionar(); Dibujar(); return; }
            if (menu.Visible || (DateTime.Now - menuCerrado).TotalMilliseconds < 200) { menu.Close(); Dibujar(); } else AbrirMenu(); };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) => { if (IsHandleCreated) BeginInvoke(new Action(Dibujar)); };
        Shown += (s, e) => { Duenio(); if (principal) Consultar(); else if (todas[0].ultimos.Count > 0) Mostrar(new List<Dispositivo>(todas[0].ultimos)); };
    }

    // ventana de herramienta que no roba el foco ni aparece en Alt+Tab
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000 | 0x8 | 0x80000; return cp; }
    }
    protected override bool ShowWithoutActivation { get { return true; } }

    bool oculto;

    // hide while the taskbar can't be seen: a full-screen app on this monitor, or an auto-hidden taskbar
    bool BarraOculta()
    {
        RECT tb; if (!GetWindowRect(barra, out tb) || !IsWindowVisible(barra)) return true;
        var mon = Screen.FromHandle(barra).Bounds;
        int visibleAlto = Math.Min(tb.Bottom, mon.Bottom) - Math.Max(tb.Top, mon.Top), visibleAncho = Math.Min(tb.Right, mon.Right) - Math.Max(tb.Left, mon.Left);
        if (visibleAlto < 8 || visibleAncho < 8) return true;   // auto-hide taskbar slid off screen

        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Handle || fg == barra) return false;
        var cls = new StringBuilder(64); GetClassName(fg, cls, 64);
        string c = cls.ToString();
        if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
        RECT w; if (!GetWindowRect(fg, out w)) return false;
        return Screen.FromHandle(fg).DeviceName == Screen.FromHandle(barra).DeviceName
            && w.Left <= mon.Left && w.Top <= mon.Top && w.Right >= mon.Right && w.Bottom >= mon.Bottom;
    }

    IntPtr duenioActual = IntPtr.Zero;
    void Duenio()
    {
        var tb = barra;
        if (tb != IntPtr.Zero && tb != duenioActual) { SetWindowLongPtr(Handle, -8, tb); duenioActual = tb; }
    }

    // true si alguna ventana del Explorador (barra de tareas y sus partes) quedo encima del texto
    bool BarraEncima()
    {
        if (duenioActual == IntPtr.Zero) return false;
        uint pidExplorer; GetWindowThreadProcessId(duenioActual, out pidExplorer);
        RECT yo; GetWindowRect(Handle, out yo);
        for (var w = GetWindow(Handle, 3); w != IntPtr.Zero; w = GetWindow(w, 3))
        {
            if (w == duenioActual) return true;
            if (!IsWindowVisible(w)) continue;
            uint pid; GetWindowThreadProcessId(w, out pid);
            if (pid != pidExplorer) continue;
            RECT r; if (!GetWindowRect(w, out r)) continue;
            if (r.Left < yo.Right && r.Right > yo.Left && r.Top < yo.Bottom && r.Bottom > yo.Top) return true;
        }
        return false;
    }

    void Consultar()
    {
        if (!principal) { todas[0].Consultar(); return; }
        if (consultando) return;
        consultando = true;
        Task.Run(() => { var l = new List<Dispositivo>(); try { l.AddRange(LeerGHub()); } catch { } try { l.AddRange(LeerXbox()); } catch { } try { l.AddRange(Corsair.Leer()); } catch { } try { l.AddRange(HyperX.Leer()); } catch { } try { foreach (var b in LeerBluetooth()) if (!l.Any(x => x.Nombre == b.Nombre)) l.Add(b); } catch { } return l; })
            .ContinueWith(t => { consultando = false; foreach (var o in todas.ToList()) if (!o.IsDisposed) o.Mostrar(new List<Dispositivo>(t.Result)); }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ---- Bluetooth generico (teclados, mouses, auriculares de cualquier marca) ----
    static List<Dispositivo> LeerBluetooth()
    {
        var res = new List<Dispositivo>();
        var kBat = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
        var kNom = new DEVPROPKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
        var kDesc = new DEVPROPKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 2 };
        IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 0x2 | 0x4);   // PRESENT | ALLCLASSES
        if (set == new IntPtr(-1)) return res;
        try
        {
            var d = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
            var id = new StringBuilder(512); var buf = new byte[1024];
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref d); i++)
            {
                int rq; if (!SetupDiGetDeviceInstanceIdW(set, ref d, id, id.Capacity, out rq)) continue;
                string iid = id.ToString().ToUpperInvariant();
                if (!(iid.StartsWith("BTHENUM") || iid.StartsWith("BTHLE"))) continue;
                uint t, req;
                if (!SetupDiGetDevicePropertyW(set, ref d, ref kBat, out t, buf, (uint)buf.Length, out req, 0) || req < 1) continue;
                int nivel = buf[0];
                if (nivel > 100) continue;
                string nombre = null;
                if (SetupDiGetDevicePropertyW(set, ref d, ref kNom, out t, buf, (uint)buf.Length, out req, 0) || SetupDiGetDevicePropertyW(set, ref d, ref kDesc, out t, buf, (uint)buf.Length, out req, 0))
                    nombre = Encoding.Unicode.GetString(buf, 0, (int)Math.Max(0, req - 2));
                if (string.IsNullOrEmpty(nombre) || res.Any(x => x.Nombre == nombre)) continue;
                res.Add(new Dispositivo { Nombre = nombre, Tipo = "", Nivel = nivel });
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return res;
    }

    // ---- Joysticks de Xbox (XInput): Windows da 4 niveles ----
    static List<Dispositivo> LeerXbox()
    {
        var res = new List<Dispositivo>();
        string[] et = { "Empty", "Low", "Medium", "Full" };
        int[] niv = { 0, 15, 50, 100 };
        for (int i = 0; i < 4; i++)
        {
            XBat b;
            if (XInputGetBatteryInformation(i, 0, out b) != 0) continue;
            if (b.Tipo == 0 || b.Tipo == 1 || b.Nivel > 3) continue;   // desconectado o con cable
            res.Add(new Dispositivo { Nombre = "Xbox Controller" + (i > 0 ? " " + (i + 1) : ""), Tipo = "GAMEPAD", Nivel = niv[b.Nivel], Etiqueta = et[b.Nivel] });
        }
        return res;
    }

    // ---- G HUB: websocket local en ws://localhost:9010 ----
    static List<Dispositivo> LeerGHub()
    {
        var res = new List<Dispositivo>();
        var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        using (var ws = new ClientWebSocket())
        {
            ws.Options.AddSubProtocol("json");
            ws.Options.SetRequestHeader("Origin", "file://");
            if (!ws.ConnectAsync(new Uri("ws://localhost:9010"), CancellationToken.None).Wait(3000)) return res;
            var lista = Pedir(ws, js, "/devices/list");
            var payload = Obj(lista, "payload");
            var infos = payload != null && payload.ContainsKey("deviceInfos") ? payload["deviceInfos"] as System.Collections.ArrayList : null;
            if (infos == null) return res;
            foreach (Dictionary<string, object> d in infos)
            {
                var cap = Obj(d, "capabilities");
                if (cap == null || !cap.ContainsKey("hasBatteryStatus") || !(cap["hasBatteryStatus"] is bool) || !(bool)cap["hasBatteryStatus"]) continue;
                var b = Obj(Pedir(ws, js, "/battery/" + d["id"] + "/state"), "payload");
                if (b == null || !b.ContainsKey("percentage") || b["percentage"] == null) continue;
                string nombre = Str(d, "extendedDisplayName") ?? Str(d, "displayName") ?? "Device";
                res.Add(new Dispositivo { Nombre = nombre, Tipo = Str(d, "deviceType") ?? "", Nivel = Convert.ToInt32(b["percentage"]) });
            }
        }
        return res;
    }

    static Dictionary<string, object> Pedir(ClientWebSocket ws, JavaScriptSerializer js, string path)
    {
        var req = Encoding.UTF8.GetBytes(js.Serialize(new { msgId = "", verb = "GET", path = path }));
        ws.SendAsync(new ArraySegment<byte>(req), WebSocketMessageType.Text, true, CancellationToken.None).Wait(3000);
        var fin = DateTime.Now.AddSeconds(3);
        var buf = new byte[65536];
        while (DateTime.Now < fin)
        {
            var ms = new System.IO.MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                var t = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                if (!t.Wait(3000)) return null;
                r = t.Result; ms.Write(buf, 0, r.Count);
            } while (!r.EndOfMessage);
            var obj = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(ms.ToArray()));
            if (Str(obj, "path") == path) return obj;
        }
        return null;
    }

    static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
    { return d != null && d.ContainsKey(k) ? d[k] as Dictionary<string, object> : null; }
    static string Str(Dictionary<string, object> d, string k)
    { return d != null && d.ContainsKey(k) && d[k] != null && d[k].ToString() != "" ? d[k].ToString() : null; }

    // ---- interfaz ----
    static string Glifo(Dispositivo d)
    {
        string t = (d.Tipo + " " + d.Nombre).ToUpperInvariant();
        if (t.Contains("GAMEPAD") || t.Contains("JOYSTICK")) return "\uE7FC";
        if (t.Contains("HEADSET") || t.Contains("AUDIO") || t.Contains("A50") || t.Contains("A40") || t.Contains("AURICULAR") || t.Contains("BUDS") || t.Contains("AIRPODS") || t.Contains("PHONES") || t.Contains("WH-") || t.Contains("WF-")) return "";
        if (t.Contains("MOUSE") || t.Contains("SUPERLIGHT") || t.Contains("RATON") || t.Contains("MX MASTER") || t.Contains("MX ANYWHERE")) return "";
        if (t.Contains("KEYBOARD") || t.Contains("TKL") || t.Contains("TECLADO") || t.Contains("KEYS") || t.Contains(" KB") || t.Contains("K380") || t.Contains("MX KEYS")) return "";
        return "";
    }

    // same text color as the taskbar clock: white on a dark taskbar, black on a light one
    static Color ColorBarra()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                var v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                if (v is int && (int)v == 1) return Color.Black;
            }
        }
        catch { }
        return Color.White;
    }

    Color ColorNivel(int n)
    {
        var baseC = ColorBarra();
        if (n < 0) return Color.FromArgb(150, baseC);
        if (pos.ColorByLevel && n <= UmbralAviso) return baseC == Color.Black ? Color.FromArgb(196, 43, 28) : Color.FromArgb(255, 99, 88);
        return baseC;
    }

    void Mostrar(List<Dispositivo> datos)
    {
        ultimos = new List<Dispositivo>(datos);
        datos = datos.Where(d => !pos.Hidden.Contains(d.Nombre)).ToList();
        if (datos.Count == 0) datos.Add(new Dispositivo { Nombre = "Open G HUB", Tipo = "", Nivel = -1 });
        foreach (var n in filas.Keys.ToList())
            if (!datos.Any(d => d.Nombre == n)) { orden.Remove(filas[n]); filas.Remove(n); }
        foreach (var d in datos)
        {
            Fila f;
            if (!filas.TryGetValue(d.Nombre, out f))
            {
                f = new Fila { Nombre = d.Nombre, Glifo = Glifo(d), Mostrado = d.Nivel, Objetivo = d.Nivel };
                filas[d.Nombre] = f; orden.Add(f);
            }
            f.Objetivo = d.Nivel; f.Etiqueta = d.Etiqueta;
            if (d.Nivel < 0 || f.Mostrado < 0 || d.Etiqueta != null || !pos.Animate) f.Mostrado = d.Nivel;

            bool ya; avisado.TryGetValue(d.Nombre, out ya);
            if (principal && Cfg.Notifications && d.Nivel >= 0 && d.Nivel <= UmbralAviso && !ya)
            {
                aviso.Visible = true;
                aviso.ShowBalloonTip(8000, "Low battery", d.Nombre + (d.Etiqueta != null ? " battery is " + d.Etiqueta.ToLower() : " is at " + d.Nivel + "%") + ". Time to charge it!", ToolTipIcon.Warning);
                avisado[d.Nombre] = true;
            }
            else if (d.Nivel > UmbralAviso + 10) avisado[d.Nombre] = false;
        }
        tip.SetToolTip(this, string.Join("\n", orden.Select(f => f.Nombre + ": " + (f.Etiqueta ?? (f.Objetivo < 0 ? "--" : f.Objetivo + "%")))));
        Reajustar();
    }

    void Pintar(Fila f) { Reajustar(); }

    // ---- dibujo: cada simbolo y numero se centra por su contorno real ----
    float TamIcono { get { return pos.IconSize; } } float TamTexto { get { return pos.TextSize; } } float EspacioEntre { get { return pos.Spacing; } }
    const float EspacioIconoTexto = 7f, PadIzq = 8f, PadDer = 8f;
    string TextoDe(Fila f) { return f.Etiqueta ?? (f.Mostrado < 0 ? "--" : f.Mostrado + (pos.ShowPercent ? "%" : "")); }

    static System.Drawing.Drawing2D.GraphicsPath Ruta(string txt, string fuente, float px)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddString(txt, new FontFamily(fuente), 0, px, PointF.Empty, StringFormat.GenericTypographic);
        return p;
    }

    IEnumerable<Tuple<string, string, float, float>> Partes(Fila f)
    {
        if (pos.ShowIcons) yield return Tuple.Create(f.Glifo, fuenteIconos, TamIcono, EspacioIconoTexto);
        yield return Tuple.Create(TextoDe(f), "Segoe UI", TamTexto, EspacioEntre);
    }

    // horizontal taskbar: total width; vertical taskbar: total height (icon above number, stacked)
    float LargoTotal()
    {
        float w = 0;
        foreach (var f in orden)
            foreach (var par in Partes(f))
                using (var p = Ruta(par.Item1, par.Item2, par.Item3))
                {
                    var b = p.GetBounds();
                    w += (vertical ? b.Height + (par.Item4 == EspacioIconoTexto ? 5 : 14) : b.Width + par.Item4);
                }
        return Math.Max(1, w - (vertical ? 14 : EspacioEntre) + PadIzq + PadDer);
    }

    void Dibujar()
    {
        if (!IsHandleCreated || Width < 2) return;
        using (var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                // fondo casi invisible (alfa 1) para que toda la zona reciba clics; al pasar el mouse, resaltado tipo boton
                var fondo = (presionado || abierto) ? Color.FromArgb(18, 255, 255, 255) : hover ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(1, 0, 0, 0);
                using (var br = new SolidBrush(fondo))
                using (var path = Redondeado(vertical ? new RectangleF(4, 0, Width - 9, Height - 1) : new RectangleF(0, 4, Width - 1, Height - 9), 5))
                    g.FillPath(br, path);
                float pos = PadIzq, cx = Width / 2f, cy = Height / 2f;
                foreach (var f in orden)
                using (var br = new SolidBrush(ColorNivel(f.Mostrado)))
                {
                    foreach (var par in Partes(f))
                    using (var p = Ruta(par.Item1, par.Item2, par.Item3))
                    {
                        var bb = p.GetBounds();
                        var m = new System.Drawing.Drawing2D.Matrix();
                        if (vertical)
                        {
                            m.Translate((float)Math.Round(cx - bb.Width / 2f) - bb.Left, pos - bb.Top);
                            pos += bb.Height + (par.Item4 == EspacioIconoTexto ? 5 : 14);
                        }
                        else
                        {
                            m.Translate(pos - bb.Left, (float)Math.Round(cy - bb.Height / 2f) - bb.Top);
                            pos += bb.Width + par.Item4;
                        }
                        p.Transform(m); g.FillPath(br, p);
                    }
                }
            }
            IntPtr sdc = GetDC(IntPtr.Zero), mdc = CreateCompatibleDC(sdc), hb = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mdc, hb);
            var sz = new SIZE { cx = Width, cy = Height }; var ps = new POINT(); var pt = new POINT { x = Left, y = Top };
            var bl = new BLEND { op = 0, flags = 0, alpha = 255, format = 1 };
            UpdateLayeredWindow(Handle, sdc, ref pt, ref sz, mdc, ref ps, 0, ref bl, 2);
            SelectObject(mdc, old); DeleteObject(hb); DeleteDC(mdc); ReleaseDC(IntPtr.Zero, sdc);
        }
    }

    static System.Drawing.Drawing2D.GraphicsPath Redondeado(RectangleF r, float rad)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath(); float d = rad * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    int altoBarra = 48, anchoBarra = 48; bool vertical, hover, presionado, abierto, arrastrando; DateTime menuCerrado;
    Point inicioArrastre; int offsetInicial;

    // read taskbar rectangle and orientation (left/right taskbars are vertical)
    bool LeerBarra(out RECT r)
    {
        var tb = barra;
        if (tb == IntPtr.Zero || !GetWindowRect(tb, out r)) { r = new RECT(); return false; }
        altoBarra = r.Bottom - r.Top; anchoBarra = r.Right - r.Left; vertical = altoBarra > anchoBarra;
        return true;
    }

    void AbrirMenu()
    {
        abierto = true; Dibujar();
        // open the menu above the button, aligned to its right edge, like the system tray flyouts
        SetForegroundWindow(Handle);
        var ps = menu.PreferredSize; var scr = Screen.FromControl(this).Bounds;
        Point donde;
        if (!vertical) donde = Top > scr.Top + scr.Height / 2 ? new Point(Width - ps.Width, -ps.Height - 6) : new Point(Width - ps.Width, Height + 6);   // bottom or top taskbar
        else donde = Left < scr.Left + scr.Width / 2 ? new Point(Width + 6, Height - ps.Height) : new Point(-ps.Width - 6, Height - ps.Height);   // left or right taskbar
        menu.Show(this, donde);
        cierreMenu.Start();
    }

    // closes the menu when the user clicks anywhere outside it (the window never takes focus, so Windows won't do it for us)
    System.Windows.Forms.Timer cierreMenu;
    void VigilarClicFuera()
    {
        if (!menu.Visible) { cierreMenu.Stop(); return; }
        if (Control.MouseButtons != MouseButtons.None && !menu.Bounds.Contains(Cursor.Position) && !Bounds.Contains(Cursor.Position))
        { menu.Close(ToolStripDropDownCloseReason.AppClicked); cierreMenu.Stop(); }
    }

    void AbrirAjustes()
    {
        if (ajustes != null && !ajustes.IsDisposed) { ajustes.Activate(); return; }
        ajustes = new SettingsForm(todas[0].ultimos.Select(d => d.Nombre).Where(n => n != "Open G HUB"), pos, Screen.FromHandle(barra).DeviceName);
        ajustes.Applied += () => { todas[0].refresco.Interval = Cfg.RefreshSeconds * 1000; foreach (var o in todas) o.Mostrar(new List<Dispositivo>(todas[0].ultimos)); };
        ajustes.Show();
    }
    void Reajustar()
    {
        RECT r; LeerBarra(out r);
        int largo = (int)Math.Ceiling(LargoTotal());
        var nuevo = vertical ? new Size(anchoBarra, largo) : new Size(largo, altoBarra);
        if (Size != nuevo) Size = nuevo;
        Posicionar();
        Dibujar();
    }

    // Allowed horizontal range: always keep a 25 px gap (the default one) from the tray icons,
    // the pinned app buttons and the screen edge. App buttons are found through UI Automation.
    const int Margen = 25;
    int appsIzq = -1, appsDer = -1, widgetsDer = -1, relojIzq = -1; DateTime appsLeidos; bool leyendoApps;
    void Limites(RECT r, int trayLeft, out int minX, out int maxX)
    {
        // UI Automation is slow: refresh the cached button positions in the background
        if (!leyendoApps && (DateTime.Now - appsLeidos).TotalSeconds > (arrastrando ? 2 : 20))
        {
            leyendoApps = true; appsLeidos = DateTime.Now;
            var rr = r; int tl = barraPrincipal ? trayLeft : r.Right;
            Task.Run(() => LeerApps(rr, tl)).ContinueWith(t => { leyendoApps = false; Posicionar(); Dibujar(); }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (pos.LeftSide)
        {
            minX = (widgetsDer > 0 ? widgetsDer : r.Left) + Margen;
            maxX = (appsIzq > 0 ? appsIzq : (r.Left + r.Right) / 2) - Margen - Width;
        }
        else
        {
            maxX = trayLeft - Width - SeparacionPx;
            minX = appsDer > 0 ? appsDer + Margen : r.Left + Margen;
        }
        if (maxX < minX) maxX = minX;
    }

    void LeerApps(RECT r, int trayLeft)
    {
        try
        {
            var asm = System.Reflection.Assembly.Load("UIAutomationClient, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            var tipos = System.Reflection.Assembly.Load("UIAutomationTypes, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            var aeT = asm.GetType("System.Windows.Automation.AutomationElement");
            object raiz = aeT.GetMethod("FromHandle").Invoke(null, new object[] { barra });
            object cond = asm.GetType("System.Windows.Automation.Condition").GetField("TrueCondition").GetValue(null);
            object scope = Enum.ToObject(tipos.GetType("System.Windows.Automation.TreeScope"), 4);   // Descendants
            var todos = (System.Collections.IEnumerable)aeT.GetMethod("FindAll").Invoke(raiz, new[] { scope, cond });
            var botones = new List<Tuple<int, int, string>>();
            foreach (object el in todos)
            {
                object cur = el.GetType().GetProperty("Current").GetValue(el, null);
                object ct = cur.GetType().GetProperty("ControlType").GetValue(cur, null);
                string prog = (string)ct.GetType().GetProperty("ProgrammaticName").GetValue(ct, null);
                bool esTexto = prog == "ControlType.Text";
                if (prog != "ControlType.Button" && prog != "ControlType.ListItem" && prog != "ControlType.MenuItem" && !(esTexto && !barraPrincipal)) continue;
                object rect = cur.GetType().GetProperty("BoundingRectangle").GetValue(cur, null);
                double l = (double)rect.GetType().GetProperty("Left").GetValue(rect, null), w = (double)rect.GetType().GetProperty("Width").GetValue(rect, null);
                if (double.IsInfinity(l) || w <= 0 || w > 200 || l + w > trayLeft || l < r.Left) continue;
                string id = (string)cur.GetType().GetProperty("AutomationId").GetValue(cur, null);
                botones.Add(Tuple.Create((int)l, (int)(l + w), id ?? ""));
            }
            if (botones.Count == 0) return;
            botones.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            // the app cluster starts at the Start button (or the first button) and continues while buttons are adjacent
            int i0 = botones.FindIndex(b => b.Item3 == "StartButton"); if (i0 < 0) i0 = 0;
            int izq = botones[i0].Item1, der = botones[i0].Item2;
            for (int i = i0 + 1; i < botones.Count && botones[i].Item1 - der < 60; i++) der = Math.Max(der, botones[i].Item2);
            int wd = -1; for (int i = 0; i < i0; i++) if (botones[i].Item2 < izq - 20 && botones[i].Item2 < r.Left + 400) wd = Math.Max(wd, botones[i].Item2);   // e.g. the Widgets button on the far left
            appsIzq = izq; appsDer = der; widgetsDer = wd;
            // secondary taskbars: the clock is the right-most group of buttons
            int j = botones.Count - 1;
            if (j > i0 && botones[j].Item1 > der + 60)
            {
                int rl = botones[j].Item1;
                for (int k = j - 1; k > i0 && rl - botones[k].Item2 < 20; k--) rl = Math.Min(rl, botones[k].Item1);
                relojIzq = rl;
            }
        }
        catch { }
    }

    void Posicionar()
    {
        RECT r, rn = new RECT();
        if (!LeerBarra(out r)) return;
        var tn = FindWindowEx(barra, IntPtr.Zero, "TrayNotifyWnd", null);
        bool hayTray = tn != IntPtr.Zero && GetWindowRect(tn, out rn);
        // OffsetX = distance moved along the taskbar (dragging), OffsetY = fine-tune across it
        if (pos.OffsetX < 0) pos.OffsetX = 0;
        if (vertical)
        {
            int trayTop = hayTray && rn.Top > r.Top ? rn.Top : r.Bottom - 330;
            int y = trayTop - Height - SeparacionPx - pos.OffsetX;
            y = Math.Max(r.Top, Math.Min(r.Bottom - Height, y));
            Location = new Point(r.Left + 0, y);
        }
        else
        {
            int trayLeft = hayTray && rn.Left > r.Left ? rn.Left : (!barraPrincipal && relojIzq > 0 ? relojIzq + 2 : r.Right - (barraPrincipal ? 330 : 82));
            int minX, maxX; Limites(r, trayLeft, out minX, out maxX);
            int x = pos.LeftSide ? minX + pos.OffsetX : maxX - pos.OffsetX;
            if (x < minX) x = minX; if (x > maxX) x = maxX;
            pos.OffsetX = pos.LeftSide ? x - minX : maxX - x;
            Location = new Point(x, r.Top + (r.Bottom - r.Top - Height) / 2 + 0);
        }
    }
}

class MenuColores : ProfessionalColorTable
{
    static readonly Color Bg = Color.FromArgb(44, 44, 44), Sel = Color.FromArgb(62, 62, 62);
    public override Color ToolStripDropDownBackground { get { return Bg; } }
    public override Color MenuBorder { get { return Color.FromArgb(70, 70, 70); } }
    public override Color MenuItemSelected { get { return Sel; } }
    public override Color MenuItemBorder { get { return Sel; } }
    public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
    public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
    public override Color SeparatorDark { get { return Color.FromArgb(70, 70, 70); } }
    public override Color SeparatorLight { get { return Bg; } }
    public override Color CheckBackground { get { return Sel; } }
    public override Color CheckSelectedBackground { get { return Sel; } }
    public override Color CheckPressedBackground { get { return Sel; } }
}
