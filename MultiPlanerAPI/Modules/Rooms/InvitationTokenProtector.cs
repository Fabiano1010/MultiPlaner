using Microsoft.AspNetCore.DataProtection;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class InvitationTokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("MultiPlaner.RoomInvitation.Token.v1");

    public string Protect(string token) => protector.Protect(token);
    public string Unprotect(string protectedToken) => protector.Unprotect(protectedToken);
}
