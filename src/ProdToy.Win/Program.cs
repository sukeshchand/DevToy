using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ProdToy;

static class Program
{
    private const string MutexName = "ProdToy_SingleInstance_Mutex";
    internal const string PipeName = "ProdToy_Pipe";

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // No arguments → check if running from install directory
        if (args.Length == 0)
        {
            if (IsRunningFromInstallDir())
                RunInstalledInstance();
            else
                Application.Run(new SetupForm());
            return;
        }

        string title = "ProdToy";
        string message = "Task completed.";
        string type = NotificationType.Info;
        string? messageFile = null;
        string? saveQuestion = null;
        string sessionId = "";
        string cwd = "";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--title" or "-t" when i + 1 < args.Length:
                    title = args[++i];
                    break;
                case "--message" or "-m" when i + 1 < args.Length:
                    message = args[++i];
                    break;
                case "--message-file" when i + 1 < args.Length:
                    messageFile = args[++i];
                    break;
                case "--save-question" when i + 1 < args.Length:
                    saveQuestion = args[++i];
                    break;
                case "--type" when i + 1 < args.Length:
                    type = args[++i].ToLowerInvariant();
                    break;
                case "--session-id" when i + 1 < args.Length:
                    sessionId = args[++i];
                    break;
                case "--cwd" when i + 1 < args.Length:
                    cwd = args[++i];
                    break;
            }
        }

        // Save question to history and exit (UserPromptSubmit hook)
        if (saveQuestion != null)
        {
            // Read from file if it's a file path (validate it's within safe directories)
            if (File.Exists(saveQuestion) && IsPathSafe(saveQuestion))
                saveQuestion = File.ReadAllText(saveQuestion, Encoding.UTF8);
            ResponseHistory.SaveQuestion(saveQuestion.Replace("\\n", "\n").Replace("\\t", "\t").Trim(), sessionId, cwd);
            return;
        }

        // Read message from file if specified (avoids command-line length limits)
        if (messageFile != null && File.Exists(messageFile) && IsPathSafe(messageFile))
            message = File.ReadAllText(messageFile, Encoding.UTF8);

        message = message.Replace("\\n", "\n").Replace("\\t", "\t");

        // Save response to history (completes pending question entry)
        ResponseHistory.SaveResponse(title, message, type, sessionId, cwd);

        using var mutex = new Mutex(true, MutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            SendToPipe(title, message, type, sessionId, cwd);
            return;
        }

        Application.Run(new PopupAppContext(title, message, type, sessionId, cwd));
    }

    private static void RunInstalledInstance()
    {
        using var mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            // Already running — tell existing instance to bring itself to front
            SendToPipe("ProdToy", "ProdToy is ready.", NotificationType.Info);
            return;
        }

        // Load last history entry or show a default welcome
        var latest = ResponseHistory.GetLatest();
        string title = latest?.Title ?? "ProdToy";
        string message = latest?.Message ?? "No notifications yet. ProdToy will notify you here.";
        string type = latest?.Type ?? NotificationType.Info;
        string sessionId = latest?.SessionId ?? "";
        string cwd = latest?.Cwd ?? "";

        Application.Run(new PopupAppContext(title, message, type, sessionId, cwd));
    }

    /// <summary>
    /// Validates that a file path is within safe directories (user profile or temp).
    /// Prevents path traversal attacks via CLI arguments.
    /// </summary>
    private static bool IsPathSafe(string filePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var tempDir = Path.GetTempPath();
            return fullPath.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRunningFromInstallDir()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
            return string.Equals(
                Path.GetFullPath(exeDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(AppPaths.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void SendToPipe(string title, string message, string type, string sessionId = "", string cwd = "")
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            var payload = JsonSerializer.Serialize(new { title, message, type, sessionId, cwd });
            var bytes = Encoding.UTF8.GetBytes(payload);
            client.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SendToPipe failed: {ex.Message}");
        }
    }
}
