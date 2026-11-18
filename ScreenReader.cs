using System;
using System.Runtime.InteropServices;

namespace AccessibleGridGame
{
    /// <summary>
    /// prism.dll 的精簡封裝：全遊戲共用同一個語音通道。
    /// 所有動態訊息都透過 <see cref="Speak"/> 直接驅動 NVDA（或 Prism 挑出的最佳後端），
    /// 不再依賴 UIA Live Region 通知。
    /// </summary>
    public static class ScreenReader
    {
        private const string PrismLibrary = "prism.dll";

        // prism.h: PRISM_ERROR_ALREADY_INITIALIZED = 15
        // create_best 取得的後端可能已完成初始化，這時不算錯誤。
        private const int PrismAlreadyInitialized = 15;

        /// <summary>
        /// 對應 prism.h 的 PrismConfig（PRISM_CONFIG_VERSION = 4）。
        /// 用 byte 代表 C 的 bool，確保 1 byte 對齊與原生結構一致。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PrismConfig
        {
            public byte version;
            public IntPtr registry;
            public IntPtr availabilityCallback;
            public IntPtr availabilityUserdata;
            public uint availabilityPollIntervalMs;
            public uint availabilityDebounceSamples;
            public uint availabilityBackoffMaxMs;
            public byte availabilityAutoPowerManage;
            public IntPtr availabilityBaselineCallback;
        }

        [DllImport(PrismLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern PrismConfig prism_config_init();

        [DllImport(PrismLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_init(ref PrismConfig cfg);

        [DllImport(PrismLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void prism_shutdown(IntPtr ctx);

        [DllImport(PrismLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr prism_registry_create_best(IntPtr ctx);

        [DllImport(PrismLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int prism_backend_initialize(IntPtr backend);

        [DllImport(PrismLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int prism_backend_speak(
            IntPtr backend,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            [MarshalAs(UnmanagedType.I1)] bool interrupt);

        [DllImport(PrismLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int prism_backend_stop(IntPtr backend);

        [DllImport(PrismLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void prism_backend_free(IntPtr backend);

        private static readonly object Sync = new object();
        private static IntPtr _context = IntPtr.Zero;
        private static IntPtr _backend = IntPtr.Zero;

        /// <summary>Prism 與後端是否都成功啟動。</summary>
        public static bool IsInitialized
        {
            get
            {
                lock (Sync)
                {
                    return _context != IntPtr.Zero && _backend != IntPtr.Zero;
                }
            }
        }

        /// <summary>
        /// 初始化 Prism 並建立優先順序最高的後端（NVDA 在場時會被選中）。
        /// 可重複呼叫；失敗時靜默降級，不讓缺少 DLL 或 NVDA 的環境直接崩潰。
        /// </summary>
        public static void Initialize()
        {
            lock (Sync)
            {
                if (_context != IntPtr.Zero)
                {
                    return;
                }

                try
                {
                    PrismConfig config = prism_config_init();
                    _context = prism_init(ref config);
                    if (_context == IntPtr.Zero)
                    {
                        return;
                    }

                    _backend = prism_registry_create_best(_context);
                    if (_backend == IntPtr.Zero)
                    {
                        prism_shutdown(_context);
                        _context = IntPtr.Zero;
                        return;
                    }

                    int initResult = prism_backend_initialize(_backend);
                    if (initResult != 0 && initResult != PrismAlreadyInitialized)
                    {
                        prism_backend_free(_backend);
                        _backend = IntPtr.Zero;
                        prism_shutdown(_context);
                        _context = IntPtr.Zero;
                    }
                }
                catch (DllNotFoundException)
                {
                    Reset();
                }
                catch (EntryPointNotFoundException)
                {
                    Reset();
                }
                catch (BadImageFormatException)
                {
                    Reset();
                }
            }
        }

        /// <summary>
        /// 透過單一語音通道朗讀文字。預設中斷上一句，確保玩家永遠聽到最新狀態。
        /// </summary>
        public static void Speak(string text, bool interrupt = true)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            lock (Sync)
            {
                if (_backend == IntPtr.Zero)
                {
                    return;
                }

                try
                {
                    prism_backend_speak(_backend, text, interrupt);
                }
                catch (DllNotFoundException)
                {
                    // 語音是加分項，失敗時不中斷遊戲流程。
                }
                catch (EntryPointNotFoundException)
                {
                    // 同上。
                }
            }
        }

        /// <summary>立刻停止目前朗讀（例如要撥放角色語音檔前）。</summary>
        public static void Stop()
        {
            lock (Sync)
            {
                if (_backend == IntPtr.Zero)
                {
                    return;
                }

                try
                {
                    prism_backend_stop(_backend);
                }
                catch (DllNotFoundException)
                {
                    // 靜默降級。
                }
                catch (EntryPointNotFoundException)
                {
                    // 靜默降級。
                }
            }
        }

        /// <summary>釋放後端與 Prism 內容；程式結束前呼叫一次即可。</summary>
        public static void Shutdown()
        {
            lock (Sync)
            {
                if (_backend != IntPtr.Zero)
                {
                    try
                    {
                        prism_backend_stop(_backend);
                    }
                    catch (DllNotFoundException)
                    {
                        // 靜默降級。
                    }
                    catch (EntryPointNotFoundException)
                    {
                        // 靜默降級。
                    }
                    finally
                    {
                        prism_backend_free(_backend);
                        _backend = IntPtr.Zero;
                    }
                }

                if (_context != IntPtr.Zero)
                {
                    prism_shutdown(_context);
                    _context = IntPtr.Zero;
                }
            }
        }

        private static void Reset()
        {
            _backend = IntPtr.Zero;
            if (_context != IntPtr.Zero)
            {
                try
                {
                    prism_shutdown(_context);
                }
                catch (DllNotFoundException)
                {
                    // 連 shutdown 都失敗也只能放行，遊戲本體不該因此掛掉。
                }
                catch (EntryPointNotFoundException)
                {
                    // 同上。
                }
                finally
                {
                    _context = IntPtr.Zero;
                }
            }
        }
    }
}
