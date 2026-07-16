using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Sourcing budgets and expenses.

public sealed record CreateBudgetRequest(string Name, decimal StartingAmount, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt);
public sealed record BudgetDto(Guid Id, string Name, decimal StartingAmount, decimal CurrentAmount, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool IsActive);
public sealed record AddExpenseRequest(Guid? InventoryItemId, Guid? SourcingTripId, Guid? SourcingTripStopId, ExpenseCategory Category, string Description, decimal Amount, DateTimeOffset? OccurredAt);
public sealed record ExpenseDto(Guid Id, Guid? InventoryItemId, Guid? SourcingTripId, Guid? SourcingTripStopId, ExpenseCategory Category, string Description, decimal Amount, DateTimeOffset OccurredAt);
public sealed record BudgetSummaryDto(Guid? BudgetId, decimal StartingAmount, decimal CurrentAmount, decimal TotalExpenses, decimal InventoryPurchases, decimal RemainingAmount);
