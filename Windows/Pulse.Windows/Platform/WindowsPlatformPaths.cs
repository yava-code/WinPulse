namespace Pulse.Windows.Platform;

using System.IO;
using Pulse.Core.Platform;

public sealed class WindowsPlatformPaths : IPlatformPaths
{
    public string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string PulseStorageDirectory => Path.Combine(LocalAppData, "Pulse");

    public string GetCodexAuthPath() => Path.Combine(UserProfile, ".codex", "auth.json");
    public string GetClaudeCredentialsPath() => Path.Combine(UserProfile, ".claude", ".credentials.json");
    public string GetCursorStateDbPath() => Path.Combine(AppData, "Cursor", "User", "globalStorage", "state.vscdb");
    public string GetGrokAuthPath() => Path.Combine(UserProfile, ".grok", "auth.json");
    public string GetCommandCodeAuthPath() => Path.Combine(UserProfile, ".commandcode", "auth.json");
    public string GetAntigravityGoogleAccountsPath() => Path.Combine(UserProfile, ".gemini", "google_accounts.json");
    public string GetAntigravityOAuthCredsPath() => Path.Combine(UserProfile, ".gemini", "oauth_creds.json");
}
