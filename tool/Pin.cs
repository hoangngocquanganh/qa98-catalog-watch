using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
    /// <summary>Một bản WINGET đã duyệt đã/không ghim được (state/pins.json); mục "pinned" cũng là một dòng của public/pins.json.</summary>
    sealed class PinEntry
    {
        [JsonPropertyName("key"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Key { get; set; }
        [JsonPropertyName("status"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Status { get; set; }
        [JsonPropertyName("reason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Reason { get; set; }
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        /// <summary>Arch của phiên bản trong catalog (khoá khớp khi nạp), không phải arch của installer.</summary>
        [JsonPropertyName("arch")] public string Arch { get; set; } = "";
        [JsonPropertyName("installer_type")] public string InstallerType { get; set; } = "";
        [JsonPropertyName("installer_arch")] public string InstallerArch { get; set; } = "";
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("signer_subject")] public string SignerSubject { get; set; } = "";
        [JsonPropertyName("signer_thumbprint")] public string SignerThumbprint { get; set; } = "";
        [JsonPropertyName("allowed_hosts")] public List<string> AllowedHosts { get; set; } = new();
        [JsonPropertyName("checked_at")] public DateTime CheckedUtc { get; set; }
        /// <summary>Chỉ ở state/pins.json: ok / exe / exe_no_switch (bản chỉ có installer exe, có / thiếu InstallerSwitches.Silent|SilentWithProgress). Thiếu = chưa kiểm.</summary>
        [JsonPropertyName("silent_check"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string SilentCheck { get; set; }
    }

    /// <summary>Một dòng status.json "silent_missing": bản WINGET chỉ có installer exe (winget có thể hiện cửa sổ cài / không cài im lặng được).</summary>
    sealed class SilentMissing
    {
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        [JsonPropertyName("winget_id")] public string WingetId { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("installer_type")] public string InstallerType { get; set; } = "";
        /// <summary>false = manifest thiếu cả InstallerSwitches.Silent lẫn SilentWithProgress.</summary>
        [JsonPropertyName("silent_switch")] public bool SilentSwitch { get; set; }
    }

    sealed class PinState
    {
        [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
        [JsonPropertyName("updated_at")] public DateTime UpdatedUtc { get; set; }
        [JsonPropertyName("catalog_revision")] public long CatalogRevision { get; set; }
        [JsonPropertyName("last_run")] public Dictionary<string, object> LastRun { get; set; } = new();
        [JsonPropertyName("entries")] public List<PinEntry> Entries { get; set; } = new();
    }

    sealed class PinFile
    {
        [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
        [JsonPropertyName("untrusted")] public bool Untrusted { get; set; } = true;
        [JsonPropertyName("generated_at")] public DateTime GeneratedUtc { get; set; }
        [JsonPropertyName("catalog_revision")] public long CatalogRevision { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("pins")] public List<PinEntry> Pins { get; set; } = new();
    }

    /// <summary>Một mục Installers của manifest winget (đã gộp giá trị mặc định ở gốc tệp) — cùng cách đọc với WingetManifestSource của app.</summary>
    sealed class ManifestInstaller
    {
        public string Arch = "", Type = "", Url = "", Sha256 = "", Scope = "", Locale = "";
        /// <summary>Có InstallerSwitches.Silent hoặc SilentWithProgress (của mục hoặc ở gốc tệp).</summary>
        public bool Silent;
    }

    static class PinLogic
    {
        static readonly Regex KeyRe = new Regex(@"^([A-Za-z][A-Za-z0-9]*):(?:\s+(.*))?$", RegexOptions.CultureInvariant);
        static readonly string[] Keys = { "Architecture", "InstallerType", "InstallerUrl", "InstallerSha256", "Scope", "InstallerLocale" };
        public static readonly Regex Hex64 = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        /// <summary>Loại installer winget mà đường DirectPinned của app ánh xạ được (Docs/WINGET-PINNED.md, WingetManifestSource.Map).</summary>
        public static bool Mapped(string t) => t is "msi" or "wix" or "inno" or "nullsoft" or "burn";

        public static string Key(string id, string version, CpuArch arch) => id.ToLowerInvariant() + "|" + version + "|" + ArchName(arch);

        public static string ArchName(CpuArch a) => a switch { CpuArch.X64 => "x64", CpuArch.X86 => "x86", CpuArch.Arm64 => "arm64", _ => "neutral" };

        public static List<ManifestInstaller> Parse(string yaml)
        {
            var root = new Dictionary<string, string>(StringComparer.Ordinal);
            var items = new List<Dictionary<string, string>>();
            Dictionary<string, string> cur = null;
            bool inList = false; int itemIndent = -1;
            Dictionary<string, string> sw = null; int swIndent = 0;   // khối InstallerSwitches đang đọc (của gốc hoặc của mục)
            foreach (string raw in (yaml ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                string t = line.TrimStart(' ');
                if (t.Length == 0 || t[0] == '#') continue;
                int indent = line.Length - t.Length;
                if (sw != null)
                {
                    if (indent > swIndent)
                    {
                        if (KeyRe.Match(t) is { Success: true } ms && ms.Groups[1].Value is "Silent" or "SilentWithProgress" && ms.Groups[2].Value.Trim().Trim('\'', '"').Length > 0) sw["Silent"] = "1";
                        continue;
                    }
                    sw = null;
                }
                if (indent == 0 && t[0] != '-')
                {
                    cur = null; inList = false;
                    if (KeyRe.Match(t) is { Success: true } m)
                    {
                        if (m.Groups[1].Value == "Installers") { inList = true; itemIndent = -1; }
                        else if (m.Groups[1].Value == "InstallerSwitches") { sw = root; swIndent = 0; }
                        else Put(root, m);
                    }
                    continue;
                }
                if (!inList) continue;
                if (t[0] == '-' && (itemIndent < 0 || indent == itemIndent))
                {
                    itemIndent = indent;
                    cur = new Dictionary<string, string>(StringComparer.Ordinal);
                    items.Add(cur);
                    t = t.Substring(1).TrimStart(' ');
                    if (KeyRe.Match(t) is { Success: true } m0) { if (m0.Groups[1].Value == "InstallerSwitches") { sw = cur; swIndent = itemIndent + 2; } else Put(cur, m0); }
                    continue;
                }
                if (cur != null && indent == itemIndent + 2 && t[0] != '-' && KeyRe.Match(t) is { Success: true } m1)
                { if (m1.Groups[1].Value == "InstallerSwitches") { sw = cur; swIndent = indent; } else Put(cur, m1); }
            }
            string Get(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : root.TryGetValue(k, out var r) ? r : "";
            return items.Select(d => new ManifestInstaller
            {
                Arch = Get(d, "Architecture").ToLowerInvariant(), Type = Get(d, "InstallerType").ToLowerInvariant(), Url = Get(d, "InstallerUrl"),
                Sha256 = Get(d, "InstallerSha256").ToLowerInvariant(), Scope = Get(d, "Scope").ToLowerInvariant(), Locale = Get(d, "InstallerLocale"),
                Silent = Get(d, "Silent").Length > 0,
            }).ToList();
        }

        static void Put(Dictionary<string, string> d, Match m)
        {
            string k = m.Groups[1].Value;
            if (Array.IndexOf(Keys, k) < 0) return;
            string v = m.Groups[2].Value.Trim();
            if (v.Length >= 2 && (v[0] == '\'' || v[0] == '"') && v[v.Length - 1] == v[0]) v = v.Substring(1, v.Length - 2);
            else { int c = v.IndexOf(" #", StringComparison.Ordinal); if (c >= 0) v = v.Substring(0, c).TrimEnd(); }
            d[k] = v;
        }

        /// <summary>
        /// Chọn MỘT installer để ghim cho bản đã duyệt. Arch cụ thể: trùng arch, không có thì neutral.
        /// Bản neutral chỉ ghim được một file: installer khác file theo arch => bỏ (winget trên máy arm64 / x86 sẽ chọn file khác — cần tách phiên bản theo arch).
        /// Còn lại ưu tiên loại ánh xạ được, Scope machine, InstallerLocale trống / en-US.
        /// </summary>
        public static ManifestInstaller Choose(List<ManifestInstaller> all, CpuArch arch, out string reason)
        {
            reason = "";
            var c = all.Where(i => i.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && Hex64.IsMatch(i.Sha256)).ToList();
            if (c.Count == 0) { reason = "manifest has no https installer with sha256"; return null; }
            string want = arch is CpuArch.X64 or CpuArch.X86 or CpuArch.Arm64 ? ArchName(arch) : "";
            if (want.Length > 0)
            {
                var hit = c.Where(i => i.Arch == want).ToList();
                if (hit.Count == 0) hit = c.Where(i => i.Arch is "neutral" or "").ToList();
                if (hit.Count == 0) { reason = "no installer for arch " + want; return null; }
                c = hit;
            }
            else
            {
                var archs = c.Select(i => i.Arch).Where(a => a is not ("neutral" or "")).Distinct().OrderBy(a => a, StringComparer.Ordinal).ToList();
                if (archs.Count > 1 && c.Select(i => i.Sha256).Distinct().Count() > 1)
                { reason = "neutral version but installer differs by arch (" + string.Join(",", archs) + "); split versions per arch to pin"; return null; }
            }
            var pick = c.OrderBy(i => Mapped(i.Type) ? 0 : 1)
                        .ThenBy(i => i.Scope == "machine" ? 0 : i.Scope == "" ? 1 : 2)
                        .ThenBy(i => i.Locale == "" ? 0 : i.Locale.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                        .First();
            if (!Mapped(pick.Type)) { reason = "installer type '" + (pick.Type.Length > 0 ? pick.Type : "unknown") + "' not supported by pinned route"; return null; }
            return pick;
        }

        /// <summary>
        /// Kiểm cài im lặng: các installer winget sẽ chọn cho arch này (trùng arch, không có thì neutral; bản neutral = mọi installer)
        /// đều là exe => "exe" (có Silent/SilentWithProgress) hoặc "exe_no_switch" (thiếu ở ít nhất một file); còn lại "ok".
        /// shortcut: inno/nullsoft "không chuẩn" (thật ra là exe tự viết) không nhận ra được từ manifest, cần chạy thử installer mới biết.
        /// </summary>
        public static string SilentCheck(List<ManifestInstaller> all, CpuArch arch)
        {
            string want = arch is CpuArch.X64 or CpuArch.X86 or CpuArch.Arm64 ? ArchName(arch) : "";
            var c = want.Length == 0 ? all : all.Where(i => i.Arch == want).ToList();
            if (c.Count == 0) c = all.Where(i => i.Arch is "neutral" or "").ToList();
            if (c.Count == 0 || c.Any(i => i.Type != "exe")) return "ok";
            return c.All(i => i.Silent) ? "exe" : "exe_no_switch";
        }

        /// <summary>Tự kiểm khi chạy (dự án test không tham chiếu tool). Trả danh sách mục hỏng (tiếng Việt); rỗng = đạt.</summary>
        public static List<string> SelfTest()
        {
            const string y = "PackageIdentifier: A.B\nInstallerType: nullsoft\nScope: machine\nInstallers:\n- Architecture: x64\n  InstallerUrl: https://h/x64.exe # c\n  InstallerSha256: " +
                             "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n  InstallerSwitches:\n    Silent: /S\n- Architecture: x86\n  InstallerType: msi\n  InstallerUrl: 'https://h/x86.msi'\n" +
                             "  InstallerSha256: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\nManifestType: installer\n";
            var bad = new List<string>();
            void Check(bool ok, string what) { if (!ok) bad.Add(what); }
            var p = Parse(y);
            Check(p.Count == 2, "đọc manifest: phải ra 2 installer");
            if (p.Count != 2) return bad;
            Check(p[0].Type == "nullsoft" && p[0].Scope == "machine", "đọc manifest: InstallerType / Scope ở gốc tệp phải áp cho từng installer");
            Check(p[0].Sha256 == new string('a', 64), "đọc manifest: InstallerSha256 phải chuyển chữ thường");
            Check(p[0].Url == "https://h/x64.exe", "đọc manifest: phải bỏ chú thích # sau URL");
            Check(p[1].Type == "msi" && p[1].Url == "https://h/x86.msi", "đọc manifest: InstallerType riêng / URL trong nháy đơn");
            Check(Choose(p, CpuArch.X86, out _)?.Url == "https://h/x86.msi", "chọn installer: bản x86 phải chọn đúng file x86");
            Check(Choose(p, CpuArch.X64, out _)?.Type == "nullsoft", "chọn installer: bản x64 phải chọn đúng file x64");
            Check(Choose(p, CpuArch.Neutral, out var r1) == null && r1.Contains("split"), "chọn installer: bản neutral có file khác nhau theo arch phải bỏ qua");
            Check(Choose(p.Take(1).ToList(), CpuArch.Neutral, out _)?.Arch == "x64", "chọn installer: bản neutral chỉ có 1 file phải ghim được");
            Check(Choose(new List<ManifestInstaller> { new() { Arch = "x64", Type = "msix", Url = "https://h/a", Sha256 = new string('c', 64) } }, CpuArch.X64, out var r2) == null && r2.Contains("msix"),
                  "chọn installer: loại msix phải bỏ qua");
            Check(Choose(p, CpuArch.Arm64, out var r3) == null && r3.Contains("arm64"), "chọn installer: không có file arm64 phải bỏ qua");
            Check(p[0].Silent && !p[1].Silent, "đọc manifest: InstallerSwitches.Silent của từng installer");
            Check(SilentCheck(p, CpuArch.X64) == "ok" && SilentCheck(p, CpuArch.X86) == "ok", "kiểm im lặng: nullsoft / msi là ok");
            var e = Parse("InstallerType: exe\nInstallerSwitches:\n  SilentWithProgress: '/quiet'\nInstallers:\n- Architecture: x64\n  InstallerUrl: https://h/a.exe\n" +
                          "- Architecture: x86\n  InstallerUrl: https://h/b.exe\n  InstallerSwitches:\n    Custom: /x\nManifestType: installer\n");
            Check(e.Count == 2 && e.All(i => i.Type == "exe" && i.Silent), "đọc manifest: InstallerSwitches ở gốc tệp phải áp cho từng installer");
            Check(SilentCheck(e, CpuArch.X64) == "exe", "kiểm im lặng: exe có SilentWithProgress phải ra exe");
            var n = Parse("InstallerType: exe\nInstallers:\n- Architecture: x64\n  InstallerUrl: https://h/a.exe\n  InstallerSwitches:\n    Silent: ''\n    Custom: /S\n- Architecture: x86\n  InstallerType: msi\n  InstallerUrl: https://h/b.msi\n");
            Check(n.Count == 2 && SilentCheck(n, CpuArch.X64) == "exe_no_switch", "kiểm im lặng: exe thiếu Silent phải ra exe_no_switch");
            Check(SilentCheck(n, CpuArch.Neutral) == "ok", "kiểm im lặng: bản neutral còn file msi là ok");
            return bad;
        }
    }

    /// <summary>
    /// Bước "pin": với bản WINGET đã duyệt, tải installer của đúng manifest, đối chiếu SHA-256 manifest, đọc chữ ký Authenticode
    /// (Get-AuthenticodeSignature — chỉ chạy trên Windows), ghi state/pins.json + public/pins.json. Không chạy file tải về; file xoá ngay sau khi kiểm.
    /// Kết quả là dữ liệu KHÔNG tin cậy: server tự đối chiếu lại manifest + bản đang duyệt trước khi đưa vào catalog ký.
    /// </summary>
    static class Pins
    {
        static readonly TimeSpan RetrySkipped = TimeSpan.FromDays(7);
        static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        internal sealed class Fetch
        {
            public bool Ok, Transient; public string Error = "", Text, Sha256 = "", FinalUrl = ""; public long Size; public List<string> Hosts = new();
        }

        public static async Task<int> RunAsync(Args a, SoftwareCatalog catalog, HttpMessageInvoker http, int timeoutSec)
        {
            var broken = PinLogic.SelfTest();
            if (broken.Count > 0)
            {
                Program.GhError("Tự kiểm bước ghim bộ cài hỏng", "Mã chọn installer của bot sai nên lượt này KHÔNG ghim gì (pins.json cũ giữ nguyên, app không bị ảnh hưởng). Cần sửa Tools/CatalogWatch/Pin.cs. Mục hỏng: " + string.Join("; ", broken));
                return 1;
            }
            if (!OperatingSystem.IsWindows()) { Program.GhError("Sai máy chạy", "Bước ghim bộ cài cần Windows (Get-AuthenticodeSignature); workflow phải dùng runs-on: windows-latest."); return 1; }
            string statePath = a.Get("pin-state", "state/pins.json"), outPath = a.Get("pin-out", "public/pins.json");
            int maxDownloads = a.Int("pin-max", 40), maxManifests = a.Int("pin-manifests", 300);
            long budget = (long)a.Int("pin-budget-mb", 8192) << 20, maxFile = (long)a.Int("pin-max-file-mb", 1024) << 20;
            var now = DateTime.UtcNow;

            PinState state;
            try { state = File.Exists(statePath) ? JsonSerializer.Deserialize<PinState>(File.ReadAllText(statePath)) ?? new PinState() : new PinState(); }
            catch (Exception ex) when (ex is JsonException || ex is IOException)
            { Program.GhError("Tệp state/pins.json hỏng", "Không đọc được danh sách đã ghim nên lượt này dừng (pins.json công khai giữ nguyên). Sửa hoặc xoá state/pins.json trong repo bot rồi chạy lại. Chi tiết: " + ex.Message); return 3; }

            var targets = LinkProbeLogic.Targets(catalog).Where(t => t.IsWinget)
                .GroupBy(t => PinLogic.Key(t.ManifestId, t.Version, t.Arch)).Select(g => g.First()).ToList();
            var keys = targets.Select(t => PinLogic.Key(t.ManifestId, t.Version, t.Arch)).ToHashSet(StringComparer.Ordinal);
            var entries = new ConcurrentDictionary<string, PinEntry>((state.Entries ?? new()).Where(e => e?.Key != null && keys.Contains(e.Key)).GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.First()), StringComparer.Ordinal);

            // bản bỏ qua từ trước khi có silent_check: kiểm lại ngay (chủ yếu chỉ tải manifest), trừ khi manifest mất / lỗi
            bool Due(ProbeTargetKey t) => !entries.TryGetValue(t.Key, out var e) || e.Status != "pinned"
                && (now - e.CheckedUtc >= RetrySkipped || e.SilentCheck == null && !(e.Reason ?? "").StartsWith("winget manifest", StringComparison.Ordinal));
            var todo = targets.Select(t => new ProbeTargetKey(PinLogic.Key(t.ManifestId, t.Version, t.Arch), t)).Where(Due)
                .OrderBy(x => x.T.Featured ? 0 : 1).ThenBy(x => x.Key, StringComparer.Ordinal).Take(maxManifests).ToList();

            int downloads = 0, pinned = 0, skipped = 0, transient = 0, deferred = 0;
            long used = 0;
            var gate = new object();
            var reasons = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            string tmpDir = Path.Combine(Path.GetTempPath(), "qa98pin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try
            {
                await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (x, ct) =>
                {
                    var t = x.T;
                    string silent = null;
                    void Skip(string why)
                    {
                        entries[x.Key] = new PinEntry { Key = x.Key, Status = "skipped", Reason = why, Id = t.ManifestId, Version = t.Version, Arch = PinLogic.ArchName(t.Arch), CheckedUtc = now, SilentCheck = silent };
                        Interlocked.Increment(ref skipped);
                        string bucket = why.StartsWith("authenticode", StringComparison.Ordinal) ? why.Split(':')[0] : Regex.Replace(why, @"\(.*\)|'[^']*'|\b\d+\b", "…");
                        reasons.AddOrUpdate(bucket, 1, (_, n) => n + 1);
                        Console.WriteLine($"{x.Key}  =>  skip: {why}");
                    }
                    void Later(string why) { Interlocked.Increment(ref transient); Console.WriteLine($"{x.Key}  =>  later: {why}"); }

                    var m = await FetchAsync(http, LinkProbeLogic.WingetManifestUrl(t.ManifestId, t.Version), null, LinkProbeLogic.MaxManifestBytes, TimeSpan.FromSeconds(Math.Max(timeoutSec, 30)), ct);
                    if (!m.Ok) { if (m.Transient) Later("manifest " + m.Error); else Skip(m.Error == "HTTP 404" ? "winget manifest missing" : "winget manifest " + m.Error); return; }
                    var parsed = PinLogic.Parse(m.Text);
                    silent = PinLogic.SilentCheck(parsed, t.Arch);
                    var pick = PinLogic.Choose(parsed, t.Arch, out string why);
                    if (pick == null) { Skip(why); return; }

                    lock (gate)
                    {
                        if (downloads >= maxDownloads || used >= budget) { deferred++; return; }
                        downloads++;
                    }
                    string file = Path.Combine(tmpDir, Guid.NewGuid().ToString("N") + (pick.Type is "msi" or "wix" ? ".msi" : ".exe"));
                    try
                    {
                        var d = await FetchAsync(http, pick.Url, file, maxFile, TimeSpan.FromMinutes(20), ct);
                        Interlocked.Add(ref used, d.Size);
                        if (!d.Ok) { if (d.Transient) Later("download " + d.Error); else Skip("download " + d.Error); return; }
                        if (d.Sha256 != pick.Sha256) { Skip($"sha256 mismatch with manifest (manifest {pick.Sha256} got {d.Sha256})"); return; }
                        var sig = await SignatureAsync(file, ct);
                        if (sig.Status != "Valid") { Skip($"authenticode {sig.Status}: {sig.Message.Replace(file, "<file>")}".Trim()); return; }
                        if (sig.Subject.Length == 0 || !Regex.IsMatch(sig.Thumbprint, "^[0-9A-F]{40}$")) { Skip("authenticode signer missing"); return; }
                        entries[x.Key] = new PinEntry
                        {
                            Key = x.Key, Status = "pinned", Id = t.ManifestId, Version = t.Version, Arch = PinLogic.ArchName(t.Arch),
                            InstallerType = pick.Type, InstallerArch = pick.Arch, Url = pick.Url, Sha256 = d.Sha256, Size = d.Size,
                            SignerSubject = sig.Subject, SignerThumbprint = sig.Thumbprint, AllowedHosts = d.Hosts, CheckedUtc = now, SilentCheck = silent,
                        };
                        Interlocked.Increment(ref pinned);
                        Console.WriteLine($"{x.Key}  =>  pinned {pick.Type} {d.Size / 1048576.0:0.0} MB [{string.Join(",", d.Hosts)}] {sig.Subject}");
                    }
                    finally { try { File.Delete(file); } catch (IOException) { } }
                });
            }
            finally { try { Directory.Delete(tmpDir, true); } catch (IOException) { } }

            var all = entries.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
            state.Schema = 1; state.UpdatedUtc = now; state.CatalogRevision = catalog.Revision; state.Entries = all;
            state.LastRun = new Dictionary<string, object>
            {
                ["at"] = now, ["seconds"] = (int)(DateTime.UtcNow - now).TotalSeconds, ["checked"] = todo.Count, ["downloads"] = downloads, ["downloaded_mb"] = used >> 20,
                ["pinned"] = pinned, ["skipped"] = skipped, ["transient"] = transient, ["deferred"] = deferred,
                ["skip_reasons"] = reasons.OrderByDescending(r => r.Value).ToDictionary(r => r.Key, r => r.Value),
            };
            var pub = new PinFile
            {
                GeneratedUtc = now, CatalogRevision = catalog.Revision,
                Pins = all.Where(e => e.Status == "pinned").Select(e => new PinEntry
                {
                    Id = e.Id, Version = e.Version, Arch = e.Arch, InstallerType = e.InstallerType, InstallerArch = e.InstallerArch, Url = e.Url, Sha256 = e.Sha256, Size = e.Size,
                    SignerSubject = e.SignerSubject, SignerThumbprint = e.SignerThumbprint, AllowedHosts = e.AllowedHosts, CheckedUtc = e.CheckedUtc,
                }).ToList(),
            };
            pub.Count = pub.Pins.Count;
            try
            {
                Program.WriteAtomic(statePath, JsonSerializer.Serialize(state, Indented));
                Program.WriteAtomic(outPath, JsonSerializer.Serialize(pub, Indented));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { Program.GhError("Không ghi được pins.json", "Kết quả ghim lượt này bị mất, lượt sau sẽ làm lại; pins.json cũ giữ nguyên. Chi tiết: " + ex.Message); return 3; }
            Console.WriteLine($"Pin xong: kiểm {todo.Count}, tải {downloads} ({used >> 20} MB), ghim mới {pinned}, bỏ qua {skipped}, lỗi tạm {transient}, để lượt sau {deferred}; tổng đang ghim {pub.Count}/{targets.Count}.");
            foreach (var r in reasons.OrderByDescending(r => r.Value)) Console.WriteLine($"  bỏ qua {r.Value}: {r.Key}");
            return 0;
        }

        sealed record ProbeTargetKey(string Key, ProbeTarget T);

        /// <summary>Danh sách "silent_missing" cho status.json từ state/pins.json (job ghim ghi). null = chưa có / không đọc được (giữ giá trị cũ).</summary>
        public static List<SilentMissing> SilentMissingList(string statePath, SoftwareCatalog catalog)
        {
            PinState st;
            try { st = File.Exists(statePath) ? JsonSerializer.Deserialize<PinState>(File.ReadAllText(statePath)) : null; }
            catch (Exception ex) when (ex is JsonException || ex is IOException) { return null; }
            if (st?.Entries == null) return null;
            var byKey = LinkProbeLogic.Targets(catalog).Where(t => t.IsWinget)
                .GroupBy(t => PinLogic.Key(t.ManifestId, t.Version, t.Arch)).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            return st.Entries.Where(e => e?.Key != null && e.SilentCheck is "exe" or "exe_no_switch" && byKey.ContainsKey(e.Key))
                .Select(e => (e, t: byKey[e.Key]))
                .GroupBy(x => x.t.Slug + "|" + x.t.WingetId + "|" + x.t.Version, StringComparer.OrdinalIgnoreCase)   // nhiều arch cùng phiên bản = 1 dòng
                .Select(g => new SilentMissing
                {
                    Slug = g.First().t.Slug, WingetId = g.First().t.WingetId.Length > 0 ? g.First().t.WingetId : g.First().e.Id, Version = g.First().t.Version,
                    InstallerType = "exe", SilentSwitch = g.All(x => x.e.SilentCheck == "exe"),
                })
                .OrderBy(s => s.SilentSwitch).ThenBy(s => s.Slug, StringComparer.Ordinal).ThenBy(s => s.Version, StringComparer.Ordinal)
                .Take(300).ToList();   // server đọc tối đa 300 mục; thiếu switch xếp trước
        }

        /// <summary>GET theo redirect (tự kiểm từng hop: https, không userinfo; IP nội bộ bị chặn ở ConnectCallback). file = null => đọc chữ vào bộ nhớ.</summary>
        internal static async Task<Fetch> FetchAsync(HttpMessageInvoker http, string url, string file, long max, TimeSpan timeout, CancellationToken ct, IReadOnlyDictionary<string, string> headers = null)
        {
            var f = new Fetch();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                for (int hop = 0; hop <= LinkProbeLogic.MaxRedirects; hop++)
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(u.UserInfo))
                    { f.Error = hop == 0 ? "url is not https" : "redirect to non-https"; return f; }
                    if (u.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6) { f.Error = "ip literal host"; return f; }
                    string host = u.IdnHost.TrimEnd('.').ToLowerInvariant();
                    if (!f.Hosts.Contains(host)) f.Hosts.Add(host);
                    using var req = new HttpRequestMessage(HttpMethod.Get, u);
                    if (headers == null || !headers.ContainsKey("User-Agent")) req.Headers.TryAddWithoutValidation("User-Agent", "QA98-CatalogWatch-Pin/1");
                    foreach (var h in headers ?? new Dictionary<string, string>())
                        if (h.Key != "Authorization" || host == "api.github.com") req.Headers.TryAddWithoutValidation(h.Key, h.Value);   // token chỉ gửi tới GitHub API, không theo redirect sang host khác
                    using var r = await http.SendAsync(req, cts.Token);
                    int code = (int)r.StatusCode;
                    if (code >= 300 && code < 400 && r.Headers.Location != null) { url = new Uri(u, r.Headers.Location).ToString(); continue; }
                    if (code != 200) { f.Error = "HTTP " + code; f.Transient = code == 408 || code == 429 || code >= 500; return f; }
                    if (r.Content.Headers.ContentLength > max) { f.Error = $"larger than {max >> 20} MB"; return f; }
                    f.FinalUrl = u.ToString();
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    using var mem = file == null ? new MemoryStream() : null;
                    using var fs = file != null ? new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, true) : null;
                    Stream dst = (Stream)fs ?? mem;
                    var buf = new byte[1 << 16];
                    int k;
                    using (var s = await r.Content.ReadAsStreamAsync(cts.Token))
                        while ((k = await s.ReadAsync(buf, cts.Token)) > 0)
                        {
                            f.Size += k;
                            if (f.Size > max) { f.Error = $"larger than {max >> 20} MB"; return f; }
                            sha.AppendData(buf, 0, k);
                            await dst.WriteAsync(buf.AsMemory(0, k), cts.Token);
                        }
                    f.Sha256 = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                    if (mem != null) f.Text = Encoding.UTF8.GetString(mem.ToArray());
                    f.Ok = true;
                    return f;
                }
                f.Error = "too many redirects";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { f.Error = "timeout"; f.Transient = true; }
            catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException || ex.InnerException?.InnerException is BlockedAddressException) { f.Error = "private address"; }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException) { f.Error = "network error: " + ex.Message; f.Transient = true; }
            return f;
        }

        /// <summary>Get-AuthenticodeSignature (WinVerifyTrust, có kiểm thu hồi) — cùng API X509 Subject / Thumbprint với kiểm chữ ký của app.</summary>
        internal static async Task<(string Status, string Subject, string Thumbprint, string Message)> SignatureAsync(string file, CancellationToken ct)
        {
            const string script = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $s = Get-AuthenticodeSignature -LiteralPath $env:QA98_SIGFILE; $c = $s.SignerCertificate; " +
                                  "[pscustomobject]@{ status = [string]$s.Status; subject = $(if ($c) { $c.Subject } else { '' }); thumbprint = $(if ($c) { $c.Thumbprint } else { '' }); message = [string]$s.StatusMessage } | ConvertTo-Json -Compress";
            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, CreateNoWindow = true };
            foreach (var x in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }) psi.ArgumentList.Add(x);
            psi.Environment["QA98_SIGFILE"] = file;
            using var p = Process.Start(psi);
            var outTask = p.StandardOutput.ReadToEndAsync(ct);
            var errTask = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            string o = (await outTask).Trim();
            try
            {
                using var doc = JsonDocument.Parse(o);
                var r = doc.RootElement;
                string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                return (S("status"), S("subject").Trim(), S("thumbprint").Trim().ToUpperInvariant(), S("message").Replace("\r", " ").Replace("\n", " ").Trim());
            }
            catch (JsonException) { return ("Error", "", "", ("powershell: " + o + " " + await errTask).Trim()); }
        }
    }
}
