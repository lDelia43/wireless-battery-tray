using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

// ---------------- Corsair (iCUE SDK v4) ----------------
static class Corsair
{
    const string Dll = "iCUESDK.x64_2019.dll";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StateHandler(IntPtr ctx, IntPtr ev);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int CorsairConnect(StateHandler h, IntPtr ctx);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int CorsairGetDevices(ref int filterMask, int sizeMax, IntPtr devices, out int size);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int CorsairReadDeviceProperty(byte[] id, int prop, uint index, IntPtr property);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int CorsairFreeProperty(IntPtr property);

    static StateHandler handler; static volatile int estado; static bool iniciado, sinDll;
    const int TamInfo = 4 + 128 * 3 + 8;   // CorsairDeviceInfo

    public static List<Dispositivo> Leer()
    {
        var res = new List<Dispositivo>();
        if (sinDll) return res;
        try
        {
            if (!iniciado)
            {
                handler = (c, ev) => { estado = Marshal.ReadInt32(ev); };
                CorsairConnect(handler, IntPtr.Zero); iniciado = true;
                for (int i = 0; i < 30 && estado != 6; i++) Thread.Sleep(100);
            }
            if (estado != 6) return res;
            int mask = unchecked((int)0xFFFFFFFF), n;
            IntPtr buf = Marshal.AllocHGlobal(TamInfo * 32), prop = Marshal.AllocHGlobal(32);
            try
            {
                if (CorsairGetDevices(ref mask, 32, buf, out n) != 0) return res;
                for (int i = 0; i < n; i++)
                {
                    IntPtr p = buf + i * TamInfo;
                    int tipo = Marshal.ReadInt32(p);
                    var id = new byte[128]; Marshal.Copy(p + 4, id, 0, 128);
                    string modelo = Marshal.PtrToStringAnsi(p + 4 + 256);
                    for (int k = 0; k < 32; k++) Marshal.WriteByte(prop, k, 0);
                    if (CorsairReadDeviceProperty(id, 9, 0, prop) != 0) continue;   // CDPI_BatteryLevel
                    int nivel = Marshal.ReadInt32(prop + 8);
                    CorsairFreeProperty(prop);
                    if (nivel < 0 || nivel > 100) continue;
                    string t = (tipo & 0x8) != 0 ? "HEADSET" : (tipo & 0x2) != 0 ? "MOUSE" : (tipo & 0x1) != 0 ? "KEYBOARD" : (tipo & 0x1000) != 0 ? "GAMEPAD" : "";
                    res.Add(new Dispositivo { Nombre = "Corsair " + modelo, Tipo = t, Nivel = nivel, Fuente = "icue" });
                }
            }
            finally { Marshal.FreeHGlobal(buf); Marshal.FreeHGlobal(prop); }
        }
        catch (DllNotFoundException) { sinDll = true; }
        catch (BadImageFormatException) { sinDll = true; }
        catch (EntryPointNotFoundException) { sinDll = true; }
        return res;
    }
}

// ---------------- HyperX Cloud II Wireless (HID directo, experimental) ----------------
static class HyperX
{
    static readonly int[][] Ids = { new[] { 0x0951, 0x1718 }, new[] { 0x03F0, 0x018B }, new[] { 0x03F0, 0x0D93 }, new[] { 0x03F0, 0x0696 } };

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, string e, IntPtr h, uint f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr d, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA did);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA did, IntPtr det, uint size, out uint req, IntPtr dev);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string n, uint acc, uint share, IntPtr sec, uint disp, uint flags, IntPtr t);

    [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, Version; }
    [StructLayout(LayoutKind.Sequential)] struct HIDP_CAPS { public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved; public ushort a, b, c, d, e, f, g, h, i, j; }
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }

    public static List<Dispositivo> Leer()
    {
        var res = new List<Dispositivo>();
        Guid hid; HidD_GetHidGuid(out hid);
        IntPtr set = SetupDiGetClassDevs(ref hid, null, IntPtr.Zero, 0x12);   // PRESENT | DEVICEINTERFACE
        if (set == new IntPtr(-1)) return res;
        try
        {
            var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref did); i++)
            {
                uint req; SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out req, IntPtr.Zero);
                IntPtr det = Marshal.AllocHGlobal((int)req);
                string ruta;
                try
                {
                    Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref did, det, req, out req, IntPtr.Zero)) continue;
                    ruta = Marshal.PtrToStringUni(det + 4);
                }
                finally { Marshal.FreeHGlobal(det); }
                string r = ruta.ToLowerInvariant();
                bool coincide = false;
                foreach (var id in Ids) if (r.Contains(string.Format("vid_{0:x4}&pid_{1:x4}", id[0], id[1]))) coincide = true;
                if (!coincide) continue;
                int nivel = Consultar(ruta);
                if (nivel >= 0) { res.Add(new Dispositivo { Nombre = "HyperX Cloud II Wireless", Tipo = "HEADSET", Nivel = nivel, Fuente = "hyperx" }); break; }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return res;
    }

    static int Consultar(string ruta)
    {
        using (var h = CreateFile(ruta, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
        {
            if (h.IsInvalid) return -1;
            IntPtr pp; if (!HidD_GetPreparsedData(h, out pp)) return -1;
            HIDP_CAPS caps; HidP_GetCaps(pp, out caps); HidD_FreePreparsedData(pp);
            if (caps.OutputReportByteLength < 4 || caps.InputReportByteLength < 8) return -1;
            using (var fs = new FileStream(h, FileAccess.ReadWrite, 64, true))
            {
                var pedido = new byte[caps.OutputReportByteLength];
                pedido[0] = 0x06; pedido[1] = 0xFF; pedido[2] = 0xBB; pedido[3] = 0x02;
                try { if (!fs.WriteAsync(pedido, 0, pedido.Length).Wait(800)) return -1; } catch { return -1; }
                var fin = DateTime.Now.AddMilliseconds(1500);
                while (DateTime.Now < fin)
                {
                    var buf = new byte[caps.InputReportByteLength];
                    var t = fs.ReadAsync(buf, 0, buf.Length);
                    if (!t.Wait(800)) return -1;
                    if (t.Result >= 8 && buf[0] == 0x06 && buf[2] == 0xBB && buf[3] == 0x02 && buf[7] <= 100) return buf[7];
                }
            }
        }
        return -1;
    }
}
