using System.IO.Pipes;
using System.Text.Json;

namespace NetProfileManager.Core.Protocol;

public static class PipeClient
{
    public static async Task<PipeResponse> SendAsync(string command, object? payload = null, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", PipeNames.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(15000, cancellationToken);

        var request = new PipeRequest
        {
            Command = command,
            Payload = payload == null ? null : JsonSerializer.Serialize(payload)
        };

        var writer = new StreamWriter(pipe, System.Text.Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        await writer.FlushAsync();

        var reader = new StreamReader(pipe, System.Text.Encoding.UTF8, false, 1024, leaveOpen: true);
        var line = await reader.ReadLineAsync();
        if (string.IsNullOrEmpty(line))
        {
            return new PipeResponse { Success = false, Message = "Le service a fermé la connexion sans répondre." };
        }
        return JsonSerializer.Deserialize<PipeResponse>(line)
            ?? new PipeResponse { Success = false, Message = "Réponse du service illisible." };
    }

    public static PipeResponse Send(string command, object? payload = null)
        => SendAsync(command, payload).GetAwaiter().GetResult();

    public static T? DeserializeData<T>(PipeResponse response)
        => string.IsNullOrEmpty(response.Data) ? default : JsonSerializer.Deserialize<T>(response.Data);
}
