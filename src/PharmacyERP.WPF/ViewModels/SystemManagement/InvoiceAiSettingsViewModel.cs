using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.SystemManagement;

public sealed record InvoiceAiProviderOption(string Id, string Name);

public sealed class InvoiceAiSettingsViewModel : ViewModelBase
{
    private readonly IInvoiceAiSettingsStore _store;
    private readonly IInvoiceAiConnectionTester _tester;
    private readonly ICurrentUserService _user;
    private InvoiceAiSettings? _saved;
    private string _provider = "LocalOCR", _model = "tesseract-ara-eng", _newApiKey = string.Empty, _status = string.Empty;
    private bool _busy;
    public InvoiceAiSettingsViewModel(IInvoiceAiSettingsStore store, IInvoiceAiConnectionTester tester, ICurrentUserService user)
    {
        _store = store; _tester = tester; _user = user;
        SaveCommand = new RelayCommand(Save, () => CanEdit);
        TestCommand = new AsyncRelayCommand(TestAsync, () => CanEdit);
        RemoveKeyCommand = new RelayCommand(RemoveKey, () => CanEdit && !IsLocalOcr && (HasSavedKey || NewApiKey.Length > 0));
    }
    public IReadOnlyList<InvoiceAiProviderOption> Providers { get; } = new[] { new InvoiceAiProviderOption("LocalOCR", "OCR محلي — مجاني بدون إنترنت"), new("Gemini", "Gemini"), new("OpenAI", "OpenAI") };
    public bool IsLocalOcr => Provider == "LocalOCR";
    public bool UsesApi => !IsLocalOcr;
    public string TestButtonText => IsLocalOcr ? "اختبار OCR المحلي" : "اختبار الاتصال";
    public bool CanManage => _user.HasPermission("Branches.Manage") || _user.HasPermission("Purchasing.ManageInvoices");
    public bool CanEdit => CanManage && !IsBusy;
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) { OnPropertyChanged(nameof(CanEdit)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } } }
    public string Provider
    {
        get => _provider;
        set
        {
            if (!SetProperty(ref _provider, value)) return;
            Model = value == "LocalOCR" ? "tesseract-ara-eng" : value == "OpenAI" ? "gpt-4.1-mini" : "gemini-2.5-flash";
            OnPropertyChanged(nameof(IsLocalOcr)); OnPropertyChanged(nameof(UsesApi)); OnPropertyChanged(nameof(TestButtonText));
            NewApiKey = string.Empty; ClearPasswordRequested?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(HasSavedKey)); OnPropertyChanged(nameof(KeyStatus));
            StatusMessage = IsLocalOcr ? "OCR يعمل محلياً دون مفتاح أو رصيد. اختبر المكونات ثم احفظ." : "أدخل مفتاح الخدمة المختارة ثم اختبر الاتصال واحفظ الإعدادات.";
        }
    }
    public string Model { get => _model; set => SetProperty(ref _model, value); }
    // PasswordBox supplies a newly typed key. Never bind the saved secret back into the form.
    public string NewApiKey { get => _newApiKey; set { if (SetProperty(ref _newApiKey, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool HasSavedKey => _saved?.Provider == Provider && !string.IsNullOrWhiteSpace(_saved.ApiKey);
    public string KeyStatus => HasSavedKey ? "يوجد مفتاح محفوظ. اترك الحقل فارغاً للاحتفاظ به، أو أدخل مفتاحاً جديداً لاستبداله." : "لم يُحفظ مفتاح لهذه الخدمة. أدخل مفتاحك هنا.";
    public string StatusMessage { get => _status; private set => SetProperty(ref _status, value); }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RemoveKeyCommand { get; }
    public AsyncRelayCommand TestCommand { get; }
    public event EventHandler? ClearPasswordRequested;
    public event EventHandler<ReceiptSettingsSaveResult>? SaveCompleted;

    public void Initialize()
    {
        // Users without management permissions do not receive a decrypted key in this screen.
        if (!CanManage) { StatusMessage = "إعداد الخدمة يتطلب صلاحية إدارة الفروع أو فواتير المشتريات."; return; }
        try
        {
            _saved = _store.Load();
            if (_saved is not null) { _provider = _saved.Provider; _model = _saved.Model; }
            StatusMessage = _saved is null ? "اختر OCR المحلي المجاني، أو أضف مفتاح خدمة سحابية." : "الإعدادات المحفوظة تُستخدم مباشرة عند تحليل الفاتورة.";
        }
        catch (Exception) { StatusMessage = "تعذر تحميل الإعدادات المحفوظة؛ اختر الخدمة واحفظ إعداداتها من جديد."; }
        NewApiKey = string.Empty; OnPropertyChanged(string.Empty);
    }

    private InvoiceAiSettings Draft() => new()
    {
        Provider = Provider, Model = Model.Trim(),
        ApiKey = IsLocalOcr ? string.Empty : string.IsNullOrWhiteSpace(NewApiKey) ? (HasSavedKey ? _saved!.ApiKey : string.Empty) : NewApiKey.Trim()
    };
    public void Save()
    {
        if (!CanEdit) { StatusMessage = "لا تملك صلاحية حفظ إعدادات الخدمة أو يوجد اختبار جارٍ."; return; }
        try
        {
            var settings = Draft();
            if (!IsLocalOcr && string.IsNullOrWhiteSpace(settings.ApiKey)) { StatusMessage = "أدخل مفتاح الخدمة قبل الحفظ."; SaveCompleted?.Invoke(this, new(false, StatusMessage)); return; }
            _store.Save(settings); _saved = settings;
            Saved("تم حفظ إعدادات قراءة الصور بنجاح. ستُطبق على التحليل القادم مباشرة.");
        }
        catch (Exception) { StatusMessage = "تعذر حفظ إعدادات الخدمة؛ راجع اسم النموذج والمفتاح وصلاحية الكتابة. بقي الإعداد السابق محفوظاً."; SaveCompleted?.Invoke(this, new(false, StatusMessage)); }
    }
    public void RemoveKey()
    {
        if (!CanEdit || IsLocalOcr) return;
        try
        {
            var settings = new InvoiceAiSettings { Provider = Provider, Model = Model.Trim() };
            _store.Save(settings); _saved = settings;
            Saved("تم حذف المفتاح المحفوظ. قراءة الصور متوقفة حتى إضافة مفتاح جديد، والإدخال اليدوي متاح.");
        }
        catch (Exception) { StatusMessage = "تعذر حذف المفتاح؛ بقي الإعداد السابق محفوظاً."; SaveCompleted?.Invoke(this, new(false, StatusMessage)); }
    }
    private void Saved(string message)
    {
        NewApiKey = string.Empty; ClearPasswordRequested?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(HasSavedKey)); OnPropertyChanged(nameof(KeyStatus)); System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        StatusMessage = message; SaveCompleted?.Invoke(this, new(true, message));
    }
    public async Task TestAsync()
    {
        if (!CanEdit) return;
        var draft = Draft();
        if (!IsLocalOcr && string.IsNullOrWhiteSpace(draft.ApiKey)) { StatusMessage = "أدخل مفتاح الخدمة أولاً."; return; }
        IsBusy = true; StatusMessage = IsLocalOcr ? "جارٍ اختبار محرك OCR وملفات اللغة..." : "جارٍ التحقق من المفتاح وتوفر النموذج...";
        try
        {
            var result = await _tester.TestAsync(draft);
            StatusMessage = result.Succeeded
                ? IsLocalOcr ? "محرك OCR المحلي وملفات العربية والإنجليزية جاهزة. اضغط حفظ ثم جرّب صورة واضحة؛ راجع النتائج قبل الاستلام." : "نجح الاتصال وتحقق النموذج. الاختبار لا يرسل فاتورة؛ حدود الاستخدام قد تؤثر على التحليل لاحقاً. اضغط حفظ لتطبيق التغييرات."
                : result.Errors.FirstOrDefault() ?? "تعذر اختبار الاتصال.";
        }
        catch (Exception) { StatusMessage = "تعذر اختبار الاتصال؛ راجع الإنترنت وإعدادات الخدمة."; }
        finally { IsBusy = false; }
    }
}
