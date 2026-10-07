using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class InvitationLinkBuilder(IConfiguration configuration)
{
    public RoomInvitationResponse CreateResponse(RoomInvitation invitation, string token)
    {
        var configuredBase = configuration["Invitations:PublicWebBaseUrl"];
        if (!Uri.TryCreate(configuredBase, UriKind.Absolute, out var webBase) ||
            webBase.Scheme != Uri.UriSchemeHttps ||
            webBase.UserInfo.Length != 0 ||
            webBase.Query.Length != 0 || webBase.Fragment.Length != 0)
        {
            throw new InvalidOperationException(
                "Configure Invitations:PublicWebBaseUrl as the HTTPS address of the web application.");
        }

        var joinPath = $"/api/invitations/{token}";
        var webPath = webBase.AbsolutePath.TrimEnd('/');
        var joinUrl = $"{webBase.GetLeftPart(UriPartial.Authority)}{webPath}/join/{token}";
        return new RoomInvitationResponse(invitation.Id, invitation.RoomId, joinPath, joinUrl,
            invitation.ExpiresAtUtc, invitation.MaxUses);
    }
}
