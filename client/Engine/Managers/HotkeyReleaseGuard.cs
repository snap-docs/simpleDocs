using System.Diagnostics;
using System.Threading.Tasks;

namespace CodeExplainer.Engine.Managers
{
    internal static class HotkeyReleaseGuard
    {
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_SPACE = 0x20;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;

        public static async Task WaitForTriggerKeysToSettleAsync(int timeoutMs = 700)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (!IsPressed(VK_SHIFT) &&
                    !IsPressed(VK_CONTROL) &&
                    !IsPressed(VK_SPACE) &&
                    !IsPressed(VK_LSHIFT) &&
                    !IsPressed(VK_RSHIFT) &&
                    !IsPressed(VK_LCONTROL) &&
                    !IsPressed(VK_RCONTROL))
                {
                    await Task.Delay(40);
                    return;
                }

                await Task.Delay(20);
            }

            await Task.Delay(40);
        }

        private static bool IsPressed(int virtualKey)
        {
            return (Win32Native.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }
    }
}
