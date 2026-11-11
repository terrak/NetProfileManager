using System.Text.Json;
using NetProfileManager.Core.Models;

namespace NetProfileManager.Core.Services;

public class AssignmentStore
{
    public const string DhcpMarker = "DHCP";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AssignmentStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "assignments.json");
    }

    public async Task<AppState> LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppState();
            }
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<AppState>(stream) ?? new AppState();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppState state)
    {
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }
}
