namespace MultiPlanerAPI.Models;

public class MessageRoom
{
    public int Id { get; set; }
    public int CalendarId { get; set; }
    
    public Calendar Calendar { get; set; } = null!;
    public MessageRoomMessages? MessageRoomMessages { get; set; }
}