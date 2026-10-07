namespace PharmacyERP.WPF.Services;

/// <summary>
/// Abstraction over MessageBox/dialog windows so ViewModels never reference
/// System.Windows types directly — keeps them unit-testable and keeps the
/// View/ViewModel separation strict.
/// </summary>
public interface IDialogService
{
    bool Confirm(string message, string title = "تأكيد");
    void ShowError(string message, string title = "خطأ");
    void ShowInfo(string message, string title = "معلومة");

    /// <summary>Resolves a dialog Window from DI without showing it, so the caller can configure its ViewModel first (e.g. load data for edit mode).</summary>
    TWindow CreateDialog<TWindow>() where TWindow : System.Windows.Window;

    /// <summary>Shows an already-created dialog modally and returns whether the user confirmed (DialogResult == true).</summary>
    bool? ShowDialog(System.Windows.Window window);
}
