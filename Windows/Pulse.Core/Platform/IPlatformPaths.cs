namespace Pulse.Core.Platform;

public interface IPlatformPaths
{
    string UserProfile { get; }
    string AppData { get; }
    string LocalAppData { get; }
    string PulseStorageDirectory { get; }

    string GetCodexAuthPath();
    string GetClaudeCredentialsPath();
    string GetCursorStateDbPath();
    string GetGrokAuthPath();
    string GetCommandCodeAuthPath();
    string GetAntigravityGoogleAccountsPath();
    string GetAntigravityOAuthCredsPath();
}
