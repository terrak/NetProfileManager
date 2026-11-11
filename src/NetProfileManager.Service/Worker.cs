using NetProfileManager.Core.Protocol;

namespace NetProfileManager.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NetProfileManager");

        var handler = new CommandHandler(dataDirectory, _logger);
        var server = new PipeServer(handler, _logger);

        _logger.LogInformation("Service démarré. Écoute du pipe {PipeName}. Données dans {DataDirectory}.", PipeNames.PipeName, dataDirectory);
        await server.RunAsync(stoppingToken);
    }
}
