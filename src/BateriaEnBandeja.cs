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
            try { if (p.Id != yo.Id && p.ProcessName.StartsWith("BateriaEnBandeja", StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); } } catch { }
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.Run(new Overlay());
        }
    }

    Overlay()
    {
        fuenteIconos = new System.Drawing.Text.InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons")
            ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(1, 1); Size = new Size(1, 1);
        

        Cfg.Load();
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColores());
        menu.ForeColor = Color.White; menu.ShowImageMargin = false; menu.Font = new Font("Segoe UI", 9.5f);
        menu.Items.Add("Refresh now", null, (s, e) => Consultar());
        menu.Items.Add("Settings...", null, (s, e) => AbrirAjustes());
        var inicio = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        inicio.Click += (s, e) => Cfg.SetStartWithWindows(inicio.Checked);
        menu.Items.Add(inicio);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => { aviso.Dispose(); Application.Exit(); });
        menu.Opening += (s, e) => inicio.Checked = Cfg.StartWithWindows;
        menu.Closed += (s, e) => { abierto = false; Dibujar(); };
        ContextMenuStrip = menu;

        var anim = new System.Windows.Forms.Timer { Interval = 60 };
        anim.Tick += (s, e) => { foreach (var f in filas.Values) if (f.Mostrado >= 0 && f.Mostrado != f.Objetivo) { f.Mostrado += Math.Sign(f.Objetivo - f.Mostrado); Pintar(f); } };
        anim.Start();

        // revisa cada 50 ms si la barra quedo encima; solo en ese caso vuelve a subir (sin redibujar de mas)
        var top = new System.Windows.Forms.Timer { Interval = 50 };
        top.Tick += (s, e) => { Duenio(); if (BarraEncima()) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013); };
        top.Start();

        refresco = new System.Windows.Forms.Timer { Interval = Cfg.RefreshSeconds * 1000 };
        refresco.Tick += (s, e) => Consultar();
        refresco.Start();

        MouseEnter += (s, e) => { hover = true; Dibujar(); };
        MouseLeave += (s, e) => { hover = false; Dibujar(); };
        Cursor = Cursors.Hand;
        MouseDown += (s, e) => { presionado = true; Dibujar(); };
        MouseUp += (s, e) => { presionado = false; AbrirMenu(); };
        Shown += (s, e) => { Duenio(); Consultar(); };
    }

    // ventana de herramienta que no roba el foco ni aparece en Alt+Tab
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000 | 0x8 | 0x80000; return cp; }
    }
    protected override bool ShowWithoutActivation { get { return true; } }

    IntPtr duenioActual = IntPtr.Zero;
    void Duenio()
    {
        var tb = FindWindow("Shell_TrayWnd", null);
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
        if (consultando) return;
        consultando = true;
        Task.Run(() => { var l = new List<Dispositivo>(); try { l.AddRange(LeerGHub()); } catch { } try { l.AddRange(LeerXbox()); } catch { } try { l.AddRange(Corsair.Leer()); } catch { } try { l.AddRange(HyperX.Leer()); } catch { } try { foreach (var b in LeerBluetooth()) if (!l.Any(x => x.Nombre == b.Nombre)) l.Add(b); } catch { } return l; })
            .ContinueWith(t => { consultando = false; Mostrar(t.Result); }, TaskScheduler.FromCurrentSynchronizationContext());
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

    static Color ColorNivel(int n)
    { return n < 0 ? Color.Gray : (Cfg.ColorByLevel && n <= UmbralAviso) ? Color.FromArgb(255, 90, 90) : Cfg.TextColor; }

    void Mostrar(List<Dispositivo> datos)
    {
        ultimos = new List<Dispositivo>(datos);
        datos = datos.Where(d => !Cfg.Hidden.Contains(d.Nombre)).ToList();
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
            if (d.Nivel < 0 || f.Mostrado < 0 || d.Etiqueta != null || !Cfg.Animate) f.Mostrado = d.Nivel;

            bool ya; avisado.TryGetValue(d.Nombre, out ya);
            if (Cfg.Notifications && d.Nivel >= 0 && d.Nivel <= UmbralAviso && !ya)
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
    static float TamIcono { get { return Cfg.IconSize; } } static float TamTexto { get { return Cfg.TextSize; } } static float EspacioEntre { get { return Cfg.Spacing; } }
    const float EspacioIconoTexto = 7f, PadIzq = 8f, PadDer = 8f;
    static string TextoDe(Fila f) { return f.Etiqueta ?? (f.Mostrado < 0 ? "--" : f.Mostrado + (Cfg.ShowPercent ? "%" : "")); }

    static System.Drawing.Drawing2D.GraphicsPath Ruta(string txt, string fuente, float px)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddString(txt, new FontFamily(fuente), 0, px, PointF.Empty, StringFormat.GenericTypographic);
        return p;
    }

    float AnchoTotal()
    {
        float w = 0;
        foreach (var f in orden)
        {
            if (Cfg.ShowIcons) using (var pi = Ruta(f.Glifo, fuenteIconos, TamIcono)) w += pi.GetBounds().Width + EspacioIconoTexto;
            using (var pt = Ruta(TextoDe(f), "Segoe UI", TamTexto)) w += pt.GetBounds().Width + EspacioEntre;
        }
        return Math.Max(1, w - EspacioEntre + PadIzq + PadDer);
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
                using (var path = Redondeado(new RectangleF(0, 4, Width - 1, Height - 9), 5))
                    g.FillPath(br, path);
                float x = PadIzq, cy = Height / 2f;
                foreach (var f in orden)
                using (var br = new SolidBrush(ColorNivel(f.Mostrado)))
                {
                    foreach (var par in (Cfg.ShowIcons ? new[] { Tuple.Create(f.Glifo, fuenteIconos, TamIcono, EspacioIconoTexto) } : new Tuple<string, string, float, float>[0]).Concat(new[] { Tuple.Create(TextoDe(f), "Segoe UI", TamTexto, EspacioEntre) }))
                    using (var p = Ruta(par.Item1, par.Item2, par.Item3))
                    {
                        var bb = p.GetBounds();
                        var m = new System.Drawing.Drawing2D.Matrix();
                        m.Translate(x - bb.Left, (float)Math.Round(cy - bb.Height / 2f) - bb.Top);
                        p.Transform(m); g.FillPath(br, p);
                        x += bb.Width + par.Item4;
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

    int altoBarra = 48; bool hover, presionado, abierto;

    void AbrirMenu()
    {
        abierto = true; Dibujar();
        // open the menu above the button, aligned to its right edge, like the system tray flyouts
        menu.Show(this, new Point(Width - menu.PreferredSize.Width, -menu.PreferredSize.Height - 6));
    }

    void AbrirAjustes()
    {
        if (ajustes != null && !ajustes.IsDisposed) { ajustes.Activate(); return; }
        ajustes = new SettingsForm(ultimos.Select(d => d.Nombre).Where(n => n != "Open G HUB"));
        ajustes.Applied += () => { refresco.Interval = Cfg.RefreshSeconds * 1000; Mostrar(new List<Dispositivo>(ultimos)); };
        ajustes.Show();
    }
    void Reajustar()
    {
        RECT r; var tb = FindWindow("Shell_TrayWnd", null);
        if (tb != IntPtr.Zero && GetWindowRect(tb, out r)) altoBarra = r.Bottom - r.Top;
        var nuevo = new Size((int)Math.Ceiling(AnchoTotal()), altoBarra);
        if (Size != nuevo) Size = nuevo;
        Posicionar();
        Dibujar();
    }

    void Posicionar()
    {
        RECT r, rn;
        var tb = FindWindow("Shell_TrayWnd", null);
        if (tb == IntPtr.Zero || !GetWindowRect(tb, out r)) return;
        int trayLeft = r.Right - 330;
        var tn = FindWindowEx(tb, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tn != IntPtr.Zero && GetWindowRect(tn, out rn) && rn.Left > r.Left) trayLeft = rn.Left;
        Location = new Point(trayLeft - Width - SeparacionPx - Cfg.OffsetX, r.Top + (r.Bottom - r.Top - Height) / 2 + Cfg.OffsetY);
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
