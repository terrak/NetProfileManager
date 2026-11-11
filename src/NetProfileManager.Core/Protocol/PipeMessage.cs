namespace NetProfileManager.Core.Protocol;

public static class PipeNames
{
    public const string PipeName = "NetProfileManager.Pipe";
}

public static class Commands
{
    public const string ListAdapters = "list-adapters";
    public const string ListProfiles = "list-profiles";
    public const string SaveProfile = "save-profile";
    public const string DeleteProfile = "delete-profile";
    public const string ApplyProfile = "apply-profile";
    public const string SetDhcp = "set-dhcp";
    public const string GetAssignments = "get-assignments";
}

public static class ErrorCodes
{
    public const string ProfileInUse = "PROFILE_IN_USE";
    public const string NotFound = "NOT_FOUND";
    public const string Invalid = "INVALID";
}

public class PipeRequest
{
    public string Command { get; set; } = string.Empty;
    public string? Payload { get; set; }
}

public class PipeResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public string? Data { get; set; }
}

public class ApplyProfilePayload
{
    public string InterfaceAlias { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
}

public class SetDhcpPayload
{
    public string InterfaceAlias { get; set; } = string.Empty;
}

public class DeleteProfilePayload
{
    public string Id { get; set; } = string.Empty;
}

public class ConflictInfo
{
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string AdapterAlias { get; set; } = string.Empty;
}
