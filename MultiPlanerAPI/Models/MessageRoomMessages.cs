using MultiPlanerSharedModels.JSON;

namespace MultiPlanerAPI.Models;

public class MessageRoomMessages
{
    public int Id { get; set; }
    public int MessageRoomId { get; set; }
    
    public MessageRoomMessagesContent Messages { get; set; } = new();
    public MessageRoom MessageRoom { get; set; } = null!;
}