using CHMBAPI.Data;
using CHMBAPI.Entities;

namespace CHMBAPI.Services;

public class PointMaturationJobService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PointMaturationJobService> _logger;

    public PointMaturationJobService(IServiceProvider serviceProvider, ILogger<PointMaturationJobService> logger)
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
                    var processor = scope.ServiceProvider.GetRequiredService<PointMaturationProcessingService>();
                    var matured = await processor.ProcessMaturingPointsAsync();

                    if (matured > 0)
                    {
                        _logger.LogInformation($"Point maturation job processed: {matured} points matured");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in point maturation job processing");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken); // Run every hour
        }
    }
}