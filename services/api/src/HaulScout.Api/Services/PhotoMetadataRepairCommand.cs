using HaulScout.Application.Inventory;

namespace HaulScout.Api.Services;

public static class PhotoMetadataRepairCommand
{
    public static async Task RunAsync(
        IServiceProvider services, ILogger logger, string[] args, CancellationToken cancellationToken)
    {
        const string usage = "Usage: repair-photo-metadata --dry-run|--apply <count:1-10> [after-photo-id:N]";
        if (args.Length is < 2 or > 3
            || (args[0] != "--dry-run" && args[0] != "--apply")
            || !int.TryParse(args[1], out int take) || take is < 1 or > 10
            || (args.Length == 3 && !Guid.TryParseExact(args[2], "N", out _)))
        {
            throw new ArgumentException(usage);
        }

        bool apply = args[0] == "--apply";
        Guid? afterPhotoId = args.Length == 3 ? Guid.ParseExact(args[2], "N") : null;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        var repair = scope.ServiceProvider.GetRequiredService<InventoryPhotoMetadataRepairService>();
        PhotoMetadataRepairPass pass = await repair.RepairAsync(afterPhotoId, take, apply, cancellationToken);
        logger.LogInformation(
            "Photo metadata repair pass. Mode={Mode} Scanned={Scanned} Reencoded={Reencoded} NextAfter={NextAfter}",
            apply ? "apply" : "dry-run", pass.Scanned, pass.Reencoded, pass.NextAfter?.ToString("N") ?? "none");
        if (!apply)
        {
            logger.LogWarning("Dry-run cursor is not a repair checkpoint; do not resume apply from it.");
        }
    }
}
