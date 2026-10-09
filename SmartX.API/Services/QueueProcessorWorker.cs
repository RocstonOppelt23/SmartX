using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace SmartX.Api.Services;

public class QueueProcessorWorker : BackgroundService
{
    private readonly CommandStreamService _service;

    public QueueProcessorWorker(CommandStreamService service)
    {
        _service = service;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run until shutdown; every 500ms try to process a batch.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _service.ProcessBatch();
            }
            catch (Exception ex)
            {
                // Swallow exceptions to keep the background worker alive.
                Console.WriteLine($"QueueProcessorWorker error: {ex.Message}");
            }

            try
            {
                await Task.Delay(500, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
        }
    }
}
