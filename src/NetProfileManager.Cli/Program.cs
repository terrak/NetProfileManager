using NetProfileManager.Core.Models;
using NetProfileManager.Core.Protocol;

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

var command = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

return command switch
{
    "list-adapters" => ListAdapters(),
    "list-profiles" or "profiles" => ListProfiles(),
    "add-profile" => SaveProfile(rest, isNew: true),
    "update-profile" => SaveProfile(rest, isNew: false),
    "delete-profile" => DeleteProfile(rest),
    "apply" => Apply(rest),
    "dhcp" => SetDhcp(rest),
    "assignments" => Assignments(),
    "help" or "--help" or "-h" => PrintUsage(),
    _ => Unknown(command)
};

static int PrintUsage()
{
    Console.WriteLine("""
        Gestionnaire de profils réseau — mode console
        (parle au service Windows NetProfileManager, aucune élévation requise)

        Commandes :
          list-adapters                          Liste les cartes réseau et leur configuration actuelle
          list-profiles                          Liste les profils enregistrés
          add-profile --name N --ip IP --mask M [--gateway G] [--dns D1,D2]
          update-profile --id ID [--name N] [--ip IP] [--mask M] [--gateway G] [--dns D1,D2]
          delete-profile --id ID
          apply --adapter "Nom de la carte" --profile <ID ou nom>
          dhcp --adapter "Nom de la carte"       Passe la carte en DHCP complet (IP, passerelle, DNS)
          assignments                            Affiche quel profil est actif sur quelle carte
        """);
    return 0;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Commande inconnue : {command}");
    PrintUsage();
    return 2;
}

static int ListAdapters()
{
    var response = PipeClient.Send(Commands.ListAdapters);
    if (!RequireSuccess(response)) return 1;
    var adapters = PipeClient.DeserializeData<List<AdapterInfo>>(response) ?? new();
    foreach (var a in adapters)
    {
        var ip = a.CurrentAddresses.Count > 0 ? string.Join(", ", a.CurrentAddresses) : "aucune";
        var dns = a.CurrentDns.Count > 0 ? string.Join(", ", a.CurrentDns) : "aucun";
        var active = a.AssignedDhcp ? "DHCP" : a.ActiveProfileId != null ? $"profil {a.ActiveProfileId}" : "aucun";
        Console.WriteLine($"[{a.InterfaceAlias}] {a.OperationalStatus} | IP={ip} | GW={a.CurrentGateway ?? "-"} | DNS={dns} | profil actif={active}");
    }
    return 0;
}

static int ListProfiles()
{
    var response = PipeClient.Send(Commands.ListProfiles);
    if (!RequireSuccess(response)) return 1;
    var profiles = PipeClient.DeserializeData<List<NetworkProfile>>(response) ?? new();
    foreach (var p in profiles)
    {
        Console.WriteLine($"{p.Id}  {p.Name}  IP={p.IpAddress}/{p.SubnetMask}  GW={p.Gateway ?? "-"}  DNS={string.Join(",", p.DnsServers)}");
    }
    return 0;
}

static int SaveProfile(string[] args, bool isNew)
{
    var opts = Cli.OptionsParser.Parse(args);
    if (!opts.TryGetValue("name", out var name) && isNew)
    {
        Console.Error.WriteLine("--name est obligatoire pour add-profile.");
        return 2;
    }

    NetworkProfile profile;
    if (isNew)
    {
        if (!opts.TryGetValue("ip", out var ip) || !opts.TryGetValue("mask", out var mask))
        {
            Console.Error.WriteLine("--ip et --mask sont obligatoires pour add-profile.");
            return 2;
        }
        profile = new NetworkProfile
        {
            Id = string.Empty,
            Name = name!,
            IpAddress = ip,
            SubnetMask = mask,
            Gateway = opts.GetValueOrDefault("gateway"),
            DnsServers = SplitList(opts.GetValueOrDefault("dns")),
            Description = opts.GetValueOrDefault("description")
        };
    }
    else
    {
        if (!opts.TryGetValue("id", out var id))
        {
            Console.Error.WriteLine("--id est obligatoire pour update-profile.");
            return 2;
        }
        var existing = PipeClient.Send(Commands.ListProfiles);
        var profiles = PipeClient.DeserializeData<List<NetworkProfile>>(existing) ?? new();
        var found = profiles.FirstOrDefault(p => p.Id == id || string.Equals(p.Name, id, StringComparison.OrdinalIgnoreCase));
        if (found == null)
        {
            Console.Error.WriteLine($"Profil introuvable : {id}");
            return 1;
        }
        profile = found;
        if (opts.TryGetValue("name", out var n)) profile.Name = n;
        if (opts.TryGetValue("ip", out var i)) profile.IpAddress = i;
        if (opts.TryGetValue("mask", out var m)) profile.SubnetMask = m;
        if (opts.TryGetValue("gateway", out var g)) profile.Gateway = string.IsNullOrWhiteSpace(g) ? null : g;
        if (opts.TryGetValue("dns", out var d)) profile.DnsServers = SplitList(d);
        if (opts.TryGetValue("description", out var desc)) profile.Description = desc;
    }

    var response = PipeClient.Send(Commands.SaveProfile, profile);
    if (!RequireSuccess(response)) return 1;
    Console.WriteLine(response.Message);
    return 0;
}

