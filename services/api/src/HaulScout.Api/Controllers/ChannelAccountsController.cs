using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/channel-accounts")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageListings)]
public sealed class ChannelAccountsController(IChannelAccountService channels, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChannelAccountDto>>> List(CancellationToken cancellationToken)
        => Ok(await channels.ListAsync(currentUser.OrganizationId, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChannelAccountDto>> Get(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await channels.GetAsync(currentUser.OrganizationId, id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ChannelAccountDto>> Create(UpsertChannelAccountRequest request, CancellationToken cancellationToken)
    {
        AppResult<ChannelAccountDto> result = await channels.CreateAsync(currentUser.OrganizationId, request, cancellationToken);
        return result.Status == AppResultStatus.Ok && result.Value is not null
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
            : ToActionResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ChannelAccountDto>> Update(Guid id, UpsertChannelAccountRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await channels.UpdateAsync(currentUser.OrganizationId, id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        AppResult result = await channels.DeleteAsync(currentUser.OrganizationId, id, cancellationToken);
        return result.Status switch
        {
            AppResultStatus.Ok => NoContent(),
            AppResultStatus.NotFound => NotFound(),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
    }

    private ActionResult<ChannelAccountDto> ToActionResult(AppResult<ChannelAccountDto> result) =>
        result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => result.Value,
            AppResultStatus.NotFound => NotFound(),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => BadRequest(result.Message),
        };
}
