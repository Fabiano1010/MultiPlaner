using Microsoft.AspNetCore.DataProtection;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class InvitationTokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("MultiPlaner.RoomInvitation.Token.v1");

    public string Protect(string token)
    {
        return protector.Protect(token);
    }

    public string Unprotect(string protectedToken)
    {
        return protector.Unprotect(protectedToken);
    }
}