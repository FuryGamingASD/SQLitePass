using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Services
{
    public static class AutoTypeService
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        private const ushort VK_TAB = 0x09;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_SPACE = 0x20;

        public static async Task ExecuteAutoTypeAsync(PwEntry? entry, IntPtr targetWindow, int initialDelayMs = 500)
        {
            if (entry == null) return;

            try
            {
                if (targetWindow != IntPtr.Zero)
                {
                    SetForegroundWindow(targetWindow);
                    await Task.Delay(initialDelayMs).ConfigureAwait(false);
                }
                else
                {
                    IntPtr fg = GetForegroundWindow();
                    if (fg != IntPtr.Zero)
                    {
                        SetForegroundWindow(fg);
                    }
                    await Task.Delay(initialDelayMs).ConfigureAwait(false);
                }

                string sequence = string.IsNullOrWhiteSpace(entry.AutoTypeSequence) 
                    ? "{USERNAME}{TAB}{PASSWORD}{ENTER}" 
                    : entry.AutoTypeSequence;

                // Parse sequence template into tokens without string-replacing values first,
                // preventing special characters (like '{' and '}') in passwords/usernames from breaking tokens.
                var tokenRegex = new Regex(@"(\{[^}]+\})");
                var parts = tokenRegex.Split(sequence);

                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    if (part.StartsWith("{") && part.EndsWith("}"))
                    {
                        string upper = part.ToUpperInvariant();
                        if (upper == "{USERNAME}")
                        {
                            if (!string.IsNullOrEmpty(entry.UserName))
                            {
                                await SendStringAsync(entry.UserName).ConfigureAwait(false);
                                await Task.Delay(30).ConfigureAwait(false);
                            }
                        }
                        else if (upper == "{PASSWORD}")
                        {
                            if (!string.IsNullOrEmpty(entry.Password))
                            {
                                await SendStringAsync(entry.Password).ConfigureAwait(false);
                                await Task.Delay(30).ConfigureAwait(false);
                            }
                        }
                        else if (upper == "{TITLE}")
                        {
                            if (!string.IsNullOrEmpty(entry.Title))
                            {
                                await SendStringAsync(entry.Title).ConfigureAwait(false);
                                await Task.Delay(30).ConfigureAwait(false);
                            }
                        }
                        else if (upper == "{URL}")
                        {
                            if (!string.IsNullOrEmpty(entry.Url))
                            {
                                await SendStringAsync(entry.Url).ConfigureAwait(false);
                                await Task.Delay(30).ConfigureAwait(false);
                            }
                        }
                        else if (upper == "{NOTES}")
                        {
                            if (!string.IsNullOrEmpty(entry.Notes))
                            {
                                await SendStringAsync(entry.Notes).ConfigureAwait(false);
                                await Task.Delay(30).ConfigureAwait(false);
                            }
                        }
                        else if (upper.StartsWith("{S:") && upper.EndsWith("}"))
                        {
                            string fieldKey = part.Substring(3, part.Length - 4);
                            var cf = entry.CustomFields?.FirstOrDefault(f => string.Equals(f.Key, fieldKey, StringComparison.OrdinalIgnoreCase));
                            if (cf != null && !string.IsNullOrEmpty(cf.Value))
                            {
                                await SendStringAsync(cf.Value).ConfigureAwait(false);
                                await Task.Delay(30).ConfigureAwait(false);
                            }
                        }
                        else if (upper == "{TAB}")
                        {
                            SendVirtualKey(VK_TAB);
                            await Task.Delay(50).ConfigureAwait(false);
                        }
                        else if (upper == "{ENTER}")
                        {
                            SendVirtualKey(VK_RETURN);
                            await Task.Delay(50).ConfigureAwait(false);
                        }
                        else if (upper == "{SPACE}")
                        {
                            SendVirtualKey(VK_SPACE);
                            await Task.Delay(50).ConfigureAwait(false);
                        }
                        else if (upper.StartsWith("{DELAY "))
                        {
                            string num = upper.Substring(7, upper.Length - 8).Trim();
                            if (int.TryParse(num, out int delay) && delay > 0 && delay < 10000)
                            {
                                await Task.Delay(delay).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            // Unrecognized token, send literal text
                            await SendStringAsync(part).ConfigureAwait(false);
                            await Task.Delay(30).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await SendStringAsync(part).ConfigureAwait(false);
                        await Task.Delay(30).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-Type execution exception: {ex.Message}");
            }
        }

        private static void SendVirtualKey(ushort vk)
        {
            var down = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = vk }
                }
            };
            var up = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP }
                }
            };

            SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
        }

        private static async Task SendStringAsync(string text)
        {
            foreach (char c in text)
            {
                var down = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wScan = c,
                            dwFlags = KEYEVENTF_UNICODE
                        }
                    }
                };
                var up = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wScan = c,
                            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
                        }
                    }
                };
                SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
    }
}
