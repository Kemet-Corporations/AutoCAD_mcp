using System;
using System.Diagnostics;

namespace AutoCADMCPPlugin.Core
{
    /// <summary>
    /// KEMET: in-process control for add-ins loaded in the same AutoCAD (Kemet-AutoCAD-Addons).
    ///
    /// The safety settings can be tightened from anywhere, including by an AI app through the MCP
    /// tool set_server_options, but they can only be LOOSENED here: turning Read-only off,
    /// destructive confirmation off or the audit log off is a decision for the person at the PC.
    /// This class is reachable only from code running inside acad.exe, never over the socket, so an
    /// AI app cannot call it. Kemet-AutoCAD-Addons finds it by name (assembly AutoCADMCPPlugin,
    /// type AutoCADMCPPlugin.Core.LocalControl), so keep the name and signatures stable.
    /// </summary>
    public static class LocalControl
    {
        /// <summary>Version of this contract. Bump when a signature changes.</summary>
        public const int ApiVersion = 1;

        /// <summary>
        /// Sets one safety setting and saves it (%AppData%\AutoCADMCP\settings.json).
        /// name: "read_only", "confirm_destructive" or "audit_log".
        /// Returns null on success, else the reason.
        /// </summary>
        public static string SetOption(string name, bool value)
        {
            try
            {
                Settings.EnsureLoaded();
                switch (name)
                {
                    case "read_only": Settings.ReadOnly = value; break;
                    case "confirm_destructive": Settings.ConfirmDestructive = value; break;
                    case "audit_log": Settings.AuditLog = value; break;
                    default: return "Unknown setting '" + name + "'. Use read_only, confirm_destructive or audit_log.";
                }
                Settings.Save();
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>This AutoCAD's process id (the same value get_capabilities reports as "pid").</summary>
        public static int ProcessId => Process.GetCurrentProcess().Id;
    }
}
