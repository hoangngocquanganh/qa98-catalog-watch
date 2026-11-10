using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SysInfoWpf.Services.SoftwareCenter;
using SysInfoWpf.Services.SoftwareCenter.Health;

namespace CatalogWatch
{
    /// <summary>Một dòng status.json "heals": link thay thế đã tải + kiểm (Authenticode Valid, cùng người ký với bản tham chiếu). Dữ liệu KHÔNG tin cậy: server tự kiểm lại.</summary>
    sealed class HealEntry
    {
        [JsonPropertyName("vkey")] public string VKey { get; set; } = "";
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        [JsonPropertyName("winget_id")] public string WingetId { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        /// <summary>same_url_new_hash / https_upgrade / browser_headers / strip_token / sourceforge_stable / versionless_name / github_latest / github_asset / html_link / winget_newer</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("signer_subject")] public string SignerSubject { get; set; } = "";
        [JsonPropertyName("signer_thumbprint")] public string SignerThumbprint { get; set; } = "";
        [JsonPropertyName("final_host")] public string FinalHost { get; set; } = "";
        [JsonPropertyName("evidence_vi")] public string EvidenceVi { get; set; } = "";
        /// <summary>Link gãy được thay (manifest nhiều installer: biết thay installer nào).</summary>
        [JsonPropertyName("from_url")] public string FromUrl { get; set; } = "";
        /// <summary>Phiên bản winget-pkgs của link mới (kind winget_newer: khác version).</summary>
        [JsonPropertyName("candidate_version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string CandidateVersion { get; set; }
        [JsonPropertyName("checked_at")] public DateTime CheckedUtc { get; set; }
    }

    sealed class HealBlocked
    {
        [JsonPropertyName("vkey")] public string VKey { get; set; } = "";
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("reason_vi")] public string ReasonVi { get; set; } = "";
    }

    /// <summary>Chữ ký tham chiếu của một gói (lấy từ tệp có SHA-256 khớp nguồn đã duyệt). Subject rỗng = tệp gốc không ký.</summary>
    sealed class HealAnchor
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("subject")] public string Subject { get; set; } = "";
        [JsonPropertyName("thumbprint")] public string Thumbprint { get; set; } = "";
        /// <summary>pin / harvest / winget</summary>
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("at")] public DateTime At { get; set; }
    }

    sealed class HealState
    {
        [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
        [JsonPropertyName("updated_at")] public DateTime UpdatedUtc { get; set; }
        [JsonPropertyName("last_run")] public Dictionary<string, object> LastRun { get; set; } = new();
        [JsonPropertyName("heals")] public List<HealEntry> Heals { get; set; } = new();
        [JsonPropertyName("heal_blocked")] public List<HealBlocked> HealBlocked { get; set; } = new();
        [JsonPropertyName("anchors")] public List<HealAnchor> Anchors { get; set; } = new();
    }

    /// <summary>Một ứng viên link thay thế. WingetSha có = SHA-256 do winget-pkgs công bố cho link này (tệp tải về phải trùng).</summary>
    sealed class HealCand
    {
        public string Url = "", Kind = "", WingetSha = "", WingetVersion = "";
        public bool Browser;
        public override string ToString() => Kind + " " + Url;
    }

    /// <summary>Phần thuần (không mạng) của bộ tự chữa: sinh ứng viên theo ma trận Docs/LINK-HEAL-MATRIX.md + quy tắc an toàn.</summary>
    static class HealLogic
    {
        public const string BrowserUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
        public const double SizeTolerance = 0.5;
        static readonly Regex VersionInName = new Regex(@"[-_. ]?v?\d+(?:[._]\d+)+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex SfMirror = new Regex(@"^(?:[a-z0-9-]+\.dl|downloads|master\.dl)\.sourceforge\.net$", RegexOptions.CultureInvariant);
        static readonly Regex GhDownload = new Regex(@"^/([^/]+)/([^/]+)/releases/download/([^/]+)/([^/]+)$", RegexOptions.CultureInvariant);
        static readonly Regex TokenQuery = new Regex(@"(?:^|&)(?:token|expires?|signature|sig|x-amz-[a-z-]+|st|e|hdnts|auth|key|policy)=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly string[] InstallerExt = { ".exe", ".msi", ".zip", ".msix", ".appx" };

        /// <summary>Lỗi tạm (timeout / mạng / 408 / 429 / 5xx): không bao giờ tự chữa, chỉ thử lại.</summary>
        public static bool IsTransientReason(string reason)
        {
            reason ??= "";
            if (reason.StartsWith("timeout", StringComparison.Ordinal) || reason.StartsWith("network error", StringComparison.Ordinal) || reason == "bot error") return true;
            var m = Regex.Match(reason, @"^HTTP (\d{3})");
            return m.Success && int.Parse(m.Groups[1].Value) is 408 or 429 or >= 500;
        }

        /// <summary>Thứ tự chiến lược theo lý do gãy (ma trận). Ứng viên winget-pkgs luôn có cho gói WINGET.</summary>
        public static List<string> Strategies(string reason, string url, bool winget)
        {
            reason ??= "";
            var s = new List<string>();
            if (IsTransientReason(reason)) return s;
            Uri.TryCreate(url ?? "", UriKind.Absolute, out var u);
            bool mismatch = reason.StartsWith("sha256 mismatch", StringComparison.Ordinal) || reason.StartsWith("size mismatch", StringComparison.Ordinal);
            bool gone = Regex.IsMatch(reason, @"^HTTP (404|410)");
            bool denied = Regex.IsMatch(reason, @"^HTTP (401|403)");
            if (u != null && u.Scheme == "http") s.Add("https_upgrade");
            if (mismatch) s.Add("same_url_new_hash");
            if (u != null && u.Query.Length > 1 && (gone || denied || TokenQuery.IsMatch(u.Query.TrimStart('?')))) s.Add("strip_token");
            if (u != null && SfMirror.IsMatch(u.Host) || u != null && u.Host == "sourceforge.net") s.Add("sourceforge_stable");
            if (denied || reason == "HTML page") s.Add("browser_headers");
            if (u != null && u.Host == "github.com" && GhDownload.IsMatch(u.AbsolutePath)) { s.Add("github_latest"); s.Add("github_asset"); }
            if (gone) s.Add("versionless_name");
            if (winget && (gone || denied || reason == "HTML page" || reason == "redirect to non-https" || reason == "url is not https")) s.Add("publisher_page");
            if (winget) s.Add("winget_newer");
            return s;
        }

        public static string HttpsOf(string url) => url != null && url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + url.Substring(7) : null;

        public static string StripToken(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Query.Length > 1 ? u.GetLeftPart(UriPartial.Path) : null;

        /// <summary>Link SourceForge ổn định: https://downloads.sourceforge.net/project/{dự án}/{đường dẫn} (server tự chuyển sang mirror ngẫu nhiên *.dl.sourceforge.net).</summary>
        public static string SourceForgeStable(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
            string path = u.AbsolutePath;
            if (u.Host == "sourceforge.net")
            {
                var m = Regex.Match(path, @"^/projects/([^/]+)/files/(.+?)(?:/download)?$");
                if (!m.Success) return null;
                path = "/project/" + m.Groups[1].Value + "/" + m.Groups[2].Value;
            }
            else if (!SfMirror.IsMatch(u.Host) || !path.StartsWith("/project/", StringComparison.Ordinal)) return null;
            string r = "https://downloads.sourceforge.net" + path;
            return r == url ? null : r;
        }

        /// <summary>Tên tệp bỏ số phiên bản (link "latest" cố định hay gặp): aimp_5.10.2418_w64.exe => aimp_w64.exe.</summary>
        public static string Versionless(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
            string path = u.AbsolutePath;
            int slash = path.LastIndexOf('/');
            string name = path.Substring(slash + 1), ext = Path.GetExtension(name);
            string stem = name.Substring(0, name.Length - ext.Length);
            string nv = VersionInName.Replace(stem, "").Trim('-', '_', '.', ' ');
            if (nv.Length == 0 || nv == stem) return null;
            return u.GetLeftPart(UriPartial.Authority) + path.Substring(0, slash + 1) + nv + ext;
        }

        /// <summary>github.com/{o}/{r}/releases/download/{tag}/{tệp} => (o, r, tag, tệp); không phải => null.</summary>
        public static (string Owner, string Repo, string Tag, string File)? GithubRelease(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Host != "github.com") return null;
            var m = GhDownload.Match(u.AbsolutePath);
            return m.Success ? (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, Uri.UnescapeDataString(m.Groups[4].Value)) : null;
        }

        public static string GithubLatest(string url)
        {
            var g = GithubRelease(url);
            if (g == null || g.Value.Tag == "latest") return null;
            return $"https://github.com/{g.Value.Owner}/{g.Value.Repo}/releases/latest/download/{Uri.EscapeDataString(g.Value.File)}";
        }

        /// <summary>Asset cùng mẫu tên: trùng tên, không có thì tên khớp khi thay mọi cụm số phiên bản bằng [\d.]+ (đổi tên theo bản).</summary>
        public static string MatchAsset(string file, IEnumerable<(string Name, string Url)> assets)
        {
            var l = assets.ToList();
            var exact = l.FirstOrDefault(a => string.Equals(a.Name, file, StringComparison.OrdinalIgnoreCase));
            if (exact.Url != null) return exact.Url;
            string pat = "^" + Regex.Replace(Regex.Escape(file), @"\d+(?:\\\.\d+)*", @"[\d.]+") + "$";
            var hit = l.Where(a => Regex.IsMatch(a.Name, pat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToList();
            return hit.Count == 1 ? hit[0].Url : null;   // nhiều asset cùng mẫu (x86/x64…) => không đoán
        }

        public static List<(string Name, string Url)> ParseReleaseAssets(string json)
        {
            var r = new List<(string, string)>();
            try
            {
                using var doc = JsonDocument.Parse(json ?? "");
                if (!doc.RootElement.TryGetProperty("assets", out var arr) || arr.ValueKind != JsonValueKind.Array) return r;
                foreach (var e in arr.EnumerateArray())
                    if (e.TryGetProperty("name", out var n) && e.TryGetProperty("browser_download_url", out var d) && n.ValueKind == JsonValueKind.String && d.ValueKind == JsonValueKind.String)
                        r.Add((n.GetString(), d.GetString()));
            }
            catch (JsonException) { }
            return r;
        }

        /// <summary>
        /// Link tải trong trang HTML trung gian: meta refresh, chuyển hướng JS (location = / location.replace), href / src / data-url trỏ tới tệp cài.
        /// Trùng tên tệp cũ xếp trước, rồi cùng đuôi. Chỉ trả https, tối đa 4.
        /// </summary>
        public static List<string> HtmlLinks(string html, string baseUrl, string wantFile)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(html) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var b)) return found;
            void Add(string raw)
            {
                raw = System.Net.WebUtility.HtmlDecode((raw ?? "").Trim().Trim('\'', '"'));
                if (raw.Length == 0 || !Uri.TryCreate(b, raw, out var u)) return;
                if (u.Scheme == "http") u = new Uri("https://" + u.ToString().Substring(7));
                if (u.Scheme == "https" && !found.Contains(u.ToString())) found.Add(u.ToString());
            }
            foreach (Match m in Regex.Matches(html, @"<meta[^>]+http-equiv\s*=\s*[""']?refresh[^>]*content\s*=\s*[""'][^""']*?url\s*=\s*([^""'>\s]+)", RegexOptions.IgnoreCase)) Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(html, @"(?:window\.|document\.)?location(?:\.href)?\s*=\s*([""'][^""']+[""'])|location\.replace\(\s*([""'][^""']+[""'])", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            int redirects = found.Count;
            var links = new List<string>();
            foreach (Match m in Regex.Matches(html, @"(?:href|src|data-url|data-href)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase))
            {
                string v = m.Groups[1].Value;
                string path = v.Split('?', '#')[0];
                if (InstallerExt.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))) links.Add(v);
            }
            string wantExt = Path.GetExtension(wantFile ?? "");
            foreach (var v in links.OrderBy(v => string.Equals(Path.GetFileName(v.Split('?', '#')[0]), wantFile, StringComparison.OrdinalIgnoreCase) ? 0
                                              : wantExt.Length > 0 && v.Split('?', '#')[0].EndsWith(wantExt, StringComparison.OrdinalIgnoreCase) ? 1 : 2))
                Add(v);
            return found.Take(Math.Max(4, redirects)).ToList();
        }

        /// <summary>PackageUrl / PublisherUrl (https) trong manifest locale winget — nơi tìm link mới khi nhà phát hành đổi domain / đổi đường dẫn.</summary>
        public static List<string> PublisherUrls(string yaml)
        {
            var r = new List<string>();
            foreach (Match m in Regex.Matches(yaml ?? "", @"^(?:PackageUrl|PublisherUrl):\s*['""]?(https?://[^'""\s#]+)", RegexOptions.Multiline))
            { string u = HttpsOf(m.Groups[1].Value) ?? m.Groups[1].Value; if (!r.Contains(u)) r.Add(u); }
            return r;
        }

        /// <summary>Loại tệp theo byte đầu: exe (MZ) / msi (OLE) / zip (PK) / html / "".</summary>
        public static string Magic(byte[] head, int n)
        {
            if (n >= 2 && head[0] == 'M' && head[1] == 'Z') return "exe";
            if (n >= 8 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0 && head[4] == 0xA1 && head[5] == 0xB1 && head[6] == 0x1A && head[7] == 0xE1) return "msi";
            if (n >= 4 && head[0] == 'P' && head[1] == 'K' && head[2] == 3 && head[3] == 4) return "zip";
            if (LinkProbeLogic.LooksLikeHtml(head, n)) return "html";
            return "";
        }

        /// <summary>Loại tệp phải có: theo InstallerType winget (msi/wix => msi; zip/msix/appx => zip; còn lại exe), không có thì theo đuôi URL.</summary>
        public static string ExpectKind(string installerType, string url)
        {
            switch ((installerType ?? "").ToLowerInvariant())
            {
                case "msi": case "wix": return "msi";
                case "zip": case "msix": case "appx": return "zip";
                case "exe": case "inno": case "nullsoft": case "burn": case "portable": return "exe";
            }
            string p = (url ?? "").Split('?', '#')[0].ToLowerInvariant();
            return p.EndsWith(".msi") ? "msi" : p.EndsWith(".zip") || p.EndsWith(".msix") || p.EndsWith(".appx") ? "zip" : p.EndsWith(".exe") ? "exe" : "";
        }

        static string NormSubject(string s) => Regex.Replace((s ?? "").Trim(), @"\s*,\s*", ", ").ToLowerInvariant();

        /// <summary>"thumbprint" = cùng chứng chỉ; "subject" = cùng chủ thể (chứng chỉ gia hạn); "" = khác người ký.</summary>
        public static string SameSigner(string aSubject, string aThumb, string bSubject, string bThumb)
        {
            if ((aThumb ?? "").Length == 40 && string.Equals(aThumb, bThumb, StringComparison.OrdinalIgnoreCase)) return "thumbprint";
            if ((aSubject ?? "").Trim().Length > 0 && NormSubject(aSubject) == NormSubject(bSubject)) return "subject";
            return "";
        }

        public static bool SizeOk(long reference, long size) => reference <= 0 || size >= reference * (1 - SizeTolerance) && size <= reference * (1 + SizeTolerance);

        /// <summary>Kết quả kiểm một ứng viên đã tải.</summary>
        public sealed class Verdict
        {
            public bool Ok;
            /// <summary>true = dấu hiệu nguy hiểm (cùng link nhưng người ký khác): dừng chữa cả gói, chờ chủ xem.</summary>
            public bool Fatal;
            public string Vi = "", Signer = "";
        }

        /// <summary>
        /// Quy tắc an toàn (ma trận, mục "Điều kiện an toàn"): đúng loại tệp; khớp SHA-256 winget nếu link lấy từ manifest;
        /// Authenticode Valid; người ký trùng bản tham chiếu (không có tham chiếu thì chỉ nhận tệp có SHA-256 trùng manifest winget-pkgs);
        /// kích thước ±50 % bản tham chiếu (trừ tệp đã trùng SHA-256 winget-pkgs).
        /// </summary>
        public static Verdict Judge(HealCand c, string expectKind, string magic, string sha, long size, string sigStatus, string subject, string thumb, HealAnchor anchor)
        {
            var v = new Verdict();
            bool wingetOk = c.WingetSha.Length == 64 && string.Equals(c.WingetSha, sha, StringComparison.OrdinalIgnoreCase);
            if (magic == "html") { v.Vi = "link trả trang HTML, không phải bộ cài"; return v; }
            if (expectKind.Length > 0 && magic != expectKind) { v.Vi = $"loại tệp khác (cần {expectKind}, nhận {(magic.Length > 0 ? magic : "không rõ")})"; return v; }
            if (c.WingetSha.Length == 64 && !wingetOk) { v.Vi = "SHA-256 không trùng manifest winget-pkgs của link này"; return v; }
            if (sigStatus != "Valid" || subject.Length == 0)
            {
                v.Vi = expectKind == "zip" ? "tệp zip không có chữ ký Authenticode để kiểm người phát hành" : $"tệp không có chữ ký Authenticode hợp lệ ({(sigStatus.Length > 0 ? sigStatus : "không đọc được")})";
                return v;
            }
            if (anchor == null)
            {
                if (!wingetOk) { v.Vi = $"chưa có chữ ký tham chiếu của gói (bản tốt đã ghim) để đối chiếu; tệp mới ký bởi \"{subject}\""; return v; }
                v.Ok = true; v.Signer = "winget"; return v;
            }
            if (anchor.Subject.Length == 0) { v.Vi = "bản gốc đã duyệt không ký Authenticode nên không kiểm được người ký liên tục"; return v; }
            v.Signer = SameSigner(anchor.Subject, anchor.Thumbprint, subject, thumb);
            if (v.Signer.Length == 0)
            {
                v.Fatal = c.Kind is "same_url_new_hash" or "https_upgrade" or "browser_headers" or "strip_token";
                v.Vi = $"NGƯỜI KÝ ĐỔI: bản tham chiếu \"{anchor.Subject}\", tệp mới \"{subject}\"";
                return v;
            }
            if (!wingetOk && !SizeOk(anchor.Size, size)) { v.Vi = $"kích thước bất thường ({Mb(size)}, bản tham chiếu {Mb(anchor.Size)}, lệch quá ±50 %)"; return v; }
            v.Ok = true;
            return v;
        }

        public static string Mb(long b) => (b / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";

        public static string KindVi(string k) => k switch
        {
            "same_url_new_hash" => "nhà phát hành thay tệp tại chỗ (cùng link, SHA-256 mới)",
            "https_upgrade" => "đổi http sang https",
            "browser_headers" => "máy chủ chặn bot: tải được với User-Agent trình duyệt + Referer trang chủ",
            "strip_token" => "bỏ token / tham số hết hạn trong link",
            "sourceforge_stable" => "dùng link SourceForge ổn định (downloads.sourceforge.net, mirror ngẫu nhiên *.dl.sourceforge.net)",
            "versionless_name" => "link cố định không ghi số phiên bản",
            "github_latest" => "GitHub releases/latest cùng tên tệp",
            "github_asset" => "asset cùng mẫu tên ở release mới nhất trên GitHub",
            "html_link" => "link tải lấy từ trang HTML trung gian",
            "winget_newer" => "installer của manifest winget-pkgs bản mới hơn",
            "publisher_page" => "link cùng tên tệp trên trang chủ nhà phát hành (PackageUrl / PublisherUrl trong winget)",
            _ => k,
        };

        /// <summary>Tự kiểm khi chạy (dự án test không tham chiếu tool). Trả danh sách mục hỏng (tiếng Việt); rỗng = đạt.</summary>
        public static List<string> SelfTest()
        {
            var bad = new List<string>();
            void Check(bool ok, string what) { if (!ok) bad.Add(what); }
            Check(Strategies("timeout", "https://a/b.exe", true).Count == 0 && Strategies("HTTP 503", "https://a/b.exe", true).Count == 0 && Strategies("HTTP 429", "https://a/b.exe", false).Count == 0,
                  "sinh ứng viên: lỗi tạm (timeout / 5xx / 429) không được tự chữa");
            Check(Strategies("sha256 mismatch", "https://a/b.exe", true).SequenceEqual(new[] { "same_url_new_hash", "winget_newer" }), "sinh ứng viên: sai SHA-256 => cùng link hash mới rồi winget mới hơn");
            Check(Strategies("url is not https", "http://a/b.exe", false)[0] == "https_upgrade", "sinh ứng viên: http => https");
            Check(Strategies("HTTP 403 (repeated)", "https://a/b.exe", false).Contains("browser_headers"), "sinh ứng viên: 403 => UA trình duyệt + Referer");
            Check(Strategies("HTML page", "https://a/b.exe", false).Contains("browser_headers"), "sinh ứng viên: trang HTML => thử UA trình duyệt");
            var gh = Strategies("HTTP 404", "https://github.com/o/r/releases/download/v1.2/app-1.2.exe", true);
            Check(gh.Contains("github_latest") && gh.Contains("github_asset") && gh.Contains("versionless_name") && gh.Contains("publisher_page") && gh[^1] == "winget_newer", "sinh ứng viên: 404 GitHub => latest + asset + bỏ số bản + trang chủ + winget");
            Check(PublisherUrls("PackageUrl: https://dolphin-emu.org\nPublisherUrl: 'https://x.org/dl'\n# PrivacyUrl: https://p\nLicenseUrl: https://l\n").SequenceEqual(new[] { "https://dolphin-emu.org", "https://x.org/dl" }),
                  "trang chủ: đọc PackageUrl / PublisherUrl của manifest locale");
            Check(Strategies("HTTP 404", "https://x.dl.sourceforge.net/project/p/a.exe", false).Contains("sourceforge_stable"), "sinh ứng viên: mirror SourceForge => link ổn định");
            Check(Strategies("HTTP 403", "https://cdn/a.exe?token=1&expires=2", false).Contains("strip_token"), "sinh ứng viên: link có token => bỏ query");
            Check(HttpsOf("http://download.deluge-torrent.org/w/d.exe") == "https://download.deluge-torrent.org/w/d.exe" && HttpsOf("https://a/b") == null, "https: đổi http => https");
            Check(SourceForgeStable("https://netix.dl.sourceforge.net/project/sevenzip/7-Zip/23.01/7z2301.exe?viasf=1") == "https://downloads.sourceforge.net/project/sevenzip/7-Zip/23.01/7z2301.exe",
                  "SourceForge: mirror => downloads.sourceforge.net");
            Check(SourceForgeStable("https://sourceforge.net/projects/p/files/a/b.exe/download") == "https://downloads.sourceforge.net/project/p/a/b.exe", "SourceForge: trang /files/.../download => link ổn định");
            Check(Versionless("https://aimp.ru/files/windows/builds/aimp_5.10.2418_w64.exe") == "https://aimp.ru/files/windows/builds/aimp_w64.exe", "bỏ số phiên bản trong tên tệp");
            Check(Versionless("https://a/setup.exe") == null, "bỏ số phiên bản: tên không có số => không sinh");
            Check(GithubLatest("https://github.com/lamquangminh/EVKey/releases/download/Release/EVKey.zip") == "https://github.com/lamquangminh/EVKey/releases/latest/download/EVKey.zip", "GitHub: releases/latest cùng tên tệp");
            var assets = new List<(string, string)> { ("app-2.0.1-x64.exe", "u1"), ("app-2.0.1-arm64.exe", "u2"), ("notes.txt", "u3") };
            Check(MatchAsset("app-1.9-x64.exe", assets) == "u1", "GitHub: asset cùng mẫu tên khi đổi số bản");
            Check(MatchAsset("app.exe", assets) == null, "GitHub: không asset nào cùng mẫu => không đoán");
            Check(ParseReleaseAssets("{\"assets\":[{\"name\":\"a.exe\",\"browser_download_url\":\"https://g/a.exe\"}]}").Count == 1, "GitHub: đọc danh sách asset");
            var html = "<html><head><meta http-equiv=\"refresh\" content=\"0; url=/dl/real.exe\"></head><body><a href=\"http://cdn.x/other.zip\">z</a>" +
                       "<a href='/files/storage-executive-win-64.zip?x=1'>w</a><script>window.location.href = 'https://cdn.x/go.exe';</script></body></html>";
            var hl = HtmlLinks(html, "https://site.x/page", "storage-executive-win-64.zip");
            Check(hl.Count >= 3 && hl[0] == "https://site.x/dl/real.exe" && hl.Contains("https://cdn.x/go.exe"), "HTML: meta refresh + chuyển hướng JS");
            Check(hl.IndexOf("https://site.x/files/storage-executive-win-64.zip?x=1") >= 0 && hl.IndexOf("https://site.x/files/storage-executive-win-64.zip?x=1") < hl.IndexOf("https://cdn.x/other.zip"),
                  "HTML: link trùng tên tệp xếp trước, http đổi sang https");
            Check(Magic(new byte[] { (byte)'M', (byte)'Z', 0, 0 }, 4) == "exe" && Magic(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, 8) == "msi"
                  && Magic(Encoding.ASCII.GetBytes("<!DOCTYPE html>"), 15) == "html", "nhận loại tệp theo byte đầu");
            Check(ExpectKind("nullsoft", "") == "exe" && ExpectKind("wix", "") == "msi" && ExpectKind("", "https://a/b.ZIP") == "zip", "loại tệp cần có theo manifest / đuôi");
            var anchor = new HealAnchor { Subject = "CN=Bandisoft International Inc., O=Bandisoft International Inc., C=KR", Thumbprint = new string('A', 40), Size = 10_000_000 };
            var same = new HealCand { Kind = "same_url_new_hash", Url = "https://a/b.exe" };
            string T2 = new string('B', 40);
            Check(Judge(same, "exe", "exe", "x", 11_000_000, "Valid", anchor.Subject, anchor.Thumbprint, anchor).Ok, "an toàn: hash đổi tại chỗ + cùng thumbprint => nhận");
            Check(Judge(same, "exe", "exe", "x", 11_000_000, "Valid", "CN=Bandisoft International Inc.,O=Bandisoft International Inc.,C=KR", T2, anchor) is { Ok: true, Signer: "subject" }, "an toàn: chứng chỉ gia hạn cùng chủ thể => nhận");
            var evil = Judge(same, "exe", "exe", "x", 11_000_000, "Valid", "CN=Evil Corp", T2, anchor);
            Check(!evil.Ok && evil.Fatal, "an toàn: cùng link nhưng NGƯỜI KÝ ĐỔI => chặn cả gói");
            Check(!Judge(same, "exe", "exe", "x", 11_000_000, "NotSigned", "", "", anchor).Ok, "an toàn: tệp không ký => không chữa");
            Check(!Judge(same, "exe", "exe", "x", 11_000_000, "HashMismatch", anchor.Subject, anchor.Thumbprint, anchor).Ok, "an toàn: chữ ký hỏng => không chữa");
            Check(!Judge(same, "exe", "exe", "x", 4_000_000, "Valid", anchor.Subject, anchor.Thumbprint, anchor).Ok, "an toàn: kích thước lệch quá ±50 % => không chữa");
            Check(!Judge(same, "exe", "msi", "x", 11_000_000, "Valid", anchor.Subject, anchor.Thumbprint, anchor).Ok, "an toàn: loại tệp khác => không chữa");
            Check(!Judge(same, "exe", "exe", "x", 11_000_000, "Valid", anchor.Subject, anchor.Thumbprint, null).Ok, "an toàn: chưa có chữ ký tham chiếu => không chữa");
            var wc = new HealCand { Kind = "winget_newer", Url = "https://a/c.exe", WingetSha = new string('c', 64) };
            Check(Judge(wc, "exe", "exe", new string('c', 64), 1, "Valid", "CN=X", T2, null).Ok, "an toàn: không có tham chiếu nhưng SHA-256 trùng winget-pkgs + ký hợp lệ => nhận");
            Check(!Judge(wc, "exe", "exe", new string('d', 64), 1, "Valid", "CN=X", T2, null).Ok, "an toàn: link winget mà SHA-256 khác manifest => không nhận");
            Check(!Judge(new HealCand { Kind = "html_link", Url = "u" }, "exe", "exe", "x", 11_000_000, "Valid", "CN=Evil", T2, anchor).Fatal, "an toàn: link khác người ký khác => bỏ ứng viên, không chặn cả gói");
            Check(!Judge(same, "exe", "exe", "x", 11_000_000, "Valid", "CN=A", T2, new HealAnchor { Subject = "" }).Ok, "an toàn: bản gốc không ký => không chữa");
            Check(IsTransientReason("network error: x") && !IsTransientReason("HTTP 404") && !IsTransientReason("sha256 mismatch"), "phân loại lỗi tạm");
            return bad;
        }
    }

    /// <summary>
    /// Bước "heal" (chạy sau bước ghim trong job catalog-pin, runner Windows): với mỗi khoá đang gãy (state.json fails &gt; 0), sinh ứng viên link thay thế
    /// theo ma trận, tải (không chạy), kiểm loại tệp + SHA-256 + Authenticode + người ký liên tục + kích thước. Ghi state/heals.json;
    /// catalog-watch chép heals / heal_blocked sang status.json. Không bao giờ sửa catalog: server tự kiểm lại rồi mới áp.
    /// </summary>
    static class Heal
    {
        static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        static readonly TimeSpan KeepHeal = TimeSpan.FromHours(36);

        sealed class Budget { public int Files, MaxFiles; public long Bytes, MaxBytes; }

        sealed class Got
        {
            public bool Ok; public string Err = "", Sha = "", Magic = "", Html, FinalHost = "", SigStatus = "", Subject = "", Thumb = "", SigMsg = "";
            public long Size;
        }

        public static string AnchorId(ProbeTarget t) => t.IsWinget ? "w:" + t.ManifestId.ToLowerInvariant() : "s:" + t.Slug.ToLowerInvariant();

        public static async Task<int> RunAsync(Args a, SoftwareCatalog catalog, HttpMessageInvoker http, int timeoutSec)
        {
            var broken = HealLogic.SelfTest();
            if (broken.Count > 0)
            {
                Program.GhError("Tự kiểm bộ tự chữa link hỏng", "Mã sinh ứng viên / quy tắc an toàn của bot sai nên lượt này KHÔNG tự chữa gì (heals.json cũ giữ nguyên). Cần sửa tool/Heal.cs. Mục hỏng: " + string.Join("; ", broken));
                return 1;
            }
            if (!OperatingSystem.IsWindows()) { Program.GhError("Sai máy chạy", "Bước tự chữa link cần Windows (Get-AuthenticodeSignature); workflow phải dùng runs-on: windows-latest."); return 1; }
            string watchPath = a.Get("state", "state.json"), pinPath = a.Get("pin-state", "state/pins.json"), healPath = a.Get("heal-state", "state/heals.json");
            var budget = new Budget { MaxFiles = a.Int("heal-max", 30), MaxBytes = (long)a.Int("heal-budget-mb", 4096) << 20 };
            long maxFile = (long)a.Int("heal-max-file-mb", 1024) << 20;
            int harvestMax = a.Int("harvest-max", 15);
            long harvestMaxFile = (long)a.Int("harvest-max-file-mb", 300) << 20;
            string ghToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "";
            var now = DateTime.UtcNow;

            WatchState watch; PinState pins; HealState hs;
            try
            {
                watch = File.Exists(watchPath) ? JsonSerializer.Deserialize<WatchState>(File.ReadAllText(watchPath)) ?? new WatchState() : new WatchState();
                pins = File.Exists(pinPath) ? JsonSerializer.Deserialize<PinState>(File.ReadAllText(pinPath)) ?? new PinState() : new PinState();
                hs = File.Exists(healPath) ? JsonSerializer.Deserialize<HealState>(File.ReadAllText(healPath)) ?? new HealState() : new HealState();
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException)
            { Program.GhError("Tệp trạng thái hỏng (bước tự chữa)", "Không đọc được state.json / state/pins.json / state/heals.json nên lượt này không tự chữa (kết quả cũ giữ nguyên). Sửa hoặc xoá state/heals.json rồi chạy lại. Chi tiết: " + ex.Message); return 3; }

            var groups = LinkProbeLogic.Targets(catalog).GroupBy(t => t.Key).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            // tham chiếu: bản ghim (đúng id, mới nhất) đè tham chiếu đã thu thập
            var anchors = new ConcurrentDictionary<string, HealAnchor>((hs.Anchors ?? new()).Where(x => x?.Id != null).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First()), StringComparer.Ordinal);
            foreach (var p in (pins.Entries ?? new()).Where(e => e?.Status == "pinned" && e.SignerSubject.Length > 0).OrderBy(e => e.CheckedUtc))
                anchors["w:" + p.Id.ToLowerInvariant()] = new HealAnchor { Id = "w:" + p.Id.ToLowerInvariant(), Version = p.Version, Sha256 = p.Sha256, Size = p.Size, Subject = p.SignerSubject, Thumbprint = p.SignerThumbprint, Source = "pin", At = p.CheckedUtc };

            var failing = (watch.Variants ?? new()).Where(v => v != null && (v.ConsecutiveFails > 0 || v.Hidden) && groups.ContainsKey(v.Key) && !HealLogic.IsTransientReason(v.LastReason))
                .OrderBy(v => groups[v.Key].Any(t => t.Featured) ? 0 : 1).ThenBy(v => v.Key, StringComparer.Ordinal).ToList();
            var prevHeal = (hs.Heals ?? new()).Where(h => h?.VKey != null).GroupBy(h => h.VKey).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var heals = new ConcurrentBag<HealEntry>();
            var blocked = new ConcurrentBag<HealBlocked>();
            string tmpDir = Path.Combine(Path.GetTempPath(), "qa98heal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            int harvested = 0;
            try
            {
                await Parallel.ForEachAsync(failing, new ParallelOptions { MaxDegreeOfParallelism = 3 }, async (v, ct) =>
                {
                    if (prevHeal.TryGetValue(v.Key, out var ph) && now - ph.CheckedUtc < KeepHeal && ph.Version == v.Version) { heals.Add(ph); Console.WriteLine($"heal {v.Key} => giữ kết quả lượt trước ({ph.Kind})"); return; }
                    var t = groups[v.Key][0];
                    try
                    {
                        var (h, b) = await HealOneAsync(http, t, v, anchors, budget, maxFile, tmpDir, ghToken, timeoutSec, now, ct);
                        if (h != null) { heals.Add(h); Console.WriteLine($"heal {v.Key} => CHỮA ĐƯỢC [{h.Kind}] {h.Url} {h.Sha256} ({h.SignerSubject})"); }
                        else { blocked.Add(b); Console.WriteLine($"heal {v.Key} => KHÔNG chữa: {b.ReasonVi}"); }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    { blocked.Add(new HealBlocked { VKey = v.Key, Slug = t.Slug, Version = t.Version, ReasonVi = "bot lỗi khi tự chữa: " + ex.GetType().Name + ": " + ex.Message }); }
                });
                harvested = await HarvestAsync(http, groups, failing.Select(f => f.Key).ToHashSet(StringComparer.Ordinal), anchors, harvestMax, harvestMaxFile, tmpDir, timeoutSec, now);
            }
            finally { try { Directory.Delete(tmpDir, true); } catch (IOException) { } }

            hs.Schema = 1; hs.UpdatedUtc = now;
            hs.Heals = heals.OrderBy(h => h.VKey, StringComparer.Ordinal).ToList();
            hs.HealBlocked = blocked.OrderBy(b => b.VKey, StringComparer.Ordinal).ToList();
            var liveIds = groups.Values.SelectMany(g => g).Select(AnchorId).ToHashSet(StringComparer.Ordinal);
            hs.Anchors = anchors.Values.Where(x => liveIds.Contains(x.Id)).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            hs.LastRun = new Dictionary<string, object>
            {
                ["at"] = now, ["seconds"] = (int)(DateTime.UtcNow - now).TotalSeconds, ["failing"] = failing.Count, ["healed"] = hs.Heals.Count, ["blocked"] = hs.HealBlocked.Count,
                ["downloads"] = budget.Files, ["downloaded_mb"] = budget.Bytes >> 20, ["harvested"] = harvested, ["anchors"] = hs.Anchors.Count,
            };
            try { Program.WriteAtomic(healPath, JsonSerializer.Serialize(hs, Indented)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { Program.GhError("Không ghi được state/heals.json", "Kết quả tự chữa lượt này bị mất, lượt sau sẽ làm lại. Chi tiết: " + ex.Message); return 3; }
            Console.WriteLine($"Tự chữa xong: {failing.Count} khoá gãy, chữa được {hs.Heals.Count}, cần chủ xem {hs.HealBlocked.Count}; tải {budget.Files} tệp ({budget.Bytes >> 20} MB); thu thập chữ ký tham chiếu mới {harvested}, tổng {hs.Anchors.Count}.");
            return 0;
        }

        static bool Take(Budget b)
        {
            lock (b) { if (b.Files >= b.MaxFiles || b.Bytes >= b.MaxBytes) return false; b.Files++; return true; }
        }

        static async Task<(HealEntry, HealBlocked)> HealOneAsync(HttpMessageInvoker http, ProbeTarget t, VariantState v, ConcurrentDictionary<string, HealAnchor> anchors, Budget budget,
                                                                 long maxFile, string tmpDir, string ghToken, int timeoutSec, DateTime now, CancellationToken ct)
        {
            HealBlocked Block(string why) => new HealBlocked { VKey = v.Key, Slug = t.Slug, Version = t.Version, ReasonVi = why };
            string reason = v.LastReason ?? "";
            string failUrl = (v.LastNote ?? "").Split(' ').FirstOrDefault(x => x.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? "";
            if (failUrl.Length == 0 && !t.IsWinget) failUrl = t.Url;
            string id = AnchorId(t);
            string expect = HealLogic.ExpectKind("", failUrl), arch = "", type = "", approvedSha = t.IsWinget ? "" : t.Sha256;
            var mtimeout = TimeSpan.FromSeconds(Math.Max(timeoutSec, 30));
            var gh = ghToken.Length > 0 ? new Dictionary<string, string> { ["Authorization"] = "Bearer " + ghToken, ["Accept"] = "application/vnd.github+json" } : new Dictionary<string, string> { ["Accept"] = "application/vnd.github+json" };

            if (t.IsWinget && reason != "winget manifest missing")
            {
                var m = await Pins.FetchAsync(http, LinkProbeLogic.WingetManifestUrl(t.ManifestId, t.Version), null, LinkProbeLogic.MaxManifestBytes, mtimeout, ct);
                if (m.Ok)
                {
                    var inst = PinLogic.Parse(m.Text);
                    var mine = inst.FirstOrDefault(i => i.Url == failUrl) ?? inst.FirstOrDefault(i => i.Arch == PinLogic.ArchName(t.Arch)) ?? inst.FirstOrDefault();
                    if (mine != null) { arch = mine.Arch; type = mine.Type; approvedSha = mine.Sha256; if (failUrl.Length == 0) failUrl = mine.Url; expect = HealLogic.ExpectKind(type, mine.Url); }
                }
            }
            if (type == "zip" || expect == "zip")
                return (null, Block("bộ cài là tệp zip: không có chữ ký Authenticode để kiểm người phát hành, không tự chữa (chủ xem SHA-256 mới rồi duyệt tay)"));

            anchors.TryGetValue(id, out var anchor);
            var strategies = HealLogic.Strategies(reason, failUrl, t.IsWinget);
            var cands = new List<HealCand>();
            void Add(string url, string kind, bool browser = false, string wsha = "", string wver = "")
            { if (!string.IsNullOrEmpty(url) && !cands.Any(c => c.Url == url && c.Browser == browser)) cands.Add(new HealCand { Url = url, Kind = kind, Browser = browser, WingetSha = wsha ?? "", WingetVersion = wver }); }
            var notes = new List<string>();

            foreach (var s in strategies)
            {
                switch (s)
                {
                    case "https_upgrade": Add(HealLogic.HttpsOf(failUrl), s); break;
                    case "same_url_new_hash": Add(failUrl, s); break;
                    case "strip_token": Add(HealLogic.StripToken(failUrl), s); break;
                    case "sourceforge_stable": Add(HealLogic.SourceForgeStable(failUrl), s); break;
                    case "browser_headers": Add(HealLogic.HttpsOf(failUrl) ?? failUrl, s, true); break;
                    case "github_latest": Add(HealLogic.GithubLatest(failUrl), s); break;
                    case "versionless_name": Add(HealLogic.Versionless(HealLogic.HttpsOf(failUrl) ?? failUrl), s); break;
                    case "github_asset":
                    {
                        var g = HealLogic.GithubRelease(failUrl);
                        var r = await Pins.FetchAsync(http, $"https://api.github.com/repos/{g.Value.Owner}/{g.Value.Repo}/releases/latest", null, LinkProbeLogic.MaxManifestBytes, mtimeout, ct, gh);
                        if (r.Ok) Add(HealLogic.MatchAsset(g.Value.File, HealLogic.ParseReleaseAssets(r.Text)), s);
                        else notes.Add("GitHub API " + r.Error);
                        break;
                    }
                    case "publisher_page":
                    {
                        string want = Path.GetFileName(failUrl.Split('?', '#')[0]);
                        var loc = await Pins.FetchAsync(http, LinkProbeLogic.WingetManifestUrl(t.ManifestId, t.Version).Replace(".installer.yaml", ".locale.en-US.yaml"), null, LinkProbeLogic.MaxManifestBytes, mtimeout, ct);
                        foreach (var page in loc.Ok ? HealLogic.PublisherUrls(loc.Text).Take(2) : new List<string>())
                        {
                            var pg = await Pins.FetchAsync(http, page, null, 2 << 20, mtimeout, ct, new Dictionary<string, string> { ["User-Agent"] = HealLogic.BrowserUa });
                            if (!pg.Ok) continue;
                            foreach (var l in HealLogic.HtmlLinks(pg.Text, pg.FinalUrl, want).Where(l => string.Equals(Path.GetFileName(l.Split('?', '#')[0]), want, StringComparison.OrdinalIgnoreCase)).Take(2)) Add(l, s);
                        }
                        break;
                    }
                    case "winget_newer":
                    {
                        var r = await Pins.FetchAsync(http, LinkProbeLogic.WingetDirApiUrl(t.ManifestId), null, LinkProbeLogic.MaxManifestBytes, mtimeout, ct, gh);
                        if (!r.Ok) { notes.Add(r.Error == "HTTP 404" ? "winget-pkgs đã xoá cả thư mục gói" : "không liệt kê được winget-pkgs (" + r.Error + ")"); break; }
                        var newer = LinkProbeLogic.ParseGithubDirListing(r.Text).Select(x => x.Name).Where(n => LinkProbeLogic.CompareVersions(n, t.Version) > 0)
                            .OrderByDescending(n => n, Comparer<string>.Create(LinkProbeLogic.CompareVersions)).Take(2).ToList();
                        if (newer.Count == 0) notes.Add("winget-pkgs chưa có bản mới hơn " + t.Version);
                        foreach (var nv in newer)
                        {
                            var mf = await Pins.FetchAsync(http, LinkProbeLogic.WingetManifestUrl(t.ManifestId, nv), null, LinkProbeLogic.MaxManifestBytes, mtimeout, ct);
                            if (!mf.Ok) continue;
                            var ni = PinLogic.Parse(mf.Text).Where(i => PinLogic.Hex64.IsMatch(i.Sha256) && (arch.Length == 0 || i.Arch == arch || i.Arch is "neutral" or "") && (type.Length == 0 || i.Type == type)).ToList();
                            foreach (var i in ni.Take(1)) Add(i.Url, s, false, i.Sha256, nv);
                        }
                        break;
                    }
                }
            }
            if (cands.Count == 0) return (null, Block($"không sinh được link thay thế cho lỗi \"{reason}\"" + (notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : "")));

            var cache = new Dictionary<string, Got>(StringComparer.Ordinal);
            var rejects = new List<string>();
            HealEntry fallback = null;
            HealEntry Make(HealCand c, Got g, string evidence) => new HealEntry
            {
                VKey = v.Key, Slug = t.Slug, WingetId = t.WingetId, Version = t.Version, Kind = c.Kind, Url = c.Url, Sha256 = g.Sha, Size = g.Size,
                SignerSubject = g.Subject, SignerThumbprint = g.Thumb, FinalHost = g.FinalHost, EvidenceVi = evidence, FromUrl = failUrl,
                CandidateVersion = c.WingetVersion.Length > 0 ? c.WingetVersion : null, CheckedUtc = now,
            };

            // Không có tham chiếu: thử trước các ứng viên winget-pkgs (SHA-256 do winget công bố) để lấy chữ ký tham chiếu, rồi mới tới link cùng bản.
            var order = anchor == null ? cands.Where(c => c.WingetSha.Length > 0).Concat(cands.Where(c => c.WingetSha.Length == 0)).ToList() : cands;
            for (int i = 0; i < order.Count && i < 10; i++)
            {
                var c = order[i];
                string ck = c.Url + (c.Browser ? "|b" : "");
                if (!cache.TryGetValue(ck, out var g))
                {
                    if (!Take(budget)) { rejects.Add("hết hạn mức tải của lượt này, để lượt sau"); break; }
                    g = cache[ck] = await DownloadAsync(http, c, maxFile, budget, tmpDir, ct);
                }
                if (!g.Ok) { rejects.Add($"{HealLogic.KindVi(c.Kind)}: {g.Err}"); continue; }
                if (g.Magic == "html")
                {
                    if (c.Kind != "html_link") foreach (var l in HealLogic.HtmlLinks(g.Html, c.Url, Path.GetFileName(failUrl.Split('?')[0])).Take(3)) if (!order.Any(o => o.Url == l)) order.Add(new HealCand { Url = l, Kind = "html_link" });
                    rejects.Add($"{HealLogic.KindVi(c.Kind)}: trang HTML");
                    continue;
                }
                if (approvedSha.Length == 64 && string.Equals(g.Sha, approvedSha, StringComparison.OrdinalIgnoreCase) && (expect.Length == 0 || g.Magic == expect))
                    return (Make(c, g, $"{HealLogic.KindVi(c.Kind)}; SHA-256 trùng tệp đã duyệt (cùng tệp, chỉ đổi link)"), null);
                var verdict = HealLogic.Judge(c, expect, g.Magic, g.Sha, g.Size, g.SigStatus, g.Subject, g.Thumb, anchor);
                if (verdict.Fatal) return (null, Block($"NGUY HIỂM — {verdict.Vi} tại {LinkProbeLogic.PublicUrl(c.Url)}. Không tự chữa, cần chủ xem."));
                if (!verdict.Ok) { rejects.Add($"{HealLogic.KindVi(c.Kind)}: {verdict.Vi} (SHA-256 {g.Sha}, {HealLogic.Mb(g.Size)})"); continue; }
                string signerVi = verdict.Signer switch
                {
                    "thumbprint" => $"cùng chứng chỉ với bản tham chiếu {anchor.Version} ({anchor.Source}, thumbprint trùng)",
                    "subject" => $"cùng người ký với bản tham chiếu {anchor.Version} ({anchor.Source}), chứng chỉ đã gia hạn",
                    _ => $"SHA-256 trùng manifest winget-pkgs bản {c.WingetVersion}",
                };
                string sizeVi = anchor != null && anchor.Size > 0 ? $"; {HealLogic.Mb(g.Size)} (tham chiếu {HealLogic.Mb(anchor.Size)})" : $"; {HealLogic.Mb(g.Size)}";
                var entry = Make(c, g, $"{HealLogic.KindVi(c.Kind)}; Authenticode hợp lệ, ký bởi \"{g.Subject}\"; {signerVi}{sizeVi}" + (c.Browser ? "; cần gửi User-Agent trình duyệt + Referer khi tải" : ""));
                if (verdict.Signer == "winget" && anchor == null)
                {
                    anchor = new HealAnchor { Id = id, Version = c.WingetVersion, Sha256 = g.Sha, Size = g.Size, Subject = g.Subject, Thumbprint = g.Thumb, Source = "winget", At = now };
                    anchors[id] = anchor;
                    fallback ??= entry;   // bản mới hơn: giữ làm phương án cuối, ưu tiên link cùng phiên bản
                    continue;
                }
                return (entry, null);
            }
            if (fallback != null) return (fallback, null);
            string why = string.Join("; ", rejects.Concat(notes).Distinct().Take(6));
            return (null, Block(why.Length > 0 ? why : "không ứng viên nào đạt"));
        }

        static async Task<Got> DownloadAsync(HttpMessageInvoker http, HealCand c, long maxFile, Budget budget, string tmpDir, CancellationToken ct)
        {
            var g = new Got();
            Dictionary<string, string> headers = null;
            if (c.Browser && Uri.TryCreate(c.Url, UriKind.Absolute, out var u))
                headers = new Dictionary<string, string> { ["User-Agent"] = HealLogic.BrowserUa, ["Referer"] = u.GetLeftPart(UriPartial.Authority) + "/", ["Accept"] = "*/*" };
            string file = Path.Combine(tmpDir, Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                var d = await Pins.FetchAsync(http, c.Url, file, maxFile, TimeSpan.FromMinutes(20), ct, headers);
                lock (budget) budget.Bytes += d.Size;
                if (!d.Ok) { g.Err = d.Error; return g; }
                var head = new byte[64];
                int n;
                using (var fs = File.OpenRead(file)) n = fs.Read(head, 0, head.Length);
                g.Magic = HealLogic.Magic(head, n);
                g.Sha = d.Sha256; g.Size = d.Size; g.FinalHost = d.Hosts.Count > 0 ? d.Hosts[^1] : "";
                if (g.Magic == "html") { g.Html = d.Size <= LinkProbeLogic.MaxManifestBytes ? File.ReadAllText(file) : ""; g.Ok = true; return g; }
                var sig = await Pins.SignatureAsync(file, ct);
                g.SigStatus = sig.Status; g.Subject = sig.Subject; g.Thumb = sig.Thumbprint; g.SigMsg = sig.Message;
                g.Ok = true;
                return g;
            }
            finally { try { File.Delete(file); } catch (IOException) { } }
        }

        /// <summary>
        /// Thu thập chữ ký tham chiếu lúc link CÒN TỐT (gói chưa có): tải installer, chỉ nhận khi SHA-256 trùng nguồn đã duyệt (manifest winget / catalog).
        /// Nhờ vậy lần sau link gãy mới kiểm được "người ký liên tục". Tệp không ký cũng ghi (subject rỗng) để biết không tự chữa được.
        /// </summary>
        static async Task<int> HarvestAsync(HttpMessageInvoker http, Dictionary<string, List<ProbeTarget>> groups, HashSet<string> failing, ConcurrentDictionary<string, HealAnchor> anchors,
                                            int max, long maxFile, string tmpDir, int timeoutSec, DateTime now)
        {
            var todo = groups.Where(g => !failing.Contains(g.Key)).Select(g => g.Value[0]).Where(t => !anchors.ContainsKey(AnchorId(t)))
                .GroupBy(AnchorId).Select(g => g.First()).OrderBy(t => t.Featured ? 0 : 1).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
            int done = 0, files = 0;
            foreach (var t in todo)
            {
                if (files >= max) break;
                string url = t.Url, sha = t.Sha256, ver = t.Version;
                if (t.IsWinget)
                {
                    var m = await Pins.FetchAsync(http, LinkProbeLogic.WingetManifestUrl(t.ManifestId, t.Version), null, LinkProbeLogic.MaxManifestBytes, TimeSpan.FromSeconds(Math.Max(timeoutSec, 30)), CancellationToken.None);
                    if (!m.Ok) continue;
                    var all = PinLogic.Parse(m.Text).Where(i => i.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && PinLogic.Hex64.IsMatch(i.Sha256) && i.Type is not ("zip" or "msix" or "appx")).ToList();
                    var pick = all.FirstOrDefault(i => i.Arch == PinLogic.ArchName(t.Arch)) ?? all.FirstOrDefault(i => i.Arch == "x64") ?? all.FirstOrDefault();
                    if (pick == null) continue;
                    url = pick.Url; sha = pick.Sha256;
                }
                if (sha.Length != 64 || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || HealLogic.ExpectKind("", url) == "zip") continue;
                files++;
                string file = Path.Combine(tmpDir, Guid.NewGuid().ToString("N") + ".bin");
                try
                {
                    var d = await Pins.FetchAsync(http, url, file, maxFile, TimeSpan.FromMinutes(10), CancellationToken.None);
                    if (!d.Ok || !string.Equals(d.Sha256, sha, StringComparison.OrdinalIgnoreCase)) continue;
                    var sig = await Pins.SignatureAsync(file, CancellationToken.None);
                    bool ok = sig.Status == "Valid" && sig.Subject.Length > 0;
                    anchors[AnchorId(t)] = new HealAnchor { Id = AnchorId(t), Version = ver, Sha256 = d.Sha256, Size = d.Size, Subject = ok ? sig.Subject : "", Thumbprint = ok ? sig.Thumbprint : "", Source = "harvest", At = now };
                    done++;
                    Console.WriteLine($"anchor {AnchorId(t)} {ver} => {(ok ? sig.Subject : "không ký (" + sig.Status + ")")}");
                }
                finally { try { File.Delete(file); } catch (IOException) { } }
            }
            return done;
        }

        /// <summary>heals / heal_blocked cho status.json từ state/heals.json, chỉ khoá còn gãy trong state.json hiện tại. null = chưa có / không đọc được (giữ giá trị cũ).</summary>
        public static (List<HealEntry> Heals, List<HealBlocked> Blocked)? ForStatus(string healPath, WatchState watch)
        {
            HealState hs;
            try { hs = File.Exists(healPath) ? JsonSerializer.Deserialize<HealState>(File.ReadAllText(healPath)) : null; }
            catch (Exception ex) when (ex is JsonException || ex is IOException) { return null; }
            if (hs == null) return null;
            var live = (watch?.Variants ?? new()).Where(v => v != null && (v.ConsecutiveFails > 0 || v.Hidden)).Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
            return ((hs.Heals ?? new()).Where(h => live.Contains(h.VKey)).Take(300).ToList(), (hs.HealBlocked ?? new()).Where(b => live.Contains(b.VKey)).Take(300).ToList());
        }
    }
}
