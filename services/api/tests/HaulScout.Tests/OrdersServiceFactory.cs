using HaulScout.Api.Services;
using HaulScout.Application.Security;
using HaulScout.Infrastructure.Common;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>The orders service wired the way DI wires it, so a cancel reverses sales in tests too.</summary>
internal static class OrdersServiceFactory
{
    public static OrdersWorkflowService Create(HaulScoutDbContext db, ICurrentUserContext currentUser, OrderAssembler? assembler = null)
    {
        assembler ??= new OrderAssembler(db, currentUser);
        SaleTransitionWorkflowService transitions = new(db, currentUser, assembler, new SaleFinancialStateService(db, currentUser));
        return new OrdersWorkflowService(db, currentUser, assembler, new SaleTransitionCanceller(transitions), new EfUnitOfWork(db));
    }
}
