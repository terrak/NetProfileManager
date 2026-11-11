using System.Net;
using System.Text.Json;
using NetProfileManager.Core.Models;
using NetProfileManager.Core.Protocol;
using NetProfileManager.Core.Services;

namespace NetProfileManager.Service;

public class CommandHandler
{
    private readonly ProfileStore _profileStore;
    private readonly AssignmentStore _assignmentStore;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    public CommandHandler(string dataDirectory, ILogger logger)
    {
        _profileStore = new ProfileStore(dataDirectory);
        _assignmentStore = new AssignmentStore(dataDirectory);
        _logger = logger;
    }

    public async Task<PipeResponse> HandleAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return request.Command switch
            {
                Commands.ListAdapters => await ListAdaptersAsync(),
                Commands.ListProfiles => Ok(await _profileStore.LoadAsync()),
                Commands.SaveProfile => await SaveProfileAsync(request.Payload),
                Commands.DeleteProfile => await DeleteProfileAsync(request.Payload),
                Commands.ApplyProfile => await ApplyProfileAsync(request.Payload, cancellationToken),
                Commands.SetDhcp => await SetDhcpAsync(request.Payload, cancellationToken),
                Commands.GetAssignments => Ok(await _assignmentStore.LoadAsync()),
                _ => new PipeResponse { Success = false, Message = $"Commande inconnue : {request.Command}", ErrorCode = ErrorCodes.Invalid }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la commande {Command}", request.Command);
            return new PipeResponse { Success = false, Message = ex.Message, ErrorCode = ErrorCodes.Invalid };
        }
    }

    private static PipeResponse Ok(object? data, string? message = null)
        => new() { Success = true, Message = message, Data = data == null ? null : JsonSerializer.Serialize(data) };

    private async Task<PipeResponse> ListAdaptersAsync()
    {
        var adapters = AdapterEnumerator.GetAdapters();
        var state = await _assignmentStore.LoadAsync();
        foreach (var adapter in adapters)
        {
            if (state.Assignments.TryGetValue(adapter.InterfaceAlias, out var value))
            {
                if (value == AssignmentStore.DhcpMarker)
                {
                    adapter.AssignedDhcp = true;
                }
                else
                {
                    adapter.ActiveProfileId = value;
                }
            }
        }
        return Ok(adapters);
    }

    private async Task<PipeResponse> SaveProfileAsync(string? payload)
    {
        var profile = JsonSerializer.Deserialize<NetworkProfile>(payload ?? string.Empty);
        if (profile == null)
        {
            return new PipeResponse { Success = false, Message = "Profil illisible.", ErrorCode = ErrorCodes.Invalid };
        }

        var error = ValidateProfile(profile);
        if (error != null)
        {
            return new PipeResponse { Success = false, Message = error, ErrorCode = ErrorCodes.Invalid };
        }

        var profiles = await _profileStore.LoadAsync();
        var duplicate = profiles.FirstOrDefault(p =>
            !string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (duplicate != null)
        {
            return new PipeResponse { Success = false, Message = $"Un profil nommé « {profile.Name} » existe déjà.", ErrorCode = ErrorCodes.Invalid };
        }

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString("N");
        }

        var existing = profiles.FirstOrDefault(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            profiles.Add(profile);
        }
        else
        {
            var index = profiles.IndexOf(existing);
            profiles[index] = profile;
        }

        await _profileStore.SaveAsync(profiles);
        return Ok(profile, $"Profil « {profile.Name} » enregistré.");
    }

    private async Task<PipeResponse> DeleteProfileAsync(string? payload)
    {
        var request = JsonSerializer.Deserialize<DeleteProfilePayload>(payload ?? string.Empty);
        if (request == null || string.IsNullOrWhiteSpace(request.Id))
        {
            return new PipeResponse { Success = false, Message = "Identifiant de profil manquant.", ErrorCode = ErrorCodes.Invalid };
        }

        var profiles = await _profileStore.LoadAsync();
        var profile = profiles.FirstOrDefault(p => string.Equals(p.Id, request.Id, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            return new PipeResponse { Success = false, Message = "Profil introuvable.", ErrorCode = ErrorCodes.NotFound };
        }

        var state = await _assignmentStore.LoadAsync();
        var conflict = state.Assignments.FirstOrDefault(kv => kv.Value == profile.Id);
        if (!string.IsNullOrEmpty(conflict.Key))
        {
            return new PipeResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ProfileInUse,
                Message = $"Le profil « {profile.Name} » est actif sur la carte « {conflict.Key} ». Libérez-la d'abord (DHCP ou autre profil).",
                Data = JsonSerializer.Serialize(new ConflictInfo { ProfileId = profile.Id, ProfileName = profile.Name, AdapterAlias = conflict.Key })
            };
        }

        profiles.Remove(profile);
        await _profileStore.SaveAsync(profiles);
        return Ok(null, $"Profil « {profile.Name} » supprimé.");
    }

    private async Task<PipeResponse> ApplyProfileAsync(string? payload, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<ApplyProfilePayload>(payload ?? string.Empty);
        if (request == null || string.IsNullOrWhiteSpace(request.InterfaceAlias) || string.IsNullOrWhiteSpace(request.ProfileId))
        {
            return new PipeResponse { Success = false, Message = "Carte ou profil manquant.", ErrorCode = ErrorCodes.Invalid };
        }

        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var profiles = await _profileStore.LoadAsync();
            var profile = profiles.FirstOrDefault(p => string.Equals(p.Id, request.ProfileId, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                return new PipeResponse { Success = false, Message = "Profil introuvable.", ErrorCode = ErrorCodes.NotFound };
            }

            var state = await _assignmentStore.LoadAsync();
            var conflict = state.Assignments.FirstOrDefault(kv =>
                kv.Value == profile.Id && !string.Equals(kv.Key, request.InterfaceAlias, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(conflict.Key))
            {
                return new PipeResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ProfileInUse,
                    Message = $"Le profil « {profile.Name} » est déjà actif sur la carte « {conflict.Key} ».",
                    Data = JsonSerializer.Serialize(new ConflictInfo { ProfileId = profile.Id, ProfileName = profile.Name, AdapterAlias = conflict.Key })
                };
            }

            NetworkApplier.ApplyStatic(request.InterfaceAlias, profile);
            state.Assignments[request.InterfaceAlias] = profile.Id;
            await _assignmentStore.SaveAsync(state);
            return Ok(null, $"Profil « {profile.Name} » appliqué sur « {request.InterfaceAlias} ».");
        }
        finally
        {
            _applyLock.Release();
        }
    }

    private async Task<PipeResponse> SetDhcpAsync(string? payload, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<SetDhcpPayload>(payload ?? string.Empty);
        if (request == null || string.IsNullOrWhiteSpace(request.InterfaceAlias))
        {
            return new PipeResponse { Success = false, Message = "Carte manquante.", ErrorCode = ErrorCodes.Invalid };
        }

        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            NetworkApplier.ApplyDhcp(request.InterfaceAlias);
            var state = await _assignmentStore.LoadAsync();
            state.Assignments[request.InterfaceAlias] = AssignmentStore.DhcpMarker;
            await _assignmentStore.SaveAsync(state);
            return Ok(null, $"La carte « {request.InterfaceAlias} » est passée en DHCP (IP, passerelle et DNS automatiques).");
        }
        finally
        {
            _applyLock.Release();
        }
    }

    private static string? ValidateProfile(NetworkProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            return "Le nom du profil est obligatoire.";
        }
        if (!IPAddress.TryParse(profile.IpAddress, out _) || profile.IpAddress.Contains(':'))
        {
            return $"Adresse IP invalide : {profile.IpAddress}";
        }
        if (!IPAddress.TryParse(profile.SubnetMask, out var mask) || !IsValidMask(mask))
        {
            return $"Masque de sous-réseau invalide : {profile.SubnetMask}";
        }
        if (!string.IsNullOrWhiteSpace(profile.Gateway) &&
            (!IPAddress.TryParse(profile.Gateway, out _) || profile.Gateway.Contains(':')))
        {
            return $"Passerelle invalide : {profile.Gateway}";
        }
        foreach (var dns in profile.DnsServers)
        {
            if (!IPAddress.TryParse(dns, out _) || dns.Contains(':'))
            {
                return $"Adresse DNS invalide : {dns}";
            }
        }
        return null;
    }

    private static bool IsValidMask(IPAddress mask)
    {
        var bytes = mask.GetAddressBytes();
        var seenZero = false;
        foreach (var b in bytes)
        {
            if (b == 0)
            {
                seenZero = true;
            }
            else if (b != 255)
            {
                if (seenZero || (b & (b + 1)) != 0)
                {
                    return false;
                }
            }
            else if (seenZero)
            {
                return false;
            }
        }
        return true;
    }
}
