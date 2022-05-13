#if UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using static AV.Toolkit.Win32;
using WSF = AV.Toolkit.Win32.WindowStyleFlags;
using WLI = AV.Toolkit.Win32.WindowLongIndex;
using System.Reflection;

namespace AV.Toolkit
{
    static class WindowsDWM
    {
        static IntPtr mainWindow;

        static Assembly EditorAsm = typeof(Editor).Assembly;
        static FieldInfo globalEventHandlerInfo = typeof(EditorApplication).GetField("globalEventHandler", AnyBind);

        static void SetGlobalKeyHandler(EditorApplication.CallbackFunction func, bool add)
        {
            var value = (EditorApplication.CallbackFunction)globalEventHandlerInfo.GetValue(null);
            if (add) value += func; else value -= func;
            globalEventHandlerInfo.SetValue(null, value);
        }

        [InitializeOnLoadMethod]
        static void Init()
        {
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            //AssemblyReloadEvents.afterAssemblyReload += AfterAssemblyReload;
            EditorApplication.quitting += ResetWndProc;
            EditorApplication.update += OnEditorUpdate;

            FindMainWindowHandle();
            EditorApplication.delayCall += () => { FindMainWindowHandle(); SetBorderless(); };

            SetGlobalKeyHandler(OnKeyEvt, true);
        }

        static IntPtr GetMainWindow()
        {
            return mainWindow;
        }

