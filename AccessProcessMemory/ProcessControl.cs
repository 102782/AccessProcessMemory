using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace AccessProcessMemory
{
    internal enum GetWindowCommands : uint
    {
        HWNDFIRST = 0,
        HWNDLAST = 1,
        HWNDNEXT = 2,
        HWNDPREV = 3,
        OWNER = 4,
        CHILD = 5,
        ENABLEDPOPUP = 6
    }

    [Flags]
    internal enum SnapshotFlags : uint
    {
        HeapList = 0x00000001,
        Process = 0x00000002,
        Thread = 0x00000004,
        Module = 0x00000008,
        Module32 = 0x00000010,
        Inherit = 0x80000000,
        All = 0x0000001F,
        NoHeaps = 0x40000000
    }

    [Flags]
    internal enum ProcessAccessFlags : uint
    {
        All = 0x001F0FFF,
        Terminate = 0x00000001,
        CreateThread = 0x00000002,
        VMOperation = 0x00000008,
        VMRead = 0x00000010,
        VMWrite = 0x00000020,
        DupHandle = 0x00000040,
        SetInformation = 0x00000200,
        QueryInformation = 0x00000400,
        Synchronize = 0x00100000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct PROCESSENTRY32
    {
        const int MAX_PATH = 260;
        internal UInt32 dwSize;
        internal UInt32 cntUsage;
        internal UInt32 th32ProcessID;
        internal IntPtr th32DefaultHeapID;
        internal UInt32 th32ModuleID;
        internal UInt32 cntThreads;
        internal UInt32 th32ParentProcessID;
        internal Int32 pcPriClassBase;
        internal UInt32 dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        internal string szExeFile;
    }

    internal delegate bool CallBackPtr(IntPtr hwnd, ref IntPtr lParam);

    public class ProcessControl : IDisposable
    {
        internal readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        #region DllImports
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateToolhelp32Snapshot(
            SnapshotFlags dwFlags,
            uint th32ProcessID
        );

        [DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        internal static extern bool Process32First(
            [In]IntPtr hSnapshot,
            ref PROCESSENTRY32 lppe
        );

        [DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        internal static extern bool Process32Next(
            [In]IntPtr hSnapshot,
            ref PROCESSENTRY32 lppe
        );

        [DllImport("kernel32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(
            [In] IntPtr hObject // オブジェクトのハンドル
        );

        [DllImport("kernel32.dll")]
        internal static extern IntPtr OpenProcess(
            ProcessAccessFlags dwDesiredAccess,                     // アクセスフラグ
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,    // ハンドルの継承オプション
            uint dwProcessId                                        // プロセス識別子
        );

        [DllImport("psapi.dll", CallingConvention=CallingConvention.StdCall, SetLastError = true)]
        internal static extern int EnumProcessModules(
            IntPtr hProcess,        // プロセスのハンドル
            [Out] IntPtr lphModule, // モジュールハンドルを受け取る配列
            uint cb,                // 配列のサイズ
            out uint lpcbNeeded     // 必要なバイト数
        );

        [DllImport("psapi.dll")]
        private extern static uint GetModuleBaseName(
            IntPtr hProcess,            // プロセスのハンドル
            IntPtr hModule,             // モジュールのハンドル
            StringBuilder lpBaseName,   // ベース名を受け取るバッファ
            uint nSize                  // 取得したい文字の最大の長さ
        );

        [DllImport("kernel32.dll", SetLastError=true)]
        internal static extern bool ReadProcessMemory(
            IntPtr hProcess,                // プロセスのハンドル
            IntPtr lpBaseAddress,           // 読み取り開始アドレス
            [Out] byte[] buffer,            // データを格納するバッファ
            UInt32 size,                    // 読み取りたいバイト数
            out uint lpNumberOfBytesRead    // 読み取ったバイト数
        );

        [DllImport("kernel32.dll",SetLastError = true)]
        internal static extern bool WriteProcessMemory(
            IntPtr hProcess,                // プロセスのハンドル
            IntPtr lpBaseAddress,           // 書き込み開始アドレス
            byte[] lpBuffer,                // データバッファ
            uint nSize,                     // 書き込みたいバイト数
            out uint lpNumberOfBytesWritten // 実際に書き込まれたバイト数
        );

        [DllImport("User32.dll")]
        private extern static bool EnumWindows(
            CallBackPtr lpEnumFunc,     // コールバック関数
            ref IntPtr lParam           // アプリケーション定義の値
        );

        [DllImport("User32.dll")]
        internal extern static IntPtr GetWindow(
            IntPtr hWnd,            // 元ウィンドウのハンドル
            GetWindowCommands uCmd  // 関係
        );

        [DllImport("User32.dll")]
        internal extern static bool IsWindowVisible(
            IntPtr hWnd   // ウィンドウのハンドル
        );

        [DllImport("User32.dll")]
        internal extern static bool SetForegroundWindow(
            IntPtr hWnd   // ウィンドウのハンドル
        );

        [DllImport("User32.dll")]
        internal extern static int GetWindowText(
            IntPtr hWnd,                // ウィンドウまたはコントロールのハンドル
            StringBuilder lpString,     // テキストバッファ
            int nMaxCount               // コピーする最大文字数
        );
        #endregion

        public uint processId { get; private set; }
        private IntPtr processHandle { get; set; }
        public string exeFile { get; private set; }

        private CallBackPtr callBackPtr;
        public string windowText { get; private set; }
        public IntPtr windowHandle { get; private set; }

        public bool isOpened { get { return this.processHandle != default(IntPtr) && this.processHandle != INVALID_HANDLE_VALUE; } }

        private bool _disposed;

        public ProcessControl(string exeFile, string windowText)
        {
            this.exeFile = exeFile;
            this.windowText = windowText;
            this.processId = default(uint);
            this.processHandle = default(IntPtr);
            this.windowHandle = default(IntPtr);

            Open();
        }

        #region DisposePattern
        protected virtual void Dispose(bool disposing)
        {
            if (!this._disposed)
            {
                if (disposing)
                {
                    Close();
                }
            }
            this._disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ProcessControl()
        {
            Dispose(false);
        }
        #endregion

        private uint SearchProcesses(string exeFile)
        {
            var handleSnap = CreateToolhelp32Snapshot(SnapshotFlags.Process, 0);
            if (handleSnap == INVALID_HANDLE_VALUE)
            {
                return 0;
            }

            var processEntry = new PROCESSENTRY32();
            processEntry.dwSize = (uint)Marshal.SizeOf(processEntry);
            var isNextProcess = Process32First(handleSnap, ref processEntry);

            uint processId = 0;
            while (isNextProcess)
            {
                if (processEntry.szExeFile == exeFile)
                {
                    processId = processEntry.th32ProcessID;
                    break;
                }
                isNextProcess = Process32Next(handleSnap, ref processEntry);
            }

            CloseHandle(handleSnap);
            return processId;
        }

        private bool Open()
        {
            this.processId = SearchProcesses(this.exeFile);
            var p = OpenProcess(ProcessAccessFlags.All, true, this.processId);
            if (p != INVALID_HANDLE_VALUE)
            {
                this.processHandle = p;
                return true;
            }
            return false;
        }

        private void Close()
        {
            if (this.isOpened)
            {
                CloseHandle(this.processHandle);
                this.processHandle = default(IntPtr);
            }
        }

        public byte[] Read(IntPtr baseAddress, uint size)
        {
            var buffer = new byte[size];
            uint numberOfBytesRead;
            if (this.isOpened)
            {
                ReadProcessMemory(this.processHandle, baseAddress, buffer, size, out numberOfBytesRead);
            }
            return buffer;
        }

        public bool write(IntPtr baseAddress, byte[] buffer, uint size)
        {
            uint numberOfBytesWritten;
            if (this.isOpened)
            {
                return WriteProcessMemory(this.processHandle, baseAddress, buffer, (uint)Marshal.SizeOf(buffer), out numberOfBytesWritten);
            }
            return false;
        }

        private bool EnumWindowsProcess(IntPtr hwnd, ref IntPtr lp)
        {
            StringBuilder strWindowText = new StringBuilder("", 1024);
            GetWindowText(hwnd, strWindowText, 1024);
            if (strWindowText.ToString() == "") return true;
            if ((GetWindow(hwnd, GetWindowCommands.OWNER) == IntPtr.Zero) && IsWindowVisible(hwnd))
            {
                if (strWindowText.ToString() == this.windowText)
                {
                    lp = hwnd;
                    return false;
                }
            }

            return true;
        }

        private void SearchWindow()
        {
            if (this.windowText == "") return;
            IntPtr w = new IntPtr();
            this.callBackPtr = new CallBackPtr(EnumWindowsProcess);
            EnumWindows(this.callBackPtr, ref w);
            if (w != INVALID_HANDLE_VALUE)
            {
                this.windowHandle = w;
            }
        }

        public void Activate()
        {
            SearchWindow();
            if (this.windowHandle != default(IntPtr) && this.windowHandle != INVALID_HANDLE_VALUE)
            {
                SetForegroundWindow(this.windowHandle);
            }
        }

        public int GetLastError()
        {
            return Marshal.GetLastWin32Error();
        }
    }
}
