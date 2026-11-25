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

class Dispositivo { public string Nombre, Tipo, Etiqueta, Fuente = ""; public int Nivel; public bool Cargando; }

class Fila { public string Nombre, Glifo, Etiqueta, Fuente = ""; public int Mostrado, Objetivo; public bool Cargando, Saliendo; public float Aparicion; public float Desde, Hasta; public DateTime Actualizado = DateTime.Now; }

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
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
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
            // the main-taskbar overlay always exists (it reads the batteries); the rest are added/removed live
            var barras = Barras();
            var lector = new Overlay(barras.First(b => b.Item3).Item1, true, true);
            lector.Shown += (s, e) => AplicarMonitores();
            Application.Run(lector);
        }
    }

    static bool falloMostrado;
    static void Fallo(Exception ex)
    {
        // show the error only once, then quit cleanly (a repeating timer could otherwise pop it up again and again)
        if (falloMostrado) return; falloMostrado = true;
        try { MessageBox.Show("Wireless Battery Tray " + Version + " crashed:\n\n" + ex, "Wireless Battery Tray", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
        Environment.Exit(1);
    }
    static string Version { get { var n = System.IO.Path.GetFileNameWithoutExtension(Application.ExecutablePath); return n.StartsWith("WirelessBatteryTray-") ? n.Substring(20) : n; } }

    // create/remove overlays to match the monitors selected in Settings, without restarting
    public static void AplicarMonitores()
    {
        var barras = Barras();
        var elegidas = barras.Where(b => Cfg.Monitors.Count == 0 ? b.Item3 : Cfg.Monitors.Contains(b.Item2)).Select(b => b.Item1).ToList();
        if (elegidas.Count == 0) elegidas.Add(barras.First(b => b.Item3).Item1);
        var lector = todas[0];
        lector.activo = elegidas.Contains(lector.barra);
        foreach (var o in todas.Skip(1).ToList())
            if (!elegidas.Contains(o.barra)) { todas.Remove(o); o.Close(); o.Dispose(); }
        foreach (var b in barras)
            if (elegidas.Contains(b.Item1) && !todas.Any(o => o.barra == b.Item1))
            {
                var o = new Overlay(b.Item1, false, b.Item3);
                o.Show();
                if (lector.ultimos.Count > 0) o.Mostrar(new List<Dispositivo>(lector.ultimos));
            }
        foreach (var o in todas) { o.oculto = !o.oculto; }   // force the visibility check to re-apply
    }

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
        menu.Items.Add("Restore factory settings...", null, (s, e) =>
        {
            if (MessageBox.Show("Restore all settings to their defaults?\n\nPositions, monitors, sizes and hidden devices will be reset.", "Wireless Battery Tray", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Cfg.FactoryReset();
            Application.Restart();
        });
        menu.Items.Add("Exit", null, (s, e) => { aviso.Dispose(); Application.Exit(); });
        menu.Opening += (s, e) => { inicio.Checked = Cfg.StartWithWindows; bloqueo.Text = pos.Locked ? "Unlock position" : "Lock position"; lado.Text = pos.LeftSide ? "Move to right side" : "Move to left side"; };
        menu.Closed += (s, e) => { abierto = false; menuCerrado = DateTime.Now; Dibujar(); };
        cierreMenu = new System.Windows.Forms.Timer { Interval = 30 }; relojes.Add(cierreMenu);
        cierreMenu.Tick += (s, e) => VigilarClicFuera();

        var anim = new System.Windows.Forms.Timer { Interval = 33 }; relojes.Add(anim);
        anim.Tick += (s, e) => PasoAnimacion();
        anim.Start();

        // revisa cada 50 ms si la barra quedo encima; solo en ese caso vuelve a subir (sin redibujar de mas)
        var top = new System.Windows.Forms.Timer { Interval = 50 }; relojes.Add(top);
        top.Tick += (s, e) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            Duenio();
            bool tapada = !activo || BarraOculta();
            if (tapada != oculto) { oculto = tapada; opacidadObjetivo = tapada ? 0f : 1f; if (!tapada) { ShowWindow(Handle, 8); Posicionar(); Dibujar(); } }   // fade out, then SW_HIDE / SW_SHOWNA
            // follow the taskbar: it slides in/out (Win key, auto-hide) and games can change the screen resolution
            RECT tbNow;
            if (!oculto && GetWindowRect(barra, out tbNow))
            {
                var scr = Screen.FromHandle(barra).Bounds;
                if (tbNow.Left != tbPrev.Left || tbNow.Top != tbPrev.Top || tbNow.Right != tbPrev.Right || tbNow.Bottom != tbPrev.Bottom || scr != scrPrev)
                { tbPrev = tbNow; scrPrev = scr; Reajustar(); }
                else if ((DateTime.Now - obstLeidos).TotalSeconds >= 1) { var antes = Location; Posicionar(); if (Location != antes) Dibujar(); }
            }
            if (!oculto && BarraEncima()) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
        };
        top.Start();

        refresco = new System.Windows.Forms.Timer { Interval = Cfg.RefreshSeconds * 1000 }; relojes.Add(refresco);
        refresco.Tick += (s, e) => Consultar();
        if (principal) { refresco.Start(); EscucharGHub(); VigilarActualizacion(); }

        MouseEnter += (s, e) => { hover = true; ActualizarTooltip(); };
        MouseLeave += (s, e) => { hover = false; tipActual = null; tip.SetToolTip(this, null); };
        Cursor = Cursors.Hand;
        MouseDown += (s, e) => { if (e.Button != MouseButtons.Left) return; presionado = true; arrastrando = false; inicioArrastre = Cursor.Position; offsetInicial = pos.OffsetX; Dibujar(); };
        MouseMove += (s, e) =>
        {
            // tooltip: just the name of the device under the cursor
            if (!presionado)
            {
                var fh = FilaEn(e.Location);
                string nom = fh == null || fh.Nombre == "Open G HUB" ? null : fh.Nombre;
                if (nom != tipActual) { tipActual = nom; tip.SetToolTip(this, nom); }
            }
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
            if (menu.Visible || (DateTime.Now - menuCerrado).TotalMilliseconds < 200) { menu.Close(); Dibujar(); return; }
            // single click opens the menu; a second click within the double-click time opens the device's app instead
            if (clicPendiente.Enabled) { clicPendiente.Stop(); AbrirAppDe(FilaEn(PointToClient(Cursor.Position)) ?? orden.FirstOrDefault(f => !f.Saliendo)); return; }
            clicPendiente.Start(); };
        clicPendiente = new System.Windows.Forms.Timer { Interval = Math.Max(150, SystemInformation.DoubleClickTime - 100) }; relojes.Add(clicPendiente);
        clicPendiente.Tick += (s, e) => { clicPendiente.Stop(); AbrirMenu(); };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) => { if (IsHandleCreated) BeginInvoke(new Action(Dibujar)); };
        Shown += (s, e) => { Duenio(); if (principal) Consultar(); else if (todas[0].ultimos.Count > 0) Mostrar(new List<Dispositivo>(todas[0].ultimos)); };
    }

    // ventana de herramienta que no roba el foco ni aparece en Alt+Tab
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000 | 0x8 | 0x80000; return cp; }
    }
    protected override bool ShowWithoutActivation { get { return true; } }

    bool oculto, activo = true;
    RECT tbPrev; Rectangle scrPrev;
    readonly List<System.Windows.Forms.Timer> relojes = new List<System.Windows.Forms.Timer>();
    protected override void OnFormClosed(FormClosedEventArgs e) { foreach (var t in relojes) { t.Stop(); t.Dispose(); } base.OnFormClosed(e); }

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
                res.Add(new Dispositivo { Nombre = nombre, Tipo = "", Nivel = nivel, Fuente = "bt" });
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
            res.Add(new Dispositivo { Nombre = "Xbox Controller" + (i > 0 ? " " + (i + 1) : ""), Tipo = "GAMEPAD", Nivel = niv[b.Nivel], Etiqueta = et[b.Nivel], Fuente = "xbox" });
        }
        return res;
    }

    // ---- G HUB: websocket local en ws://localhost:9010 ----
    // the installer writes the new exe name to current.txt; if it isn't us, start it (it closes this one)
    void VigilarActualizacion()
    {
        string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
        string yo = System.IO.Path.GetFileName(Application.ExecutablePath);
        var t = new System.Windows.Forms.Timer { Interval = 3000 };
        t.Tick += (s, e) =>
        {
            try
            {
                string f = System.IO.Path.Combine(dir, "current.txt");
                if (!System.IO.File.Exists(f)) return;
                string actual = System.IO.File.ReadAllText(f).Trim();
                if (actual == "" || string.Equals(actual, yo, StringComparison.OrdinalIgnoreCase)) return;
                string ruta = System.IO.Path.Combine(dir, actual);
                if (!System.IO.File.Exists(ruta)) return;
                t.Stop();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true, WorkingDirectory = dir });
            }
            catch { }
        };
        t.Start();
    }

    // G HUB pushes events when a device connects/disconnects or its battery changes: refresh right away
    void EscucharGHub()
    {
        var hilo = new Thread(() =>
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            while (!IsDisposed)
            {
                try
                {
                    using (var ws = new ClientWebSocket())
                    {
                        ws.Options.AddSubProtocol("json");
                        ws.Options.SetRequestHeader("Origin", "file://");
                        if (!ws.ConnectAsync(new Uri("ws://localhost:9010"), CancellationToken.None).Wait(3000)) { Thread.Sleep(15000); continue; }
                        foreach (var path in new[] { "/devices/state/changed", "/battery/state/changed", "/devices/list/changed" })
                        {
                            var req = Encoding.UTF8.GetBytes(js.Serialize(new { msgId = "", verb = "SUBSCRIBE", path = path }));
                            ws.SendAsync(new ArraySegment<byte>(req), WebSocketMessageType.Text, true, CancellationToken.None).Wait(3000);
                        }
                        var buf = new byte[65536];
                        while (ws.State == WebSocketState.Open && !IsDisposed)
                        {
                            var ms = new System.IO.MemoryStream(); WebSocketReceiveResult r;
                            do { r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result; ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
                            if (r.MessageType == WebSocketMessageType.Close) break;
                            string txt = Encoding.UTF8.GetString(ms.ToArray());
                            if (txt.Contains("changed") || txt.Contains("\"verb\":\"EVENT\""))
                                try { BeginInvoke(new Action(ConsultarPronto)); } catch { }
                        }
                    }
                }
                catch { }
                Thread.Sleep(5000);
            }
        }) { IsBackground = true };
        hilo.Start();
    }

    // several events can arrive together, and G HUB may need a moment to report the new level
    System.Windows.Forms.Timer pronto;
    void ConsultarPronto()
    {
        if (pronto == null) { pronto = new System.Windows.Forms.Timer { Interval = 1500 }; pronto.Tick += (s, e) => { pronto.Stop(); Consultar(); }; }
        pronto.Stop(); pronto.Start();
    }

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
                res.Add(new Dispositivo { Nombre = nombre, Tipo = Str(d, "deviceType") ?? "", Nivel = Convert.ToInt32(b["percentage"]), Fuente = "ghub", Cargando = b.ContainsKey("charging") && b["charging"] is bool && (bool)b["charging"] });
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

    readonly Dictionary<string, bool> avisadoLleno = new Dictionary<string, bool>();

    void Mostrar(List<Dispositivo> datos)
    {
        ultimos = new List<Dispositivo>(datos);
        datos = datos.Where(d => !pos.Hidden.Contains(d.Nombre)).ToList();
        if (datos.Count == 0) datos.Add(new Dispositivo { Nombre = "Open G HUB", Tipo = "", Nivel = -1 });
        // devices that went away slide out instead of vanishing
        foreach (var f in orden) if (!datos.Any(d => d.Nombre == f.Nombre)) f.Saliendo = true;
        foreach (var d in datos)
        {
            Fila f;
            if (!filas.TryGetValue(d.Nombre, out f))
            {
                f = new Fila { Nombre = d.Nombre, Glifo = Glifo(d), Mostrado = d.Nivel, Objetivo = d.Nivel, Aparicion = pos.Animate && primeraCarga ? 0f : 1f };
                filas[d.Nombre] = f; orden.Add(f);
            }
            f.Saliendo = false;
            if (f.Objetivo != d.Nivel || f.Cargando != d.Cargando) f.Actualizado = DateTime.Now;
            f.Objetivo = d.Nivel; f.Etiqueta = d.Etiqueta; f.Cargando = d.Cargando; f.Fuente = d.Fuente;
            if (d.Nivel < 0 || f.Mostrado < 0 || d.Etiqueta != null || !pos.Animate) f.Mostrado = d.Nivel;

            bool ya; avisado.TryGetValue(d.Nombre, out ya);
            if (principal && Cfg.Notifications && d.Nivel >= 0 && d.Nivel <= UmbralAviso && !d.Cargando && !ya)
            {
                aviso.Visible = true;
                aviso.ShowBalloonTip(8000, "Low battery", d.Nombre + (d.Etiqueta != null ? " battery is " + d.Etiqueta.ToLower() : " is at " + d.Nivel + "%") + ". Time to charge it!", ToolTipIcon.Warning);
                avisado[d.Nombre] = true;
            }
            else if (d.Nivel > UmbralAviso + 10) avisado[d.Nombre] = false;

            bool lleno; avisadoLleno.TryGetValue(d.Nombre, out lleno);
            if (principal && Cfg.Notifications && d.Cargando && d.Nivel >= 100 && !lleno)
            {
                aviso.Visible = true;
                aviso.ShowBalloonTip(8000, "Fully charged", d.Nombre + " is fully charged. You can unplug it.", ToolTipIcon.Info);
                avisadoLleno[d.Nombre] = true;
            }
            else if (!d.Cargando || d.Nivel < 95) avisadoLleno[d.Nombre] = false;
        }
        primeraCarga = true;
        ActualizarTooltip();
        Reajustar();
    }
    bool primeraCarga;

    static string NombreFuente(string f)
    {
        switch (f) { case "ghub": return "Logitech G HUB"; case "icue": return "Corsair iCUE"; case "bt": return "Bluetooth"; case "xbox": return "Xbox Wireless"; case "hyperx": return "HyperX (USB)"; default: return ""; }
    }

    static string Hace(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalSeconds < 60) return "just now";
        if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " min ago";
        return (int)d.TotalHours + " h ago";
    }

    void ActualizarTooltip()
    {
        if (true) return;
        var lineas = orden.Where(f => !f.Saliendo).Select(f =>
        {
            string nivel = f.Etiqueta ?? (f.Objetivo < 0 ? "--" : f.Objetivo + "%");
            string extra = f.Cargando ? (f.Objetivo >= 100 ? "  ·  fully charged" : "  ·  charging") : "";
            string fuente = NombreFuente(f.Fuente);
            return f.Nombre + "  —  " + nivel + extra + (fuente != "" ? "\n      via " + fuente + "  ·  changed " + Hace(f.Actualizado) : "");
        });
        // tooltip removed on request: the readout itself shows what matters
    }

    void Pintar(Fila f) { Reajustar(); }

    System.Windows.Forms.Timer clicPendiente;
    string tipActual;

    Fila FilaEn(Point p)
    {
        float v = vertical ? p.Y : p.X;
        return orden.FirstOrDefault(f => !f.Saliendo && v >= f.Desde - 6 && v <= f.Hasta + 2);
    }

    // open the manufacturer's app (or the Windows settings page) for a device
    static void AbrirAppDe(Fila f)
    {
        if (f == null) return;
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidatos = new List<string>();
        switch (f.Fuente)
        {
            case "ghub": candidatos.Add(System.IO.Path.Combine(pf, @"LGHUB\lghub.exe")); break;
            case "icue":
                try { foreach (var d in System.IO.Directory.GetDirectories(System.IO.Path.Combine(pf, "Corsair"))) candidatos.Add(System.IO.Path.Combine(d, "iCUE.exe")); } catch { }
                break;
            case "xbox": candidatos.Add("ms-settings:devices"); break;
            default: candidatos.Add("ms-settings:bluetooth"); break;
        }
        if (f.Nombre == "Open G HUB") candidatos.Insert(0, System.IO.Path.Combine(pf, @"LGHUB\lghub.exe"));
        foreach (var c in candidatos)
            try
            {
                if (!c.StartsWith("ms-settings:") && !System.IO.File.Exists(c)) continue;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(c) { UseShellExecute = true });
                return;
            }
            catch { }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); } catch { }
    }

    // ---- animations ----
    float opacidad = 0f, opacidadObjetivo = 1f, hoverT; double fase; DateTime ultimoPaso = DateTime.Now;
    static float Suave(float t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }   // smoothstep
    bool pos_LowPulse(Fila f) { return pos.Animate && pos.ColorByLevel && !f.Cargando && f.Mostrado >= 0 && f.Etiqueta == null && f.Mostrado <= UmbralAviso; }

    void PasoAnimacion()
    {
        if (IsDisposed || !IsHandleCreated) return;
        bool animar = pos.Animate;
        bool cambio = false, tamano = false;
        float dt = 0.033f;
        // fade in/out
        float objO = opacidadObjetivo;
        if (Math.Abs(opacidad - objO) > 0.001f)
        {
            opacidad = animar ? (opacidad < objO ? Math.Min(objO, opacidad + dt / 0.18f) : Math.Max(objO, opacidad - dt / 0.18f)) : objO;
            cambio = true;
            if (opacidad <= 0.001f && objO == 0f) ShowWindow(Handle, 0);
        }
        // hover highlight
        float objH = hover ? 1f : 0f;
        if (Math.Abs(hoverT - objH) > 0.001f) { hoverT = animar ? (hoverT < objH ? Math.Min(1, hoverT + dt / 0.12f) : Math.Max(0, hoverT - dt / 0.18f)) : objH; cambio = true; }
        // rows entering / leaving
        foreach (var f in orden.ToList())
        {
            float obj = f.Saliendo ? 0f : 1f;
            if (Math.Abs(f.Aparicion - obj) > 0.001f)
            {
                f.Aparicion = animar ? (f.Aparicion < obj ? Math.Min(1, f.Aparicion + dt / 0.28f) : Math.Max(0, f.Aparicion - dt / 0.22f)) : obj;
                tamano = true;
            }
            if (f.Saliendo && f.Aparicion <= 0.001f) { orden.Remove(f); filas.Remove(f.Nombre); tamano = true; }
        }
        // counting numbers (about 16 steps per second)
        if ((DateTime.Now - ultimoPaso).TotalMilliseconds >= 60)
        {
            ultimoPaso = DateTime.Now;
            foreach (var f in orden)
                if (f.Mostrado >= 0 && f.Mostrado != f.Objetivo)
                {
                    int antes = TextoDe(f).Length;
                    f.Mostrado += Math.Sign(f.Objetivo - f.Mostrado);
                    if (TextoDe(f).Length != antes) tamano = true; else cambio = true;
                }
        }
        // low-battery pulse
        if (orden.Any(pos_LowPulse)) { fase += dt * Math.PI * 2 / 1.6; cambio = true; }

        if (tamano) Reajustar();
        else if (cambio) Dibujar();
    }

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
        if (f.Cargando && f.Etiqueta == null && f.Mostrado >= 0)
        {
            yield return Tuple.Create(TextoDe(f), "Segoe UI", TamTexto, 3f);
            yield return Tuple.Create("\uE945", fuenteIconos, TamTexto * 0.8f, EspacioEntre);   // lightning bolt
        }
        else yield return Tuple.Create(TextoDe(f), "Segoe UI", TamTexto, EspacioEntre);
    }

    // horizontal taskbar: total width; vertical taskbar: total height (icon above number, stacked)
    float LargoTotal()
    {
        float w = 0;
        foreach (var f in orden)
        {
            float wf = 0;
            foreach (var par in Partes(f))
                using (var p = Ruta(par.Item1, par.Item2, par.Item3))
                {
                    var b = p.GetBounds();
                    wf += (vertical ? b.Height + (par.Item4 == EspacioIconoTexto ? 5 : par.Item4 == 3f ? 3 : 14) : b.Width + par.Item4);
                }
            w += wf * Suave(f.Aparicion);
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
                int a = (presionado || abierto) ? 18 : (int)Math.Round(1 + 27 * Suave(hoverT));
                var tinte = ColorBarra() == Color.Black ? 0 : 255;
                var fondo = Color.FromArgb(Math.Max(1, a), tinte, tinte, tinte);
                using (var br = new SolidBrush(fondo))
                using (var path = Redondeado(vertical ? new RectangleF(4, 0, Width - 9, Height - 1) : new RectangleF(0, 4, Width - 1, Height - 9), 5))
                    g.FillPath(br, path);
                float pos = PadIzq, cx = Width / 2f, cy = Height / 2f;
                foreach (var f in orden)
                {
                    float ap = Suave(f.Aparicion);
                    var c = ColorNivel(f.Mostrado);
                    float alfa = ap;
                    if (pos_LowPulse(f)) alfa *= 0.55f + 0.45f * (float)(0.5 + 0.5 * Math.Cos(fase));
                    using (var br = new SolidBrush(Color.FromArgb((int)(c.A * alfa), c)))
                    {
                    f.Desde = pos;
                    float escala = ap, inicio = pos;
                    foreach (var par in Partes(f))
                    using (var p = Ruta(par.Item1, par.Item2, par.Item3))
                    {
                        var bb = p.GetBounds();
                        var m = new System.Drawing.Drawing2D.Matrix();
                        if (vertical)
                        {
                            m.Translate((float)Math.Round(cx - bb.Width / 2f) - bb.Left, pos - bb.Top);
                            pos += (bb.Height + (par.Item4 == EspacioIconoTexto ? 5 : par.Item4 == 3f ? 3 : 14)) * escala;
                        }
                        else
                        {
                            m.Translate(pos - bb.Left, (float)Math.Round(cy - bb.Height / 2f) - bb.Top);
                            pos += (bb.Width + par.Item4) * escala;
                        }
                        if (escala < 0.999f)   // entering/leaving: grow from the row's center
                        {
                            var cen = vertical ? new PointF(cx, inicio) : new PointF(inicio, cy);
                            m.Translate(-cen.X, -cen.Y, System.Drawing.Drawing2D.MatrixOrder.Append);
                            m.Scale(0.6f + 0.4f * escala, 0.6f + 0.4f * escala, System.Drawing.Drawing2D.MatrixOrder.Append);
                            m.Translate(cen.X, cen.Y, System.Drawing.Drawing2D.MatrixOrder.Append);
                        }
                        p.Transform(m); g.FillPath(br, p);
                    }
                    f.Hasta = pos;
                    }
                }
            }
            IntPtr sdc = GetDC(IntPtr.Zero), mdc = CreateCompatibleDC(sdc), hb = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mdc, hb);
            var sz = new SIZE { cx = Width, cy = Height }; var ps = new POINT(); var pt = new POINT { x = Left, y = Top };
            var bl = new BLEND { op = 0, flags = 0, alpha = (byte)Math.Round(255 * Suave(opacidad)), format = 1 };
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
        ajustes.Applied += () => { todas[0].refresco.Interval = Cfg.RefreshSeconds * 1000; AplicarMonitores(); foreach (var o in todas) o.Mostrar(new List<Dispositivo>(todas[0].ultimos)); };
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
    const int Nada = int.MinValue;
    int appsIzq = Nada, appsDer = Nada, widgetsDer = Nada, relojIzq = Nada; DateTime appsLeidos; bool leyendoApps;
    // other things sitting on this taskbar (e.g. TrafficMonitor, other tray widgets): windows of other
    // processes overlapping the taskbar, plus unknown child windows of the taskbar itself
    List<RECT> obstaculos = new List<RECT>(); DateTime obstLeidos; int trayPrev = Nada;
    static readonly HashSet<string> ClasesBarra = new HashSet<string> { "Start", "TrayNotifyWnd", "ReBarWindow32", "MSTaskSwWClass", "MSTaskListWClass", "TrayShowDesktopButtonWClass",
        "Windows.UI.Composition.DesktopWindowContentBridge", "Windows.UI.Input.InputSite.WindowClass", "SysPager", "ToolbarWindow32", "TrayClockWClass", "TrayButton", "TrayInputIndicatorWClass",
        "TrayDummySearchControl", "Button", "WorkerW" };

    void LeerObstaculos(RECT r)
    {
        obstLeidos = DateTime.Now;
        var res = new List<RECT>();
        uint pidYo = (uint)System.Diagnostics.Process.GetCurrentProcess().Id, pidExp; GetWindowThreadProcessId(barra, out pidExp);
        var propias = new HashSet<IntPtr>(todas.Where(o => o.IsHandleCreated).Select(o => o.Handle));
        EnumProc revisar = (h, l) =>
        {
            if (propias.Contains(h) || !IsWindowVisible(h)) return true;
            RECT w; if (!GetWindowRect(h, out w)) return true;
            int ancho = w.Right - w.Left, alto = w.Bottom - w.Top;
            if (ancho < 8 || alto < 8 || ancho > (r.Right - r.Left) / 2) return true;            // skip tiny and huge windows
            if (w.Top < r.Top - 2 || w.Bottom > r.Bottom + 2) return true;                          // must sit inside the taskbar band
            if (w.Right <= r.Left || w.Left >= r.Right) return true;
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == pidYo) return true;
            if (pid == pidExp && GetParent(h) != barra) return true;                                 // explorer's own popups
            var cls = new StringBuilder(128); GetClassName(h, cls, 128);
            if (pid == pidExp && ClasesBarra.Contains(cls.ToString())) return true;               // regular taskbar parts
            res.Add(w);
            return true;
        };
        try { EnumWindows(revisar, IntPtr.Zero); EnumChildWindows(barra, revisar, IntPtr.Zero); } catch { }
        GC.KeepAlive(revisar);
        obstaculos = res;
    }

    void Limites(RECT r, int trayLeft, out int minX, out int maxX)
    {
        if ((DateTime.Now - obstLeidos).TotalSeconds >= 1 || trayLeft != trayPrev) { trayPrev = trayLeft; LeerObstaculos(r); }
        // UI Automation is slow: refresh the cached button positions in the background
        if (!leyendoApps && (DateTime.Now - appsLeidos).TotalSeconds > (arrastrando ? 2 : 20))
        {
            leyendoApps = true; appsLeidos = DateTime.Now;
            var rr = r; int tl = barraPrincipal ? trayLeft : r.Right;
            Task.Run(() => LeerApps(rr, tl)).ContinueWith(t => { leyendoApps = false; Posicionar(); Dibujar(); }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (pos.LeftSide)
        {
            minX = (widgetsDer != Nada ? widgetsDer : r.Left) + Margen;
            maxX = (appsIzq != Nada ? appsIzq : (r.Left + r.Right) / 2) - Margen - Width;
            foreach (var o in obstaculos)
            {
                int limiteApps = appsIzq != Nada ? appsIzq : (r.Left + r.Right) / 2;
                if (o.Right <= limiteApps && o.Right > r.Left) minX = Math.Max(minX, o.Right + Margen);
            }
        }
        else
        {
            maxX = trayLeft - Width - SeparacionPx;
            // keep the default gap from anything else sitting between the app icons and the tray
            int izqApps = appsDer != Nada ? appsDer : r.Left;
            foreach (var o in obstaculos)
                if (o.Left > izqApps && o.Left < trayLeft) maxX = Math.Min(maxX, o.Left - Margen - Width);
            minX = appsDer != Nada ? appsDer + Margen : r.Left + Margen;
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
            int wd = Nada; for (int i = 0; i < i0; i++) if (botones[i].Item2 < izq - 20 && botones[i].Item2 < r.Left + 400) wd = Math.Max(wd, botones[i].Item2);   // e.g. the Widgets button on the far left
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
            int trayLeft = hayTray && rn.Left > r.Left ? rn.Left : (!barraPrincipal && relojIzq != Nada ? relojIzq - 4 : r.Right - (barraPrincipal ? 330 : 82));
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
