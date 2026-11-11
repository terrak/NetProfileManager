using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using NetProfileManager.Core.Protocol;

namespace NetProfileManager.Service;

public class PipeServer
{
    private readonly CommandHandler _handler;
    private readonly ILogger _logger;

    public PipeServer(CommandHandler handler, ILogger logger)
    {
        _handler = handler;
        _logger = logger;
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new PipeAccessRule(everyone, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeNames.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096,
                    4096,
                    CreatePipeSecurity());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible de créer le pipe nommé.");
                await Task.Delay(2000, cancellationToken);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                _ = HandleClientAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'attente d'un client.");
                pipe.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                pipe.Dispose();
                return;
            }

            var request = JsonSerializer.Deserialize<PipeRequest>(line);
            var response = request == null
                ? new PipeResponse { Success = false, Message = "Requête illisible.", ErrorCode = ErrorCodes.Invalid }
                : await _handler.HandleAsync(request, cancellationToken);

            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du traitement d'un client.");
        }
        finally
        {
            try { pipe.WaitForPipeDrain(); } catch { /* déconnexion client */ }
            pipe.Dispose();
        }
    }
}
