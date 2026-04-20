using CHMBAPI.Data;
using CHMBAPI.Entities;

namespace CHMBAPI.Services;

public class EarnJobService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EarnJobService> _logger;

    public EarnJobService(IServiceProvider serviceProvider, ILogger<EarnJobService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var earnProcessor = scope.ServiceProvider.GetRequiredService<EarnProcessingService>();
                    var (linked, earned) = await earnProcessor.ProcessPendingOrdersAsync();

                    if (linked > 0 || earned > 0)
                    {
                        _logger.LogInformation($"Earn job processed: {linked} orders linked, {earned} points earned");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in earn job processing");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Run every 5 minutes
        }
    }
}