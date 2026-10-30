namespace TaskDesk.Domain;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Staff = "Staff";

    /// <summary>Machine-to-machine callers authenticated with an API key (e.g. the Helpdesk system).</summary>
    public const string IntegrationClient = "IntegrationClient";

    public static readonly IReadOnlyList<string> UserRoles = [Admin, Manager, Staff];
}

public static class AppClaimTypes
{
    public const string DepartmentId = "taskdesk:department_id";
    public const string FullName = "taskdesk:full_name";
}
