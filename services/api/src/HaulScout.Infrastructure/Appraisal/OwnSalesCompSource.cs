using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Appraisal;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Appraisal;

/// <summary>
/// The business's own sales of matching items. Found by name, not by product identity: items are
/// almost never linked to one, so an identity match would find nothing.
/// </summary>
internal sealed class OwnSalesCompSource(HaulScoutDbContext db) : ICompSource
{
    private const int CandidateLimit = 200;
    private const int ResultLimit = 25;

    public string Name => "Your sales";
    public bool IsConfigured => true;
    public TimeSpan Deadline => TimeSpan.FromSeconds(5);

    public async Task<CompSourceResult> FindAsync(CompQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> itemTokens = CompMatching.Tokens(query.Item.Title, query.Item.Brand, query.Item.Model);
        string[] searchTokens = itemTokens
            .Where(token => token.Length >= 3 || token.Any(char.IsDigit))
            .OrderByDescending(token => token.Length)
            .Take(8)
            .ToArray();
        if (searchTokens.Length == 0)
        {
            return CompSourceResult.Empty;
        }

        var candidates = await db.MarketObservations
            .AsNoTracking()
            .Where(observation => observation.OrganizationId == query.OrganizationId
                && observation.ObservationType == MarketObservationType.InternalSale
                && !observation.IsArchived)
            .Where(TitleContainsAny(searchTokens))
            .OrderByDescending(observation => observation.SoldAt ?? observation.ObservedAt)
            .Take(CandidateLimit)
            .Select(observation => new
            {
                observation.Title,
                observation.Source,
                observation.Price,
                observation.ShippingPrice,
                observation.Condition,
                Date = observation.SoldAt ?? observation.ObservedAt,
                Brand = observation.InventoryItem != null ? observation.InventoryItem.Brand : null,
            })
            .ToListAsync(cancellationToken);

        Comp[] comps = candidates
            .Select(sale => new { Sale = sale, Tokens = CompMatching.Tokens(sale.Title, sale.Brand) })
            .Where(entry => CompMatching.NamesSameItem(itemTokens, entry.Tokens) && !CompMatching.ModelConflict(itemTokens, entry.Tokens))
            .OrderByDescending(entry => CompMatching.Coverage(itemTokens, entry.Tokens))
            .ThenByDescending(entry => entry.Sale.Date)
            .Take(ResultLimit)
            .Select(entry => new Comp(
                CompKind.YourSale,
                entry.Sale.Source is "Internal" or "" ? "Recorded sale" : entry.Sale.Source,
                entry.Sale.Title,
                entry.Sale.Price,
                entry.Sale.ShippingPrice,
                entry.Sale.Condition,
                Url: null,
                entry.Sale.Date,
                Verified: true))
            .ToArray();

        return new CompSourceResult(comps);
    }

    // OR of "title contains token", built as one expression so it runs as a single query on any provider.
    private static Expression<Func<MarketObservation, bool>> TitleContainsAny(IReadOnlyList<string> tokens)
    {
        ParameterExpression observation = Expression.Parameter(typeof(MarketObservation), "observation");
        Expression title = Expression.Call(
            Expression.Property(observation, nameof(MarketObservation.Title)),
            typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        Expression body = tokens
            .Select(token => (Expression)Expression.Call(
                title,
                typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
                Expression.Property(Expression.Constant(new Term(token)), nameof(Term.Value))))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<MarketObservation, bool>>(body, observation);
    }

    // Read through a member so EF sends each token as a parameter rather than inlining it.
    private sealed record Term(string Value);
}
