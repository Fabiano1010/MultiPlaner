using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class InvitationLinkBuilder(IConfiguration configuration)
{
    public RoomInvitationResponse CreateResponse(RoomInvitation invitation, string token)
    {
        var webBase = PublicWebUrl.GetRequiredBase(
            configuration,
            "Invitations:PublicWebBaseUrl",
            "Configure Invitations:PublicWebBaseUrl as the HTTPS address of the web application.");
        return new RoomInvitationResponse(invitation.Id, invitation.RoomId, $"/api/invitations/{token}",
            $"{webBase}/join/{token}", invitation.ExpiresAtUtc, invitation.MaxUses);
    }
}