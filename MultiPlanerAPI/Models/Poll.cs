using MultiPlanerSharedModels.JSON;

namespace MultiPlanerAPI.Models;

public class Poll
{
    public int Id { get; set; }
    public PollContent Content { get; set; } = new();
    public PollResult Result { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public ICollection<CalendarPoll> CalendarPolls { get; set; } = new List<CalendarPoll>();
    public ICollection<PollUser> PollUsers { get; set; } = new List<PollUser>();
}