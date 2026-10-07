using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Inventory;

/// <summary>
/// A single editable row used by the inline-grid CRUD pattern on the Lookups
/// screen (Categories / Units / Manufacturers). IsDirty/IsNew track whether
/// the row needs to be sent to the server when "Save All" is pressed.
/// </summary>
public class EditableLookupRow : ViewModelBase
{
    private int _id;
    private string _code = string.Empty;
    private string _name = string.Empty;
    private string? _extra;
    private bool _isActive = true;
    private bool _isDirty;
    private int _itemCount;

    public int Id { get => _id; set => SetProperty(ref _id, value); }
    public string Code { get => _code; set { if (SetProperty(ref _code, value)) IsDirty = true; } }
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) IsDirty = true; } }

    /// <summary>Used only by the Manufacturers tab to hold "Country"; unused (hidden) for Categories/Units.</summary>
    public string? Extra { get => _extra; set { if (SetProperty(ref _extra, value)) IsDirty = true; } }

    public bool IsActive { get => _isActive; set { if (SetProperty(ref _isActive, value)) IsDirty = true; } }
    public bool IsNew => Id == 0;
    public bool IsDirty { get => _isDirty; set => SetProperty(ref _isDirty, value); }
    public int ItemCount { get => _itemCount; set => SetProperty(ref _itemCount, value); }
}