        static void FindMainWindowHandle()
        {
            var unityVersion = Application.unityVersion;
            uint threadId = Win32.GetCurrentThreadId();
            Win32.EnumThreadWindows(threadId, (hWnd, lParam) =>
            {
                var title = GetWindowTitle(hWnd);
                if (title.Contains(unityVersion))
                {
                    mainWindow = hWnd;
                    //Debug.Log(title);
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }
//
        const string BorderlessPrefKey = "Editor Borderless Mode";
        static void BeforeAssemblyReload()
        {
            if (defWndProc == IntPtr.Zero) return;
            ResetWndProc();
        }
        static void AfterAssemblyReload()
        {
            if (EditorPrefs.GetBool(BorderlessPrefKey))
                SetWndProc();
        }

        static bool appFocused;
        static void OnEditorUpdate()
        {
            var isAppActive = UnityEditorInternal.InternalEditorUtility.isApplicationActive;
            if (!appFocused && isAppActive)
                OnUnityFocus(appFocused = true);
            else if (appFocused && !isAppActive)
                OnUnityFocus(appFocused = false);
        }
        
        static void OnUnityFocus(bool focus)
        {
            // Auto Refresh on alt-tab doesn't work in Borderless mode
            if (focus && defWndProc != IntPtr.Zero)
                AssetDatabase.Refresh();
        }

        const BindingFlags AnyBind = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (hWnd == IntPtr.Zero)
                return IntPtr.Zero;

            var activeWnd = Win32.GetActiveWindow();
            //Debug.Log($"{hWnd} {activeWnd}");
           
            //Debug.Log((WM)msg);
            //if (hWnd == activeWnd)
            {
                switch ((WM)msg)
                {
                    //case WM.INPUT: break;

                    //case WM.NCACTIVATE: if (!isMainWndActive) break;// if (!menuToggle) HideMenuBar(hWnd); break;
                    //case WM.NCPAINT: if (!isMainWndActive) { return IntPtr.Zero; } break; //if (!menuToggle) { HideMenuBar(hWnd); DrawMenuBar(hWnd); } break;

                    case WM.NCACTIVATE: // issue: when menu bar is hidden, floating windows do not respond to window drag
                    case WM.NCCALCSIZE: if (!menuToggle) { return IntPtr.Zero; } break;
                }
            }
            return Win32.CallWindowProc(defWndProc, hWnd, msg, wParam, lParam);
        }

        static bool menuToggle;
        static void OnKeyEvt()
        {
            var evt = Event.current;
            
            bool altPress = evt.alt && evt.keyCode == KeyCode.BackQuote && evt.type == EventType.KeyUp;

            if (!altPress) 
                return;
            menuToggle = !menuToggle;

            var targetWindow = GetMainWindow();
            var menuHandle = Win32.GetMenu(targetWindow);
            if (menuHandle != IntPtr.Zero)
            {
                if (menuToggle)
                {
                    Win32.GetWindowRect(targetWindow, out var r);
                    Win32.DrawMenuBar(targetWindow);
                    //Debug.Log(r);
                    //EditorApplication.delayCall += () => SetWindowPos(targetWindow, r.l - 4, r.t - 4, r.r + 6, r.b + 6);
                    //var isFullscreen = Win32.IsFullscreen(targetWindow);
                    //Debug.Log(isFullscreen);
                }

                if (!menuToggle)
                {
                    HideMenuBar(targetWindow);
                    Win32.DrawMenuBar(targetWindow);
                }
            }
        }

        static void HideMenuBar(IntPtr wnd)
        {
            var menuHandle = Win32.GetMenu(wnd);
            var menuItemCount = Win32.GetMenuItemCount(menuHandle);
            for (var i = 0; i < menuItemCount; i++)
                Win32.SetMenu(menuHandle, IntPtr.Zero);
        }

        static IntPtr defWndProc;

        //
        [MenuItem("View/Toggle Borderless _F11")]
        //[Shortcut("Toggle Borderless", KeyCode.F11)] // shortcut doesn't work in fullscreen game view :(
        static void ToggleBorderless()
        {
            EditorPrefs.SetBool(BorderlessPrefKey, !EditorPrefs.GetBool(BorderlessPrefKey));
            SetBorderless();
        }
        static void SetBorderless()
        {
            var toggle = EditorPrefs.GetBool(BorderlessPrefKey);
            var targetWindow = GetMainWindow();
            var winTitle = Win32.GetWindowTitle(targetWindow);

            var pad = Win32.WindowPadding;
            if (toggle && defWndProc == IntPtr.Zero)
            {
                SetWndProc();
                SetStyle();
            }
            else
            {
                pad = -pad;
                ResetWndProc();
                ResetStyle();
            }
            var isFullscreen = Win32.IsFullscreen(targetWindow);
            if (isFullscreen)
            {
                SetWindowPadding(targetWindow, pad, pad, pad, pad);
            }
            Win32.DrawMenuBar(targetWindow);
        }

        static void SetWindowPos(IntPtr targetWindow, int l, int t, int r, int b)
        {
            Win32.SetWindowPos
            (
                targetWindow, 0, l, t, r, b,
                SetWindowPosFlags.ShowWindow | SetWindowPosFlags.NoOwnerZOrder | SetWindowPosFlags.NoSendChanging
            );
        }
        static void SetWindowPadding(IntPtr targetWindow, int l, int t, int r, int b)
        {
            Win32.GetWindowRect(targetWindow, out var rect);
            rect.Pad(l, t, r, b);
            Win32.SetWindowPos
            (
                targetWindow, 0, rect.l, rect.t, rect.r, rect.b,
                SetWindowPosFlags.ShowWindow | SetWindowPosFlags.NoOwnerZOrder | SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoSendChanging
            );
        }

        const WSF DefaultStyleBase = WSF.Tiled | WSF.ExtendedLeft | WSF.TiledWindow | WSF.ExtendedRightScrollbar | WSF.ExtendedLTRReading;   
        const WSF DefaultStyle = DefaultStyleBase | WSF.OverlappedWindow | WSF.Maximize | WSF.ExtendedComposited | WSF.ClipSiblings | WSF.Visible;
        const WSF DefaultStyleExtended = DefaultStyleBase | WSF.ExtendedWindowEdge;

        const WSF NewStyle = DefaultStyle & ~(
                    //WSF.Caption     | // composite of Border and DialogFrame
                    WSF.Border        |
                    WSF.DialogFrame   |
                    WSF.ThickFrame    
                    //WSF.SystemMenu  | 
                    //WSF.MaximizeBox | // same as TabStop
                    //WSF.MinimizeBox   // same as Group
                );
        const WSF NewStyleExtended = DefaultStyleExtended & ~(
                    WSF.ExtendedDlgModalFrame |
                    WSF.ExtendedComposited    |
                    WSF.ExtendedWindowEdge    |
                    WSF.ExtendedClientEdge    |
                    WSF.ExtendedLayered       |
                    WSF.ExtendedStaticEdge    |
                    WSF.ExtendedToolWindow    |
                    WSF.ExtendedAppWindow
                );

        static void SetStyle()
        {
            var targetWindow = GetMainWindow();
            //var defaultStyle = (WSF)Win32.GetWindowLong(targetWindow, WLI.Style);
            //var defaultStyleExtended = (WSF)Win32.GetWindowLong(targetWindow, WLI.ExtendedStyle);
            Win32.SetWindowLong(targetWindow, WLI.Style, (IntPtr)NewStyle);
            Win32.SetWindowLong(targetWindow, WLI.ExtendedStyle, (IntPtr)NewStyleExtended);
        }

        static void SetWndProc()
        {
            if (defWndProc == IntPtr.Zero)
            {
                var targetWindow = GetMainWindow();
                defWndProc = Win32.GetWindowLong(targetWindow, WLI.WindowProc);

                Win32.WndProc newWndProc = WindowProc;
                Win32.SetWindowLong(targetWindow, WLI.WindowProc, newWndProc.Method.MethodHandle.GetFunctionPointer());
            }
        }
        static void ResetWndProc()
        {
            if (defWndProc != IntPtr.Zero)
            {
                Win32.SetWindowLong(GetMainWindow(), WLI.WindowProc, defWndProc);
                defWndProc = IntPtr.Zero;
            }
        }
        static void ResetStyle()
        {
            var targetWindow = GetMainWindow();
            Win32.SetWindowLong(targetWindow, WLI.Style, (IntPtr)DefaultStyle);
            Win32.SetWindowLong(targetWindow, WLI.ExtendedStyle, (IntPtr)DefaultStyleExtended);
        }


        // ?
        [DllImport("dwmapi.dll")]
        static extern bool DwmDefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref IntPtr plResult);
    }
}
#endif
