namespace PharmacyERP.Domain.Common;

/// <summary>
/// Base class for all domain entities. Provides a strongly typed primary key
/// and a domain-events bag so business rules can raise events without
/// depending on infrastructure concerns.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }

    private readonly List<object> _domainEvents = new();
    public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(object domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
