namespace PharmacyERP.Application.Features.Inventory.DTOs;

/// <summary>A bounded catalogue page; HasMore avoids a second full COUNT query.</summary>
public sealed class ItemPageDto
{
    public List<ItemDto> Items { get; init; } = new();
    public bool HasMore { get; init; }
    public int NextSkip { get; init; }
}
