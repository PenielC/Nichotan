namespace Nichotan.Domain.Common;

/// <summary>Base for persisted entities: a stable Guid identity and UTC creation/update times.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>A business rule was broken. The message is safe to show to the user.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>The record doesn't exist, or the caller may not know it exists.</summary>
public class NotFoundException(string message = "Not found.") : Exception(message);

/// <summary>The caller is signed in but not allowed to do this.</summary>
public class ForbiddenException(string message = "You don't have permission to do that.") : Exception(message);

/// <summary>The request conflicts with the current state (e.g. an email that is already registered).</summary>
public class ConflictException(string message) : Exception(message);
