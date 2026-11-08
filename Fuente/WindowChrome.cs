using System.Runtime.InteropServices;

namespace NitroPulse;

/// <summary>Matches the OS title bar / window frame to the active NitroPulse theme (Win10/11 DWM).</summary>
internal static class WindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Form form)
    {
        if (!form.IsHandleCreated) return;
        IntPtr hwnd = form.Handle;
        int dark = Theme.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        // COLORREF = 0x00BBGGRR
        int caption = ToColorRef(Theme.Bg);
        int border = ToColorRef(Theme.Bg);
        int text = ToColorRef(Theme.Text);
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);
}
