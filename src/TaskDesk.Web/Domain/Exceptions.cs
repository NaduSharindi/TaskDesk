namespace TaskDesk.Domain;

/// <summary>A business rule was broken (shown to the user as a validation message / HTTP 422).</summary>
public class BusinessRuleException(string message) : Exception(message)
{
}

/// <summary>HTTP 404. Also used when a user may not see a record, so its existence is not leaked.</summary>
public sealed class NotFoundException(string resource, object key) : Exception($"{resource} '{key}' was not found.")
{
}

/// <summary>HTTP 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message)
{
}

/// <summary>HTTP 409. Someone else saved the record first (optimistic concurrency).</summary>
public sealed class ConcurrencyException()
    : Exception("This task was changed by someone else while you were editing. Reload it and apply your changes again.")
{
}
