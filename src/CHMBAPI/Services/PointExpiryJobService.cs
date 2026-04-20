using CHMBAPI.Data;
using CHMBAPI.Entities;

namespace CHMBAPI.Services;

public class PointExpiryJobService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PointExpiryJobService> _logger;

    public PointExpiryJobService(IServiceProvider serviceProvider, ILogger<PointExpiryJobService> logger)
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
                    var processor = scope.ServiceProvider.GetRequiredService<PointExpiryProcessingService>();
                    var expired = await processor.ProcessExpiringPointsAsync();

                    if (expired > 0)
                    {
                        _logger.LogInformation($"Point expiry job processed: {expired} points expired");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in point expiry job processing");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken); // Run every hour
        }
    }
}