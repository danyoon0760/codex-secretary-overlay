using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SecretaryOverlay;

internal static class CodexStreamingClient
{
    internal static ProcessStartInfo CreateStartInfo(string folder)
    {
        var start = CodexProcess.CreateStartInfo(folder);
        start.ArgumentList.Add("app-server"); start.ArgumentList.Add("--listen"); start.ArgumentList.Add("stdio://");
        foreach (string value in new[] { "mcp_servers={}", "plugins={}", "project_doc_max_bytes=0", "web_search=\"disabled\"",
            "approval_policy=\"never\"", "sandbox_mode=\"read-only\"", "developer_instructions=\"\"" })
        { start.ArgumentList.Add("-c"); start.ArgumentList.Add(value); }
        foreach (string feature in new[] { "hooks", "plugins", "apps", "memories", "shell_tool", "unified_exec", "multi_agent", "multi_agent_v2",
            "browser_use", "computer_use", "image_generation", "skill_search", "workspace_dependencies", "code_mode_host", "in_app_browser", "goals" })
        { start.ArgumentList.Add("--disable"); start.ArgumentList.Add(feature); }
        return start;
    }

    public static async Task<string> GenerateAsync(string folder, IReadOnlyList<string> images, string prompt, ModelProfile profile,
        JsonElement? schema, Action<string>? progress, CancellationToken token, Action<string>? reasoning = null)
    {
        var selected = ModelProfile.Validated(profile);
        using var process = new Process { StartInfo = CreateStartInfo(folder) };
        process.Start();
        using var cancel = token.Register(() => Kill(process));
        var errors = DrainAsync(process.StandardError); // Never persist model or user content from diagnostics.
        async Task Send(object message)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
        }
        var session = new CodexGenerationSession(folder, images, prompt, selected, schema, progress, reasoning);
        try
        {
            await Send(CodexRequestMessages.Initialize());
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                if (line.Length > 2 * 1024 * 1024) throw new InvalidOperationException("AI 연결에서 너무 큰 응답을 받았어요.");
                using var doc = JsonDocument.Parse(line);
                await session.AcceptAsync(doc.RootElement, Send);
                if (session.Completed) return session.Text;
            }
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("AI 연결이 응답 도중 종료됐어요. 다시 시도해 주세요.");
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync();
            await errors;
        }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer) > 0) { }
    }
}