static int DeleteProfile(string[] args)
{
    var opts = Cli.OptionsParser.Parse(args);
    if (!opts.TryGetValue("id", out var id))
    {
        Console.Error.WriteLine("--id est obligatoire.");
        return 2;
    }
    var response = PipeClient.Send(Commands.DeleteProfile, new DeleteProfilePayload { Id = id });
    if (!RequireSuccess(response)) return 1;
    Console.WriteLine(response.Message);
    return 0;
}

static int Apply(string[] args)
{
    var opts = Cli.OptionsParser.Parse(args);
    if (!opts.TryGetValue("adapter", out var adapter) || !opts.TryGetValue("profile", out var profileRef))
    {
        Console.Error.WriteLine("--adapter et --profile sont obligatoires.");
        return 2;
    }

    var profilesResponse = PipeClient.Send(Commands.ListProfiles);
    var profiles = PipeClient.DeserializeData<List<NetworkProfile>>(profilesResponse) ?? new();
    var profile = profiles.FirstOrDefault(p => p.Id == profileRef || string.Equals(p.Name, profileRef, StringComparison.OrdinalIgnoreCase));
    if (profile == null)
    {
        Console.Error.WriteLine($"Profil introuvable : {profileRef}");
        return 1;
    }

    var response = PipeClient.Send(Commands.ApplyProfile, new ApplyProfilePayload { InterfaceAlias = adapter, ProfileId = profile.Id });
    if (response.ErrorCode == ErrorCodes.ProfileInUse)
    {
        var conflict = PipeClient.DeserializeData<ConflictInfo>(response);
        Console.Error.WriteLine($"CONFLIT: {response.Message} Carte concernée : {conflict?.AdapterAlias}");
        Console.Error.WriteLine("Résolvez le conflit (dhcp --adapter \"...\" ou apply --adapter \"...\" --profile <autre>) puis réessayez.");
        return 3;
    }
    if (!RequireSuccess(response)) return 1;
    Console.WriteLine(response.Message);
    return 0;
}

static int SetDhcp(string[] args)
{
    var opts = Cli.OptionsParser.Parse(args);
    if (!opts.TryGetValue("adapter", out var adapter))
    {
        Console.Error.WriteLine("--adapter est obligatoire.");
        return 2;
    }
    var response = PipeClient.Send(Commands.SetDhcp, new SetDhcpPayload { InterfaceAlias = adapter });
    if (!RequireSuccess(response)) return 1;
    Console.WriteLine(response.Message);
    return 0;
}

static int Assignments()
{
    var response = PipeClient.Send(Commands.GetAssignments);
    if (!RequireSuccess(response)) return 1;
    var state = PipeClient.DeserializeData<AppState>(response);
    if (state == null || state.Assignments.Count == 0)
    {
        Console.WriteLine("Aucune carte n'est gérée pour le moment.");
        return 0;
    }
    foreach (var kv in state.Assignments)
    {
        Console.WriteLine($"{kv.Key} -> {(kv.Value == "DHCP" ? "DHCP" : kv.Value)}");
    }
    return 0;
}

static bool RequireSuccess(PipeResponse response)
{
    if (response.Success) return true;
    Console.Error.WriteLine($"Erreur : {response.Message}");
    if (response.ErrorCode == null || response.ErrorCode != ErrorCodes.ProfileInUse)
    {
        Console.Error.WriteLine("(Le service Windows « NetProfileManager » est-il installé et démarré ?)");
    }
    return false;
}

static List<string> SplitList(string? value)
    => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? new List<string>();

namespace NetProfileManager.Cli
{
    public static class OptionsParser
    {
        public static Dictionary<string, string> Parse(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i].TrimStart('-');
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    result[key] = args[i + 1];
                    i++;
                }
                else
                {
                    result[key] = "true";
                }
            }
            return result;
        }
    }
}
