namespace Pulse.Core.Models;

public enum Provider
{
    ClaudeCode,
    Codex,
    Kiro,
    Antigravity,
    Cursor,
    OpenCodeGo,
    KimiCode,
    OllamaCloud,
    Zai,
    GlmCoding,
    MiniMax,
    MiniMaxCN,
    Copilot,
    Grok,
    GrokBot,
    Volcengine,
    CommandCode,
    DeepSeek,
    Devin,
    XiaomiMiMo
}

public static class ProviderExtensions
{
    public static string Id(this Provider provider) => provider switch
    {
        Provider.ClaudeCode => "claudeCode",
        Provider.Codex => "codex",
        Provider.Kiro => "kiro",
        Provider.Antigravity => "antigravity",
        Provider.Cursor => "cursor",
        Provider.OpenCodeGo => "openCodeGo",
        Provider.KimiCode => "kimiCode",
        Provider.OllamaCloud => "ollamaCloud",
        Provider.Zai => "zai",
        Provider.GlmCoding => "glmCoding",
        Provider.MiniMax => "minimax",
        Provider.MiniMaxCN => "minimaxCN",
        Provider.Copilot => "copilot",
        Provider.Grok => "grok",
        Provider.GrokBot => "grokBot",
        Provider.Volcengine => "volcengine",
        Provider.CommandCode => "commandCode",
        Provider.DeepSeek => "deepSeek",
        Provider.Devin => "devin",
        Provider.XiaomiMiMo => "xiaomiMiMo",
        _ => provider.ToString().ToLowerInvariant()
    };

    public static string DisplayName(this Provider provider) => provider switch
    {
        Provider.ClaudeCode => "Claude Code",
        Provider.Codex => "Codex",
        Provider.Kiro => "Kiro",
        Provider.Antigravity => "Antigravity",
        Provider.Cursor => "Cursor",
        Provider.OpenCodeGo => "OpenCode Go",
        Provider.KimiCode => "Kimi Code",
        Provider.OllamaCloud => "Ollama Cloud",
        Provider.Zai => "z.ai",
        Provider.GlmCoding => "Zhipu",
        Provider.MiniMax => "MiniMax",
        Provider.MiniMaxCN => "MiniMax CN",
        Provider.Copilot => "GitHub Copilot",
        Provider.Grok => "Grok",
        Provider.GrokBot => "Grok Bot",
        Provider.Volcengine => "Volcengine",
        Provider.CommandCode => "Command Code",
        Provider.DeepSeek => "DeepSeek",
        Provider.Devin => "Devin",
        Provider.XiaomiMiMo => "Xiaomi Coding Plan",
        _ => provider.ToString()
    };

    public static string IconResource(this Provider provider) => provider switch
    {
        Provider.ClaudeCode => "claude",
        Provider.Codex => "openai",
        Provider.Kiro => "kiro",
        Provider.Antigravity => "antigravity",
        Provider.Cursor => "cursor",
        Provider.OpenCodeGo => "opencode",
        Provider.KimiCode => "kimi",
        Provider.OllamaCloud => "ollama",
        Provider.Zai => "zai",
        Provider.GlmCoding => "zai",
        Provider.MiniMax => "minimax",
        Provider.MiniMaxCN => "minimax",
        Provider.Copilot => "github",
        Provider.Grok => "grok",
        Provider.GrokBot => "xai",
        Provider.Volcengine => "volcengine",
        Provider.CommandCode => "commandcode",
        Provider.DeepSeek => "deepseek",
        Provider.Devin => "devin",
        Provider.XiaomiMiMo => "xiaomimimo",
        _ => "default"
    };

    public static bool SupportsMultipleAccounts(this Provider provider) => provider switch
    {
        Provider.ClaudeCode or Provider.Codex or Provider.Antigravity or Provider.Grok or Provider.GrokBot => true,
        _ => false
    };

    public static bool UsesApiKey(this Provider provider) => provider switch
    {
        Provider.OpenCodeGo or Provider.KimiCode or Provider.Zai or Provider.GlmCoding
            or Provider.MiniMax or Provider.MiniMaxCN or Provider.Volcengine
            or Provider.CommandCode or Provider.DeepSeek or Provider.Devin => true,
        _ => false
    };

    public static Provider? FromId(string id) => id switch
    {
        "claudeCode" => Provider.ClaudeCode,
        "codex" => Provider.Codex,
        "kiro" => Provider.Kiro,
        "antigravity" => Provider.Antigravity,
        "cursor" => Provider.Cursor,
        "openCodeGo" => Provider.OpenCodeGo,
        "kimiCode" => Provider.KimiCode,
        "ollamaCloud" => Provider.OllamaCloud,
        "zai" => Provider.Zai,
        "glmCoding" => Provider.GlmCoding,
        "minimax" => Provider.MiniMax,
        "minimaxCN" => Provider.MiniMaxCN,
        "copilot" => Provider.Copilot,
        "grok" => Provider.Grok,
        "grokBot" => Provider.GrokBot,
        "volcengine" => Provider.Volcengine,
        "commandCode" => Provider.CommandCode,
        "deepSeek" => Provider.DeepSeek,
        "devin" => Provider.Devin,
        "xiaomiMiMo" => Provider.XiaomiMiMo,
        _ => null
    };
}
