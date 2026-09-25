using HaulScout.Application.Common;
using HaulScout.Application.Inventory;

namespace HaulScout.Api.Services;

/// <summary>
/// Brings listing photos onto their inventory items, a few at a time, in the background.
///
/// It runs here rather than inside the scan that delivered the listings because photos are heavy: a first sync of a
/// whole wardrobe is hundreds of images, and a browser waiting on a batch call must not carry that. Draining it
/// steadily instead means an operator sees photos fill in over a few minutes, and a marketplace that will not
/// answer costs a scan nothing.
///
/// Each organization is served in its own scope with its own tenant, so every query and write is scoped exactly as
/// it would be inside that operator's request.
/// </summary>
public sealed class ListingPhotoSyncWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<ListingPhotoSyncWorker> logger) : BackgroundService
{
    private bool Enabled => configuration.GetValue("Sync:Photos:Enabled", true);
    private TimeSpan Interval => TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Sync:Photos:IntervalSeconds", 15), 5, 600));
    private int OrganizationsPerCycle => Math.Clamp(configuration.GetValue("Sync:Photos:OrganizationsPerCycle", 3), 1, 20);
    private int ListingsPerCycle => Math.Clamp(configuration.GetValue("Sync:Photos:ListingsPerCycle", 5), 1, 50);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (Enabled)
            {
                try
                {
                    await DrainAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // One bad cycle must not stop the worker for the life of the service.
                    logger.LogError(exception, "Listing photo sync failed this cycle.");
                }
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset retryBefore = clock.GetUtcNow().AddHours(-1);
        IReadOnlyList<Guid> organizations;
        await using (AsyncServiceScope finding = scopes.CreateAsyncScope())
        {
            IInventoryPhotoSyncStore store = finding.ServiceProvider.GetRequiredService<IInventoryPhotoSyncStore>();
            organizations = await store.ListOrganizationsWithPendingPhotosAsync(OrganizationsPerCycle, retryBefore, cancellationToken);
        }

        foreach (Guid organizationId in organizations)
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetOrganization(organizationId);
            InventoryPhotoSyncService sync = scope.ServiceProvider.GetRequiredService<InventoryPhotoSyncService>();
            PhotoSyncOutcome outcome = await sync.SyncPendingAsync(ListingsPerCycle, cancellationToken);

            if (outcome.ItemsUpdated > 0 || outcome.PhotosAdded > 0 || outcome.PhotosRemoved > 0)
            {
                logger.LogInformation(
                    "Listing photos synced. OrganizationId={OrganizationId} Listings={Listings} Items={Items} Added={Added} Removed={Removed}",
                    organizationId, outcome.Listings, outcome.ItemsUpdated, outcome.PhotosAdded, outcome.PhotosRemoved);
            }

            foreach (string failure in outcome.Failures)
            {
                logger.LogWarning("Listing photos could not be synced. OrganizationId={OrganizationId} Reason={Reason}", organizationId, failure);
            }
        }
    }
}
