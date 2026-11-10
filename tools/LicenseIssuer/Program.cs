using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PharmacyERP.Application.Features.Licensing;

// Vendor-only executable. Never add this tool or a private key to the customer installer.
return LicenseIssuerProgram.Run(args);

internal static class LicenseIssuerProgram
{

    public static int Run(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);
            if (args.Length == 0 && !Console.IsInputRedirected) { Interactive(new()); return 0; }
            if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal)) { Usage(); return 0; }
            var options = Parse(args.Skip(1).ToArray());
            switch (args[0])
            {
                case "interactive":
                    Allowed(options, "private-key", "password-env", "ledger");
                    Interactive(options);
                    break;
                case "keygen":
                    Allowed(options, "private-key", "public-key", "password-env");
                    GenerateKeys(options);
                    break;
                case "validate-public-key":
                    Allowed(options, "public-key");
                    using (var key = ReadPublicKeyFile(Require(options, "public-key"))) { }
                    Console.WriteLine("Public verification key is valid.");
                    break;
                case "validate-release-key":
                    Allowed(options, "assembly", "public-key");
                    ValidateReleaseKey(options);
                    break;
                case "issue":
                    Allowed(options, "private-key", "password-env", "customer", "hardware", "branches", "users", "days", "expires", "perpetual", "output", "ledger", "amount", "currency", "reference");
                    Issue(options, renewal: false);
                    break;
                case "renew":
                    Allowed(options, "private-key", "password-env", "license", "branches", "users", "days", "expires", "perpetual", "output", "ledger", "amount", "currency", "reference");
                    Issue(options, renewal: true);
                    break;
                case "list":
                    Allowed(options, "ledger");
                    List(options);
                    break;
                default: throw new ArgumentException("Unknown command. Use --help.");
            }
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
            or CryptographicException or FormatException or JsonException or InvalidOperationException or BadImageFormatException)
        {
            // Never print key contents, a passphrase or the full command line.
            Console.Error.WriteLine("License tool failed: " + ex.Message);
            return 1;
        }
    }

    private static string DefaultLedgerPath()
    {
        var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrWhiteSpace(data))
            throw new ArgumentException("تعذر تحديد مجلد بيانات المورّد. حدد مسار السجل باستخدام --ledger.");
        return Path.Combine(data, "PharmacyERP-Vendor", "licenses.jsonl");
    }

    private static string DesktopLicensePath(string customer, string licenseKey)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            throw new ArgumentException("تعذر العثور على سطح المكتب. حدد مجلد الحفظ باستخدام --output.");
        const string windowsInvalid = "<>:\"/\\|?*";
        var safe = new string(customer.Select(c => char.IsControl(c) || windowsInvalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c).Take(70).ToArray()).Trim('-', '.', ' ');
        if (string.IsNullOrWhiteSpace(safe)) safe = "Pharmacy";
        return Path.Combine(desktop, $"{safe}-{licenseKey}.lic");
    }

    private static void Interactive(Dictionary<string, string?> options)
    {
        Console.WriteLine("=== أداة إصدار ترخيص الصيدلية — للمورّد فقط ===");
        if (!options.ContainsKey("private-key"))
        {
            Console.Write("مسار ملف المفتاح الخاص المشفّر: ");
            options["private-key"] = Console.ReadLine()?.Trim().Trim('\"');
        }
        Console.Write("اسم العميل: "); options["customer"] = Console.ReadLine()?.Trim();
        Console.Write("معرّف الجهاز من شاشة الترخيص: "); options["hardware"] = Console.ReadLine()?.Trim();
        Console.Write("عدد الفروع: "); options["branches"] = Console.ReadLine()?.Trim();
        Console.Write("عدد المستخدمين: "); options["users"] = Console.ReadLine()?.Trim();
        Console.Write("المدة بالأيام (اتركها فارغة لترخيص دائم): ");
        var days = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(days)) options["perpetual"] = null; else options["days"] = days;
        Console.WriteLine("سيُحفظ ملف الترخيص على سطح المكتب، وسجل الإصدار في مجلد بيانات المورّد.");
        Issue(options, renewal: false);
    }

    private static void GenerateKeys(Dictionary<string, string?> options)
    {
        var privatePath = Path.GetFullPath(Require(options, "private-key"));
        var publicPath = Path.GetFullPath(Require(options, "public-key"));
        if (string.Equals(privatePath, publicPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Private and public key paths must be different.");
        RequireNewFile(privatePath);
        RequireNewFile(publicPath);
        var password = Password(options, confirm: true);
        using var rsa = RSA.Create(3072);
        var parameters = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 200000);
        WriteNew(privatePath, rsa.ExportEncryptedPkcs8PrivateKeyPem(password, parameters), isPrivate: true);
        WriteNew(publicPath, rsa.ExportSubjectPublicKeyInfoPem());
        Console.WriteLine("RSA key pair created. Keep the encrypted private key and passphrase with the vendor only.");
        Console.WriteLine("Private key file: " + privatePath);
        Console.WriteLine("Public key file: " + publicPath);
    }

    private static void ValidateReleaseKey(Dictionary<string, string?> options)
    {
        using var expected = ReadPublicKeyFile(Require(options, "public-key"));
        var assemblyPath = Path.GetFullPath(Require(options, "assembly"));
        // Load bytes so the check does not lock the output file or instantiate any application service.
        var assembly = Assembly.Load(File.ReadAllBytes(assemblyPath));
        using var resource = assembly.GetManifestResourceStream("PharmacyERP.Licensing.PublicKey.pem")
            ?? throw new InvalidOperationException("The release does not contain the embedded license verification key.");
        using var reader = new StreamReader(resource);
        using var actual = LicenseDocumentCodec.ReadPublicKey(reader.ReadToEnd());
        if (!CryptographicOperations.FixedTimeEquals(expected.ExportSubjectPublicKeyInfo(), actual.ExportSubjectPublicKeyInfo()))
            throw new InvalidOperationException("The embedded release key differs from the selected public key.");
        Console.WriteLine("Release contains the selected public verification key.");
    }

    private static void Issue(Dictionary<string, string?> options, bool renewal)
    {
        var outputArgument = options.GetValueOrDefault("output");
        var ledger = Path.GetFullPath(options.GetValueOrDefault("ledger") ?? DefaultLedgerPath());
        decimal? amount = null;
        if (options.TryGetValue("amount", out var amountText))
        {
            if (!decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value < 0)
                throw new ArgumentException("--amount must be a non-negative decimal number, using a dot as the decimal separator.");
            amount = value;
        }
        var currency = options.GetValueOrDefault("currency") ?? "IQD";
        if (currency.Length != 3 || currency.Any(c => !char.IsAsciiLetterUpper(c)))
            throw new ArgumentException("--currency must be a three-letter uppercase code, for example IQD.");
        var reference = options.GetValueOrDefault("reference") ?? string.Empty;
        if (reference.Length > 160 || reference.Any(char.IsControl)) throw new ArgumentException("Payment reference is invalid.");

        using var privateKey = ReadPrivateKey(options);
        LicensePayload payload;
        DateTime termStart;
        var now = DateTime.UtcNow;
        if (renewal)
        {
            if (!LicenseDocumentCodec.TryRead(File.ReadAllText(Require(options, "license")), out var old, out var error))
                throw new ArgumentException(error);
            if (!LicenseDocumentCodec.Verify(old!, privateKey))
                throw new ArgumentException("The old license is not signed by this issuer key. Re-issue it explicitly instead of renewing it.");
            payload = old!.Payload!;
            termStart = payload.ExpiryDate.HasValue && payload.ExpiryDate.Value.Date > now.Date ? payload.ExpiryDate.Value.Date : now.Date;
            payload.IssuedAtUtc = now;
            if (options.ContainsKey("branches")) payload.MaxBranches = PositiveNumber(options, "branches");
            if (options.ContainsKey("users")) payload.MaxUsers = PositiveNumber(options, "users");
        }
        else
        {
            payload = new LicensePayload
            {
                LicenseKey = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                CustomerName = Require(options, "customer"),
                HardwareId = Require(options, "hardware"),
                MaxBranches = PositiveNumber(options, "branches"),
                MaxUsers = PositiveNumber(options, "users"),
                IssuedAtUtc = now
            };
            termStart = now.Date;
        }
        payload.ExpiryDate = Expiry(options, termStart);
        var output = Path.GetFullPath(string.IsNullOrWhiteSpace(outputArgument)
            ? DesktopLicensePath(payload.CustomerName, payload.LicenseKey) : outputArgument);
        RequireNewFile(output);
        if (string.Equals(output, ledger, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("License and ledger paths must be different.");
        var signed = LicenseDocumentCodec.Sign(payload, privateKey);
        var record = new IssuanceRecord
        {
            Action = renewal ? "renew" : "issue",
            RecordedAtUtc = now,
            License = payload,
            OutputFile = output,
            DocumentSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signed))),
            Amount = amount,
            Currency = currency,
            PaymentReference = reference
        };
        // Validate an existing ledger before creating a license, so a corrupt registry cannot be silently extended.
        ReadLedger(ledger);
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        using (var ledgerStream = new FileStream(ledger, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            ledgerStream.Seek(0, SeekOrigin.End);
            WriteNew(output, signed);
            try
            {
                using var writer = new StreamWriter(ledgerStream, new UTF8Encoding(false), leaveOpen: true);
                writer.WriteLine(JsonSerializer.Serialize(record));
                writer.Flush();
                ledgerStream.Flush(flushToDisk: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException("The license was created, but its ledger entry could not be saved. Retain the new license and repair the vendor ledger before issuing another one.", ex);
            }
        }
        Console.WriteLine(renewal ? "License renewed." : "License issued.");
        Console.WriteLine("License ID: " + payload.LicenseKey);
        Console.WriteLine("Customer: " + payload.CustomerName);
        Console.WriteLine("Expiry: " + (payload.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "perpetual"));
        Console.WriteLine("License file: " + output);
        Console.WriteLine("Vendor ledger: " + ledger);
    }

    private static DateTime? Expiry(Dictionary<string, string?> options, DateTime start)
    {
        var choices = new[] { "days", "expires", "perpetual" }.Count(options.ContainsKey);
        if (choices != 1) throw new ArgumentException("Choose exactly one term: --days N, --expires yyyy-MM-dd, or --perpetual.");
        if (options.ContainsKey("perpetual")) return null;
        if (options.TryGetValue("expires", out var value))
        {
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date.Date < DateTime.UtcNow.Date)
                throw new ArgumentException("Expiry must be today or later, in yyyy-MM-dd format.");
            return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        }
        var days = PositiveNumber(options, "days");
        if (days > 36500) throw new ArgumentException("--days must not exceed 36500; use --perpetual for a permanent license.");
        return DateTime.SpecifyKind(start.AddDays(days), DateTimeKind.Utc);
    }

    private static void List(Dictionary<string, string?> options)
    {
        var records = ReadLedger(Path.GetFullPath(options.GetValueOrDefault("ledger") ?? DefaultLedgerPath()));
        Console.WriteLine("License ID\tCustomer\tExpiry\tBranches\tUsers\tLast payment\tAction\tDevice");
        foreach (var record in records.GroupBy(r => r.License.LicenseKey).Select(g => g.Last()).OrderBy(r => r.License.CustomerName, StringComparer.Ordinal))
        {
            var p = record.License;
            Console.WriteLine(string.Join("\t", new object?[] { p.LicenseKey, p.CustomerName, p.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "perpetual",
                p.MaxBranches, p.MaxUsers, record.Amount?.ToString(CultureInfo.InvariantCulture) + " " + record.Currency, record.Action, p.HardwareId }));
        }
        Console.WriteLine($"Customers/licenses: {records.Select(r => r.License.LicenseKey).Distinct().Count()}; issuance/renewal records: {records.Count}");
    }

    private static List<IssuanceRecord> ReadLedger(string path)
    {
        var records = new List<IssuanceRecord>();
        if (!File.Exists(path)) return records;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var record = JsonSerializer.Deserialize<IssuanceRecord>(line)
                ?? throw new InvalidOperationException("The vendor ledger contains an invalid entry.");
            if (record.License is null || LicenseDocumentCodec.ValidatePayload(record.License) is not null)
                throw new InvalidOperationException("The vendor ledger contains invalid license data.");
            records.Add(record);
        }
        return records;
    }

    private static RSA ReadPrivateKey(Dictionary<string, string?> options)
    {
        var pem = File.ReadAllText(Require(options, "private-key"));
        var rsa = RSA.Create();
        try
        {
            if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal)) rsa.ImportFromEncryptedPem(pem, Password(options));
            else rsa.ImportFromPem(pem);
            if (rsa.KeySize < 2048) throw new ArgumentException("The RSA key must have at least 2048 bits.");
            // Ensure a public key was not supplied where a private signing key is required.
            _ = rsa.SignData("PharmacyERP issuer key check"u8, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            return rsa;
        }
        catch { rsa.Dispose(); throw; }
    }

    private static RSA ReadPublicKeyFile(string path) => LicenseDocumentCodec.ReadPublicKey(File.ReadAllText(path));

    private static string Password(Dictionary<string, string?> options, bool confirm = false)
    {
        string password;
        if (options.TryGetValue("password-env", out var variable))
            password = Environment.GetEnvironmentVariable(variable!) ?? throw new ArgumentException("The named passphrase environment variable is not set.");
        else
        {
            if (Console.IsInputRedirected) throw new ArgumentException("Use --password-env NAME for noninteractive key operations; never put a passphrase on the command line.");
            password = ReadHidden("Private key passphrase: ");
            if (confirm && password != ReadHidden("Repeat passphrase: ")) throw new ArgumentException("Passphrases do not match.");
        }
        if (password.Length < 12) throw new ArgumentException("The private key passphrase must contain at least 12 characters.");
        return password;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Escape) throw new ArgumentException("Key operation cancelled.");
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
            else if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
        }
    }

    private static Dictionary<string, string?> Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Expected a named option starting with --.");
            var name = args[i][2..];
            if (name == "perpetual")
            {
                if (!values.TryAdd(name, null)) throw new ArgumentException("Duplicate option: --" + name);
                continue;
            }
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing option value: --" + name);
            if (!values.TryAdd(name, args[i])) throw new ArgumentException("Duplicate option: --" + name);
        }
        return values;
    }

    private static void Allowed(Dictionary<string, string?> options, params string[] names)
    {
        foreach (var name in options.Keys)
            if (!names.Contains(name, StringComparer.Ordinal)) throw new ArgumentException("Unknown option: --" + name);
    }

    private static string Require(Dictionary<string, string?> options, string name) => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException("Required option: --" + name);

    private static int PositiveNumber(Dictionary<string, string?> options, string name) => int.TryParse(Require(options, name), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
        ? number : throw new ArgumentException("--" + name + " must be a positive whole number.");

    private static void RequireNewFile(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("The output already exists. Use a new path; existing keys and licenses are never overwritten.");
    }

    private static void WriteNew(string path, string value, bool isPrivate = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (isPrivate && !OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(value);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static void Usage() => Console.WriteLine("""
PharmacyERP LicenseIssuer — vendor only; do not distribute.
  interactive [--private-key PATH] [--password-env NAME] [--ledger PATH]
  keygen --private-key PATH --public-key PATH [--password-env NAME]
  validate-public-key --public-key PATH
  validate-release-key --assembly Infrastructure.dll --public-key PATH
  issue --private-key PATH --customer NAME --hardware ID --branches N --users N
        (--days N | --expires yyyy-MM-dd | --perpetual) [--output PATH] [--ledger PATH]
        [--password-env NAME] [--amount NUMBER --currency IQD --reference TEXT]
  renew --private-key PATH --license OLD.lic (--days N | --expires yyyy-MM-dd | --perpetual)
        [--output NEW.lic] [--ledger PATH] [--branches N --users N] [--password-env NAME]
        [--amount NUMBER --currency IQD --reference TEXT]
  list [--ledger PATH]

Private keys are encrypted with a passphrase. Enter it invisibly at the prompt or
supply its environment variable name with --password-env; never pass its value as an argument.
Without --output, the .lic file is saved on the Desktop. Without --ledger, the vendor registry
is saved under local application data/PharmacyERP-Vendor. Running with no arguments opens
the Arabic issuance prompts in an interactive console. Existing files are never overwritten.
Retain and back up the vendor key pair and ledger.
""");

    private sealed class IssuanceRecord
    {
        public IssuanceRecord() { }
        public string Action { get; set; } = string.Empty;
        public DateTime RecordedAtUtc { get; set; }
        public LicensePayload License { get; set; } = new();
        public string OutputFile { get; set; } = string.Empty;
        public string DocumentSha256 { get; set; } = string.Empty;
        public decimal? Amount { get; set; }
        public string Currency { get; set; } = "IQD";
        public string PaymentReference { get; set; } = string.Empty;
    }
}
