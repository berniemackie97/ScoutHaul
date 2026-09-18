using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using HaulScout.Api.Authorization;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.PlatformOrders;
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
        "ListingsController",
        "MarketAnalysisController",
        "MarketObservationsController",
        "PlatformListingsController",
        "ProductIdentitiesController",
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

    /// <summary>
    /// Controllers that declare no gate because they resolve the caller inside their own services — each
    /// for a reason that makes an organization gate wrong. This list may only change deliberately.
    /// </summary>
    private static readonly Dictionary<string, string> ControllersGatedInsideTheirServices = new()
    {
        ["OrganizationsController"] = "creating or joining an organization is how a user without one gets one",
        ["SessionController"] = "the session bootstrap has to answer before the user has an organization",
    };

    /// <summary>
    /// Every endpoint says who may call it.
    ///
    /// The operator's user context is only resolved by the gate attributes. An endpoint without one
    /// reads an unresolved context — no active organization — and answers every caller as if they
    /// belonged to none. That is not a hypothetical: search shipped that way and returned no records
    /// to anyone, while its unit tests passed against a stub that was always resolved.
    /// </summary>
    [Fact]
    public void Every_controller_declares_who_may_call_it()
    {
        string[] ungated = ControllerTypes()
            .Where(type => !IsGated(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ControllersGatedInsideTheirServices.Keys.OrderBy(name => name, StringComparer.Ordinal), ungated);
    }

    /// <summary>Gated at the class, or on every endpoint it exposes.</summary>
    private static bool IsGated(Type controller)
    {
        if (DeclaresGate(controller.GetCustomAttributes(inherit: true)))
        {
            return true;
        }

        MethodInfo[] endpoints = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .ToArray();

        return endpoints.Length > 0 && endpoints.All(method => DeclaresGate(method.GetCustomAttributes(inherit: true)));
    }

    /// <summary>
    /// An organization gate, an explicit opt-out, or the shopper's own sign-in. A bare [Authorize] does
    /// not count: it authenticates the operator without resolving their organization, which is the
    /// exact state this test exists to rule out.
    /// </summary>
    private static bool DeclaresGate(object[] attributes) => attributes.Any(attribute =>
        attribute is RequireOrganizationPermissionAttribute or RequireOrganizationMemberAttribute or AllowAnonymousAttribute
        || attribute is AuthorizeAttribute { AuthenticationSchemes: StorefrontAuthDefaults.Scheme });

    /// <summary>A controller that has been de-persisted must stay that way.</summary>
    [Theory]
    [InlineData(typeof(SalesController))]
    [InlineData(typeof(InventoryController))]
    [InlineData(typeof(OrdersController))]
    [InlineData(typeof(PlatformOrdersController))]
    public void De_persisted_controllers_stay_pure_http_adapters(Type controller)
    {
        ParameterInfo[] dependencies = controller.GetConstructors()
            .SelectMany(ctor => ctor.GetParameters())
            .ToArray();

        Assert.DoesNotContain(dependencies, parameter => parameter.ParameterType == typeof(HaulScoutDbContext));
    }

    [Theory]
    [InlineData(typeof(PlatformOrderIngestionService))]
    [InlineData(typeof(PlatformOrderFeeEnrichmentService))]
    [InlineData(typeof(PlatformOrderImportWorkflowService))]
    [InlineData(typeof(MarketplaceRefundImportService))]
    public void Platform_order_import_use_cases_do_not_take_the_DbContext(Type workflow)
    {
        ParameterInfo[] dependencies = workflow.GetConstructors()
            .SelectMany(ctor => ctor.GetParameters())
            .ToArray();

        Assert.DoesNotContain(dependencies, parameter => parameter.ParameterType == typeof(HaulScoutDbContext));
    }

    [Fact]
    public void Marketplace_import_use_cases_do_not_live_in_api_services()
    {
        string[] misplaced = typeof(PlatformOrdersController).Assembly
            .GetTypes()
            .Where(type => type.IsClass
                && type.Namespace == "HaulScout.Api.Services"
                && (type.Name.Contains("PlatformOrder", StringComparison.Ordinal)
                    || type.Name.Contains("MarketplaceRefund", StringComparison.Ordinal)))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(misplaced);
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

    /// <summary>
    /// Application code may use neutral Microsoft.Extensions abstractions, but it must not take a dependency on
    /// ASP.NET Core. HTTP types belong in Api adapters only.
    /// </summary>
    [Fact]
    public void Application_does_not_depend_on_AspNetCore()
    {
        string[] referenced = ReferencedAssemblyNames(typeof(AppResult).Assembly);

        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
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
