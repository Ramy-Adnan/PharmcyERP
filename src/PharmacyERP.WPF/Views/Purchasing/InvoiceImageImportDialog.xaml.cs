using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.WPF.ViewModels.Purchasing;
namespace PharmacyERP.WPF.Views.Purchasing;
public partial class InvoiceImageImportDialog : Window
{
    private readonly InvoiceImageImportViewModel _vm;
    public InvoiceImageImportDialog(InvoiceImageImportViewModel vm)
    {
        InitializeComponent(); _vm = vm; DataContext = vm;
        Closing += (_, e) => { if (_vm.IsBusy) { _vm.CancelReading(); e.Cancel = true; } };
    }
    private async void ChoosePhoto(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBusy) return;
        if (_vm.Lines.Count > 0 && MessageBox.Show(this, "اختيار صورة أخرى سيستبدل المراجعة الحالية غير المحفوظة. هل تريد المتابعة؟", "استبدال الصورة", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var dialog = new OpenFileDialog { Filter = "صور الفواتير|*.jpg;*.jpeg;*.png;*.webp", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 8 * 1024 * 1024) { _vm.Message = "حجم الصورة لا يتجاوز 8MB."; return; }
            var mime = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
            await _vm.AnalyzeAsync(new InvoiceImageInput(Path.GetFileName(dialog.FileName), mime, await File.ReadAllBytesAsync(dialog.FileName)));
            if (_vm.Lines.Count > 0) Rows.SelectedIndex = 0;
        }
        catch (IOException) { _vm.Message = "تعذر فتح الصورة؛ اختر ملفاً متاحاً."; }
        catch (UnauthorizedAccessException) { _vm.Message = "ليس لديك صلاحية قراءة ملف الصورة."; }
    }
    private async void AddBaseUnit(object sender, RoutedEventArgs e) => await _vm.AddBaseUnitAsync(NewUnitName.Text);
    private void PreviewPhoto(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBusy || _vm.OriginalPhoto is null) return;
        try
        {
            using var stream = new MemoryStream(_vm.OriginalPhoto);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage(); bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            var picture = new Image { Source = bitmap, Stretch = Stretch.None };
            var zoom = new Slider { Minimum = 0.2, Maximum = 2, Value = 0.7, Margin = new Thickness(12) };
            var scale = new ScaleTransform(); picture.LayoutTransform = scale;
            System.Windows.Data.BindingOperations.SetBinding(scale, ScaleTransform.ScaleXProperty, new System.Windows.Data.Binding("Value") { Source = zoom });
            System.Windows.Data.BindingOperations.SetBinding(scale, ScaleTransform.ScaleYProperty, new System.Windows.Data.Binding("Value") { Source = zoom });
            var panel = new DockPanel(); DockPanel.SetDock(zoom, Dock.Top); panel.Children.Add(zoom);
            panel.Children.Add(new ScrollViewer { Content = picture, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            new Window { Owner = this, Title = "الفاتورة الأصلية — التكبير من الشريط", Width = 950, Height = 780, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel }.ShowDialog();
        }
        catch (NotSupportedException) { _vm.Message = "معاينة هذه الصورة غير مدعومة في Windows؛ استخدم JPG أو PNG أو راجع الملف الأصلي."; }
        catch (Exception) { _vm.Message = "تعذر عرض المعاينة؛ راجع الصورة الأصلية."; }
    }
    private void CancelRead(object sender, RoutedEventArgs e) => _vm.CancelReading();
    private void CloseDialog(object sender, RoutedEventArgs e) { if (!_vm.IsBusy) DialogResult = false; }
    private async void SaveReviewed(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBusy) return;
        if (!CommitAndValidate(this)) { _vm.Message = "صحّح القيم غير الصالحة قبل الحفظ."; return; }
        if (await _vm.SaveAsync()) { MessageBox.Show(this, _vm.Message, "استيراد الفاتورة", MessageBoxButton.OK, MessageBoxImage.Information); DialogResult = true; }
    }
    private static bool CommitAndValidate(DependencyObject node)
    {
        if (node is DataGrid grid && (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true))) return false;
        if (Validation.GetHasError(node)) return false;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++) if (!CommitAndValidate(VisualTreeHelper.GetChild(node, index))) return false;
        return true;
    }
}
