using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// Customers are created by checkout and by imports, so their details arrive as the platform spelled
/// them. Correcting those details must be possible, and must fail in a way the operator understands
/// when the correction collides with somebody else.
/// </summary>
public class CustomerEditTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, Guid Org, Guid User) NewContext(string store)
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return (db, org, user);
    }

    private static Customer SeedCustomer(HaulScoutDbContext db, Guid org, string? email, string? name)
    {
        Customer customer = new() { OrganizationId = org, Email = email, DisplayName = name };
        db.Customers.Add(customer);
        db.SaveChanges();
        return customer;
    }

    [Fact]
    public async Task A_customers_details_can_be_corrected()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_customers_details_can_be_corrected));
        await using HaulScoutDbContext _ = db;
        Customer customer = SeedCustomer(db, org, "typo@exmaple.com", "jane");
        CustomersController controller = new(db, new StubCurrentUser(org, user));

        ActionResult<CustomerDetailDto> result = await controller.Update(
            customer.Id,
            new UpdateCustomerRequest("Jane Doe", "jane@example.com", "(941) 555-0123", AcceptsMarketing: true),
            CancellationToken.None);

        Assert.NotNull(result.Value);
        Customer saved = await db.Customers.SingleAsync();
        Assert.Equal("Jane Doe", saved.DisplayName);
        Assert.Equal("jane@example.com", saved.Email);
        Assert.Equal("(941) 555-0123", saved.Phone);
        Assert.True(saved.AcceptsMarketing);
    }

    [Fact]
    public async Task Blank_details_clear_rather_than_storing_whitespace()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Blank_details_clear_rather_than_storing_whitespace));
        await using HaulScoutDbContext _ = db;
        Customer customer = SeedCustomer(db, org, "someone@example.com", "Someone");
        CustomersController controller = new(db, new StubCurrentUser(org, user));

        await controller.Update(
            customer.Id,
            new UpdateCustomerRequest("   ", null, "  ", AcceptsMarketing: false),
            CancellationToken.None);

        Customer saved = await db.Customers.SingleAsync();
        Assert.Null(saved.DisplayName);
        Assert.Null(saved.Email);
        Assert.Null(saved.Phone);
    }

    [Fact]
    public async Task An_email_already_used_by_someone_else_is_refused_with_an_explanation()
    {
        // The unique index would otherwise surface as "An error occurred while processing your request".
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(An_email_already_used_by_someone_else_is_refused_with_an_explanation));
        await using HaulScoutDbContext _ = db;
        SeedCustomer(db, org, "taken@example.com", "First");
        Customer second = SeedCustomer(db, org, "second@example.com", "Second");
        CustomersController controller = new(db, new StubCurrentUser(org, user));

        ActionResult<CustomerDetailDto> result = await controller.Update(
            second.Id,
            new UpdateCustomerRequest("Second", "TAKEN@example.com", null, AcceptsMarketing: false),
            CancellationToken.None);

        ProblemDetails conflict = ProblemAssert.Blocked(result.Result);
        Assert.Contains("already uses that email", conflict.Detail ?? "", StringComparison.OrdinalIgnoreCase);

        // Nothing was written.
        Customer unchanged = await db.Customers.SingleAsync(candidate => candidate.Id == second.Id);
        Assert.Equal("second@example.com", unchanged.Email);
    }

    [Fact]
    public async Task Keeping_your_own_email_is_not_a_collision()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Keeping_your_own_email_is_not_a_collision));
        await using HaulScoutDbContext _ = db;
        Customer customer = SeedCustomer(db, org, "same@example.com", "Old Name");
        CustomersController controller = new(db, new StubCurrentUser(org, user));

        ActionResult<CustomerDetailDto> result = await controller.Update(
            customer.Id,
            new UpdateCustomerRequest("New Name", "same@example.com", null, AcceptsMarketing: false),
            CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal("New Name", (await db.Customers.SingleAsync()).DisplayName);
    }

    [Fact]
    public async Task A_malformed_email_is_refused()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_malformed_email_is_refused));
        await using HaulScoutDbContext _ = db;
        Customer customer = SeedCustomer(db, org, "good@example.com", "Someone");
        CustomersController controller = new(db, new StubCurrentUser(org, user));

        ActionResult<CustomerDetailDto> result = await controller.Update(
            customer.Id,
            new UpdateCustomerRequest("Someone", "not-an-email", null, AcceptsMarketing: false),
            CancellationToken.None);

        ProblemAssert.Invalid(result.Result);
        Assert.Equal("good@example.com", (await db.Customers.SingleAsync()).Email);
    }

    [Fact]
    public async Task Another_organizations_customer_is_not_found()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Another_organizations_customer_is_not_found));
        await using HaulScoutDbContext _ = db;
        Customer customer = SeedCustomer(db, org, "theirs@example.com", "Theirs");
        CustomersController controller = new(db, new StubCurrentUser(Guid.NewGuid(), user));

        ActionResult<CustomerDetailDto> result = await controller.Update(
            customer.Id,
            new UpdateCustomerRequest("Hacked", "hacked@example.com", null, AcceptsMarketing: true),
            CancellationToken.None);

        ProblemAssert.Missing(result.Result);
    }
}
