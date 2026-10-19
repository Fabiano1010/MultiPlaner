namespace MultiPlanerSharedUI.Services;

public sealed class RoomSelection
{
    private readonly HashSet<int> _ids = new();
    
    public event Action? Changed;
    
    public int Count => _ids.Count;
    public bool Active => _ids.Count > 0;
    
    public IReadOnlyCollection<int> Ids => _ids;
    public bool IsSelected(int id) => _ids.Contains(id);

    public async Task Toggle(int id) {
        if(!_ids.Remove(id)) _ids.Add(id);
        Changed?.Invoke();
    }

    public async Task Clear() {
        if (_ids.Count == 0 ) return;
        _ids.Clear();
        Changed?.Invoke();
    }

    public async Task Prune(IEnumerable<int> existingIds) {
        if (_ids.RemoveWhere(id => !existingIds.Contains(id)) > 0) {
            Changed?.Invoke();
        }
    }
}