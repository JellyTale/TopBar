using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static TopBar.NativeMethods;

namespace TopBar
{
    public sealed class TrayIconInfo
    {
        public string Name { get; set; } = "";
        public ImageSource? Icon { get; set; }
        internal AutomationElement? Element { get; set; }
    }

    internal static class TrayIconReader
    {
        public static string LastDebugInfo { get; private set; } = "";

        // These are standard taskbar UI elements, NOT tray icons
        private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Notification Chevron", "Show Hidden Icons", "Start",
            "Search", "Task View", "Widgets", "Chat", "Copilot",
            "Show desktop", "Desktops", "Taskbar", "Running applications",
            "System Promoted Notification Area", ""
        };

        // Known container names/IDs that hold notification icons
        private static readonly string[] NotifyContainerMarkers = {
            "NotifyIcon", "SystemTray", "Notification", "System Tray",
            "TrayNotify", "SysPager"
        };

        public static List<TrayIconInfo> GetTrayIcons()
        {
            var result = new List<TrayIconInfo>();
            var debug = new StringBuilder();

            try
            {
                var trayHwnd = FindWindow("Shell_TrayWnd", null);
                debug.AppendLine($"Shell_TrayWnd: {(trayHwnd != IntPtr.Zero ? "found" : "NOT found")}");
                if (trayHwnd == IntPtr.Zero) { LastDebugInfo = debug.ToString(); return result; }

                // Enumerate ALL child windows of Shell_TrayWnd
                var childWindows = new List<IntPtr>();
                EnumChildWindows(trayHwnd, (hwnd, _) => { childWindows.Add(hwnd); return true; }, IntPtr.Zero);
                debug.AppendLine($"Child HWNDs: {childWindows.Count}");

                var classNameBuf = new System.Text.StringBuilder(256);

                foreach (var hwnd in childWindows)
                {
                    GetClassName(hwnd, classNameBuf, 256);
                    var cls = classNameBuf.ToString();

                    // Only probe windows that are likely to contain the notification area
                    bool shouldProbe =
                        cls.Contains("DesktopWindowContentBridge") ||
                        cls == "TrayNotifyWnd" ||
                        cls == "SysPager" ||
                        cls == "ToolbarWindow32";

                    if (!shouldProbe) continue;

                    try
                    {
                        var element = AutomationElement.FromHandle(hwnd);
                        debug.AppendLine($"Probing: cls='{cls}'");

                        // Step 1: find notification area containers in this tree
                        var notifyContainers = new List<AutomationElement>();
                        FindNotifyContainers(element, notifyContainers, 0, 20, debug);
                        debug.AppendLine($"  Notify containers: {notifyContainers.Count}");

                        // Step 2: collect buttons ONLY from within those containers
                        foreach (var container in notifyContainers)
                        {
                            var buttons = new List<AutomationElement>();
                            CollectAllButtons(container, buttons, 0, 10);
                            debug.AppendLine($"  Container '{SafeName(container)}': {buttons.Count} buttons");

                            foreach (var btn in buttons)
                            {
                                try
                                {
                                    var name = btn.Current.Name ?? "";
                                    if (!string.IsNullOrWhiteSpace(name) && !SkipNames.Contains(name))
                                    {
                                        result.Add(new TrayIconInfo { Name = name, Element = btn });
                                        debug.AppendLine($"    + '{name}'");
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch (Exception ex) { debug.AppendLine($"  ERR: {ex.Message}"); }
                }

                // ── Overflow window ──
                var overflowHwnd = FindWindow("NotifyIconOverflowWindow", null);
                if (overflowHwnd == IntPtr.Zero)
                    overflowHwnd = FindWindow("TopLevelWindowForOverflowXamlIsland", null);
                debug.AppendLine($"Overflow: {(overflowHwnd != IntPtr.Zero ? "found" : "NOT found")}");

                if (overflowHwnd != IntPtr.Zero)
                {
                    try
                    {
                        var ovChildren = new List<IntPtr> { overflowHwnd };
                        EnumChildWindows(overflowHwnd, (h, _) => { ovChildren.Add(h); return true; }, IntPtr.Zero);

                        foreach (var hwnd in ovChildren)
                        {
                            try
                            {
                                var el = AutomationElement.FromHandle(hwnd);
                                // For overflow, grab all buttons — they're all tray icons
                                var buttons = new List<AutomationElement>();
                                CollectAllButtons(el, buttons, 0, 15);
                                foreach (var btn in buttons)
                                {
                                    var name = btn.Current.Name ?? "";
                                    if (!string.IsNullOrWhiteSpace(name) && !SkipNames.Contains(name))
                                    {
                                        result.Add(new TrayIconInfo { Name = name, Element = btn });
                                        debug.AppendLine($"  OV + '{name}'");
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex) { debug.AppendLine($"  OV ERR: {ex.Message}"); }
                }

                // ── Diagnostic: if nothing found, dump tree structure ──
                if (result.Count == 0)
                {
                    debug.AppendLine("=== Full tree dump (first 4 levels) ===");
                    foreach (var hwnd in childWindows)
                    {
                        GetClassName(hwnd, classNameBuf, 256);
                        var cls = classNameBuf.ToString();
                        if (!cls.Contains("DesktopWindowContentBridge")) continue;

                        try
                        {
                            var el = AutomationElement.FromHandle(hwnd);
                            DumpTree(el, debug, 0, 4);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { debug.AppendLine($"FATAL: {ex.Message}"); }

            // Deduplicate
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<TrayIconInfo>();
            foreach (var icon in result)
            {
                if (!string.IsNullOrWhiteSpace(icon.Name) && seen.Add(icon.Name))
                    deduped.Add(icon);
            }
            result = deduped;

            // Resolve icons from process executables
            foreach (var icon in result)
                icon.Icon ??= FindProcessIcon(icon.Name);

            debug.AppendLine($"Total: {result.Count} icons");
            LastDebugInfo = debug.ToString();
            return result;
        }

        // ── Tree walking helpers ────────────────────────────────────────────

        /// <summary>
        /// Walk the raw tree to find elements whose Name or AutomationId
        /// indicates they are notification area containers.
        /// </summary>
        private static void FindNotifyContainers(AutomationElement parent,
            List<AutomationElement> found, int depth, int maxDepth, StringBuilder debug)
        {
            if (depth > maxDepth) return;

            var walker = TreeWalker.RawViewWalker;
            AutomationElement? child = null;
            try { child = walker.GetFirstChild(parent); }
            catch { return; }

            int siblings = 0;
            while (child != null && siblings < 100)
            {
                siblings++;
                try
                {
                    var name = child.Current.Name ?? "";
                    var autoId = child.Current.AutomationId ?? "";

                    bool isNotifyContainer = false;
                    foreach (var marker in NotifyContainerMarkers)
                    {
                        if (name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            autoId.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isNotifyContainer = true;
                            break;
                        }
                    }

                    if (isNotifyContainer)
                    {
                        found.Add(child);
                        debug.AppendLine($"  Found container: '{name}' id='{autoId}'");
                    }
                    else
                    {
                        // Keep searching deeper
                        FindNotifyContainers(child, found, depth + 1, maxDepth, debug);
                    }
                }
                catch { }

                try { child = walker.GetNextSibling(child); }
                catch { break; }
            }
        }

        /// <summary>
        /// Collect all Button-type elements from within a subtree.
        /// </summary>
        private static void CollectAllButtons(AutomationElement parent,
            List<AutomationElement> found, int depth, int maxDepth)
        {
            if (depth > maxDepth || found.Count > 100) return;

            var walker = TreeWalker.RawViewWalker;
            AutomationElement? child = null;
            try { child = walker.GetFirstChild(parent); }
            catch { return; }

            int siblings = 0;
            while (child != null && siblings < 100)
            {
                siblings++;
                try
                {
                    var ct = child.Current.ControlType;
                    if (ct == ControlType.Button)
                        found.Add(child);

                    CollectAllButtons(child, found, depth + 1, maxDepth);
                }
                catch { }

                try { child = walker.GetNextSibling(child); }
                catch { break; }
            }
        }

        /// <summary>Dump tree for diagnostics.</summary>
        private static void DumpTree(AutomationElement parent, StringBuilder debug,
            int depth, int maxDepth)
        {
            if (depth > maxDepth) return;

            var walker = TreeWalker.RawViewWalker;
            AutomationElement? child = null;
            try { child = walker.GetFirstChild(parent); }
            catch { return; }

            int siblings = 0;
            while (child != null && siblings < 30)
            {
                siblings++;
                try
                {
                    var ct = child.Current.ControlType;
                    var name = child.Current.Name ?? "";
                    var autoId = child.Current.AutomationId ?? "";
                    var cls = child.Current.ClassName ?? "";
                    var indent = new string(' ', depth * 2);
                    debug.AppendLine($"{indent}[{ct.ProgrammaticName}] '{name}' id='{autoId}' cls='{cls}'");

                    DumpTree(child, debug, depth + 1, maxDepth);
                }
                catch { }

                try { child = walker.GetNextSibling(child); }
                catch { break; }
            }
        }

        private static string SafeName(AutomationElement el)
        {
            try { return el.Current.Name ?? ""; }
            catch { return ""; }
        }

        // ── Click forwarding ────────────────────────────────────────────────

        public static void SendClick(TrayIconInfo info)
        {
            if (info.Element == null) return;
            try
            {
                if (info.Element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
                    ((InvokePattern)pattern).Invoke();
            }
            catch { }
        }

        public static void SendRightClick(TrayIconInfo info) { }

        // ── Process icon matching ───────────────────────────────────────────

        private static ImageSource? FindProcessIcon(string trayName)
        {
            if (string.IsNullOrWhiteSpace(trayName)) return null;
            var lower = trayName.ToLowerInvariant();

            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        var mod = proc.MainModule;
                        if (mod?.FileName == null) continue;

                        var procName = proc.ProcessName.ToLowerInvariant();
                        if (procName.Length < 3) continue;

                        var fi = FileVersionInfo.GetVersionInfo(mod.FileName);
                        var desc = (fi.FileDescription ?? "").ToLowerInvariant();
                        var product = (fi.ProductName ?? "").ToLowerInvariant();

                        bool match = lower.Contains(procName);
                        if (!match && desc.Length > 2) match = lower.Contains(desc);
                        if (!match && product.Length > 2) match = lower.Contains(product);
                        if (!match)
                        {
                            var words = trayName.Split(new[] { ' ', '-', ':', '\n', '\r' },
                                StringSplitOptions.RemoveEmptyEntries);
                            if (words.Length > 0 && words[0].Length > 2 &&
                                procName.Contains(words[0].ToLowerInvariant()))
                                match = true;
                        }

                        if (match)
                        {
                            var icon = System.Drawing.Icon.ExtractAssociatedIcon(mod.FileName);
                            if (icon != null)
                            {
                                var src = Imaging.CreateBitmapSourceFromHIcon(
                                    icon.Handle, Int32Rect.Empty,
                                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                                src.Freeze();
                                icon.Dispose();
                                return src;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }
    }
}
