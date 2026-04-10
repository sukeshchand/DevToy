namespace ProdToy.Sdk;

/// <summary>
/// Lifecycle interface that every plugin must implement.
/// The host calls methods in order: Initialize → Start → (running) → Stop → Dispose.
///
/// Start() contract:
///   - Verify all external integrations are properly configured (hooks, hotkeys, etc.)
///   - Fix anything that's broken or out of sync with plugin settings
///   - Register all resources the plugin needs (hotkeys, timers, hooks)
///
/// Stop() contract:
///   - Remove ALL external integrations this plugin installed (hooks, hotkeys, scripts, etc.)
///   - Close all open forms and stop all background work
///   - After Stop(), the system should be as if the plugin was never installed
/// </summary>
public interface IPlugin : IDisposable
{
    /// <summary>
    /// Called once after the plugin assembly is loaded.
    /// Wire up references only — do not show UI or start background work.
    /// </summary>
    void Initialize(IPluginContext context);

    /// <summary>
    /// Called after all plugins are initialized, and also when re-enabled at runtime.
    /// Verify and fix all external integrations. Register hotkeys, start timers.
    /// </summary>
    void Start();

    /// <summary>
    /// Called during shutdown, disable, or uninstall.
    /// MUST remove all external integrations: hooks, hotkeys, scripts, registry entries.
    /// Close all forms, stop all timers. Leave no trace.
    /// </summary>
    void Stop();

    /// <summary>
    /// Items for the tray right-click context menu. Quick actions.
    /// </summary>
    IReadOnlyList<MenuContribution> GetMenuItems();

    /// <summary>
    /// Items for the dashboard tile grid. Main plugin actions shown on the home screen.
    /// Return empty list if this plugin has no dashboard presence.
    /// </summary>
    IReadOnlyList<MenuContribution> GetDashboardItems();

    /// <summary>
    /// Settings page this plugin contributes to the Settings dialog.
    /// Return null if this plugin has no settings UI.
    /// </summary>
    SettingsPageContribution? GetSettingsPage();
}
