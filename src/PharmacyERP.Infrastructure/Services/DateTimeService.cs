using PharmacyERP.Application.Common.Interfaces;

namespace PharmacyERP.Infrastructure.Services;

public class DateTimeService : IDateTime
{
    public DateTime UtcNow => DateTime.UtcNow;
}
