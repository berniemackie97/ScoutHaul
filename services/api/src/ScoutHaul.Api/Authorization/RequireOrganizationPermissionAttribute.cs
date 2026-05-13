using Microsoft.AspNetCore.Mvc.Filters;
using ScoutHaul.Api.Services;

namespace ScoutHaul.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireOrganizationPermissionAttribute(string permission) : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
        var access = context.HttpContext.RequestServices.GetRequiredService<OrganizationAccessService>();
        await access.RequirePermission(currentUser.OrganizationId, permission, context.HttpContext.RequestAborted);
    }
}
