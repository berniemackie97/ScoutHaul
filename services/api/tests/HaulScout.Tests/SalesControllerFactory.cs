using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Security;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// SalesController is a pure HTTP adapter — it owns no persistence, so the tests wire the same workflow services DI
/// composes in production. One factory keeps every sales test on the real composition instead of re-listing it.
/// </summary>
internal static class SalesControllerFactory
{
    public static SalesController Create(HaulScoutDbContext db, ICurrentUserContext currentUser)
    {
        OrderAssembler assembler = new(db, currentUser);
        SalesChannelWorkflowService channels = new(db, currentUser);
        SaleFinancialStateService saleState = new(db, currentUser);
        return new SalesController(
            channels,
            new SaleRecordingWorkflowService(db, currentUser, assembler, channels),
            new SaleQueryService(db, currentUser),
            new SaleGroupingWorkflowService(db, currentUser, assembler),
            new SaleDeletionWorkflowService(db, currentUser, assembler),
            new SaleTransitionWorkflowService(db, currentUser, assembler, saleState),
            new SaleCorrectionWorkflowService(db, currentUser, assembler, channels, saleState),
            new SaleRefundWorkflowService(db, currentUser, saleState),
            new SaleReturnWorkflowService(db, currentUser, saleState, TimeProvider.System));
    }
}
