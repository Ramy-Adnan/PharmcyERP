namespace PharmacyERP.Application.Common.Interfaces;

/// <summary>Abstraction over the system clock so use cases and tests can control "now".</summary>
public interface IDateTime
{
    DateTime UtcNow { get; }
}
