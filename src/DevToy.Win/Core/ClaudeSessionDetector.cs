using System.Diagnostics;
using System.Text;

namespace DevToy;

record ClaudeSession(
    int Pid,
    string SessionName,
    string TabTitle,
    int TerminalPid,
    IntPtr TerminalHandle,
    int ShellPid,
    string CommandLine);

static class ClaudeSessionDetector
{
    public static List<ClaudeSession> GetActiveSessions()
    {
        var sessions = new List<ClaudeSession>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c wmic process where \"name='node.exe'\" get ProcessId,ParentProcessId,CommandLine /format:csv",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var proc = Process.Start(psi);
            if (proc == null) return sessions;

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.Contains("claude-code") || !trimmed.Contains("cli.js"))
                    continue;

                var parts = ParseCsvLine(trimmed);
                if (parts.Count < 4) continue;

                string cmdLine = parts[1];
                if (!int.TryParse(parts[parts.Count - 2], out int parentPid)) continue;
                if (!int.TryParse(parts[parts.Count - 1], out int pid)) continue;

                string sessionName = ExtractFlag(cmdLine, "--name") ?? ExtractFlag(cmdLine, "-n") ?? "";

                // Walk process tree to find terminal window and shell PID
                int termPid = 0;
                IntPtr termHandle = IntPtr.Zero;
                int shellPid = 0;
                int currentPid = parentPid;
                int previousPid = pid;

                for (int i = 0; i < 8; i++)
                {
                    try
                    {
                        var parentProc = Process.GetProcessById(currentPid);
                        if (parentProc.MainWindowHandle != IntPtr.Zero &&
                            !string.IsNullOrEmpty(parentProc.MainWindowTitle))
                        {
                            termPid = currentPid;
                            termHandle = parentProc.MainWindowHandle;
                            shellPid = previousPid;
                            break;
                        }

                        int nextParent = GetParentProcessId(currentPid);
                        if (nextParent <= 0 || nextParent == currentPid) break;
                        previousPid = currentPid;
                        currentPid = nextParent;
                    }
                    catch
                    {
                        break;
                    }
                }

                // Get the actual tab title by attaching to the shell's console
                string tabTitle = GetConsoleTitle(shellPid);

                sessions.Add(new ClaudeSession(pid, sessionName, tabTitle, termPid, termHandle, shellPid, cmdLine));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to detect Claude sessions: {ex.Message}");
        }

        return sessions;
    }

    /// <summary>
    /// Gets the console title for a specific process by attaching to its console.
    /// Each Windows Terminal tab has its own console with its own title.
    /// </summary>
    private static string GetConsoleTitle(int processId)
    {
        if (processId <= 0) return "";

        try
        {
            NativeMethods.FreeConsole();
            if (NativeMethods.AttachConsole(processId))
            {
                var sb = new StringBuilder(1024);
                int len = NativeMethods.GetConsoleTitle(sb, sb.Capacity);
                NativeMethods.FreeConsole();
                if (len > 0) return sb.ToString();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetConsoleTitle failed for PID {processId}: {ex.Message}");
        }
        return "";
    }

    /// <summary>
    /// Sets the terminal tab title by attaching to the shell console and
    /// writing an ANSI escape sequence.
    /// </summary>
    public static bool SetTerminalTitle(ClaudeSession session, string newTitle)
    {
        if (session.ShellPid <= 0 && session.TerminalHandle == IntPtr.Zero)
            return false;

        if (session.ShellPid > 0)
        {
            try
            {
                NativeMethods.FreeConsole();
                if (NativeMethods.AttachConsole(session.ShellPid))
                {
                    var handle = NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE);
                    if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                    {
                        string escapeSeq = $"\x1b]0;{newTitle}\x07";
                        NativeMethods.WriteConsole(handle, escapeSeq, escapeSeq.Length, out _, IntPtr.Zero);
                    }
                    NativeMethods.FreeConsole();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AttachConsole failed: {ex.Message}");
            }
        }

        if (session.TerminalHandle != IntPtr.Zero)
            return NativeMethods.SetWindowText(session.TerminalHandle, newTitle);

        return false;
    }

    private static int GetParentProcessId(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c wmic process where \"ProcessId={pid}\" get ParentProcessId /value",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var proc = Process.Start(psi);
            if (proc == null) return -1;

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("ParentProcessId="))
                {
                    if (int.TryParse(trimmed["ParentProcessId=".Length..], out int parentPid))
                        return parentPid;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetParentProcessId failed: {ex.Message}");
        }
        return -1;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                int end = line.IndexOf('"', i + 1);
                if (end < 0) end = line.Length;
                result.Add(line[(i + 1)..end]);
                i = end + 2;
            }
            else
            {
                int comma = line.IndexOf(',', i);
                if (comma < 0) comma = line.Length;
                result.Add(line[i..comma]);
                i = comma + 1;
            }
        }
        return result;
    }

    private static string? ExtractFlag(string cmdLine, string flag)
    {
        int idx = cmdLine.IndexOf(flag, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        int valueStart = idx + flag.Length;
        var rest = cmdLine[valueStart..].TrimStart();
        if (rest.Length == 0) return null;

        if (rest[0] == '"')
        {
            int endQuote = rest.IndexOf('"', 1);
            return endQuote > 0 ? rest[1..endQuote] : rest[1..];
        }

        int space = rest.IndexOf(' ');
        return space > 0 ? rest[..space] : rest;
    }
}
