using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Purchasing;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

public class GoodsReceiptsViewModel : ViewModelBase
{
    private readonly IPurchasingService _purchasingService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private GoodsReceiptNoteDto? _selectedNote;
    private bool _isBusy;

    public GoodsReceiptsViewModel(IPurchasingService purchasingService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _purchasingService = purchasingService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Notes = new ObservableCollection<GoodsReceiptNoteDto>();

        RefreshCommand = new AsyncRelayCommand(LoadNotesAsync);
        AddNoteCommand = new AsyncRelayCommand(AddNoteAsync, () => CanReceive);
        EditNoteCommand = new AsyncRelayCommand(EditNoteAsync, () => CanReceive && SelectedNote is not null && SelectedNote.Status == GoodsReceiptStatus.Draft);
        PostNoteCommand = new AsyncRelayCommand(PostNoteAsync, () => CanReceive && SelectedNote is not null && SelectedNote.Status == GoodsReceiptStatus.Draft);
        DeleteNoteCommand = new AsyncRelayCommand(DeleteNoteAsync, () => CanReceive && SelectedNote is not null && SelectedNote.Status == GoodsReceiptStatus.Draft);
    }

    public bool CanReceive => _currentUserService.HasPermission("Purchasing.ReceiveGoods");

    public ObservableCollection<GoodsReceiptNoteDto> Notes { get; }

    public GoodsReceiptNoteDto? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (SetProperty(ref _selectedNote, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddNoteCommand { get; }
    public AsyncRelayCommand EditNoteCommand { get; }
    public AsyncRelayCommand PostNoteCommand { get; }
    public AsyncRelayCommand DeleteNoteCommand { get; }

    public async Task InitializeAsync() => await LoadNotesAsync();

    private async Task LoadNotesAsync()
    {
        IsBusy = true;
        try
        {
            var notes = await _purchasingService.GetGoodsReceiptNotesAsync();
            Notes.Clear();
            foreach (var n in notes) Notes.Add(n);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddNoteAsync()
    {
        var window = _dialogService.CreateDialog<GoodsReceiptEditDialog>();
        var vm = (GoodsReceiptEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadNotesAsync();
    }

    private async Task EditNoteAsync()
    {
        if (SelectedNote is null) return;

        var window = _dialogService.CreateDialog<GoodsReceiptEditDialog>();
        var vm = (GoodsReceiptEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedNote.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadNotesAsync();
    }

    private async Task PostNoteAsync()
    {
        if (SelectedNote is null) return;
        if (!_dialogService.Confirm($"ترحيل سند الاستلام '{SelectedNote.Number}' سيُنشئ دفعات مخزون جديدة ولا يمكن التراجع عنه. هل تريد المتابعة؟")) return;

        var result = await _purchasingService.PostGoodsReceiptAsync(SelectedNote.Id, _currentUserService.UserId);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر ترحيل سند الاستلام.");
            return;
        }

        _dialogService.ShowInfo("تم ترحيل سند الاستلام وتحديث المخزون بنجاح.");
        await LoadNotesAsync();
    }

    private async Task DeleteNoteAsync()
    {
        if (SelectedNote is null) return;
        if (!_dialogService.Confirm($"هل أنت متأكد من حذف سند الاستلام '{SelectedNote.Number}'؟")) return;

        var result = await _purchasingService.DeleteGoodsReceiptAsync(SelectedNote.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حذف سند الاستلام.");
            return;
        }

        await LoadNotesAsync();
    }
}
