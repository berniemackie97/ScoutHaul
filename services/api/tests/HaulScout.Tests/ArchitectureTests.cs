using System.Reflection;
using HaulScout.Api.Controllers;
using HaulScout.Application.Common;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// The dependency rules, enforced by the build instead of by memory. Layering that is only a convention drifts —
/// that is exactly how ~90 SaveChanges calls ended up inside HTTP handlers. These fail CI the moment a boundary is
/// crossed, so the cleanup can only ever ratchet forward.
/// </summary>
public class ArchitectureTests
{
    /// <summary>
    /// Controllers that still inject the DbContext and therefore own persistence. This list may only SHRINK — a new
    /// name here means a use case leaked back into an HTTP handler. De-persisting one (extract its use cases into
    /// Api/Services, see SalesController/InventoryController/OrdersController) means deleting its name here.
    /// </summary>
    private static readonly string[] ControllersPendingDePersistence =
    [
        "AcquisitionLotsController",
        "BudgetsController",
        "ChannelListingsController",
        "CustomersController",
        "DashboardController",
        "ListingsController",
        "MarketAnalysisController",
        "MarketObservationsController",
        "OrganizationsController",
        "PlatformListingsController",
        "PlatformOrdersController",
        "ProductIdentitiesController",
        "ReportsController",
        "StoreController",
        "StorefrontController",
        "TasksController",
    ];

    [Fact]
    public void Controllers_do_not_inject_the_DbContext_except_the_documented_backlog()
    {
        string[] violating = ControllerTypes()
            .Where(type => type.GetConstructors()
                .Any(ctor => ctor.GetParameters().Any(parameter => parameter.ParameterType == typeof(HaulScoutDbContext))))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Exact match, not a subset: a new violation fails, and cleaning one fails until the name is removed here —
        // so the backlog can never silently grow and always documents the real remaining work.
        Assert.Equal(ControllersPendingDePersistence, violating);
    }

    /// <summary>A controller that has been de-persisted must stay that way.</summary>
    [Theory]
    [InlineData(typeof(SalesController))]
    [InlineData(typeof(InventoryController))]
    [InlineData(typeof(OrdersController))]
    public void De_persisted_controllers_stay_pure_http_adapters(Type controller)
    {
        ParameterInfo[] dependencies = controller.GetConstructors()
            .SelectMany(ctor => ctor.GetParameters())
            .ToArray();

        Assert.DoesNotContain(dependencies, parameter => parameter.ParameterType == typeof(HaulScoutDbContext));
    }

    /// <summary>The Domain is the centre: it depends on no other layer and knows nothing about persistence.</summary>
    [Fact]
    public void Domain_depends_on_no_other_layer()
    {
        string[] referenced = ReferencedAssemblyNames(typeof(Sale).Assembly);

        Assert.DoesNotContain(referenced, name => name.StartsWith("HaulScout.", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.Contains("EntityFrameworkCore", StringComparison.Ordinal));
    }

    /// <summary>
    /// The Application layer holds pure business logic (calculators, parsers, catalogs). It may know the Domain and
    /// nothing else — no EF, no Infrastructure, no web. This purity is what makes it testable in isolation.
    /// </summary>
    [Fact]
    public void Application_depends_only_on_the_domain()
    {
        string[] referenced = ReferencedAssemblyNames(typeof(AppResult).Assembly);

        Assert.DoesNotContain(referenced, name => name.Contains("EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name is "HaulScout.Infrastructure" or "HaulScout.Api");
    }

    /// <summary>Infrastructure owns persistence; it must never depend on the web layer.</summary>
    [Fact]
    public void Infrastructure_does_not_depend_on_the_api()
    {
        string[] referenced = ReferencedAssemblyNames(typeof(HaulScoutDbContext).Assembly);

        Assert.DoesNotContain(referenced, name => name is "HaulScout.Api");
    }

    private static IEnumerable<Type> ControllerTypes() => typeof(SalesController).Assembly
        .GetTypes()
        .Where(type => type.IsClass
            && !type.IsAbstract
            && type.Namespace == "HaulScout.Api.Controllers"
            && type.Name.EndsWith("Controller", StringComparison.Ordinal));

    private static string[] ReferencedAssemblyNames(Assembly assembly) => assembly
        .GetReferencedAssemblies()
        .Select(reference => reference.Name ?? string.Empty)
        .ToArray();
}
