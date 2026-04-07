using System.Diagnostics;
using Microsoft.Win32;

namespace ProdToy;

/// <summary>
/// Manages Windows "Apps &amp; Features" registration via the Uninstall registry key.
/// All operations target HKEY_CURRENT_USER (no elevation required).
/// </summary>
static class AppRegistry
{
    private const string UninstallKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ProdToy";

    /// <summary>
    /// Returns true if ProdToy is registered in Windows "Apps &amp; Features".
    /// </summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            return key != null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AppRegistry.IsRegistered failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns the installed DisplayVersion from the registry, or null if not registered.
    /// </summary>
    public static string? GetInstalledVersion()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            return key?.GetValue("DisplayVersion") as string;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AppRegistry.GetInstalledVersion failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns the InstallLocation from the registry, or null if not registered.
    /// </summary>
    public static string? GetInstallLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AppRegistry.GetInstallLocation failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates or updates the registry key that makes ProdToy appear in
    /// Windows "Apps &amp; Features". Idempotent — safe to call on every startup.
    /// </summary>
    public static void Register()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
            string uninstallCmd = $"\"{AppPaths.ExePath}\" --uninstall";

            key.SetValue("DisplayName", "ProdToy");
            key.SetValue("UninstallString", uninstallCmd);
            key.SetValue("QuietUninstallString", uninstallCmd);
            key.SetValue("DisplayVersion", AppVersion.Current);
            key.SetValue("Publisher", "ProdToy");
            key.SetValue("InstallLocation", AppPaths.Root);
            key.SetValue("DisplayIcon", AppPaths.ExePath);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));

            try
            {
                var fileInfo = new FileInfo(AppPaths.ExePath);
                if (fileInfo.Exists)
                    key.SetValue("EstimatedSize", (int)(fileInfo.Length / 1024), RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppRegistry.Register: could not set EstimatedSize: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AppRegistry.Register failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the registry key so ProdToy no longer appears in "Apps &amp; Features".
    /// </summary>
    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKey(UninstallKeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AppRegistry.Unregister failed: {ex.Message}");
        }
    }
}
