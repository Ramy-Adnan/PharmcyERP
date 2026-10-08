namespace PharmacyERP.Application.Features.Inventory.DTOs;
public sealed record PurchaseUnitOption(int? Id, string Name, int BaseUnitCount)
{ public string DisplayName => $"{Name} = {BaseUnitCount} جزء مخزون"; }
