using System.Text.Json;
using NetProfileManager.Core.Models;

namespace NetProfileManager.Core.Services;

public class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ProfileStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "profiles.json");
    }

    public async Task<List<NetworkProfile>> LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_filePath))
            {
                return new List<NetworkProfile>();
            }
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<NetworkProfile>>(stream) ?? new List<NetworkProfile>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(List<NetworkProfile> profiles)
    {
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(profiles, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }
}
