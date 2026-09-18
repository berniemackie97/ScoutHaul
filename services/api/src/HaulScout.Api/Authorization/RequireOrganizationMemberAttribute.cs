using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using HaulScout.Application.Security;

namespace HaulScout.Api.Authorization;

/// <summary>
/// Requires a signed-in member of an organization, and no particular permission.
///
/// For endpoints assembled from several areas — search, the home console — that check each part
/// against its own permission and leave out what the caller may not see. They still need what
/// <see cref="RequireOrganizationPermissionAttribute"/> does before any permission is checked: the
/// caller resolved to a verified membership. Without it the user context is never resolved, the
/// active organization reads as none, and the endpoint answers every caller with nothing — which is
/// how the portal's search shipped returning no records at all.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireOrganizationMemberAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
        {
            return;
        }

        ICurrentUserContext currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
        await currentUser.ResolveAsync();

        // Resolution verifies membership of the requested organization; no active organization
        // means the caller belongs to none, or asked for one they do not belong to.
        if (currentUser.ActiveOrganizationId is null)
        {
            context.Result = new ForbidResult();
        }
    }
}
