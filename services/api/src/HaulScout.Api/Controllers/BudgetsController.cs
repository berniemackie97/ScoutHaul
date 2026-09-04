using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/budgets")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageBudgets)]
public sealed class BudgetsController(HaulScoutDbContext db, ICurrentUserContext currentUser) : HaulScoutControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BudgetDto>> Create(CreateBudgetRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Invalid("Budget name is required.");
        }

        if (request.StartingAmount < 0)
        {
            return Invalid("Starting amount cannot be negative.");
        }

        List<Budget> activeBudgets = await db.Budgets
            .Where(item => item.OrganizationId == currentUser.OrganizationId && item.IsActive)
            .ToListAsync(cancellationToken);

        foreach (Budget activeBudget in activeBudgets)
        {
            activeBudget.IsActive = false;
        }

        Budget budget = new()
        {
            OrganizationId = currentUser.OrganizationId,
            Name = request.Name.Trim(),
            StartingAmount = request.StartingAmount,
            CurrentAmount = request.StartingAmount,
            StartsAt = request.StartsAt ?? DateTimeOffset.UtcNow,
            EndsAt = request.EndsAt,
            IsActive = true
        };

        db.Budgets.Add(budget);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetCurrent), budget.ToDto());
    }

    [HttpGet("current")]
    public async Task<ActionResult<BudgetDto>> GetCurrent(CancellationToken cancellationToken)
    {
        Budget? budget = await db.Budgets
            .Where(item => item.OrganizationId == currentUser.OrganizationId && item.IsActive)
            .OrderByDescending(item => item.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);

        return budget is null ? Missing() : budget.ToDto();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBudget(Guid id, CancellationToken cancellationToken)
    {
        Budget? budget = await db.Budgets.SingleOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

        if (budget is null)
        {
            return Missing();
        }

        db.Budgets.Remove(budget);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("expenses")]
    public async Task<ActionResult<ExpenseDto>> AddExpense(AddExpenseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Invalid("Expense description is required.");
        }

        if (request.Amount <= 0)
        {
            return Invalid("Expense amount must be greater than zero.");
        }

        Expense expense = new()
        {
            OrganizationId = currentUser.OrganizationId,
            InventoryItemId = request.InventoryItemId,
            SourcingTripId = request.SourcingTripId,
            SourcingTripStopId = request.SourcingTripStopId,
            Category = request.Category,
            Description = request.Description.Trim(),
            Amount = request.Amount,
            OccurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow
        };

        db.Expenses.Add(expense);

        Budget? budget = await db.Budgets
            .Where(item => item.OrganizationId == currentUser.OrganizationId && item.IsActive)
            .OrderByDescending(item => item.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (budget is not null)
        {
            budget.CurrentAmount -= request.Amount;
        }

        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(ListExpenses), expense.ToDto());
    }

    [HttpGet("expenses")]
    public async Task<ActionResult<IReadOnlyList<ExpenseDto>>> ListExpenses(CancellationToken cancellationToken)
    {
        List<ExpenseDto> expenses = await db.Expenses
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .OrderByDescending(item => item.OccurredAt)
            .Take(200)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return expenses;
    }

    [HttpDelete("expenses/{id:guid}")]
    public async Task<IActionResult> DeleteExpense(Guid id, CancellationToken cancellationToken)
    {
        Expense? expense = await db.Expenses.SingleOrDefaultAsync(
            item => item.Id == id && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);

        if (expense is null)
        {
            return Missing();
        }

        Budget? budget = await db.Budgets
            .Where(item => item.OrganizationId == currentUser.OrganizationId && item.IsActive)
            .OrderByDescending(item => item.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (budget is not null
            && expense.OccurredAt >= budget.StartsAt
            && (!budget.EndsAt.HasValue || expense.OccurredAt <= budget.EndsAt.Value))
        {
            budget.CurrentAmount += expense.Amount;
        }

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<BudgetSummaryDto>> Summary(CancellationToken cancellationToken)
    {
        Budget? budget = await db.Budgets
            .Where(item => item.OrganizationId == currentUser.OrganizationId && item.IsActive)
            .OrderByDescending(item => item.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);

        IQueryable<Expense> expenseQuery = db.Expenses.Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (budget is not null)
        {
            expenseQuery = expenseQuery.Where(item => item.OccurredAt >= budget.StartsAt);

            if (budget.EndsAt.HasValue)
            {
                expenseQuery = expenseQuery.Where(item => item.OccurredAt <= budget.EndsAt.Value);
            }
        }

        List<Expense> expenses = await expenseQuery.ToListAsync(cancellationToken);

        decimal totalExpenses = expenses.Sum(item => item.Amount);
        decimal inventoryPurchases = expenses
            .Where(item => item.Category == ExpenseCategory.InventoryPurchase)
            .Sum(item => item.Amount);

        return new BudgetSummaryDto(
            budget?.Id,
            budget?.StartingAmount ?? 0,
            budget?.CurrentAmount ?? 0,
            totalExpenses,
            inventoryPurchases,
            budget?.CurrentAmount ?? 0);
    }
}
