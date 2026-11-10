using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SysInfoWpf.Services.SoftwareCenter.Health
{
    // =====================================================================================================================
    // Logic dò link của bot Tools/CatalogWatch (chạy trên GitHub Actions). Không có giao diện: reason / note là mã kỹ thuật tiếng Anh
    // (ghi vào link_health.json + report.md cho chủ dự án đọc); báo cáo tiếng Việt do tool dựng.
    // Chỉ GET (Range 64 byte để xem tiêu đề, hoặc tải cả file vào luồng băm SHA-256) — không ghi đĩa, không bao giờ chạy file.
    // =====================================================================================================================

    /// <summary>Một biến thể cần dò (1 phiên bản đã duyệt của 1 gói, 1 kiến trúc).</summary>
    public sealed class ProbeTarget
    {
        public string Key = "", WingetId = "", Slug = "", Version = "";
        public InstallerType Type;
        public string Url = "", Sha256 = "";
        public long SizeBytes;
        public CpuArch Arch;
        /// <summary>Id dùng dựng đường dẫn manifest winget (id của phiên bản, không có thì id của gói).</summary>
        public string ManifestId = "";
        public bool Featured;
        /// <summary>Nguồn dự phòng (cùng file, cùng hash) — app tự chuyển sang khi link chính hỏng.</summary>
        public List<string> Fallbacks = new List<string>();
        public bool IsWinget => Type == InstallerType.Winget;
    }

    public sealed class WingetInstaller
    {
        public string Url = "", Sha256 = "", Arch = "";
    }

    /// <summary>Kết quả dò 1 khoá (gộp mọi biến thể kiến trúc cùng khoá).</summary>
    public sealed class ProbeResult
    {
        public ProbeOutcome Outcome;
        /// <summary>true = không đủ điều kiện kết luận (file quá giới hạn tải thử) => KHÔNG gọi Step.</summary>
        public bool Skipped;
        public string Reason = "", Note = "";
        public string Fingerprint = "";
        /// <summary>true = lượt này đã tải cả file và SHA-256 khớp (được cập nhật Fingerprint / HashedUtc).</summary>
        public bool Hashed;
        public List<string> Tried = new List<string>();
    }

    /// <summary>Kết quả tìm nguồn thay thế cho khoá đang lỗi (chỉ đề xuất, không bao giờ tự áp vào catalog).</summary>
    public sealed class Discovery
    {
        /// <summary>same_artifact / new_artifact / retire_candidate / needs_review / "" (không tìm được gì).</summary>
        public string Kind = "";
        public string Note = "";
        public string CandidateVersion = "", CandidateUrl = "", CandidateSha256 = "", ManifestUrl = "", ManifestRef = "";
    }

    /// <summary>
    /// Tham số vận hành bot đọc từ web admin (bot_config.php) mỗi lượt. CHỈ tham số chạy — không bao giờ có URL tải, hash hay danh sách ẩn.
    /// Lỗi tải / sai định dạng / giá trị ngoài khoảng => giá trị mặc định an toàn.
    /// </summary>
    public sealed class BotConfig
    {
        public bool Enabled = true;
        public int MaxPerRun = 100;
        public int IntervalHours = 6;
        public DateTime? RunNowAfterUtc;
        public bool FeaturedFirst = true;
        public int ConcurrencyPerHost = 2;
        /// <summary>true = đọc được từ server; false = đang dùng mặc định.</summary>
        public bool FromServer;

        public const int MaxBytes = 64 * 1024;
        /// <summary>Cron chạy mỗi giờ và hay trễ vài phút: tới hạn sớm hơn tối đa chừng này vẫn tính là tới hạn.</summary>
        public static readonly TimeSpan DueSlack = TimeSpan.FromMinutes(15);

        public static BotConfig Parse(string json)
        {
            var c = new BotConfig();
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes) return c;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var r = doc.RootElement;
                if (r.ValueKind != System.Text.Json.JsonValueKind.Object) return c;
                bool? B(string k) => r.TryGetProperty(k, out var v) && (v.ValueKind == System.Text.Json.JsonValueKind.True || v.ValueKind == System.Text.Json.JsonValueKind.False) ? v.GetBoolean() : null;
                int? I(string k, int min, int max) => r.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out int n) && n >= min && n <= max ? n : null;
                c.Enabled = B("enabled") ?? c.Enabled;
                c.FeaturedFirst = B("featured_first") ?? c.FeaturedFirst;
                c.MaxPerRun = I("max_per_run", 1, 1000) ?? c.MaxPerRun;
                c.IntervalHours = I("interval_hours", 1, 168) ?? c.IntervalHours;
                c.ConcurrencyPerHost = I("concurrency_per_host", 1, 8) ?? c.ConcurrencyPerHost;
                if (r.TryGetProperty("run_now_after", out var rn) && rn.ValueKind == System.Text.Json.JsonValueKind.String
                    && DateTime.TryParse(rn.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
                    c.RunNowAfterUtc = DateTime.SpecifyKind(t, DateTimeKind.Utc);
                c.FromServer = true;
            }
            catch (System.Text.Json.JsonException) { return new BotConfig(); }
            return c;
        }

        /// <summary>Có chạy lượt dò không: chưa từng dò / đã qua interval_hours / admin bấm "Chạy ngay" (run_now_after mới hơn lượt trước và không ở tương lai) / ép chạy.</summary>
        public bool IsDue(DateTime? lastProbeUtc, DateTime nowUtc, bool force = false)
        {
            if (!Enabled) return false;
            if (force || lastProbeUtc == null) return true;
            if (nowUtc - lastProbeUtc.Value >= TimeSpan.FromHours(IntervalHours) - DueSlack) return true;
            return RunNowAfterUtc is DateTime rn && rn > lastProbeUtc.Value && rn <= nowUtc;
        }
    }

    /// <summary>Host phân giải ra IP nội bộ (ConnectCallback của tool ném) — coi là link hỏng, không kết nối.</summary>
    public sealed class BlockedAddressException : Exception
    {
        public BlockedAddressException(string host) : base("blocked address for host " + host) { }
    }

    public static class LinkProbeLogic
    {
        public const int SniffBytes = 64;
        /// <summary>Dư cho phép trên size_bytes đã duyệt trước khi coi là sai file.</summary>
        public const long SizeSlack = 64 * 1024;
        public const long DefaultMaxBytes = 2L * 1024 * 1024 * 1024;
        public const int MaxRedirects = 5;
        public const int MaxManifestBytes = 1024 * 1024;

        static bool Approved(PackageAction a) => a is PackageAction.ApprovedDirectDownload or PackageAction.ApprovedInteractiveInstall or PackageAction.ApprovedUnattendedInstall;

        /// <summary>Các biến thể cần dò: gói có hành động đã duyệt (không phải mô-đun riêng), phiên bản hợp lệ cấu trúc.
        /// Winget không ghi phiên bản thì bỏ (winget cài bản mới nhất, không có manifest cố định để dò).</summary>
        public static List<ProbeTarget> Targets(SoftwareCatalog c)
        {
            var list = new List<ProbeTarget>();
            foreach (var p in c?.Packages ?? new List<SoftwarePackage>())
            {
                if (!Approved(p.Action) || p.Module.Length > 0) continue;
                foreach (var v in p.Versions)
                {
                    if (v.Invalid.Length > 0) continue;
                    var t = new ProbeTarget
                    {
                        WingetId = p.WingetId, Slug = p.Slug, Version = v.Version, Type = v.Type, Url = v.Url, Sha256 = v.Sha256,
                        SizeBytes = v.Size, Arch = v.Arch, ManifestId = v.WingetId.Length > 0 ? v.WingetId : p.WingetId, Featured = p.Featured,
                        Key = LinkHealthLogic.KeyOf(p.WingetId, p.Slug, v.Version),
                    };
                    if (t.IsWinget) { if (t.Version.Length == 0 || !SoftwareCatalogParser.IsSafeWingetId(t.ManifestId)) continue; }
                    else t.Fallbacks = v.Fallbacks.Where(f => f.Status != SourceStatus.Broken).Select(f => f.Url).ToList();
                    list.Add(t);
                }
            }
            return list;
        }

        // '+' hợp lệ trong path và có thật trong id winget (VD Microsoft.VCRedist.2015+.x64) => giữ nguyên
        static string Seg(string s) => Uri.EscapeDataString(s).Replace("%2B", "+");

        /// <summary>Giữ trạng thái của khoá còn trong catalog (khoá đã bỏ / đổi bản => trạng thái cũ bị bỏ, không còn trong link_health.json).</summary>
        public static Dictionary<string, VariantState> KeepInCatalog(IEnumerable<VariantState> states, ICollection<string> catalogKeys)
        {
            var d = new Dictionary<string, VariantState>(StringComparer.Ordinal);
            foreach (var s in states ?? Enumerable.Empty<VariantState>())
                if (s != null && catalogKeys.Contains(s.Key)) d[s.Key] = s;
            return d;
        }

        /// <summary>manifests/{chữ đầu thường}/{id tách dấu chấm thành thư mục}/{version}/{id}.installer.yaml</summary>
        public static string WingetManifestUrl(string id, string version)
        {
            string path = string.Join("/", id.Split('.').Select(Seg));
            return "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/" + char.ToLowerInvariant(id[0]) + "/" + path + "/"
                   + Seg(version) + "/" + Seg(id) + ".installer.yaml";
        }

        /// <summary>Đọc tối giản installer.yaml: các mục trong "Installers:" lấy Architecture / InstallerUrl / InstallerSha256 (bỏ khoá lồng sâu hơn).</summary>
        public static List<WingetInstaller> ParseInstallerYaml(string yaml)
        {
            var res = new List<WingetInstaller>();
            bool inList = false; int itemIndent = -1; WingetInstaller cur = null;
            foreach (var raw in (yaml ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                string trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;
                int indent = line.Length - trimmed.Length;
                if (!inList) { if (indent == 0 && trimmed.StartsWith("Installers:", StringComparison.Ordinal)) inList = true; continue; }
                if (indent == 0 && !trimmed.StartsWith("- ", StringComparison.Ordinal)) break;   // khoá gốc kế tiếp => hết danh sách
                if (trimmed.StartsWith("- ", StringComparison.Ordinal) && (itemIndent < 0 || indent == itemIndent))
                {
                    itemIndent = indent; cur = new WingetInstaller(); res.Add(cur);
                    trimmed = trimmed.Substring(2).TrimStart(); indent = itemIndent + 2;   // "- Key: v" = khoá đầu của mục
                }
                if (cur == null || indent != itemIndent + 2) continue;   // khoá lồng (InstallerSwitches:, AppsAndFeaturesEntries:…) => bỏ
                int colon = trimmed.IndexOf(':');
                if (colon <= 0) continue;
                string k = trimmed.Substring(0, colon), val = YamlScalar(trimmed.Substring(colon + 1));
                if (k == "Architecture") cur.Arch = val;
                else if (k == "InstallerUrl") cur.Url = val;
                else if (k == "InstallerSha256") cur.Sha256 = val.ToLowerInvariant();
            }
            return res.Where(i => i.Url.Length > 0).ToList();
        }

        static string YamlScalar(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0]) return s.Substring(1, s.Length - 2);
            int hash = s.IndexOf(" #", StringComparison.Ordinal);
            return (hash >= 0 ? s.Substring(0, hash) : s).Trim();
        }

        /// <summary>Installer khớp kiến trúc đã duyệt (kèm neutral); không có cái nào khớp / bản duyệt là neutral => tất cả.</summary>
        public static List<WingetInstaller> PickInstallers(List<WingetInstaller> all, CpuArch arch)
        {
            string want = arch switch { CpuArch.X64 => "x64", CpuArch.X86 => "x86", CpuArch.Arm64 => "arm64", _ => "" };
            if (want.Length == 0) return all;
            var hit = all.Where(i => i.Arch.Equals(want, StringComparison.OrdinalIgnoreCase) || i.Arch.Equals("neutral", StringComparison.OrdinalIgnoreCase)).ToList();
            return hit.Count > 0 ? hit : all;
        }

        /// <summary>ETag|Last-Modified|Content-Length; cả 3 trống => "" (luôn phải tải cả file).</summary>
        public static string Fingerprint(string etag, string lastModified, long? length)
        {
            etag ??= ""; lastModified ??= "";
            if (etag.Length == 0 && lastModified.Length == 0 && length == null) return "";
            return etag + "|" + lastModified + "|" + (length?.ToString(CultureInfo.InvariantCulture) ?? "");
        }

        /// <summary>null = sống (đọc tiếp); còn lại = kết luận ngay theo mã HTTP.</summary>
        public static ProbeOutcome? ClassifyStatus(int code) => code switch
        {
            200 or 206 => null,
            408 or 429 => ProbeOutcome.Transient,
            >= 500 => ProbeOutcome.Transient,
            _ => ProbeOutcome.Broken,   // 404 / 410 / 401-403 lặp lại / 4xx khác / 2xx-3xx lạ
        };

        static readonly string[] HtmlStarts = { "<!doctype", "<html", "<head", "<body", "<script", "<?xml", "<!--" };

        /// <summary>Đầu file giống trang HTML / XML lỗi (bộ cài nhị phân không bao giờ bắt đầu thế này).</summary>
        public static bool LooksLikeHtml(byte[] head, int count)
        {
            int n = Math.Min(count, Math.Min(head?.Length ?? 0, SniffBytes));
            // ASCII giải BOM UTF-8 thành '?' => bỏ cùng khoảng trắng đầu
            string s = Encoding.ASCII.GetString(head ?? Array.Empty<byte>(), 0, n).TrimStart('?', ' ', '\t', '\r', '\n');
            return HtmlStarts.Any(h => s.StartsWith(h, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsHtmlContentType(string mediaType)
            => string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase) || string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);

        /// <summary>Dung lượng khai báo lệch size_bytes đã duyệt (cả 2 phải biết).</summary>
        public static bool SizeMismatch(long approved, long? actual) => approved > 0 && actual != null && actual.Value != approved;

        /// <summary>Byte tối đa được tải khi băm: size_bytes + dư, không có thì 2 GB.</summary>
        public static long ByteLimit(long approved) => approved > 0 ? approved + SizeSlack : DefaultMaxBytes;

        /// <summary>IP không được phép kết nối: loopback / private / link-local (gồm 169.254.169.254) / CGNAT / multicast / không xác định.</summary>
        public static bool IsBlockedAddress(IPAddress ip)
        {
            if (ip == null) return true;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                return b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
                       || b[0] == 169 && b[1] == 254
                       || b[0] == 172 && b[1] >= 16 && b[1] <= 31
                       || b[0] == 192 && b[1] == 168
                       || b[0] == 100 && b[1] >= 64 && b[1] <= 127
                       || b[0] == 192 && b[1] == 0 && b[2] == 0;
            }
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var b = ip.GetAddressBytes();
                return ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC;
            }
            return true;
        }

        /// <summary>URL công khai để lưu làm bằng chứng: bỏ query / fragment (có thể chứa token hết hạn).</summary>
        public static string PublicUrl(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Path) : "";

        /// <summary>So sánh phiên bản winget theo từng đoạn (số so theo số, chữ so ordinal). Dùng để chỉ đề xuất bản MỚI HƠN.</summary>
        public static int CompareVersions(string a, string b)
        {
            var x = (a ?? "").Split('.', '-', '+'); var y = (b ?? "").Split('.', '-', '+');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                string p = i < x.Length ? x[i] : "0", q = i < y.Length ? y[i] : "0";
                int c = long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out long m) && long.TryParse(q, NumberStyles.None, CultureInfo.InvariantCulture, out long n)
                    ? m.CompareTo(n) : string.CompareOrdinal(p, q);
                if (c != 0) return Math.Sign(c);
            }
            return 0;
        }

        /// <summary>Thư mục manifest của id trên GitHub API (liệt kê các bản có trong winget-pkgs).</summary>
        public static string WingetDirApiUrl(string id)
            => "https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/" + char.ToLowerInvariant(id[0]) + "/" + string.Join("/", id.Split('.').Select(Seg));

        /// <summary>Đọc kết quả GitHub contents API: chỉ thư mục có tên bắt đầu bằng chữ số (bỏ thư mục gói con như "Beta").</summary>
        public static List<(string Name, string Path, string Sha)> ParseGithubDirListing(string json)
        {
            var res = new List<(string, string, string)>();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json ?? "");
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return res;
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : "";
                    string name = S("name");
                    if (S("type") == "dir" && name.Length > 0 && char.IsDigit(name[0])) res.Add((name, S("path"), S("sha")));
                }
            }
            catch (System.Text.Json.JsonException) { }
            return res;
        }

        /// <summary>Loại đề xuất cho người duyệt (proposals.json, dữ liệu KHÔNG tin cậy).</summary>
        public static string Classify(ProbeOutcome o, bool hidden, Discovery d)
        {
            if (o == ProbeOutcome.Ok) return "healthy";
            if (o == ProbeOutcome.Transient) return "transient";
            string k = d?.Kind ?? "";
            if (k == "same_artifact" || k == "new_artifact" || k == "needs_review") return k;
            if (k == "retire_candidate") return hidden ? k : "suspect";   // mới mất manifest chưa đủ đề xuất xoá
            return hidden ? "needs_review" : "suspect";
        }

        /// <summary>Lượt có quá nhiều lỗi tạm / mạng (> 30 % số đã dò) => coi là sự cố diện rộng của runner, không thêm mục ẩn mới.</summary>
        public const double WideOutageFraction = 0.30;
        public static bool IsWideOutage(int probed, int transient) => probed > 0 && transient > probed * WideOutageFraction;

        /// <summary>Thứ tự ưu tiên lỗi giữa các kiến trúc: hỏng / sai file > lỗi tạm > đạt.</summary>
        public static ProbeOutcome Worst(IEnumerable<ProbeOutcome> all)
        {
            var l = all.ToList();
            if (l.Contains(ProbeOutcome.Broken)) return ProbeOutcome.Broken;
            if (l.Contains(ProbeOutcome.HashMismatch)) return ProbeOutcome.HashMismatch;
            if (l.Contains(ProbeOutcome.Transient)) return ProbeOutcome.Transient;
            return ProbeOutcome.Ok;
        }
    }

    /// <summary>
    /// Dò 1 khoá qua HttpMessageInvoker (tool dùng SocketsHttpHandler chặn IP nội bộ; test dùng handler giả).
    /// Handler phải tắt AllowAutoRedirect — redirect tự theo ở đây để kiểm từng hop.
    /// </summary>
    public sealed class LinkProber
    {
        readonly HttpMessageInvoker _http;
        readonly ConcurrentDictionary<string, SemaphoreSlim> _hosts = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        public int PerHost = 2;
        public TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);
        public TimeSpan DownloadTimeout = TimeSpan.FromMinutes(20);
        /// <summary>Retry-After dài hơn mức này => không chờ, ghi lỗi tạm luôn.</summary>
        public TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);
        public TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
        /// <summary>File lớn hơn mức này thì bỏ qua băm lượt này (Skipped, không tính đạt / lỗi). Mặc định 2 GB.</summary>
        public long MaxFileBytes = LinkProbeLogic.DefaultMaxBytes;
        public string UserAgent = "QA98-CatalogWatch/1";
        /// <summary>GITHUB_TOKEN tự có của Actions (tuỳ chọn): chỉ gửi tới api.github.com để nâng hạn mức liệt kê manifest.</summary>
        public string GithubToken = "";

        public LinkProber(HttpMessageInvoker http) { _http = http; }

        sealed class Head
        {
            public ProbeOutcome? Fail; public string Reason = "", Note = "", Fingerprint = "";
            public long? Total; public bool TooBig;
        }

        sealed class Source { public string Url = "", Sha256 = ""; public long Size; public Head H; }

        public async Task<ProbeResult> ProbeAsync(IReadOnlyList<ProbeTarget> group, VariantState state, DateTime nowUtc, CancellationToken ct)
        {
            var res = new ProbeResult();
            var perTarget = new List<(ProbeTarget T, List<Source> Alive, ProbeOutcome? Fail, string Reason, string Note)>();
            foreach (var t in group)
            {
                var sources = new List<Source>();
                if (t.IsWinget)
                {
                    string murl = LinkProbeLogic.WingetManifestUrl(t.ManifestId, t.Version);
                    res.Tried.Add(LinkProbeLogic.PublicUrl(murl));
                    var (yaml, mf, mr) = await GetTextAsync(murl, ct);
                    if (mf != null) { perTarget.Add((t, null, mf, mr == "HTTP 404" ? "winget manifest missing" : "winget manifest " + mr, murl)); continue; }
                    var inst = LinkProbeLogic.PickInstallers(LinkProbeLogic.ParseInstallerYaml(yaml), t.Arch);
                    if (inst.Count == 0) { perTarget.Add((t, null, ProbeOutcome.Broken, "winget manifest has no installers", murl)); continue; }
                    sources.AddRange(inst.Select(i => new Source { Url = i.Url, Sha256 = i.Sha256 }));
                }
                else
                {
                    sources.Add(new Source { Url = t.Url, Sha256 = t.Sha256, Size = t.SizeBytes });
                    sources.AddRange(t.Fallbacks.Select(f => new Source { Url = f, Sha256 = t.Sha256, Size = t.SizeBytes }));
                }
                foreach (var s in sources) { res.Tried.Add(LinkProbeLogic.PublicUrl(s.Url)); s.H = await HeadAsync(s.Url, s.Size, ct); }
                var alive = sources.Where(s => s.H.Fail == null).ToList();
                if (alive.Count > 0) { perTarget.Add((t, alive, null, "", "")); continue; }
                var pick = sources.FirstOrDefault(s => s.H.Fail == ProbeOutcome.Transient) ?? sources[0];   // nguồn thay thế: còn lỗi tạm thì chưa kết luận hỏng
                perTarget.Add((t, null, pick.H.Fail, pick.H.Reason, LinkProbeLogic.PublicUrl(pick.Url) + " " + pick.H.Note));
            }

            res.Fingerprint = string.Join(";", perTarget.Where(p => p.Alive != null).SelectMany(p => p.Alive).Select(s => s.H.Fingerprint));
            bool full = perTarget.All(p => p.Alive != null) && LinkHealthLogic.NeedsFullHash(state, res.Fingerprint, nowUtc);
            var done = new List<(ProbeOutcome O, string Reason, string Note)>();
            bool skipped = false;
            foreach (var p in perTarget)
            {
                if (p.Alive == null) { done.Add((p.Fail.Value, p.Reason, p.Note)); continue; }
                if (!full) { done.Add((ProbeOutcome.Ok, "", "")); continue; }
                (ProbeOutcome O, string Reason, string Note)? best = null;
                foreach (var s in p.Alive.Where(s => !s.H.TooBig))
                {
                    var (o, r, n) = await HashAsync(s.Url, s.Sha256, s.Size, ct);
                    if (o == null) continue;   // vượt giới hạn tải thử
                    if (o == ProbeOutcome.Ok) { best = (o.Value, "", ""); break; }
                    if (best == null || best.Value.O != ProbeOutcome.Transient) best = (o.Value, r, LinkProbeLogic.PublicUrl(s.Url) + " " + n);
                }
                if (best == null) { skipped = true; continue; }
                done.Add(best.Value);
            }
            res.Outcome = LinkProbeLogic.Worst(done.Select(d => d.O));
            res.Skipped = skipped && res.Outcome == ProbeOutcome.Ok;
            res.Hashed = full && !skipped && res.Outcome == ProbeOutcome.Ok;
            if (res.Outcome != ProbeOutcome.Ok)
            {
                var first = done.First(d => d.O == res.Outcome);
                res.Reason = first.Reason; res.Note = first.Note.Trim();
            }
            else if (res.Skipped) res.Note = "file larger than download limit for this run";
            return res;
        }

        /// <summary>
        /// Tìm nguồn thay thế qua winget-pkgs cho khoá Broken / HashMismatch:
        /// cùng bản có installer cùng SHA-256 khác URL (tải kiểm lại) => same_artifact; cùng bản khác hash => needs_review;
        /// bản đã duyệt mất khỏi winget-pkgs nhưng có bản MỚI HƠN => new_artifact (không tải, chờ duyệt); cả thư mục gói mất => retire_candidate.
        /// </summary>
        public async Task<Discovery> DiscoverAsync(ProbeTarget t, CancellationToken ct)
        {
            var d = new Discovery();
            if (!SoftwareCatalogParser.IsSafeWingetId(t.ManifestId) || t.Version.Length == 0) return d;
            string murl = LinkProbeLogic.WingetManifestUrl(t.ManifestId, t.Version);
            var (yaml, fail, _) = await GetTextAsync(murl, ct);
            if (fail == null)
            {
                var inst = LinkProbeLogic.PickInstallers(LinkProbeLogic.ParseInstallerYaml(yaml), t.Arch);
                d.ManifestUrl = murl;
                if (t.IsWinget || t.Sha256.Length != 64) { d.Kind = "needs_review"; d.Note = "winget manifest exists but installer does not serve the expected file"; return d; }
                foreach (var i in inst.Where(i => string.Equals(i.Sha256, t.Sha256, StringComparison.OrdinalIgnoreCase) && LinkProbeLogic.PublicUrl(i.Url) != LinkProbeLogic.PublicUrl(t.Url)))
                {
                    var (o, _, _) = await HashAsync(i.Url, t.Sha256, t.SizeBytes, ct);
                    if (o != ProbeOutcome.Ok) continue;
                    d.Kind = "same_artifact"; d.CandidateVersion = t.Version; d.CandidateUrl = LinkProbeLogic.PublicUrl(i.Url); d.CandidateSha256 = t.Sha256;
                    d.Note = "same sha256 verified by download" + (LinkProbeLogic.PublicUrl(i.Url) != i.Url ? "; query removed from url" : "");
                    return d;
                }
                if (inst.Count > 0 && inst.All(i => !string.Equals(i.Sha256, t.Sha256, StringComparison.OrdinalIgnoreCase)))
                { d.Kind = "needs_review"; d.Note = "winget has same version with different sha256"; d.CandidateSha256 = inst[0].Sha256; d.CandidateUrl = LinkProbeLogic.PublicUrl(inst[0].Url); }
                return d;
            }
            if (fail != ProbeOutcome.Broken) return d;   // lỗi tạm: chưa kết luận
            string api = LinkProbeLogic.WingetDirApiUrl(t.ManifestId);
            var (json, lf, lr) = await GetTextAsync(api, ct);
            if (lf == ProbeOutcome.Broken && lr == "HTTP 404") { d.Kind = "retire_candidate"; d.Note = "package folder missing from winget-pkgs"; d.ManifestUrl = api; return d; }
            if (lf != null) return d;
            var newer = LinkProbeLogic.ParseGithubDirListing(json).Where(v => LinkProbeLogic.CompareVersions(v.Name, t.Version) > 0)
                .OrderByDescending(v => v.Name, Comparer<string>.Create(LinkProbeLogic.CompareVersions)).FirstOrDefault();
            if (newer.Name == null) { d.Kind = "needs_review"; d.Note = "approved version missing from winget-pkgs, no newer version"; return d; }
            string nurl = LinkProbeLogic.WingetManifestUrl(t.ManifestId, newer.Name);
            var (ny, nf, _) = await GetTextAsync(nurl, ct);
            var ni = nf == null ? LinkProbeLogic.PickInstallers(LinkProbeLogic.ParseInstallerYaml(ny), t.Arch) : new List<WingetInstaller>();
            d.Kind = "new_artifact"; d.CandidateVersion = newer.Name; d.ManifestUrl = nurl; d.ManifestRef = newer.Path + "@" + newer.Sha;
            if (ni.Count > 0) { d.CandidateUrl = LinkProbeLogic.PublicUrl(ni[0].Url); d.CandidateSha256 = ni[0].Sha256; }
            d.Note = "approved version missing from winget-pkgs; newer manifest found (needs review, not downloaded)";
            return d;
        }

        // ------------------------------------------------------------------ HTTP

        /// <summary>GET (theo redirect tự kiểm từng hop). Lỗi => (null, outcome, reason). Trả response chưa đọc thân (người gọi Dispose).</summary>
        async Task<(HttpResponseMessage R, ProbeOutcome? Fail, string Reason)> SendAsync(string url, bool range, CancellationToken ct)
        {
            for (int hop = 0; hop <= LinkProbeLogic.MaxRedirects; hop++)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(u.UserInfo))
                    return (null, ProbeOutcome.Broken, hop == 0 ? "url is not https" : "redirect to non-https");
                if ((u.HostNameType == UriHostNameType.IPv4 || u.HostNameType == UriHostNameType.IPv6) && LinkProbeLogic.IsBlockedAddress(IPAddress.Parse(u.DnsSafeHost)))
                    return (null, ProbeOutcome.Broken, "private address");
                var req = new HttpRequestMessage(HttpMethod.Get, u);
                req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                if (u.Host == "api.github.com")
                {
                    req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                    if (GithubToken.Length > 0) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + GithubToken);
                }
                if (range) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, LinkProbeLogic.SniffBytes - 1);
                HttpResponseMessage r;
                try { r = await _http.SendAsync(req, ct); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (null, ProbeOutcome.Transient, "timeout"); }
                catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException || ex.InnerException?.InnerException is BlockedAddressException)
                { return (null, ProbeOutcome.Broken, "private address"); }
                catch (HttpRequestException ex) { return (null, ProbeOutcome.Transient, "network error: " + ex.Message); }
                int code = (int)r.StatusCode;
                if (code >= 300 && code < 400 && r.Headers.Location != null)
                {
                    url = new Uri(u, r.Headers.Location).ToString();
                    r.Dispose();
                    continue;
                }
                return (r, null, "");
            }
            return (null, ProbeOutcome.Broken, "too many redirects");
        }

        /// <summary>Gửi có thử lại: lỗi tạm 1 lần (tôn trọng Retry-After), 401/403 thử lại 1 lần rồi mới coi là hỏng.</summary>
        async Task<(HttpResponseMessage R, ProbeOutcome? Fail, string Reason)> SendRetryAsync(string url, bool range, CancellationToken ct)
        {
            (HttpResponseMessage R, ProbeOutcome? Fail, string Reason) last = default;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                last = await SendAsync(url, range, ct);
                if (last.R == null && last.Fail != ProbeOutcome.Transient) return last;
                int code = last.R == null ? 0 : (int)last.R.StatusCode;
                var cls = last.R == null ? last.Fail : LinkProbeLogic.ClassifyStatus(code);
                if (cls == null || cls == ProbeOutcome.Broken && code != 401 && code != 403) return last;
                if (attempt == 1) break;
                TimeSpan wait = RetryDelay;
                var ra = last.R?.Headers.RetryAfter;
                if (ra != null)
                {
                    var d = ra.Delta ?? (ra.Date.HasValue ? ra.Date.Value - DateTimeOffset.UtcNow : TimeSpan.Zero);
                    if (d > MaxRetryWait) break;   // máy chủ bảo chờ lâu => để lượt sau
                    if (d > wait) wait = d;
                }
                last.R?.Dispose();
                await Task.Delay(wait, ct);
            }
            return last;
        }

        static string StatusReason(HttpResponseMessage r)
        {
            int code = (int)r.StatusCode;
            return code == 401 || code == 403 ? $"HTTP {code} (repeated)" : "HTTP " + code.ToString(CultureInfo.InvariantCulture);
        }

        SemaphoreSlim Gate(string url) => _hosts.GetOrAdd(Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "", _ => new SemaphoreSlim(PerHost));

        async Task<(string Text, ProbeOutcome? Fail, string Reason)> GetTextAsync(string url, CancellationToken ct)
        {
            var gate = Gate(url);
            await gate.WaitAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HeaderTimeout);
            try
            {
                var (r, fail, reason) = await SendRetryAsync(url, false, cts.Token);
                if (r == null) return (null, fail, reason);
                using (r)
                {
                    var cls = LinkProbeLogic.ClassifyStatus((int)r.StatusCode);
                    if (cls != null) return (null, cls, StatusReason(r));
                    using var s = await r.Content.ReadAsStreamAsync(cts.Token);
                    var buf = new byte[LinkProbeLogic.MaxManifestBytes + 1];
                    int n = 0, k;
                    while (n < buf.Length && (k = await s.ReadAsync(buf.AsMemory(n), cts.Token)) > 0) n += k;
                    if (n > LinkProbeLogic.MaxManifestBytes) return (null, ProbeOutcome.Broken, "manifest too large");
                    return (Encoding.UTF8.GetString(buf, 0, n), null, "");
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (null, ProbeOutcome.Transient, "timeout"); }
            catch (Exception ex) when (ex is HttpRequestException || ex is System.IO.IOException) { return (null, ProbeOutcome.Transient, "network error: " + ex.Message); }
            finally { gate.Release(); }
        }

        /// <summary>GET Range 0-63: mã HTTP, kiểu nội dung, 64 byte đầu, dung lượng tổng, dấu vân tay.</summary>
        async Task<Head> HeadAsync(string url, long size, CancellationToken ct)
        {
            var h = new Head();
            var gate = Gate(url);
            await gate.WaitAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HeaderTimeout);
            try
            {
                var (r, fail, reason) = await SendRetryAsync(url, true, cts.Token);
                if (r == null) { h.Fail = fail; h.Reason = reason; return h; }
                using (r)
                {
                    int code = (int)r.StatusCode;
                    var cls = LinkProbeLogic.ClassifyStatus(code);
                    if (cls != null) { h.Fail = cls; h.Reason = StatusReason(r); return h; }
                    var ch = r.Content.Headers;
                    if (LinkProbeLogic.IsHtmlContentType(ch.ContentType?.MediaType)) { h.Fail = ProbeOutcome.Broken; h.Reason = "HTML page"; h.Note = "content-type " + ch.ContentType.MediaType; return h; }
                    h.Total = code == 206 ? ch.ContentRange?.Length : ch.ContentLength;
                    var buf = new byte[LinkProbeLogic.SniffBytes];
                    int n = 0, k;
                    using (var s = await r.Content.ReadAsStreamAsync(cts.Token))
                        while (n < buf.Length && (k = await s.ReadAsync(buf.AsMemory(n), cts.Token)) > 0) n += k;
                    if (LinkProbeLogic.LooksLikeHtml(buf, n)) { h.Fail = ProbeOutcome.Broken; h.Reason = "HTML page"; return h; }
                    if (LinkProbeLogic.SizeMismatch(size, h.Total))
                    { h.Fail = ProbeOutcome.HashMismatch; h.Reason = "size mismatch"; h.Note = $"expected {size} got {h.Total}"; return h; }
                    h.Fingerprint = LinkProbeLogic.Fingerprint(r.Headers.ETag?.Tag, ch.LastModified?.ToString("R", CultureInfo.InvariantCulture), h.Total);
                    h.TooBig = (h.Total ?? size) > MaxFileBytes;
                    return h;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { h.Fail = ProbeOutcome.Transient; h.Reason = "timeout"; return h; }
            catch (Exception ex) when (ex is HttpRequestException || ex is System.IO.IOException) { h.Fail = ProbeOutcome.Transient; h.Reason = "network error"; h.Note = ex.Message; return h; }
            finally { gate.Release(); }
        }

        /// <summary>Tải cả file vào luồng băm SHA-256 (không ghi đĩa), dừng khi vượt giới hạn byte. O = null: không có size_bytes và vượt MaxFileBytes (bỏ qua lượt này).</summary>
        async Task<(ProbeOutcome? O, string Reason, string Note)> HashAsync(string url, string sha256, long size, CancellationToken ct)
        {
            var gate = Gate(url);
            await gate.WaitAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DownloadTimeout);
            try
            {
                var (r, fail, reason) = await SendRetryAsync(url, false, cts.Token);
                if (r == null) return (fail.Value, reason, "");
                using (r)
                {
                    var cls = LinkProbeLogic.ClassifyStatus((int)r.StatusCode);
                    if (cls != null) return (cls.Value, StatusReason(r), "");
                    long limit = size > 0 ? LinkProbeLogic.ByteLimit(size) : Math.Min(MaxFileBytes, LinkProbeLogic.DefaultMaxBytes);
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buf = new byte[81920];
                    long total = 0; int k;
                    using (var s = await r.Content.ReadAsStreamAsync(cts.Token))
                        while ((k = await s.ReadAsync(buf, cts.Token)) > 0)
                        {
                            total += k;
                            if (total > limit) return size > 0 ? (ProbeOutcome.HashMismatch, "size mismatch", $"more than {size} bytes") : (null, "", "");
                            sha.AppendData(buf, 0, k);
                        }
                    if (LinkProbeLogic.SizeMismatch(size, total)) return (ProbeOutcome.HashMismatch, "size mismatch", $"expected {size} got {total}");
                    string got = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                    if (sha256.Length == 64 && !string.Equals(got, sha256, StringComparison.OrdinalIgnoreCase))
                        return (ProbeOutcome.HashMismatch, "sha256 mismatch", $"expected {sha256} got {got}");
                    if (sha256.Length != 64) return (ProbeOutcome.Broken, "no sha256 to compare", "");
                    return (ProbeOutcome.Ok, "", "");
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (ProbeOutcome.Transient, "timeout", ""); }
            catch (Exception ex) when (ex is HttpRequestException || ex is System.IO.IOException) { return (ProbeOutcome.Transient, "network error", ex.Message); }
            finally { gate.Release(); }
        }
    }
}
