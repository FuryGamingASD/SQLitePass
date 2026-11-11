using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
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
                    await Task.Delay(initialDelayMs).ConfigureAwait(false);
                }

                string sequence = string.IsNullOrWhiteSpace(entry.AutoTypeSequence) 
                    ? "{USERNAME}{TAB}{PASSWORD}{ENTER}" 
                    : entry.AutoTypeSequence;

                // Replace standard tags
                sequence = sequence.Replace("{USERNAME}", entry.UserName ?? "", StringComparison.OrdinalIgnoreCase);
                sequence = sequence.Replace("{PASSWORD}", entry.Password ?? "", StringComparison.OrdinalIgnoreCase);
                sequence = sequence.Replace("{TITLE}", entry.Title ?? "", StringComparison.OrdinalIgnoreCase);
                sequence = sequence.Replace("{URL}", entry.Url ?? "", StringComparison.OrdinalIgnoreCase);
                sequence = sequence.Replace("{NOTES}", entry.Notes ?? "", StringComparison.OrdinalIgnoreCase);

                // Replace custom field tags: {S:FieldName}
                if (entry.CustomFields != null)
                {
                    foreach (var cf in entry.CustomFields.ToArray())
                    {
                        if (cf != null && !string.IsNullOrEmpty(cf.Key))
                        {
                            string tag = $"{{S:{cf.Key}}}";
                            sequence = sequence.Replace(tag, cf.Value ?? "", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }

                // Parse and send tokens
                var tokenRegex = new Regex(@"(\{[^}]+\})");
                var parts = tokenRegex.Split(sequence);

                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    if (part.StartsWith("{") && part.EndsWith("}"))
                    {
                        string upper = part.ToUpperInvariant();
                        if (upper == "{TAB}")
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
                    }
                    else
                    {
                        SendString(part);
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

        private static void SendString(string text)
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
                Thread.Sleep(10);
            }
        }
    }
}
