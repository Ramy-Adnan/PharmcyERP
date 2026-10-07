namespace PharmacyERP.Domain.Exceptions;

/// <summary>
/// Raised when a domain invariant is violated (e.g. attempting to deactivate
/// the last remaining administrator, or assigning a duplicate permission code).
/// </summary>
public class DomainException : Exception
{
    public DomainException() { }
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}
