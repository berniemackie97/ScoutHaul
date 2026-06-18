using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public interface IMarketplaceSearchLinkGenerator
{
    MarketplaceSearchLinksDto Generate(string query);
}
