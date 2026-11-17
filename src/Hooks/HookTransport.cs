using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace SecretaryOverlay;

internal static class HookTransport
{
    private const int ReadTimeoutMs = 1500;
    private const int ConnectTimeoutMs = 350;
    private const int MaxHookCharacters = 16 * 1024 * 1024;
    private const int MaxMessageCharacters = 32768;

    public static readonly string PipeName = "CodexSecretary-" +
        WindowsIdentity.GetCurrent().User!.Value.Replace('-', '_');

    public static async Task ForwardHookAsync(bool preview)
    {
        // Forward routing metadata only. Message bodies are filtered by the transcript reader.
        using var input = Console.OpenStandardInput();
        using var reader = new StreamReader(input, Encoding.UTF8);
        using var timeout = new CancellationTokenSource(ReadTimeoutMs);
        var raw = await reader.ReadToEndAsync(timeout.Token);
        if (raw.Length > MaxHookCharacters) return;

        using var doc = JsonDocument.Parse(raw);
        if (HookEventParser.Parse(doc.RootElement, preview) is { } ev) await SendAsync(ev);
    }

    public static async Task SendAsync(PetEvent ev)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(ConnectTimeoutMs);
            await pipe.ConnectAsync(timeout.Token);
            var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev) + "\n");
            await pipe.WriteAsync(data, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            // Hooks must remain harmless when the overlay is closed or unavailable.
        }
    }

    public static async Task ListenAsync(Action<PetEvent> received, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 8,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(ReadTimeoutMs);
                    var line = await reader.ReadLineAsync(deadline.Token);
                    if (line is not null && line.Length < MaxMessageCharacters)
                    {
                        var ev = JsonSerializer.Deserialize<PetEvent>(line);
                        if (ev is not null) received(ev);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // A stalled client must not prevent the next connection.
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppStorage.LogError(ex);
                    await Task.Delay(300, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutdown also cancels any retry delay or connected client read.
        }
    }
}
