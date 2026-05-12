using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Application.Services;

public interface IMarketplaceSearchLinkGenerator
{
    MarketplaceSearchLinksDto Generate(string query);
}
