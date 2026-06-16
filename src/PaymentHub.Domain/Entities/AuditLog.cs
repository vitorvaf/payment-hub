namespace PaymentHub.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? ApplicationId { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string? Entity { get; private set; }
    public string? EntityId { get; private set; }
    public string? MetadataJson { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private AuditLog() { }

    public AuditLog(
        Guid id,
        string actor,
        string action,
        Guid? tenantId = null,
        Guid? applicationId = null,
        string? entity = null,
        string? entityId = null,
        string? metadataJson = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Actor is required.", nameof(actor));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action is required.", nameof(action));

        Id = id;
        Actor = actor.Trim();
        Action = action.Trim();
        TenantId = tenantId;
        ApplicationId = applicationId;
        Entity = entity;
        EntityId = entityId;
        MetadataJson = metadataJson;
        CreatedAt = DateTime.UtcNow;
    }
}
