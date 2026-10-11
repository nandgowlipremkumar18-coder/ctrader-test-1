$csharp = @"
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class BrightnessController {
    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

    [DllImport("dxva2.dll")]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll")]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll")]
    public static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);

    [DllImport("dxva2.dll")]
    public static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    public delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    public static void SetBrightness(uint targetBrightness) {
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData) {
                uint num = 0;
                if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref num) && num > 0) {
                    PHYSICAL_MONITOR[] pms = new PHYSICAL_MONITOR[num];
                    if (GetPhysicalMonitorsFromHMONITOR(hMonitor, num, pms)) {
                        for (int i = 0; i < num; i++) {
                            SetMonitorBrightness(pms[i].hPhysicalMonitor, targetBrightness);
                        }
                        DestroyPhysicalMonitors(num, pms);
                    }
                }
                return true;
            }, IntPtr.Zero);
    }
}
"@

# Compile the C# code
Add-Type -TypeDefinition $csharp -ErrorAction Stop

# Check current theme setting to determine if we are toggling TO Night Mode or TO Day Mode
$themePath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$appsUseLightTheme = Get-ItemPropertyValue -Path $themePath -Name 'AppsUseLightTheme' -ErrorAction SilentlyContinue

$targetBrightness = 100
$targetThemeValue = 1

if ($null -eq $appsUseLightTheme -or $appsUseLightTheme -eq 1) {
    # Currently Light Mode, switch to Night Mode
    $targetBrightness = 0
    $targetThemeValue = 0
    Write-Host "Switching to Night Mode (Brightness 0%, Dark Theme)"
} else {
    # Currently Night Mode, switch to Day Mode
    $targetBrightness = 100
    $targetThemeValue = 1
    Write-Host "Switching to Day Mode (Brightness 100%, Light Theme)"
}

# 1. Set screen brightness
try {
    [BrightnessController]::SetBrightness($targetBrightness)
} catch {
    Write-Warning "Failed to set monitor brightness via DDC/CI."
}

# 2. Set Windows Dark/Light Theme
try {
    Set-ItemProperty -Path $themePath -Name 'AppsUseLightTheme' -Value $targetThemeValue -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $themePath -Name 'SystemUsesLightTheme' -Value $targetThemeValue -ErrorAction SilentlyContinue
} catch {
    Write-Warning "Failed to set Windows theme."
}
