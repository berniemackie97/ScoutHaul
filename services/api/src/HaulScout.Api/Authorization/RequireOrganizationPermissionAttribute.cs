using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using HaulScout.Api.Services;

namespace HaulScout.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireOrganizationPermissionAttribute(string permission) : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Endpoints explicitly marked [AllowAnonymous] opt out of org-permission enforcement
        // (e.g. GUID-addressed public image bytes that <Image> fetches without auth headers).
        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
        {
            return;
        }

        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
        await currentUser.ResolveAsync();

        // Org-less users (no membership yet) cannot touch org-scoped endpoints — they must
        // onboard first (create an org or redeem an invite).
        if (currentUser.ActiveOrganizationId is not Guid organizationId)
        {
            context.Result = new ForbidResult();
            return;
        }

        var access = context.HttpContext.RequestServices.GetRequiredService<OrganizationAccessService>();
        await access.RequirePermission(organizationId, permission, context.HttpContext.RequestAborted);
    }
}
