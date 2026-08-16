using MultiPlanerSharedModels.JSON;

namespace MultiPlanerAPI.Models;

public class UserSettings
{
    public int IdUser { get; set; }
    public UserSettingsContent Settings { get; set; } = new();

    public User User { get; set; } = null!;
}