namespace MultiPlanerAPI.Models;

public class PollUser
{
    public int IdUser { get; set; }
    public int IdPoll { get; set; }
    public bool Voted { get; set; }
    public bool IsOwner { get; set; }

    public User User { get; set; } = null!;
    public Poll Poll { get; set; } = null!;
}