using Microsoft.Extensions.Options;
using HaulScout.Application.Inventory;
using HaulScout.Infrastructure.Inventory;

namespace HaulScout.Api.Services;

/// <summary>
/// Moves photos that were stored before object storage was configured into it, then stops.
///
/// Turning object storage on is a setting, not a migration: new photos go straight to the bucket and reads fall back
/// to the database columns, so nothing breaks in the meantime. This works through whatever was already there, a
/// batch at a time, and finishes for good once the backlog is empty — nothing refills it.
///
/// The space those images took is not returned to the volume until the table is rewritten (`VACUUM FULL
/// "InventoryPhotos"`), which is an operator's decision to make during a quiet moment, not a service's.
/// </summary>
public sealed class PhotoObjectStorageBackfillWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    IOptions<PhotoObjectStorageOptions> storage,
    ILogger<PhotoObjectStorageBackfillWorker> logger) : BackgroundService
{
    private bool Enabled => configuration.GetValue("Storage:Photos:Backfill:Enabled", true);
    private int BatchSize => Math.Clamp(configuration.GetValue("Storage:Photos:Backfill:BatchSize", 20), 1, 200);
    private TimeSpan Interval => TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Storage:Photos:Backfill:IntervalSeconds", 5), 1, 600));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!storage.Value.IsConfigured || !Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                InventoryPhotoBackfillService backfill = scope.ServiceProvider.GetRequiredService<InventoryPhotoBackfillService>();
                PhotoBackfillPass pass = await backfill.MoveAsync(BatchSize, stoppingToken);

                if (pass.Moved > 0)
                {
                    logger.LogInformation(
                        "Photo images moved to object storage. Moved={Moved} Remaining={Remaining}", pass.Moved, pass.Remaining);
                }

                if (pass.Remaining == 0)
                {
                    logger.LogInformation("Every photo image is in object storage. Nothing left to move.");
                    return;
                }

                if (pass.Moved == 0)
                {
                    // Rows are owed but none could be moved. Retrying immediately would spin; leave it to the delay.
                    logger.LogWarning("Photo images are owed to object storage but none moved. Remaining={Remaining}", pass.Remaining);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A bucket that is briefly unreachable must not end the move for the life of the service.
                logger.LogError(exception, "Moving photo images to object storage failed this pass.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
