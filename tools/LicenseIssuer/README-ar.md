# أداة إدارة التراخيص — للمورّد فقط

لا تُرسل هذه الأداة أو المفتاح الخاص أو سجل الزبائن إلى الصيدلية. البرنامج المنشور يحمل المفتاح العام للتحقق فقط، ويمكن لجميع الزبائن استخدام نفس المثبّت مع ملفات تراخيص مختلفة.

الأداة تستخدم RSA / SHA-256 / PSS. ملفات HMAC القديمة تحتاج إعادة إصدار؛ لا يوجد مفتاح مشترك أو مسار قبول للتوقيع القديم في برنامج العميل.

## إنشاء المفتاح مرة واحدة

من جذر المشروع على جهاز المورّد:

```powershell
dotnet run --project tools/LicenseIssuer -- keygen --private-key "C:\PharmacyVendor\Keys\issuer.private.pem" --public-key "C:\PharmacyVendor\Keys\issuer.public.pem"
```

تطلب الأداة عبارة سرية مخفية من 12 حرفاً على الأقل، وتشفّر المفتاح الخاص بها. احتفظ بنسخة احتياطية آمنة من المفتاحين والعبارة السرية خارج مجلد المشروع. لا تغيّر المفتاح بين تحديثات البرنامج؛ تغيير المفتاح يعني إعادة إصدار تراخيص الزبائن. لا تضع المفتاح الخاص أو العبارة السرية في Git أو في `appsettings.json` أو داخل Advanced Installer.

للتشغيل الآلي فقط يمكن تمرير **اسم** متغير يحمل العبارة السرية باستخدام `--password-env PHARMACYERP_VENDOR_KEY_PASSWORD`. لا تمرّر قيمة العبارة كوسيط سطر أوامر. لا يحتاج جهاز الصيدلية هذا المتغير.


## إصدار تفاعلي وحفظ على سطح المكتب

تشغيل الأداة دون وسائط في نافذة كونسول تفاعلية يعرض أسئلة اسم العميل ومعرّف الجهاز والحدود والمدة، مع دعم UTF-8 للعربية. أو شغّل:

```powershell
dotnet run --project tools/LicenseIssuer -- interactive --private-key "C:\PharmacyVendor\Keys\issuer.private.pem"
```

يُحفظ ملف `.lic` تلقائياً على سطح المكتب باسم العميل ومعرّف الترخيص، ويُحفظ سجل الإصدار افتراضياً في `%LOCALAPPDATA%\PharmacyERP-Vendor\licenses.jsonl`. تترك المدة فارغة للترخيص الدائم. يطلب المفتاح المشفّر عبارته السرية بإدخال مخفي. يمكن أيضاً حذف `--output` و`--ledger` من أمر `issue` لاستخدام الوجهات الافتراضية؛ إن لم يكن سطح المكتب متاحاً، حدد `--output` صراحةً. لا تُستبدل الملفات السابقة.

## إصدار ترخيص سنوي

يرسل الزبون المعرّف الظاهر في شاشة الترخيص بعد تثبيت البرنامج. أدخله كما هو:

```powershell
dotnet run --project tools/LicenseIssuer -- issue --private-key "C:\PharmacyVendor\Keys\issuer.private.pem" --customer "صيدلية النخيل" --hardware "معرف-الجهاز-من-البرنامج" --branches 1 --users 5 --days 365 --output "C:\PharmacyVendor\Licenses\Nakhil-2026.lic" --ledger "C:\PharmacyVendor\licenses.jsonl" --amount 250000 --currency IQD --reference "دفعة الاشتراك السنوي"
```

المبلغ والمرجع اختياريان ويحفظان في سجل المورّد فقط. الأداة تسجّل المدفوع الذي تُدخله ولا تجري تحصيلاً مصرفياً تلقائياً. أرسل ملف `.lic` الناتج للزبون ليستورده من شاشة الترخيص. استخدم `--perpetual` بدلاً من `--days 365` للترخيص الدائم، أو `--expires 2027-12-31` لتاريخ محدد. تختار مدة واحدة فقط في الأمر.

## التجديد أو زيادة المستخدمين

```powershell
dotnet run --project tools/LicenseIssuer -- renew --private-key "C:\PharmacyVendor\Keys\issuer.private.pem" --license "C:\PharmacyVendor\Licenses\Nakhil-2026.lic" --days 365 --users 10 --output "C:\PharmacyVendor\Licenses\Nakhil-2027.lic" --ledger "C:\PharmacyVendor\licenses.jsonl" --amount 250000 --currency IQD
```

يحافظ التجديد على معرّف الترخيص والعميل والجهاز، ويمكن زيادة حدود الفروع والمستخدمين. عند التجديد بالأيام، تُضاف المدة إلى تاريخ الانتهاء الحالي إن كان مستقبلياً، وإلا تبدأ من اليوم. لا يعاد تنصيب البرنامج ولا تُغيّر قاعدة البيانات. لا تُستبدل الملفات القديمة؛ اختر اسماً جديداً للمخرجات للاحتفاظ بالسجل. نقل الزبون إلى جهاز آخر يحتاج إصداراً جديداً للجهاز الجديد.

## سجل الزبائن والتراخيص

```powershell
dotnet run --project tools/LicenseIssuer -- list --ledger "C:\PharmacyVendor\licenses.jsonl"
```

يعرض الأمر آخر إصدار لكل معرّف ترخيص، مع العميل والانتهاء والحدود وآخر مبلغ مدخل. ملف JSONL يحتفظ بكل عمليات الإصدار والتجديد؛ انسخه احتياطياً مع ملفات التراخيص. هذا سجل محلي للمورّد، ولا توجد خدمة دفع أو إلغاء عن بُعد.

## التحقق قبل النشر

```powershell
dotnet run --project tools/LicenseIssuer -- validate-public-key --public-key "C:\PharmacyVendor\Keys\issuer.public.pem"
dotnet run --project tools/LicenseIssuer -- validate-release-key --assembly "artifacts\customer\PharmacyERP.Infrastructure.dll" --public-key "C:\PharmacyVendor\Keys\issuer.public.pem"
```

المفتاح العام يضاف وقت البناء كـ embedded resource إلى `PharmacyERP.Infrastructure` باسم `PharmacyERP.Licensing.PublicKey.pem` باستخدام خاصية MSBuild `LicensePublicKeyFile`. التحقق الثاني يفشل إن كانت نسخة النشر لا تحمل المفتاح العام المتوقع. يرفض الأمر ملف المفتاح الخاص أو مفتاح RSA أصغر من 2048 بت، ولا يطبع محتويات المفاتيح.

## حدود التفعيل دون الإنترنت

التوقيع يحمي محتويات الترخيص من التعديل دون المفتاح الخاص، لكنه لا يجعل تطبيق .NET غير قابل للتعديل. معرّف التثبيت الحالي اسم الكمبيوتر ومعرّف محلي محفوظ، وليس بصمة عتاد غير قابلة للنسخ. يوجد كشف محلي بسيط لإرجاع التاريخ بأكثر من يوم؛ ليس ساعة موثوقة ولا حماية منيعة من العبث بملفات الجهاز. الإلغاء الفوري عن بُعد والتحقق الموثوق من الوقت يحتاجان خدمة واتصالاً دورياً. انتهاء الترخيص لا يحذف البيانات؛ تتاح القراءة والنسخ الاحتياطي وفق سياسة الوصول داخل البرنامج.
