using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

public static class OrganizationMapping
{
    public static OrganizationDto ToDto(this Organization item) => new(item.Id, item.Name, item.CreatedAt, item.StoreDomain);
}
