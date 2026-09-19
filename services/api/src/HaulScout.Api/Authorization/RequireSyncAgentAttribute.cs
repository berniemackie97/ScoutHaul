using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using HaulScout.Api.Authentication;
using HaulScout.Application.Security;

namespace HaulScout.Api.Authorization;

/// <summary>
/// Only a paired browser, by its device token, may call this — and only as the member who paired it.
///
/// It is the agent's counterpart of <see cref="RequireOrganizationMemberAttribute"/>: it names the
/// device-token scheme, so a person's portal session can never be used here and a device token can
/// never be used anywhere else, then resolves the caller's organization the same way every other gate
/// does, so the tenant filter and permission checks behind it work unchanged.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireSyncAgentAttribute : AuthorizeAttribute, IAsyncAuthorizationFilter
{
    public RequireSyncAgentAttribute()
    {
        AuthenticationSchemes = SyncAgentAuthDefaults.Scheme;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
        {
            return;
        }

        ICurrentUserContext currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();
        await currentUser.ResolveAsync();
        if (currentUser.ActiveOrganizationId is null)
        {
            context.Result = new ForbidResult(SyncAgentAuthDefaults.Scheme);
        }
    }
}
