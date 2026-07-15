// DFM80 Web Driver — 桌面版（把 v1.0.0 網頁驅動包成 exe）
// 內建 localhost server 服務 index.html → WebView2 載入（localhost 是 secure context，WebHID/navigator.hid 才能用）。
// 功能與 index.html 完全相同；HID 全在網頁層跑，C# 只當殼＋系統匣。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DFM80WebDriverApp
{
    static class Program
    {
        public const string VERSION = "1.0.1";
        public const int PORT = 45870;
        public static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DFM80WebDriver");
        public static readonly string LogPath = Path.Combine(DataDir, "log.txt");
        public static byte[] IndexHtml;

        public static void Log(string m)
        {
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss ") + m + "\r\n"); } catch { }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetDllDirectory(string lpPathName);

        static byte[] Res(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var b = new byte[s.Length]; int off = 0, n;
                while (off < b.Length && (n = s.Read(b, off, b.Length - off)) > 0) off += n;
                return b;
            }
        }

        static void SetupSingleExe()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate (object sender, ResolveEventArgs e)
            {
                try
                {
                    string sn = new AssemblyName(e.Name).Name;
                    string res = sn == "Microsoft.Web.WebView2.Core" ? "Microsoft.Web.WebView2.Core.dll"
                               : sn == "Microsoft.Web.WebView2.WinForms" ? "Microsoft.Web.WebView2.WinForms.dll" : null;
                    if (res == null) return null;
                    var bytes = Res(res);
                    return bytes == null ? null : Assembly.Load(bytes);
                }
                catch { return null; }
            };
            try
            {
                string binDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DFM80WebDriver", "bin");
                Directory.CreateDirectory(binDir);
                string loaderPath = Path.Combine(binDir, "WebView2Loader.dll");
                var native = Res("WebView2Loader.dll");
                if (native != null && (!File.Exists(loaderPath) || new FileInfo(loaderPath).Length != native.Length))
                    File.WriteAllBytes(loaderPath, native);
                SetDllDirectory(binDir);
            }
            catch (Exception ex) { Log("SetupSingleExe: " + ex.Message); }
        }

        static Mutex singleMutex;
        static EventWaitHandle showEvent;

        [STAThread]
        static void Main()
        {
            // 單一實例：已有一個在跑 → 通知它顯示視窗，自己退出（等於從系統匣點開）
            bool createdNew;
            singleMutex = new Mutex(true, "DFM80WebDriver_SingleInstance", out createdNew);
            if (!createdNew)
            {
                try { EventWaitHandle.OpenExisting("DFM80WebDriver_ShowWindow").Set(); } catch { }
                return;
            }
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "DFM80WebDriver_ShowWindow");
            var listener = new Thread(() =>
            {
                while (true)
                {
                    try { showEvent.WaitOne(); } catch { break; }
                    var inst = MainForm.Instance;
                    if (inst != null) { try { inst.BeginInvoke((Action)(() => inst.ShowFromTray())); } catch { } }
                }
            });
            listener.IsBackground = true; listener.Start();

            SetupSingleExe();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Directory.CreateDirectory(DataDir); } catch { }
            Log("=== DFM80 Web Driver App " + VERSION + " starting, port " + PORT + " ===");
            Cfg.Load();
            IndexHtml = Res("index.html");
            StartHttpServer();
            Application.Run(new MainForm());
        }

        // ---- 內建 localhost server：secure context 讓 WebHID 可用 ----
        static void StartHttpServer()
        {
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add("http://localhost:" + PORT + "/");
                l.Start();
                Log("HTTP listening on localhost:" + PORT);
                var th = new Thread(() =>
                {
                    while (true)
                    {
                        try { var ctx = l.GetContext(); System.Threading.ThreadPool.QueueUserWorkItem(_ => Handle(ctx)); }
                        catch (Exception ex) { Log("GetContext: " + ex.Message); Thread.Sleep(300); }
                    }
                });
                th.IsBackground = true; th.Start();
            }
            catch (Exception ex) { Log("StartHttpServer: " + ex.Message); }
        }

        static void Handle(HttpListenerContext ctx)
        {
            try
            {
                var res = ctx.Response;
                res.ContentType = "text/html; charset=utf-8";
                res.Headers["Cache-Control"] = "no-store";
                byte[] body = IndexHtml ?? Encoding.UTF8.GetBytes("<h1>index.html missing</h1>");
                res.ContentLength64 = body.Length;
                res.OutputStream.Write(body, 0, body.Length);
                res.OutputStream.Close();
            }
            catch (Exception ex) { Log("Handle: " + ex.Message); }
        }
    }

    // 桌面 app 行為設定
    class AppConfig { public bool autostart = false; public bool minimizeToTray = false; public bool pseudoSleep = false; public int sleepThresholdMin = 3; }
    static class Cfg
    {
        public static AppConfig C = new AppConfig();
        static readonly string Path_ = Path.Combine(Program.DataDir, "config.json");
        static readonly JavaScriptSerializer J = new JavaScriptSerializer();
        public static void Load()
        {
            try { if (File.Exists(Path_)) C = J.Deserialize<AppConfig>(File.ReadAllText(Path_)) ?? new AppConfig(); }
            catch { C = new AppConfig(); }
        }
        public static void Save()
        {
            try { File.WriteAllText(Path_, J.Serialize(C)); } catch (Exception ex) { Program.Log("Cfg.Save: " + ex.Message); }
        }
    }

    // ============================================================
    //  HID 通訊層（假休眠用：讀 report_rate/highspeed、改 report_rate）
    //  得來速式開/關 handle，與 WebView2 的 WebHID 共享存取（FILE_SHARE_READ|WRITE）
    // ============================================================
    static class Hid
    {
        public static int HzToVal(int hz) { return hz == 125 ? 1 : hz == 250 ? 2 : hz == 500 ? 3 : hz == 1000 ? 4 : 3; }

        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        const uint DIGCF_PRESENT = 2, DIGCF_DEVICEINTERFACE = 0x10;
        static readonly IntPtr INVALID = new IntPtr(-1);

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS c);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint idx, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size, ref uint reqSize, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(IntPtr h, byte[] buf, uint len, out uint written, ref NativeOverlapped ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(IntPtr h, byte[] buf, uint len, out uint read, ref NativeOverlapped ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateEvent(IntPtr attr, bool manualReset, bool initial, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetOverlappedResult(IntPtr h, ref NativeOverlapped ov, out uint n, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CancelIo(IntPtr h);

        [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid guid; public uint flags; public IntPtr reserved; }
        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        static readonly ushort[] VIDS = { 0xA8A5, 0xA8A4 };
        const ushort VENDOR_USAGE_PAGE = 0xFF01, VENDOR_USAGE = 0x10;
        static readonly object Lock = new object();

        public static string FindPath()
        {
            Guid g; HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == INVALID) return null;
            try
            {
                var did = new SP_DEVICE_INTERFACE_DATA(); did.cbSize = Marshal.SizeOf(did);
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did); i++)
                {
                    uint need = 0;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, ref need, IntPtr.Zero);
                    if (need == 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)need);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        uint r2 = 0;
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref did, detail, need, ref r2, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                        if (string.IsNullOrEmpty(path)) continue;
                        string lp = path.ToLowerInvariant();
                        bool vidOk = false;
                        foreach (var v in VIDS) if (lp.Contains("vid_" + v.ToString("x4"))) vidOk = true;
                        if (!vidOk) continue;
                        if (IsVendorCollection(path)) return path;
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return null;
        }

        static bool IsVendorCollection(string path)
        {
            IntPtr h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID) return false;
            try
            {
                IntPtr pp;
                if (!HidD_GetPreparsedData(h, out pp)) return false;
                try
                {
                    var caps = new HIDP_CAPS();
                    if (HidP_GetCaps(pp, ref caps) != unchecked((int)0x00110000)) return false;
                    return caps.UsagePage == VENDOR_USAGE_PAGE && caps.Usage == VENDOR_USAGE;
                }
                finally { HidD_FreePreparsedData(pp); }
            }
            finally { CloseHandle(h); }
        }

        public static bool IsConnected() { return FindPath() != null; }

        // 讀 report_rate(韌體值 1-4) + highspeed_mode(電競 0/1)
        public static bool ReadState(out int rate, out int highspeed)
        {
            rate = 0; highspeed = 0;
            lock (Lock)
            {
                string path = FindPath(); if (path == null) return false;
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == INVALID) return false;
                try
                {
                    int outLen, inLen; if (!GetReportLengths(h, out inLen, out outLen)) { outLen = 65; inLen = 65; }
                    byte[] d = ReadConfig(h, inLen, outLen); if (d == null) return false;
                    rate = d[10]; highspeed = d[53]; return true;
                }
                finally { CloseHandle(h); }
            }
        }

        // 改 report_rate 為韌體值 rateVal（read-modify-write，不動其他設定）
        public static bool ApplyRateVal(int rateVal)
        {
            lock (Lock)
            {
                string path = FindPath();
                if (path == null) { Program.Log("ApplyRateVal: no device"); return false; }
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == INVALID) { Program.Log("ApplyRateVal: open fail " + Marshal.GetLastWin32Error()); return false; }
                try
                {
                    int outLen, inLen; if (!GetReportLengths(h, out inLen, out outLen)) { outLen = 65; inLen = 65; }
                    byte[] cur = ReadConfig(h, inLen, outLen);
                    if (cur == null) { Program.Log("ApplyRateVal: read cfg fail"); return false; }
                    byte[] pkt = BuildWritePacket(cur, rateVal, -1, outLen);
                    bool ok = WriteReport(h, pkt);
                    Program.Log("ApplyRateVal val=" + rateVal + " -> " + (ok ? "OK" : "FAIL"));
                    return ok;
                }
                finally { CloseHandle(h); }
            }
        }

        static bool GetReportLengths(IntPtr h, out int inLen, out int outLen)
        {
            inLen = outLen = 0;
            IntPtr pp;
            if (!HidD_GetPreparsedData(h, out pp)) return false;
            try
            {
                var caps = new HIDP_CAPS();
                if (HidP_GetCaps(pp, ref caps) != unchecked((int)0x00110000)) return false;
                inLen = caps.InputReportByteLength; outLen = caps.OutputReportByteLength;
                return inLen > 0 && outLen > 0;
            }
            finally { HidD_FreePreparsedData(pp); }
        }

        static byte[] ReadConfig(IntPtr h, int inLen, int outLen)
        {
            byte[] q = new byte[outLen];
            q[1] = 0x55; q[2] = 0x0E; q[3] = 1; q[4] = 11; q[5] = 48;
            if (!WriteReport(h, q)) return null;
            for (int tries = 0; tries < 12; tries++)
            {
                byte[] r = ReadReport(h, inLen, 700);
                if (r == null) return null;
                if (r[1] == 0xAA && r[2] == 0x0E) { var d = new byte[64]; Array.Copy(r, 1, d, 0, Math.Min(64, r.Length - 1)); return d; }
            }
            return null;
        }

        // rateVal/sleepVal 傳 -1 表示保留原值（read-modify-write 只改指定欄位）
        static byte[] BuildWritePacket(byte[] d, int rateVal, int sleepVal, int outLen)
        {
            byte[] a = new byte[Math.Max(65, outLen)];
            a[1] = 0x55; a[2] = 0x0F; a[3] = 174; a[4] = 10; a[5] = 47; a[6] = 1; a[7] = 1; a[8] = 1; a[9] = 0;
            a[10] = d[9];
            a[11] = (byte)(rateVal >= 0 ? rateVal : d[10]);   // report_rate
            a[12] = 6;
            int dpiIdx = d[12] - 1; if (dpiIdx < 0) dpiIdx = 0;
            a[13] = (byte)(dpiIdx + 1);
            for (int i = 0; i < 6; i++) { a[14 + 2 * i] = d[13 + 2 * i]; a[15 + 2 * i] = d[14 + 2 * i]; }
            a[49] = d[48]; a[50] = d[49]; a[51] = d[50]; a[52] = d[51];
            a[53] = (byte)(sleepVal >= 0 ? sleepVal : d[52]); // sleep_light（休眠時間分鐘；0=不休眠）
            a[54] = d[53]; a[55] = d[55];
            return a;
        }

        // 改 sleep_light 為 minutes（0=不休眠）
        public static bool ApplySleepLight(int minutes)
        {
            lock (Lock)
            {
                string path = FindPath(); if (path == null) return false;
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == INVALID) return false;
                try
                {
                    int outLen, inLen; if (!GetReportLengths(h, out inLen, out outLen)) { outLen = 65; inLen = 65; }
                    byte[] cur = ReadConfig(h, inLen, outLen); if (cur == null) return false;
                    byte[] pkt = BuildWritePacket(cur, -1, minutes, outLen);
                    bool ok = WriteReport(h, pkt);
                    Program.Log("ApplySleepLight " + minutes + "min -> " + (ok ? "OK" : "FAIL"));
                    return ok;
                }
                finally { CloseHandle(h); }
            }
        }

        static bool WriteReport(IntPtr h, byte[] buf)
        {
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, null);
            var ov = new NativeOverlapped(); ov.EventHandle = ev;
            try
            {
                uint w;
                if (WriteFile(h, buf, (uint)buf.Length, out w, ref ov)) return true;
                if (Marshal.GetLastWin32Error() != 997) return false;
                if (WaitForSingleObject(ev, 1200) != 0) { CancelIo(h); return false; }
                uint n; return GetOverlappedResult(h, ref ov, out n, false);
            }
            finally { CloseHandle(ev); }
        }

        static byte[] ReadReport(IntPtr h, int inLen, uint timeoutMs)
        {
            byte[] buf = new byte[inLen];
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, null);
            var ov = new NativeOverlapped(); ov.EventHandle = ev;
            try
            {
                uint rd;
                if (ReadFile(h, buf, (uint)inLen, out rd, ref ov)) return buf;
                if (Marshal.GetLastWin32Error() != 997) return null;
                if (WaitForSingleObject(ev, timeoutMs) != 0) { CancelIo(h); return null; }
                uint n; if (!GetOverlappedResult(h, ref ov, out n, false)) return null;
                return buf;
            }
            finally { CloseHandle(ev); }
        }
    }

    static class Idle
    {
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
        [DllImport("kernel32.dll")] static extern uint GetTickCount();
        public static int Seconds()
        {
            var l = new LASTINPUTINFO(); l.cbSize = (uint)Marshal.SizeOf(l);
            if (!GetLastInputInfo(ref l)) return 0;
            long diff = (long)GetTickCount() - l.dwTime;
            if (diff < 0) diff = 0;
            return (int)(diff / 1000);
        }
    }

    class MainForm : Form
    {
        public static MainForm Instance;
        WebView2 wv;
        NotifyIcon tray;
        System.Windows.Forms.Timer sleepTimer;
        bool throttled = false;       // 目前是否降頻（假休眠）中
        int savedRate = 3, savedHs = 0; // 進休眠前的 report_rate 與電競狀態
        volatile bool busy = false;

        public void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); BringToFront(); }

        public MainForm()
        {
            Instance = this;
            Text = "DFM80 簡易驅動";
            try { Icon = LoadIcon(); } catch { }
            Width = 1040; Height = 900; StartPosition = FormStartPosition.CenterScreen;
            wv = new WebView2(); wv.Dock = DockStyle.Fill; Controls.Add(wv);
            InitWeb();
            SetupTray();
            // 假休眠：閒置達門檻降 125Hz，偵測到操作恢復原頻率（背景運作，不碰 UI）
            sleepTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            sleepTimer.Tick += SleepTick;
            sleepTimer.Start();
            // 啟動時若假休眠開著，確保韌體維持不休眠（滑鼠已連才成功，否則使用者連上後切一下 toggle）
            if (Cfg.C.pseudoSleep) Task.Run(() => Hid.ApplySleepLight(0));
            // 關閉視窗：勾「最小化到系統匣」才縮托盤，否則直接結束
            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing && Cfg.C.minimizeToTray) { e.Cancel = true; Hide(); }
            };
            FormClosed += (s, e) => { try { if (tray != null) tray.Visible = false; } catch { } };
        }

        // ---- 假休眠狀態機（每秒檢查全系統閒置）----
        void SleepTick(object sender, EventArgs e)
        {
            if (busy || !Cfg.C.pseudoSleep) return; // 只在假休眠開啟時運作
            int idle = Idle.Seconds();
            int threshold = Math.Max(1, Cfg.C.sleepThresholdMin) * 60;
            if (idle >= threshold && !throttled) DoSleep();
            else if (idle < threshold && throttled) DoWake();
        }

        void DoSleep()
        {
            busy = true;
            Task.Run(() =>
            {
                int rate, hs;
                bool ok = Hid.ReadState(out rate, out hs);
                if (ok && rate >= 1 && rate <= 4)
                {
                    savedRate = rate; savedHs = hs;             // 記住原頻率＋電競狀態
                    if (rate != 1) Hid.ApplyRateVal(1);         // 降 125Hz（已是 125 就不動）
                    Program.Log("SLEEP: saved rate=" + rate + " hs=" + hs + " -> 125Hz");
                    Done(() => throttled = true);
                }
                else Done(null);
            });
        }

        void DoWake()
        {
            busy = true;
            Task.Run(() =>
            {
                int targetVal = (savedHs == 1) ? 4 : savedRate; // 電競(移動喚醒)→1000；否則→原頻率
                if (targetVal < 1 || targetVal > 4) targetVal = 3;
                Hid.ApplyRateVal(targetVal);
                Program.Log("WAKE: restore val=" + targetVal + (savedHs == 1 ? " (電競→1000)" : ""));
                Done(() => throttled = false);
            });
        }

        void Done(Action set)
        {
            try { BeginInvoke((Action)(() => { if (set != null) set(); busy = false; PushThrottle(); })); }
            catch { busy = false; }
        }

        // 即時通知網頁：目前是否降頻中（連線列「省電模式中」標籤）
        void PushThrottle()
        {
            try
            {
                if (wv == null || wv.CoreWebView2 == null) return;
                var o = new Dictionary<string, object> { { "type", "throttle" }, { "throttled", throttled } };
                wv.CoreWebView2.PostWebMessageAsString(new JavaScriptSerializer().Serialize(o));
            }
            catch { }
        }

        Icon LoadIcon()
        {
            try { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico")) if (s != null) return new Icon(s); } catch { }
            return SystemIcons.Application;
        }

        async void InitWeb()
        {
            string udf = Path.Combine(Program.DataDir, "wv2");
            var env = await CoreWebView2Environment.CreateAsync(null, udf);
            await wv.EnsureCoreWebView2Async(env);
            wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            wv.CoreWebView2.Settings.AreDevToolsEnabled = true; // 需要時 F12
            // WebHID 裝置權限：自動允許（使用者已在 requestDevice 選擇器挑過裝置）
            try { wv.CoreWebView2.PermissionRequested += OnPermission; } catch { }
            wv.CoreWebView2.WebMessageReceived += OnWebMessage;
            wv.CoreWebView2.Navigate("http://localhost:" + Program.PORT + "/");
        }

        void OnPermission(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            try { e.State = CoreWebView2PermissionState.Allow; } catch { }
        }

        // 桌面版設定方框 ↔ C# 同步
        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var m = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(e.TryGetWebMessageAsString());
                string type = m.ContainsKey("type") ? m["type"].ToString() : "";
                if (type == "deskready") PushDeskCfg();
                else if (type == "deskcfg")
                {
                    Cfg.C.autostart = m.ContainsKey("autostart") && Convert.ToBoolean(m["autostart"]);
                    Cfg.C.minimizeToTray = m.ContainsKey("minimizeToTray") && Convert.ToBoolean(m["minimizeToTray"]);
                    Cfg.Save();
                    SetAutostart(Cfg.C.autostart);
                }
                else if (type == "pseudosleep")
                {
                    bool en = m.ContainsKey("enabled") && Convert.ToBoolean(m["enabled"]);
                    int th = m.ContainsKey("thresholdMin") ? Convert.ToInt32(m["thresholdMin"]) : Cfg.C.sleepThresholdMin;
                    if (th < 1) th = 1; if (th > 255) th = 255;
                    Cfg.C.pseudoSleep = en; Cfg.C.sleepThresholdMin = th; Cfg.Save();
                    // 假休眠開→韌體寫不休眠(0)；關→寫回真休眠時間(th)。若剛關且正在降頻先恢復。
                    Task.Run(() => Hid.ApplySleepLight(en ? 0 : th));
                    if (!en && throttled) DoWake();
                    Program.Log("pseudosleep enabled=" + en + " threshold=" + th);
                }
            }
            catch (Exception ex) { Program.Log("OnWebMessage: " + ex.Message); }
        }

        void PushDeskCfg()
        {
            try
            {
                var o = new Dictionary<string, object> { { "type", "deskcfg" }, { "autostart", Cfg.C.autostart }, { "minimizeToTray", Cfg.C.minimizeToTray }, { "pseudoSleep", Cfg.C.pseudoSleep }, { "thresholdMin", Cfg.C.sleepThresholdMin } };
                wv.CoreWebView2.PostWebMessageAsString(new JavaScriptSerializer().Serialize(o));
            }
            catch { }
        }

        void SetupTray()
        {
            tray = new NotifyIcon();
            tray.Icon = LoadIcon();
            tray.Text = "DFM80 簡易驅動";
            tray.Visible = true;
            tray.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
            var menu = new ContextMenuStrip();
            menu.Items.Add("開啟", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("重新載入", null, (s, e) => { try { wv.CoreWebView2.Reload(); } catch { } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("結束", null, (s, e) => { tray.Visible = false; Application.Exit(); });
            tray.ContextMenuStrip = menu;

            // 開機啟動狀態與 config 對齊（防呆：使用者手動刪了登錄機碼）
            SetAutostart(Cfg.C.autostart);
        }

        void SetAutostart(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (on) k.SetValue("DFM80WebDriver", "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue("DFM80WebDriver") != null) k.DeleteValue("DFM80WebDriver", false);
                }
            }
            catch (Exception ex) { Program.Log("SetAutostart: " + ex.Message); }
        }
    }
}
