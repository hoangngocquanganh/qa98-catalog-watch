using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SysInfoWpf.Services.SoftwareCenter
{
    // Mô hình danh mục Software Center (schema: docs/software-catalog-schema.md, "schema": 1).
    // Nguyên tắc: danh mục chỉ MÔ TẢ gói; KHÔNG BAO GIỜ mang lệnh / tham số dòng lệnh. Lệnh nằm cứng trong adapter (SoftwareInstallAdapters).
    // Giá trị lạ (loại trình cài / hành động mới) => Unknown => client báo NEEDS_CLIENT_UPDATE, không đoán.

    public enum LicenseLabel { Unknown, Free, OpenSource, Trial, Paid }

    public enum PackageAction { Unknown, OfficialPageOnly, OfficialDownloadUserDriven, ApprovedDirectDownload, ApprovedInteractiveInstall, ApprovedUnattendedInstall, Blocked }

    public enum InstallerType { Unknown, Msi, ExeInteractive, ZipPortable, Winget, Msix, WebInstaller }

    public enum SourceStatus { Unknown, Healthy, Degraded, Broken }

    public enum CpuArch { Unknown, X86, X64, Arm64, Neutral }

    // ---- GĐ5: schema v2 — dữ liệu khai báo trong KHUNG ĐÓNG (Docs/GD5-SCHEMA-V2.md). Switch nằm trong code client theo template. ----

    /// <summary>installer_kind (enum đóng). None = catalog không ghi (v1: client tự nhận dạng EXE); Unknown = giá trị lạ => NEEDS_CLIENT_UPDATE.</summary>
    public enum InstallerKind { None, Unknown, Msi, Inno, Nsis, InstallShieldBasicMsi, InstallShieldScript, Burn, Squirrel, ClickOnce, PortableZip, WingetOnly }

    public enum SilentLevel { Full, Progress }

    public enum PackageLifecycle { Active, Deprecated, VendorChanged, SignerChanged, Unknown }

    public enum DetectionType { Arp, MsiUpgradeCode, File, Unins, Squirrel }

    /// <summary>Một tín hiệu nhận diện đã cài khai báo trong catalog (chỉ dữ liệu, không regex / lệnh).</summary>
    public sealed class DetectionSignal
    {
        public DetectionType Type;
        public string DisplayName = "", Publisher = "", Key = "";   // arp
        public string UpgradeCode = "";                             // msi_upgradecode
        public string Root = "", Path = "";                         // file: gốc trong tập đóng + đường dẫn tương đối
        public string AppId = "";                                   // unins (Inno AppId)
        public string Folder = "";                                  // squirrel (%LOCALAPPDATA%\folder)
        public string MinVersion = "";
    }

    public sealed class SoftwareCategory
    {
        public string Slug = "", NameVi = "", NameEn = "";
        public int Order;
    }

    /// <summary>Cách nhận biết gói đã cài (chỉ ĐỌC registry Uninstall / MSI / winget). Không dùng regex từ máy chủ.</summary>
    public sealed class DetectRule
    {
        public string DisplayName = "";      // tên trong Uninstall registry: khớp nếu DisplayName bắt đầu bằng chuỗi này (không phân biệt hoa thường)
        public string Publisher = "";        // nếu có: Publisher trong registry phải chứa chuỗi này
        public string MsiProductCode = "";   // {GUID}: khớp chính xác tên khóa Uninstall
        public string WingetId = "";         // id winget chính xác
        public bool IsEmpty => DisplayName.Length == 0 && MsiProductCode.Length == 0 && WingetId.Length == 0;
    }

    /// <summary>Nguồn dự phòng: CÙNG artifact (sha256 / signer / size kế thừa từ phiên bản), chỉ khác URL + host.</summary>
    public sealed class FallbackSource
    {
        public string Url = "";
        public List<string> AllowedHosts = new List<string>();
        public SourceStatus Status = SourceStatus.Unknown;
    }

    public sealed class SoftwareVersion
    {
        public string Version = "", MinClientVersion = "";
        public CpuArch Arch = CpuArch.Unknown;
        public string ArchRaw = "";
        public InstallerType Type = InstallerType.Unknown;
        public string TypeRaw = "";
        public string Url = "";
        public string Sha256 = "";
        public long Size;
        public string SignerSubject = "";
        public List<string> AllowedHosts = new List<string>();
        public string WingetId = "";
        /// <summary>Phạm vi cài winget do admin ghi ("machine" | "user"); rỗng / giá trị lạ = không ghi. Chỉ là dữ liệu, client tự dựng cờ --scope.</summary>
        public string Scope = "";
        public DetectRule Detect = new DetectRule();
        public SourceStatus Status = SourceStatus.Unknown;
        public List<FallbackSource> Fallbacks = new List<FallbackSource>();
        /// <summary>Lý do phiên bản này không dùng được để tải/cài (rỗng = hợp lệ về cấu trúc).</summary>
        public string Invalid = "";

        // GĐ5 schema v2 (mọi trường tùy chọn với catalog v1)
        public InstallerKind Kind = InstallerKind.None;
        public string KindRaw = "";
        public SilentLevel Silent = SilentLevel.Full;
        /// <summary>Client không bao giờ tự khởi động lại máy (catalog chỉ được ghi noreboot: true).</summary>
        public bool NoReboot = true;
        /// <summary>null = catalog không ghi (để mặc định của trình cài).</summary>
        public bool? PerMachine;
        public List<DetectionSignal> Detection = new List<DetectionSignal>();
        /// <summary>SHA-256 từng đoạn 4 MiB (rỗng nếu không có / sai số đoạn).</summary>
        public List<string> ChunkSha256 = new List<string>();
        /// <summary>Gói đã test: không im lặng hoàn toàn được => giao diện nói rõ "cần bạn bấm 1 lần".</summary>
        public bool NeedsUserClick;
        /// <summary>null = suy theo Action như v1; true = chỉ tải + kiểm, không bao giờ chạy; false = được cài.</summary>
        public bool? DownloadOnly;
        /// <summary>Thumbprint SHA-1 (40 hex, IN HOA) chứng chỉ ký kỳ vọng cho gói quan trọng; rỗng = chỉ so CN / DN.</summary>
        public string SignerThumbprint = "";
    }

    public sealed class SoftwarePackage
    {
        public string Slug = "", NameVi = "", NameEn = "", Publisher = "", Category = "";
        public string DescriptionVi = "", DescriptionEn = "";
        public string OfficialUrl = "", LicenseUrl = "", WingetId = "", IconUrl = "";
        public int MinOsBuild;
        public LicenseLabel License = LicenseLabel.Unknown;
        public PackageAction Action = PackageAction.Unknown;
        public string ActionRaw = "";
        public bool Popular;
        /// <summary>Thuộc "Bộ cài Win mới (1 chạm)" — admin bật ở hoangngocquanganh.com/qa98/admin (cờ featured).</summary>
        public bool Featured;
        /// <summary>Gói đặc biệt dẫn sang mô-đun riêng (VD "office" => Office Manager). Không tải/cài từ danh mục.</summary>
        public string Module = "";
        /// <summary>Cấu hình sau cài do admin đặt trên server (null = không có / không hợp lệ).</summary>
        public PostInstallSpec PostInstall;
        public List<SoftwareVersion> Versions = new List<SoftwareVersion>();
        // GĐ5
        public PackageLifecycle Lifecycle = PackageLifecycle.Active;
        public string LifecycleNote = "";
        /// <summary>Freshness hết hạn / thiếu / sai => dừng cài mới (Action đã hạ về OFFICIAL_PAGE_ONLY), vẫn cho xem.</summary>
        public bool InstallFrozen;
    }

    /// <summary>
    /// Việc làm sau khi winget cài gói portable (ZIP), admin soạn ở web admin (trường "post_install" trong danh mục đã ký):
    /// đặt tệp cấu hình cạnh tệp chạy rồi (tùy chọn) mở chương trình. Chỉ tên tệp trần + dữ liệu, không lệnh / tham số / đường dẫn.
    /// </summary>
    public sealed class PostInstallSpec
    {
        public string Exe = "", ExeX86 = "";
        public List<KeyValuePair<string, byte[]>> Files = new List<KeyValuePair<string, byte[]>>();
        public bool Overwrite, Launch;

        public const int MaxFiles = 8, MaxFileBytes = 256 * 1024;
        static readonly Regex ExeRe = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,62}\.exe$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex NameRe = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,63}$", RegexOptions.CultureInvariant);
        static readonly string[] RunnableExt = { ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".lnk", ".scr", ".cpl", ".hta", ".reg", ".sys" };

        /// <summary>Đọc + kiểm. Sai bất kỳ chỗ nào => null (không làm nửa vời).</summary>
        public static PostInstallSpec Parse(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Object) return null;
            var s = new PostInstallSpec
            {
                Exe = e.TryGetProperty("exe", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "",
                ExeX86 = e.TryGetProperty("exe_x86", out var x8) && x8.ValueKind == JsonValueKind.String ? x8.GetString() ?? "" : "",
                Overwrite = e.TryGetProperty("overwrite", out var ow) && ow.ValueKind == JsonValueKind.True,
                Launch = e.TryGetProperty("launch", out var la) && la.ValueKind == JsonValueKind.True,
            };
            if (!ExeRe.IsMatch(s.Exe) || s.ExeX86.Length > 0 && !ExeRe.IsMatch(s.ExeX86)) return null;
            if (e.TryGetProperty("files", out var fs))
            {
                if (fs.ValueKind != JsonValueKind.Array || fs.GetArrayLength() > MaxFiles) return null;
                foreach (var f in fs.EnumerateArray())
                {
                    string name = f.ValueKind == JsonValueKind.Object && f.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    string b64 = f.ValueKind == JsonValueKind.Object && f.TryGetProperty("b64", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
                    if (!SafeFileName(name) || b64.Length == 0 || b64.Length > MaxFileBytes / 3 * 4 + 4) return null;
                    byte[] data;
                    try { data = Convert.FromBase64String(b64); } catch (FormatException) { return null; }
                    if (data.Length == 0 || data.Length > MaxFileBytes || s.Files.Any(k => string.Equals(k.Key, name, StringComparison.OrdinalIgnoreCase))) return null;
                    s.Files.Add(new KeyValuePair<string, byte[]>(name, data));
                }
            }
            return s.Files.Count > 0 || s.Launch ? s : null;
        }

        public static bool SafeFileName(string name)
            => NameRe.IsMatch(name ?? "") && !name.EndsWith(".", StringComparison.Ordinal)
               && !RunnableExt.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase));
    }

    public sealed class SoftwareCatalog
    {
        public int Schema;
        public long Revision;
        public long IssuedAt, ExpiresAt;   // giây Unix (UTC)
        /// <summary>GĐ5: catalog đòi freshness.json (schema 2 luôn đòi; schema 1 khi có "requires_freshness": true).</summary>
        public bool RequiresFreshness;
        /// <summary>GĐ3 availability: quản trị TẮT lớp tạm ẩn (availability.json) qua catalog đã ký. Thiếu = false.</summary>
        public bool AvailabilityDisabled;
        /// <summary>GĐ3 availability: kid khoá vận hành bị thu hồi (client từ chối availability.json ký bằng các khoá này). Thiếu = rỗng.</summary>
        public List<string> AvailabilityRevokedKids = new List<string>();
        public List<SoftwareCategory> Categories = new List<SoftwareCategory>();
        public List<SoftwarePackage> Packages = new List<SoftwarePackage>();
    }

    /// <summary>Đọc JSON payload ĐÃ verify chữ ký. Bất kỳ lỗi cấu trúc nào ở mức gói => bỏ gói đó (ghi lý do), không làm hỏng cả danh mục.</summary>
    public static class SoftwareCatalogParser
    {
        public const int SupportedSchema = 1;
        /// <summary>GĐ5: client đọc được schema 1 (v1 hiện hành) và 2 (v2). Bản 3.0.1 cũ gặp 2 => UnsupportedSchema (từ chối an toàn, dùng cache).</summary>
        public const int MaxSupportedSchema = 2;

        /// <summary>Tên trường bị cấm ở mọi cấp: danh mục không được mang lệnh / tham số tùy ý.</summary>
        static readonly string[] ForbiddenFields = { "args", "arguments", "argument", "command", "commands", "cmd", "script", "silent_args", "install_args", "parameters", "switches", "commandline", "command_line",
            // GĐ5 mở rộng (v2): mọi biến thể tham số / lệnh / thuộc tính MSI tự do
            "install_command", "uninstall_command", "uninstall_args", "custom_args", "extra_args", "silent_switch", "silent_switches", "switch",
            "pre_install", "post_install_command", "post_install_script", "exec", "shell", "powershell", "properties", "msi_properties", "transforms", "env" };

        /// <summary>GĐ5: tên trường chứa một "từ" nguy hiểm (tách theo _ / -) cũng bị cấm, VD "silent_args", "pre-install-cmd", "msiexec_params".</summary>
        static readonly string[] ForbiddenTokens = { "arg", "args", "argument", "arguments", "switch", "switches", "command", "commands", "cmd", "cmdline", "commandline",
            "script", "scripts", "param", "params", "parameters", "exec", "shell", "powershell" };

        static readonly Regex Sha256Re = new Regex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
        static readonly Regex SlugRe = new Regex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
        static readonly Regex GuidRe = new Regex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$", RegexOptions.CultureInvariant);

        public static SoftwareCatalog Parse(string json, List<string> warnings = null)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var r = doc.RootElement;
            bool fromWeb = r.TryGetProperty("schema_version", out _);
            var body = fromWeb ? r.GetProperty("catalog") : r;
            var health = r.TryGetProperty("health", out var h) && h.ValueKind == JsonValueKind.Object ? h : default;
            var c = new SoftwareCatalog
            {
                Schema = (int)Num(r, fromWeb ? "schema_version" : "schema"),
                Revision = Num(r, "revision"),
                IssuedAt = Num(r, "issued_at"),
                ExpiresAt = Num(r, "expires_at"),
                RequiresFreshness = r.TryGetProperty("requires_freshness", out var rf) && rf.ValueKind == JsonValueKind.True,
            };
            if (c.Schema >= 2) c.RequiresFreshness = true;
            c.AvailabilityDisabled = r.TryGetProperty("availability_disabled", out var ad) && ad.ValueKind == JsonValueKind.True;
            if (r.TryGetProperty("availability_revoked_kids", out var rk) && rk.ValueKind == JsonValueKind.Array)
                foreach (var k in rk.EnumerateArray())
                    if (k.ValueKind == JsonValueKind.String && (k.GetString() ?? "").Trim().Length > 0) c.AvailabilityRevokedKids.Add(k.GetString().Trim());
            if (body.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
                foreach (var e in cats.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Object && SlugRe.IsMatch(Str(e, "slug")))
                        c.Categories.Add(new SoftwareCategory { Slug = Str(e, "slug"), NameVi = Str(e, "name_vi"), NameEn = Str(e, "name_en"), Order = (int)Num(e, fromWeb ? "sort" : "order") });
            if (body.TryGetProperty("packages", out var pkgs) && pkgs.ValueKind == JsonValueKind.Array)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in pkgs.EnumerateArray())
                {
                    var p = ParsePackage(e, health, c.Schema, out string why);
                    if (p == null || !seen.Add(p.Slug)) { warnings?.Add(why.Length > 0 ? why : "duplicate slug"); continue; }
                    c.Packages.Add(p);
                }
            }
            return c;
        }

        static SoftwarePackage ParsePackage(JsonElement e, JsonElement health, int schema, out string why)
        {
            why = "";
            if (e.ValueKind != JsonValueKind.Object) { why = "package is not an object"; return null; }
            string slug = Str(e, "slug");
            if (!SlugRe.IsMatch(slug)) { why = "bad slug"; return null; }
            if (HasForbidden(e, out string f)) { why = $"{slug}: forbidden field '{f}'"; return null; }
            var p = new SoftwarePackage
            {
                Slug = slug,
                NameVi = Str(e, "name_vi"), NameEn = Str(e, "name_en"),
                Publisher = Str(e, "publisher"), Category = Str(e, "category"),
                DescriptionVi = Str(e, "description_vi"), DescriptionEn = Str(e, "description_en"),
                OfficialUrl = HttpsOrEmpty(Str(e, "homepage")), LicenseUrl = HttpsOrEmpty(Str(e, "license_url")),
                WingetId = Str(e, "winget_id"), IconUrl = Str(e, "icon_url"), MinOsBuild = (int)Num(e, "min_os_build"),
                License = ParseLicense(Str(e, "license_label")),
                ActionRaw = Str(e, "action"),
                Popular = e.TryGetProperty("popular", out var pop) && pop.ValueKind == JsonValueKind.True,
                Featured = e.TryGetProperty("featured", out var fe) && fe.ValueKind == JsonValueKind.True,
                Module = Str(e, "module"),
            };
            if (p.OfficialUrl.Length == 0) p.OfficialUrl = HttpsOrEmpty(Str(e, "official_url"));
            if (!IsCatalogIconUrl(p.IconUrl)) p.IconUrl = "";
            if (!IsSafeWingetId(p.WingetId)) p.WingetId = "";
            if (e.TryGetProperty("post_install", out var pi)) p.PostInstall = PostInstallSpec.Parse(pi);
            p.Action = ParseAction(p.ActionRaw);
            p.Lifecycle = Str(e, "lifecycle") switch
            {
                "" or "active" => PackageLifecycle.Active, "deprecated" => PackageLifecycle.Deprecated,
                "vendor_changed" => PackageLifecycle.VendorChanged, "signer_changed" => PackageLifecycle.SignerChanged, _ => PackageLifecycle.Unknown,
            };
            p.LifecycleNote = Str(e, "lifecycle_note");
            if (p.Lifecycle == PackageLifecycle.Deprecated) p.Featured = false;   // deprecated: vẫn hiện, không nằm trong bộ cài tự động 1 chạm
            if (p.NameVi.Length == 0) p.NameVi = p.NameEn;
            if (p.NameEn.Length == 0) p.NameEn = p.NameVi;
            if (p.NameVi.Length == 0) { why = $"{slug}: missing name"; return null; }
            if (e.TryGetProperty("versions", out var vs) && vs.ValueKind == JsonValueKind.Array)
                foreach (var ve in vs.EnumerateArray())
                    if (ve.ValueKind == JsonValueKind.Object) p.Versions.Add(ParseVersion(ve, health, schema));
            return p;
        }

        static SoftwareVersion ParseVersion(JsonElement e, JsonElement health, int schema)
        {
            var v = new SoftwareVersion
            {
                Version = Str(e, "version"), MinClientVersion = Str(e, "min_client_version"),
                ArchRaw = Str(e, "arch"),
                TypeRaw = Str(e, "installer_type"),
                Url = Str(e, "url"),
                Sha256 = Str(e, "sha256").ToLowerInvariant(),
                Size = Num(e, "size"),
                SignerSubject = Str(e, "signer_subject"),
                AllowedHosts = Hosts(e, "allowed_hosts"),
                WingetId = Str(e, "winget_id"),
                Scope = Str(e, "scope").Trim().ToLowerInvariant() is "machine" or "user" ? Str(e, "scope").Trim().ToLowerInvariant() : "",
                Status = ParseSourceStatus(e, health),
            };
            v.Arch = ParseArch(v.ArchRaw);
            v.Type = ParseType(v.TypeRaw);
            string v2Bad = ParseV2(e, v);
            if (e.TryGetProperty("detect", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                v.Detect = new DetectRule { DisplayName = Str(d, "uninstall_display_name"), Publisher = Str(d, "uninstall_publisher"), MsiProductCode = Str(d, "msi_product_code"), WingetId = Str(d, "winget_id") };
                if (v.Detect.DisplayName.Length == 0) v.Detect.DisplayName = Str(d, "display_name");
                if (v.Detect.Publisher.Length == 0) v.Detect.Publisher = Str(d, "publisher");
            }
            if (e.TryGetProperty("fallbacks", out var fb) && fb.ValueKind == JsonValueKind.Array)
                foreach (var x in fb.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.Object)
                        v.Fallbacks.Add(new FallbackSource { Url = Str(x, "url"), AllowedHosts = Hosts(x, "allowed_hosts"), Status = ParseSourceStatus(x, health) });
            v.Invalid = Validate(e, v, schema, v2Bad);
            return v;
        }

        // ---------------------------------------------------------------- GĐ5 schema v2

        static readonly string[] FileRoots = { "programfiles", "programfilesx86", "localappdata", "programdata" };
        static readonly Regex HexRe = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        static readonly Regex ThumbRe = new Regex("^[0-9A-F]{40}$", RegexOptions.CultureInvariant);
        static readonly Regex NameTokenRe = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 ._()+-]{0,127}$", RegexOptions.CultureInvariant);

        public static InstallerKind ParseKind(string s) => s switch
        {
            "" => InstallerKind.None,
            "msi" => InstallerKind.Msi, "inno" => InstallerKind.Inno, "nsis" => InstallerKind.Nsis,
            "installshield-basicmsi" => InstallerKind.InstallShieldBasicMsi, "installshield-script" => InstallerKind.InstallShieldScript,
            "burn" => InstallerKind.Burn, "squirrel" => InstallerKind.Squirrel, "clickonce" => InstallerKind.ClickOnce,
            "portable-zip" => InstallerKind.PortableZip, "winget-only" => InstallerKind.WingetOnly,
            _ => InstallerKind.Unknown,
        };

        /// <summary>Loại trình cài v1 tương ứng với installer_kind (để catalog v2 bỏ được installer_type).</summary>
        static InstallerType TypeOfKind(InstallerKind k) => k switch
        {
            InstallerKind.Msi => InstallerType.Msi,
            InstallerKind.PortableZip => InstallerType.ZipPortable,
            InstallerKind.WingetOnly => InstallerType.Winget,
            InstallerKind.Inno or InstallerKind.Nsis or InstallerKind.InstallShieldBasicMsi or InstallerKind.InstallShieldScript or InstallerKind.Burn or InstallerKind.Squirrel => InstallerType.ExeInteractive,
            _ => InstallerType.Unknown,
        };

        static bool KindMatchesType(InstallerKind k, InstallerType t)
            => t == TypeOfKind(k) || (t == InstallerType.WebInstaller && TypeOfKind(k) == InstallerType.ExeInteractive);

        /// <summary>Đọc trường v2 của 1 phiên bản. Trả "" hoặc lý do (giá trị ngoài khung đóng => NEEDS_CLIENT_UPDATE, tham số tự do => forbidden).</summary>
        static string ParseV2(JsonElement e, SoftwareVersion v)
        {
            string bad = "";
            v.KindRaw = Str(e, "installer_kind").ToLowerInvariant();
            v.Kind = ParseKind(v.KindRaw);
            if (v.TypeRaw.Length == 0 && v.Kind != InstallerKind.None) v.Type = TypeOfKind(v.Kind);
            long sb = Num(e, "size_bytes");
            if (sb > 0) v.Size = sb;
            switch (Str(e, "silent_level"))
            {
                case "": case "full": v.Silent = SilentLevel.Full; break;
                case "progress": v.Silent = SilentLevel.Progress; break;
                default: bad = "NEEDS_CLIENT_UPDATE"; break;
            }
            if (e.TryGetProperty("noreboot", out var nr) && nr.ValueKind != JsonValueKind.True) bad = "NEEDS_CLIENT_UPDATE";   // chỉ có giá trị true
            if (e.TryGetProperty("per_machine", out var pm))
            {
                if (pm.ValueKind == JsonValueKind.True) v.PerMachine = true;
                else if (pm.ValueKind == JsonValueKind.False) v.PerMachine = false;
                else bad = "NEEDS_CLIENT_UPDATE";
            }
            v.NeedsUserClick = e.TryGetProperty("needs_user_click", out var nu) && nu.ValueKind == JsonValueKind.True;
            if (e.TryGetProperty("download_only", out var dl))
                v.DownloadOnly = dl.ValueKind == JsonValueKind.True ? true : dl.ValueKind == JsonValueKind.False ? false : (bool?)true;   // giá trị lạ => an toàn: chỉ tải
            string thumb = Str(e, "signer_thumbprint").Replace(" ", "").ToUpperInvariant();
            if (thumb.Length > 0) { if (ThumbRe.IsMatch(thumb)) v.SignerThumbprint = thumb; else bad = "bad signer_thumbprint"; }
            if (e.TryGetProperty("chunk_sha256", out var ch) && ch.ValueKind == JsonValueKind.Array)
            {
                bool sizeOk = !e.TryGetProperty("chunk_size", out var cs) || cs.ValueKind == JsonValueKind.Number && cs.TryGetInt64(out long csv) && csv == Trust.ChunkHashes.ChunkSize;
                var list = ch.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? (x.GetString() ?? "").ToLowerInvariant() : "").ToList();
                // Tùy chọn: sai số đoạn / sai định dạng => bỏ danh sách (vẫn kiểm hash toàn tệp như cũ)
                if (sizeOk && list.All(HexRe.IsMatch) && Trust.ChunkHashes.Usable(list, v.Size)) v.ChunkSha256 = list;
            }
            if (e.TryGetProperty("detection", out var det) && det.ValueKind == JsonValueKind.Array)
                foreach (var x in det.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.Object) continue;
                    if (HasForbidden(x, out string f)) return $"forbidden field '{f}'";
                    var s = ParseSignal(x);
                    if (s != null) v.Detection.Add(s);
                }
            return bad;
        }

        /// <summary>Tín hiệu sai / loại lạ => bỏ tín hiệu đó (chỉ là dữ liệu đọc, không làm hỏng phiên bản).</summary>
        static DetectionSignal ParseSignal(JsonElement x)
        {
            var s = new DetectionSignal { MinVersion = Str(x, "min_version") };
            switch (Str(x, "type"))
            {
                case "arp":
                    s.Type = DetectionType.Arp; s.DisplayName = Str(x, "display_name"); s.Publisher = Str(x, "publisher"); s.Key = Str(x, "key");
                    if (s.Key.Length > 0 && !(GuidRe.IsMatch(s.Key) || NameTokenRe.IsMatch(s.Key)) || s.Key.Length == 0 && s.DisplayName.Length == 0) return null;
                    return s;
                case "msi_upgradecode":
                    s.Type = DetectionType.MsiUpgradeCode; s.UpgradeCode = Str(x, "code").ToUpperInvariant();
                    return GuidRe.IsMatch(s.UpgradeCode) ? s : null;
                case "file":
                    s.Type = DetectionType.File; s.Root = Str(x, "root").ToLowerInvariant(); s.Path = Str(x, "path").Replace('/', '\\');
                    return FileRoots.Contains(s.Root) && SafeRelativePath(s.Path) ? s : null;
                case "unins":
                    s.Type = DetectionType.Unins; s.AppId = Str(x, "app_id");
                    return s.AppId.Length > 0 && s.AppId.Length <= 128 && (GuidRe.IsMatch(s.AppId) || NameTokenRe.IsMatch(s.AppId)) ? s : null;
                case "squirrel":
                    s.Type = DetectionType.Squirrel; s.Folder = Str(x, "folder");
                    return NameTokenRe.IsMatch(s.Folder) && !s.Folder.Contains("..") ? s : null;
                default: return null;
            }
        }

        static bool SafeRelativePath(string p)
            => p.Length > 0 && p.Length <= 260 && !p.Contains(':') && p.IndexOf('\0') < 0 && p[0] != '\\' && !System.IO.Path.IsPathRooted(p)
               && p.Split('\\').All(seg => seg.Length > 0 && seg != "." && seg != "..");

        /// <summary>Kiểm tra cấu trúc 1 phiên bản. Không hợp lệ => không bao giờ được tải/chạy (gói vẫn hiện nút trang chính thức).</summary>
        static string Validate(JsonElement e, SoftwareVersion v, int schema, string v2Bad)
        {
            if (HasForbidden(e, out string f)) return $"forbidden field '{f}'";
            if (v2Bad.StartsWith("forbidden", StringComparison.Ordinal)) return v2Bad;
            if (e.TryGetProperty("fallbacks", out var fbs) && fbs.ValueKind == JsonValueKind.Array)
                foreach (var x in fbs.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.Object && HasForbidden(x, out f)) return $"forbidden field '{f}'";
            if (v.Kind == InstallerKind.Unknown || v.Kind == InstallerKind.ClickOnce) return "NEEDS_CLIENT_UPDATE";   // ClickOnce: chưa có template an toàn
            if (v2Bad.Length > 0) return v2Bad;
            if (v.Type == InstallerType.Unknown) return "NEEDS_CLIENT_UPDATE";
            if (v.Kind != InstallerKind.None && !KindMatchesType(v.Kind, v.Type)) return "installer_kind does not match installer_type";
            if (schema >= 2 && v.Type != InstallerType.Winget && v.Size <= 0) return "missing size_bytes";
            if (v.Arch == CpuArch.Unknown) return "unknown arch";
            if (v.Detect.MsiProductCode.Length > 0 && !GuidRe.IsMatch(v.Detect.MsiProductCode)) return "bad msi_product_code";
            if (v.Type == InstallerType.Winget)
                return IsSafeWingetId(v.WingetId) ? "" : "bad winget_id";
            if (!IsHttps(v.Url)) return "url must be https";
            if (!Sha256Re.IsMatch(v.Sha256)) return "missing sha256";
            if (v.AllowedHosts.Count == 0) return "missing allowed_hosts";
            if (!HostAllowed(v.Url, v.AllowedHosts)) return "url host not in allowed_hosts";
            bool needsSigner = v.Type != InstallerType.ZipPortable;
            if (needsSigner && v.SignerSubject.Length == 0) return "missing signer_subject";
            foreach (var fb in v.Fallbacks)
                if (!IsHttps(fb.Url) || fb.AllowedHosts.Count == 0 || !HostAllowed(fb.Url, fb.AllowedHosts)) fb.Status = SourceStatus.Broken;   // nguồn dự phòng sai cấu trúc => coi như hỏng
            return "";
        }

        /// <summary>Id winget: chỉ ký tự an toàn (Publisher.Name), không bắt đầu bằng "-", không khoảng trắng.</summary>
        public static bool IsSafeWingetId(string id) => !string.IsNullOrEmpty(id) && id.Length <= 128 && Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9._+-]*$", RegexOptions.CultureInvariant);

        static SourceStatus ParseSourceStatus(JsonElement source, JsonElement health)
        {
            long id = Num(source, "source_id");
            var status = ParseStatus(Str(source, "source_status"));
            if (id <= 0 || status == SourceStatus.Broken || health.ValueKind != JsonValueKind.Object) return status;
            var overlay = ParseStatus(Str(health, id.ToString(CultureInfo.InvariantCulture)));
            return overlay is SourceStatus.Degraded or SourceStatus.Broken ? overlay : status;
        }

        public static bool IsCatalogIconUrl(string url)
            => url != null && Regex.IsMatch(url, @"\Ahttps://hoangngocquanganh\.com/qa98/assets/software-icons/[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}\.(png|svg)\z", RegexOptions.CultureInvariant);

        public static bool IsHttps(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.UserInfo);

        /// <summary>Host khớp CHÍNH XÁC một mục trong allowed_hosts (không wildcard, không so hậu tố), chỉ HTTPS, cổng mặc định.</summary>
        public static bool HostAllowed(string url, IReadOnlyCollection<string> hosts)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || !u.IsDefaultPort || !string.IsNullOrEmpty(u.UserInfo)) return false;
            string h = u.IdnHost.TrimEnd('.');
            return hosts != null && hosts.Any(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase));
        }

        static bool HasForbidden(JsonElement e, out string field)
        {
            foreach (var p in e.EnumerateObject())
                if (IsForbiddenName(p.Name)) { field = p.Name; return true; }
            field = "";
            return false;
        }

        public static bool IsForbiddenName(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return ForbiddenFields.Contains(n) || n.Split('_', '-', '.').Any(t => ForbiddenTokens.Contains(t));
        }

        static string HttpsOrEmpty(string url) => IsHttps(url) ? url : "";

        static List<string> Hosts(JsonElement e, string name)
        {
            var list = new List<string>();
            if (e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array)
                foreach (var x in a.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String)
                    {
                        string h = (x.GetString() ?? "").Trim().TrimEnd('.').ToLowerInvariant();
                        if (h.Length > 0 && Uri.CheckHostName(h) == UriHostNameType.Dns && !h.Contains('*')) list.Add(h);
                    }
            return list;
        }

        static string Str(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

        static long Num(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n)) return n;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return 0;
        }

        public static LicenseLabel ParseLicense(string s) => s switch
        {
            "FREE" => LicenseLabel.Free, "OPEN_SOURCE" => LicenseLabel.OpenSource, "TRIAL" => LicenseLabel.Trial, "PAID" => LicenseLabel.Paid, _ => LicenseLabel.Unknown,
        };

        public static PackageAction ParseAction(string s) => s switch
        {
            "OFFICIAL_PAGE_ONLY" => PackageAction.OfficialPageOnly,
            "OFFICIAL_DOWNLOAD_USER_DRIVEN" => PackageAction.OfficialDownloadUserDriven,
            "APPROVED_DIRECT_DOWNLOAD" => PackageAction.ApprovedDirectDownload,
            "APPROVED_INTERACTIVE_INSTALL" => PackageAction.ApprovedInteractiveInstall,
            "APPROVED_UNATTENDED_INSTALL" => PackageAction.ApprovedUnattendedInstall,
            "BLOCKED" => PackageAction.Blocked,
            _ => PackageAction.Unknown,
        };

        public static InstallerType ParseType(string s) => s switch
        {
            "MSI" => InstallerType.Msi, "EXE_INTERACTIVE" => InstallerType.ExeInteractive, "ZIP_PORTABLE" => InstallerType.ZipPortable,
            "WINGET" => InstallerType.Winget, "MSIX" => InstallerType.Msix, "WEB_INSTALLER" => InstallerType.WebInstaller, _ => InstallerType.Unknown,
        };

        public static SourceStatus ParseStatus(string s) => s switch
        {
            "HEALTHY" => SourceStatus.Healthy, "DEGRADED" => SourceStatus.Degraded, "BROKEN" => SourceStatus.Broken, _ => SourceStatus.Unknown,
        };

        public static CpuArch ParseArch(string s) => (s ?? "").ToLowerInvariant() switch
        {
            "x86" => CpuArch.X86, "x64" => CpuArch.X64, "arm64" => CpuArch.Arm64, "neutral" => CpuArch.Neutral, _ => CpuArch.Unknown,
        };
    }
}
