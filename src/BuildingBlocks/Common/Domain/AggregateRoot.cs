namespace NexUS.BuildingBlocks.Common.Domain;

/// <summary>Kolom wajib sesuai CONTEXT.md bagian 5; ID internal tidak diekspos ke API.</summary>
public abstract class Entity
{
    public long Id { get; protected set; }
    public Guid PublicId { get; protected set; } = Guid.NewGuid();
    public long TenantId { get; protected set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long RowVersion { get; set; }
    public bool IsDeleted { get; protected set; }

    public void MarkDeleted() => IsDeleted = true;
}

public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    private bool _childrenChanged;

    /// <summary>
    /// Menandai bahwa entitas anak aggregate ini (mis. anggota proyek) berubah, sehingga versi aggregate ikut naik saat
    /// disimpan dan perubahan anak yang bersamaan bentrok versi (409).
    /// </summary>
    protected void MarkChildrenChanged() => _childrenChanged = true;

    public bool HasChildChanges() => _childrenChanged;

    public void AcceptChildChanges() => _childrenChanged = false;

    public IReadOnlyList<IDomainEvent> PopDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }
}
