using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SysInfoWpf.Services.SoftwareCenter;
using SysInfoWpf.Services.SoftwareCenter.Health;
using SysInfoWpf.Services.SoftwareCenter.Trust;

namespace SysInfoWpf.Services { static class Diag { public static void Log(params object[] a) { } public static void Silent(params object[] a) { } } }

namespace CatalogWatch
{
    /// <summary>state.json của bot: trạng thái từng khoá + con trỏ xoay vòng + revision catalog đã thấy (chống catalog cũ phát lại).</summary>
    sealed class WatchState
    {
        [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
        [JsonPropertyName("catalog_revision")] public long CatalogRevision { get; set; }
        [JsonPropertyName("cursor")] public string Cursor { get; set; } = "";
        [JsonPropertyName("featured_cursor")] public string FeaturedCursor { get; set; } = "";
        [JsonPropertyName("updated_at")] public DateTime UpdatedUtc { get; set; }
        [JsonPropertyName("last_probe_at")] public DateTime? LastProbeUtc { get; set; }
        [JsonPropertyName("variants")] public List<VariantState> Variants { get; set; } = new();
    }

    /// <summary>status.json — tóm tắt công khai để web admin hiện "bot còn sống" (không có dữ liệu nhạy cảm).</summary>
    sealed class WatchStatus
    {
        [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
        [JsonPropertyName("tool_version")] public string ToolVersion { get; set; } = "";
        [JsonPropertyName("checked_at")] public DateTime CheckedUtc { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("config_from_server")] public bool ConfigFromServer { get; set; }
        [JsonPropertyName("config")] public Dictionary<string, object> Config { get; set; } = new();
        [JsonPropertyName("ran_probe")] public bool RanProbe { get; set; }
        [JsonPropertyName("last_probe_at")] public DateTime? LastProbeUtc { get; set; }
        [JsonPropertyName("catalog_revision")] public long CatalogRevision { get; set; }
        [JsonPropertyName("total_keys")] public int TotalKeys { get; set; }
        [JsonPropertyName("hidden")] public int Hidden { get; set; }
        /// <summary>Vị trí con trỏ xoay vòng / tổng số khoá (tiến độ một vòng quét).</summary>
        [JsonPropertyName("cycle_position")] public int CyclePosition { get; set; }
        [JsonPropertyName("error")] public string Error { get; set; } = "";
        [JsonPropertyName("pages")] public PagesStatus Pages { get; set; } = new();
        [JsonPropertyName("last_probe")] public ProbeSummary LastProbe { get; set; }
    }

    sealed class PagesStatus
    {
        [JsonPropertyName("catalog_revision")] public long CatalogRevision { get; set; }
        [JsonPropertyName("changed")] public bool Changed { get; set; }
        /// <summary>ok / none (server không có) / invalid / fetch_error / skipped</summary>
        [JsonPropertyName("freshness")] public string Freshness { get; set; } = "skipped";
        [JsonPropertyName("availability")] public string Availability { get; set; } = "skipped";
        [JsonPropertyName("error")] public string Error { get; set; } = "";
    }

    sealed class ProbeSummary
    {
        [JsonPropertyName("at")] public DateTime At { get; set; }
        [JsonPropertyName("seconds")] public int Seconds { get; set; }
        [JsonPropertyName("probed")] public int Probed { get; set; }
        [JsonPropertyName("ok")] public int Ok { get; set; }
        [JsonPropertyName("hashed")] public int Hashed { get; set; }
        [JsonPropertyName("transient")] public int Transient { get; set; }
        [JsonPropertyName("broken")] public int Broken { get; set; }
        [JsonPropertyName("hash_mismatch")] public int HashMismatch { get; set; }
        [JsonPropertyName("skipped")] public int Skipped { get; set; }
        [JsonPropertyName("wide_outage")] public bool WideOutage { get; set; }
    }

    static class Program
    {
        const string ToolVersion = "1.1.0";
        const string ApiBase = "https://hoangngocquanganh.com/qa98/api/v1/software/";
        const string DefaultConfigUrl = ApiBase + "bot_config.php";
        const string DefaultFreshnessUrl = ApiBase + "freshness.php";        // chưa xác minh endpoint thật: 404 = server chưa có freshness
        const string DefaultAvailabilityUrl = ApiBase + "availability.php";
        const string DefaultCatalogUrl = "https://hoangngocquanganh.com/qa98/api/v1/software/catalog.php";   // = SoftwareCenterService.CatalogUrl
        static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        static async Task<int> Main(string[] argv)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Args a;
            try { a = Args.Parse(argv); }
            catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return Usage(); }
            if (a.Has("help")) return Usage();

            string catalogArg = a.Get("catalog", DefaultCatalogUrl);
            string configUrl = a.Get("config-url", DefaultConfigUrl), freshnessUrl = a.Get("freshness-url", DefaultFreshnessUrl), availabilityUrl = a.Get("availability-url", DefaultAvailabilityUrl);
            string statePath = a.Get("state", "state.json"), outPath = a.Get("out", "link_health.json"), reportPath = a.Get("report", "report.md");
            string proposalsPath = a.Get("proposals", "proposals.json"), statusPath = a.Get("status", "status.json"), publicDir = a.Get("public", "public");
            int timeoutSec = a.Int("timeout", 30), maxFileMb = a.Int("max-file-mb", 2048);
            var now = DateTime.UtcNow;
            long nowUnix = new DateTimeOffset(now).ToUnixTimeSeconds();
            var status = new WatchStatus { ToolVersion = ToolVersion, CheckedUtc = now };
            var prevStatus = ReadJson<WatchStatus>(statusPath);
            status.LastProbe = prevStatus?.LastProbe;

            using var handler = SafeHandler(TimeSpan.FromSeconds(timeoutSec));
            using var http = new HttpMessageInvoker(handler);

            WatchState state;
            bool stateMissing = !File.Exists(statePath);
            try { state = !stateMissing ? JsonSerializer.Deserialize<WatchState>(File.ReadAllText(statePath)) ?? new WatchState() : new WatchState(); }
            catch (Exception ex) when (ex is JsonException || ex is IOException)
            { status.Error = "state.json unreadable: " + ex.Message; WriteStatus(statusPath, status); Console.Error.WriteLine("state.json hỏng, dừng để không mất trạng thái ẩn: " + ex.Message); return 3; }
            state.Variants ??= new();
            status.LastProbeUtc = state.LastProbeUtc;

            // 0) Cấu hình vận hành từ web admin (lỗi => mặc định an toàn). Tham số dòng lệnh (chạy tay) đè lên.
            var cfg = BotConfig.Parse((await GetAsync(http, configUrl, BotConfig.MaxBytes, timeoutSec)).Text);
            int max = a.Int("max", cfg.MaxPerRun), perHost = a.Int("concurrency-per-host", cfg.ConcurrencyPerHost);
            status.Enabled = cfg.Enabled; status.ConfigFromServer = cfg.FromServer;
            status.Config = new Dictionary<string, object>
            {
                ["max_per_run"] = cfg.MaxPerRun, ["interval_hours"] = cfg.IntervalHours, ["featured_first"] = cfg.FeaturedFirst,
                ["concurrency_per_host"] = cfg.ConcurrencyPerHost, ["run_now_after"] = cfg.RunNowAfterUtc?.ToString("o", CultureInfo.InvariantCulture) ?? "",
            };

            // 1) Catalog: đọc + KIỂM CHỮ KÝ. Sai => dừng, không đụng danh sách cũ / public/ (chỉ ghi status.json).
            string text;
            if (catalogArg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var g = await GetAsync(http, catalogArg, SoftwareCatalogTrust.MaxPayloadBytes * 2, Math.Max(timeoutSec, 30));
                text = g.Code == 200 ? g.Text : null;
                if (text == null) return Fail(statusPath, status, 2, "catalog fetch failed: " + (g.Code == 0 ? g.Error : "HTTP " + g.Code));
            }
            else
            {
                try { text = File.ReadAllText(catalogArg); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Fail(statusPath, status, 2, "catalog read failed: " + ex.Message); }
            }
            var ring = SoftwareCatalogKeys.Ring();
            var trust = SoftwareCatalogTrust.Verify(CatalogPublishLogic.UnwrapEnvelope(text), ring, nowUnix, state.CatalogRevision);
            if (!trust.Ok) return Fail(statusPath, status, 2, $"catalog rejected ({trust.Error}: {trust.Detail})");
            var catalog = trust.Catalog;
            status.CatalogRevision = catalog.Revision;

            // 1a) Bước ghim installer winget (job riêng, runner Windows): không đồng bộ public/, không dò link, không đụng state.json / status.json
            if (a.Has("pin")) return cfg.Enabled ? await Pins.RunAsync(a, catalog, http, timeoutSec) : 0;

            // 1b) Đồng bộ public/ cho Cloudflare Pages (mỗi lượt kiểm, kể cả khi chưa tới hạn dò)
            bool pagesChanged = false;
            if (!a.Has("no-pages"))
            {
                try { pagesChanged = await SyncPagesAsync(http, text, trust, ring, publicDir, freshnessUrl, availabilityUrl, nowUnix, status.Pages, timeoutSec); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Fail(statusPath, status, 3, "public/ write failed: " + ex.Message); }
            }

            // 1c) Admin tắt bot / chưa tới hạn => thoát sớm (không dò; chỉ ghi status khi có gì đổi)
            if (!cfg.Enabled)
            {
                Console.WriteLine("Bot đang tắt trong web admin (enabled=false).");
                if (prevStatus?.Enabled != false || pagesChanged) WriteStatus(statusPath, status);
                return 0;
            }
            if (!cfg.IsDue(state.LastProbeUtc, now, a.Has("force")))
            {
                Console.WriteLine($"Chưa tới hạn dò (lượt trước {state.LastProbeUtc:u}, mỗi {cfg.IntervalHours} giờ).");
                if (pagesChanged) WriteStatus(statusPath, status);
                return 0;
            }

            // 2) Biến thể cần dò, gộp theo khoá (nhiều kiến trúc cùng phiên bản = 1 khoá như app lọc)
            var groups = LinkProbeLogic.Targets(catalog).GroupBy(t => t.Key).ToDictionary(g => g.Key, g => (IReadOnlyList<ProbeTarget>)g.ToList(), StringComparer.Ordinal);
            var states = LinkProbeLogic.KeepInCatalog(state.Variants, groups.Keys);   // bỏ khoá không còn trong catalog

            // 3) Xoay vòng. Mất state => dò lại chậm (nửa hạn mức); trạng thái mới tinh không bao giờ ẩn ngay (cần nhiều lượt cách giờ).
            if (stateMissing) max = Math.Max(1, max / 2);
            var keys = groups.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            bool Urgent(string k) => states.TryGetValue(k, out var s) && (s.Hidden || s.ConsecutiveFails > 0);
            bool Featured(string k) => cfg.FeaturedFirst && groups[k].Any(t => t.Featured);
            var urgent = keys.Where(Urgent).ToList();
            var featured = Rotate(keys.Where(k => !Urgent(k) && Featured(k)).ToList(), state.FeaturedCursor);
            var rest = Rotate(keys.Where(k => !Urgent(k) && !Featured(k)).ToList(), state.Cursor);
            // trần ưu tiên 60 % để gói ít dùng không bị bỏ đói; phần dư của bên này nhường bên kia
            var prio = urgent.Concat(featured).ToList();
            int prioCap = Math.Max(1, max * 3 / 5);
            int restTake = Math.Min(rest.Count, max - Math.Min(prio.Count, prioCap));
            var pick = prio.Take(max - restTake).Concat(rest.Take(restTake)).ToList();
            var tookFeatured = pick.Where(featured.Contains).ToList();
            if (tookFeatured.Count > 0) state.FeaturedCursor = tookFeatured[^1];
            if (restTake > 0) state.Cursor = rest[restTake - 1];

            // 4) Dò song song (giới hạn theo host nằm trong LinkProber)
            var prober = new LinkProber(http)
            {
                PerHost = Math.Max(1, perHost), HeaderTimeout = TimeSpan.FromSeconds(timeoutSec), MaxFileBytes = (long)Math.Max(1, maxFileMb) * 1024 * 1024,
                GithubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "",
            };
            var results = new ConcurrentDictionary<string, ProbeResult>(StringComparer.Ordinal);
            await Parallel.ForEachAsync(pick, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (k, ct) =>
            {
                var s = states.TryGetValue(k, out var x) ? x : new VariantState();
                try { results[k] = await prober.ProbeAsync(groups[k], s, now, ct); }
                catch (Exception ex) { results[k] = new ProbeResult { Outcome = ProbeOutcome.Transient, Reason = "bot error", Note = ex.GetType().Name + ": " + ex.Message }; }
                Console.WriteLine($"{k}  =>  {(results[k].Skipped ? "Skipped" : results[k].Outcome.ToString())} {results[k].Reason}");
            });

            // 4b) Khoá hỏng / sai file: tìm nguồn thay thế qua winget-pkgs (chỉ để đề xuất)
            var found = new ConcurrentDictionary<string, Discovery>(StringComparer.Ordinal);
            var failing = pick.Where(k => !results[k].Skipped && results[k].Outcome is ProbeOutcome.Broken or ProbeOutcome.HashMismatch).ToList();
            await Parallel.ForEachAsync(failing, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (k, ct) =>
            {
                try { found[k] = await prober.DiscoverAsync(groups[k][0], ct); }
                catch (Exception ex) { found[k] = new Discovery { Note = "discovery error: " + ex.GetType().Name }; }
            });

            // 5) Cập nhật trạng thái (tuần tự). Lỗi tạm / mạng > 30 % => sự cố diện rộng: không thêm mục ẩn mới (vẫn cho hiện lại).
            int probed = pick.Count(k => !results[k].Skipped), transient = pick.Count(k => !results[k].Skipped && results[k].Outcome == ProbeOutcome.Transient);
            bool wideOutage = LinkProbeLogic.IsWideOutage(probed, transient);
            var restored = new List<VariantState>();
            var newlyHidden = new List<VariantState>();
            var held = new List<VariantState>();
            foreach (var k in pick)
            {
                var r = results[k];
                if (r.Skipped) continue;
                var t = groups[k][0];
                if (!states.TryGetValue(k, out var s)) states[k] = s = new VariantState { Key = k };
                s.WingetId = t.WingetId; s.Slug = t.Slug; s.Version = t.Version;
                bool was = s.Hidden;
                LinkHealthLogic.Step(s, r.Outcome, now, r.Reason, r.Note);
                if (!was && s.Hidden && wideOutage) { s.Hidden = false; held.Add(s); }
                if (r.Hashed) { s.Fingerprint = r.Fingerprint; s.HashedUtc = now; }
                if (was && !s.Hidden) restored.Add(s);
                if (!was && s.Hidden) newlyHidden.Add(s);
            }

            state.CatalogRevision = catalog.Revision;
            state.UpdatedUtc = now;
            state.LastProbeUtc = now;
            state.Variants = states.Values.OrderBy(s => s.Key, StringComparer.Ordinal).ToList();
            var proposals = Proposals(now, catalog.Revision, groups, pick, results, states, found);

            // 6) Ghi tệp atomic
            int hiddenCount;
            try
            {
                hiddenCount = WriteOutputs(outPath, state.Variants, catalog.Revision, now);
                WriteAtomic(statePath, JsonSerializer.Serialize(state, Indented));
                WriteAtomic(proposalsPath, JsonSerializer.Serialize(proposals, Indented));
                WriteAtomic(reportPath, Report(now, catalog, groups, pick, results, state.Variants, restored, newlyHidden, held, wideOutage, proposals));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Fail(statusPath, status, 3, "write failed: " + ex.Message); }

            var done = pick.Select(k => results[k]).ToList();
            status.RanProbe = true; status.LastProbeUtc = now; status.TotalKeys = groups.Count; status.Hidden = hiddenCount;
            status.CyclePosition = keys.Count(k => string.CompareOrdinal(k, state.Cursor ?? "") <= 0);
            status.LastProbe = new ProbeSummary
            {
                At = now, Seconds = (int)(DateTime.UtcNow - now).TotalSeconds, Probed = probed, Transient = transient, WideOutage = wideOutage,
                Ok = done.Count(r => !r.Skipped && r.Outcome == ProbeOutcome.Ok), Hashed = done.Count(r => r.Hashed), Skipped = done.Count(r => r.Skipped),
                Broken = done.Count(r => !r.Skipped && r.Outcome == ProbeOutcome.Broken), HashMismatch = done.Count(r => !r.Skipped && r.Outcome == ProbeOutcome.HashMismatch),
            };
            if (!WriteStatus(statusPath, status)) return 3;

            Console.WriteLine($"Xong: dò {pick.Count}/{groups.Count} khoá, đang ẩn {hiddenCount}{(wideOutage ? " (sự cố diện rộng: không thêm mục ẩn mới)" : "")}.");
            return 0;
        }

        static T ReadJson<T>(string path) where T : class
        {
            try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null; }
            catch (Exception ex) when (ex is JsonException || ex is IOException) { return null; }
        }

        static bool WriteStatus(string path, WatchStatus st)
        {
            try { WriteAtomic(path, JsonSerializer.Serialize(st, Indented)); return true; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Console.Error.WriteLine("Không ghi được status.json: " + ex.Message); return false; }
        }

        static int Fail(string statusPath, WatchStatus st, int code, string error)
        {
            st.Error = error;
            Console.Error.WriteLine("LỖI: " + error + " — giữ nguyên danh sách cũ.");
            WriteStatus(statusPath, st);
            return code;
        }

        /// <summary>GET giới hạn byte qua handler an toàn (không theo redirect). Code 0 = lỗi mạng / timeout / quá lớn (Error ghi lý do).</summary>
        static async Task<(string Text, int Code, string Error)> GetAsync(HttpMessageInvoker http, string url, int maxBytes, int timeoutSec)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", "QA98-CatalogWatch/" + ToolVersion);
                using var r = await http.SendAsync(req, cts.Token);
                if (r.StatusCode != HttpStatusCode.OK) return (null, (int)r.StatusCode, "");
                using var s = await r.Content.ReadAsStreamAsync(cts.Token);
                var buf = new byte[maxBytes + 1];
                int n = 0, k;
                while (n < buf.Length && (k = await s.ReadAsync(buf.AsMemory(n), cts.Token)) > 0) n += k;
                if (n > maxBytes) return (null, 0, "response too large");
                return (Encoding.UTF8.GetString(buf, 0, n), 200, "");
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is IOException || ex is UriFormatException || ex is InvalidOperationException)
            { return (null, 0, ex.GetType().Name + ": " + ex.Message); }
        }

        static bool WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content) return false;
            WriteAtomic(path, content);
            return true;
        }

        /// <summary>
        /// public/ cho Cloudflare Pages: catalog.json = phong bì gốc NGUYÊN VĂN (đã kiểm chữ ký, revision ≥ bản đang có), freshness.json nếu server có
        /// (phải kiểm chữ ký + khớp revision/SHA-256 của catalog, sai => không ghi cả cặp), availability.json nguyên văn từ server (server tự ký).
        /// </summary>
        static async Task<bool> SyncPagesAsync(HttpMessageInvoker http, string catalogText, CatalogTrustResult trust, SysInfoWpf.Services.Licensing.LicenseKeyRing ring,
                                               string dir, string freshnessUrl, string availabilityUrl, long nowUnix, PagesStatus ps, int timeoutSec)
        {
            string catPath = Path.Combine(dir, "catalog.json"), frPath = Path.Combine(dir, "freshness.json"), avPath = Path.Combine(dir, "availability.json");
            long repoRev = File.Exists(catPath) ? CatalogPublishLogic.CatalogIdentity(File.ReadAllText(catPath)).Revision : 0;
            long rev = trust.Catalog.Revision;
            ps.CatalogRevision = Math.Max(repoRev, 0);
            if (rev < repoRev) { ps.Error = $"catalog revision {rev} < {repoRev} already in public/"; return false; }

            bool changed = false;
            var f = await GetAsync(http, freshnessUrl, CatalogFreshness.MaxBytes, timeoutSec);
            if (f.Code == 200)
            {
                var fr = CatalogFreshness.Verify(CatalogPublishLogic.UnwrapEnvelope(f.Text), ring, nowUnix, rev, 0);
                if (!fr.Ok || fr.Doc.CatalogRevision != rev || !string.Equals(fr.Doc.CatalogSha256, trust.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                { ps.Freshness = "invalid"; ps.Error = "freshness rejected: " + (fr.Ok ? "does not match catalog" : fr.Error + " " + fr.Detail); return false; }
                ps.Freshness = "ok";
                changed |= WriteIfChanged(frPath, f.Text);
            }
            else if (f.Code == 404)
            {
                ps.Freshness = "none";
                if (File.Exists(frPath)) { File.Delete(frPath); changed = true; }   // freshness cũ sẽ lệch catalog mới => bỏ theo server
            }
            else { ps.Freshness = "fetch_error"; ps.Error = "freshness fetch failed: " + (f.Code == 0 ? f.Error : "HTTP " + f.Code); return false; }   // không chắc => giữ nguyên cặp cũ

            changed |= WriteIfChanged(catPath, catalogText);
            ps.CatalogRevision = rev;

            var av = await GetAsync(http, availabilityUrl, HealthList.MaxBytes, timeoutSec);
            if (av.Code == 200) { ps.Availability = "ok"; changed |= WriteIfChanged(avPath, av.Text); }
            else ps.Availability = av.Code == 404 ? "none" : "fetch_error";
            ps.Changed = changed;
            return changed;
        }

        /// <summary>Danh sách sau con trỏ trước, rồi vòng lại đầu.</summary>
        static List<string> Rotate(List<string> sorted, string cursor)
        {
            int i = sorted.FindIndex(k => string.CompareOrdinal(k, cursor ?? "") > 0);
            return i <= 0 ? sorted : sorted.Skip(i).Concat(sorted.Take(i)).ToList();
        }

        /// <summary>
        /// MỘT chỗ duy nhất ghi đầu ra cho client (link_health.json). Khi đổi sang availability.json có ký khoá vận hành thì chỉ sửa hàm này.
        /// Trả số mục đang ẩn.
        /// </summary>
        static int WriteOutputs(string outPath, List<VariantState> states, long catalogRevision, DateTime now)
        {
            var health = LinkHealthLogic.Build(states, catalogRevision, now);
            WriteAtomic(outPath, health.ToJson());
            return health.Hidden.Count;
        }

        /// <summary>proposals.json — dữ liệu KHÔNG tin cậy cho người duyệt; không bao giờ tự áp vào catalog, không tự hạ phiên bản.</summary>
        static ProposalFile Proposals(DateTime now, long revision, Dictionary<string, IReadOnlyList<ProbeTarget>> groups, List<string> pick,
                                      ConcurrentDictionary<string, ProbeResult> results, Dictionary<string, VariantState> states, ConcurrentDictionary<string, Discovery> found)
        {
            var f = new ProposalFile { BaseRevision = revision, GeneratedUtc = now };
            foreach (var k in pick)
            {
                var r = results[k];
                if (r.Skipped) continue;
                var t = groups[k][0];
                found.TryGetValue(k, out var d);
                bool hidden = states.TryGetValue(k, out var s) && s.Hidden;
                f.Items.Add(new ProposalItem
                {
                    Key = k, WingetId = t.WingetId, Slug = t.Slug, Version = t.Version,
                    Arch = string.Join(",", groups[k].Select(x => x.Arch.ToString().ToLowerInvariant()).Distinct()),
                    Kind = LinkProbeLogic.Classify(r.Outcome, hidden, d), Outcome = r.Outcome.ToString(), Reason = r.Reason, Note = r.Note,
                    Tried = r.Tried.Distinct().ToList(),
                    CandidateVersion = d?.CandidateVersion ?? "", CandidateUrl = d?.CandidateUrl ?? "", CandidateSha256 = d?.CandidateSha256 ?? "",
                    Evidence = d == null ? "" : string.Join(" ", new[] { LinkProbeLogic.PublicUrl(d.ManifestUrl), d.ManifestRef, d.Note }.Where(x => x.Length > 0)),
                });
            }
            return f;
        }

        static int Usage()
        {
            Console.WriteLine("CatalogWatch — dò link bộ cài của catalog QA98 đã ký, phát link_health.json (danh sách tạm ẩn).");
            Console.WriteLine("  --catalog <url|tệp>   mặc định " + DefaultCatalogUrl);
            Console.WriteLine("  --state state.json  --out link_health.json  --report report.md  --proposals proposals.json  --status status.json  --public public");
            Console.WriteLine("  --config-url <url>    mặc định " + DefaultConfigUrl + " (lỗi => mặc định an toàn)");
            Console.WriteLine("  --freshness-url / --availability-url   nguồn cho public/ (404 = server chưa có)");
            Console.WriteLine("  --force (dò dù chưa tới hạn)  --no-pages (không đồng bộ public/)");
            Console.WriteLine("  --max / --concurrency-per-host: đè cấu hình web admin khi chạy tay");
            Console.WriteLine("  --max 150  --concurrency-per-host 2  --timeout 30 (giây)  --max-file-mb 2048 (file lớn hơn: bỏ qua băm lượt này)");
            Console.WriteLine("  --pin (chỉ Windows): ghim installer bản WINGET -> --pin-state state/pins.json --pin-out public/pins.json");
            Console.WriteLine("        --pin-max 40 (số file tải)  --pin-budget-mb 8192  --pin-max-file-mb 1024  --pin-manifests 300 (số bản kiểm mỗi lượt)");
            return 1;
        }

        /// <summary>Chỉ kết nối IP công khai: chặn private / loopback / link-local (169.254.169.254) ngay lúc kết nối (cả khi DNS trỏ lại).</summary>
        static SocketsHttpHandler SafeHandler(TimeSpan connectTimeout) => new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = connectTimeout, PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = async (ctx, ct) =>
            {
                string host = ctx.DnsEndPoint.Host;
                var addrs = IPAddress.TryParse(host, out var lit) ? new[] { lit } : await Dns.GetHostAddressesAsync(host, ct);
                if (addrs.Length == 0 || addrs.Any(LinkProbeLogic.IsBlockedAddress)) throw new BlockedAddressException(host);
                var sock = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try { await sock.ConnectAsync(addrs, ctx.DnsEndPoint.Port, ct); return new NetworkStream(sock, ownsSocket: true); }
                catch { sock.Dispose(); throw; }
            },
        };

        internal static void WriteAtomic(string path, string content)
        {
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string tmp = full + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            File.Move(tmp, full, overwrite: true);
        }

        // ------------------------------------------------------------------ report.md (tiếng Việt cho chủ dự án)

        static string Md(string s) => string.IsNullOrEmpty(s) ? "—" : s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        static string D(DateTime? t) => t == null ? "—" : t.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        static string Name(VariantState s) => s.WingetId.Length > 0 ? s.WingetId : s.Slug;

        static string Report(DateTime now, SoftwareCatalog catalog, Dictionary<string, IReadOnlyList<ProbeTarget>> groups, List<string> pick,
                             ConcurrentDictionary<string, ProbeResult> results, List<VariantState> states, List<VariantState> restored, List<VariantState> newlyHidden,
                             List<VariantState> held, bool wideOutage, ProposalFile proposals)
        {
            var rs = pick.Select(k => results[k]).ToList();
            int ok = rs.Count(r => !r.Skipped && r.Outcome == ProbeOutcome.Ok), skip = rs.Count(r => r.Skipped);
            int tr = rs.Count(r => !r.Skipped && r.Outcome == ProbeOutcome.Transient), br = rs.Count(r => !r.Skipped && r.Outcome == ProbeOutcome.Broken);
            int hm = rs.Count(r => !r.Skipped && r.Outcome == ProbeOutcome.HashMismatch), hashed = rs.Count(r => r.Hashed);
            var hidden = states.Where(s => s.Hidden).ToList();
            string Tried(string key) => results.TryGetValue(key, out var r) && r.Tried.Count > 0 ? string.Join("<br>", r.Tried.Distinct().Select(Md)) : "—";

            var sb = new StringBuilder();
            sb.AppendLine("# Báo cáo dò link bộ cài QA98");
            sb.AppendLine();
            sb.AppendLine($"- Lượt chạy: {D(now)} — catalog revision {catalog.Revision}, {groups.Count} khoá (gói × phiên bản) cần dò.");
            sb.AppendLine($"- Đã dò lượt này: **{pick.Count}** — đạt {ok} (tải cả file kiểm SHA-256: {hashed}), lỗi tạm {tr}, link chết {br}, sai file {hm}, bỏ qua (file quá giới hạn tải thử) {skip}.");
            sb.AppendLine($"- Đang tạm ẩn: **{hidden.Count}** (mới ẩn lượt này {newlyHidden.Count}, vừa hiện lại {restored.Count}).");
            if (groups.Count > 0 && hidden.Count > groups.Count * HealthList.MaxHiddenFraction)
                sb.AppendLine($"- ⚠ Số gói ẩn vượt {HealthList.MaxHiddenFraction:P0} danh mục: app sẽ BỎ QUA cả danh sách (coi như bot hỏng). Kiểm tra bot / mạng của runner.");
            if (wideOutage)
                sb.AppendLine($"- ⚠ SỰ CỐ DIỆN RỘNG: lỗi tạm / mạng vượt {LinkProbeLogic.WideOutageFraction:P0} số đã dò => lượt này KHÔNG thêm mục ẩn mới (giữ lại {held.Count} mục đủ ngưỡng ẩn, chỉ cho hiện lại).");
            var kinds = proposals.Items.GroupBy(i => i.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}");
            sb.AppendLine("- Đề xuất (proposals.json, dữ liệu chưa tin cậy): " + string.Join(", ", kinds) + ".");
            sb.AppendLine();
            sb.AppendLine($"Ngưỡng: link chết / sai file ẩn sau {LinkHealthLogic.BrokenFailsToHide} lượt lỗi, mỗi lượt cách lượt trước ≥ {LinkHealthLogic.BrokenMinGap.TotalHours:0} giờ; lỗi tạm (timeout, 429, 5xx) không bao giờ ẩn; đang ẩn cần {LinkHealthLogic.OksToRestore} lượt đạt (có kiểm SHA-256) mới hiện lại.");
            sb.AppendLine();

            sb.AppendLine("## Thùng rác — chờ duyệt");
            sb.AppendLine();
            if (hidden.Count == 0) sb.AppendLine("Không có gói nào đang bị ẩn.");
            else
            {
                sb.AppendLine("| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var s in hidden)
                    sb.AppendLine($"| {Md(Name(s))} | {Md(s.Version)} | {Md(s.LastReason)} | {D(s.FirstFailUtc)} | {s.ConsecutiveFails} | {Md(s.LastNote)} | {Tried(s.Key)} |");
                sb.AppendLine();
                sb.AppendLine("Duyệt: sửa link / hash trong catalog rồi ký lại (bản mới => khoá mới, mục cũ tự hết tác dụng), hoặc chờ bot tự hiện lại khi link hết lỗi. Hiện lại bằng tay: xoá mục khỏi state.json (README).");
            }
            sb.AppendLine();

            sb.AppendLine("## Vừa hiện lại");
            sb.AppendLine();
            if (restored.Count == 0) sb.AppendLine("Không có.");
            else
            {
                sb.AppendLine("| Gói | Bản | Đạt lần cuối |");
                sb.AppendLine("|---|---|---|");
                foreach (var s in restored) sb.AppendLine($"| {Md(Name(s))} | {Md(s.Version)} | {D(s.LastOkUtc)} |");
            }
            sb.AppendLine();

            var review = proposals.Items.Where(i => i.Kind is "same_artifact" or "new_artifact" or "needs_review" or "retire_candidate").ToList();
            sb.AppendLine("## Đề xuất cần duyệt");
            sb.AppendLine();
            if (review.Count == 0) sb.AppendLine("Không có.");
            else
            {
                sb.AppendLine("| Gói | Bản | Loại | Bản đề xuất | URL đề xuất | SHA-256 đề xuất | Bằng chứng |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var i in review)
                    sb.AppendLine($"| {Md(i.WingetId.Length > 0 ? i.WingetId : i.Slug)} | {Md(i.Version)} | {i.Kind} | {Md(i.CandidateVersion)} | {Md(i.CandidateUrl)} | {Md(i.CandidateSha256)} | {Md(i.Evidence)} |");
                sb.AppendLine();
                sb.AppendLine("same_artifact = cùng SHA-256 đã tải kiểm, chỉ đổi URL; new_artifact = bản mới hơn trong winget-pkgs (chưa tải, phải kiểm file + chữ ký trên máy quản trị trước khi ký catalog); retire_candidate = cả thư mục gói mất khỏi winget-pkgs.");
            }
            sb.AppendLine();

            var failing = states.Where(s => !s.Hidden && s.ConsecutiveFails > 0).ToList();
            sb.AppendLine("## Đang lỗi, chưa đủ ngưỡng ẩn");
            sb.AppendLine();
            if (failing.Count == 0) sb.AppendLine("Không có.");
            else
            {
                sb.AppendLine("| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var s in failing)
                    sb.AppendLine($"| {Md(Name(s))} | {Md(s.Version)} | {Md(s.LastReason)} | {D(s.FirstFailUtc)} | {s.ConsecutiveFails} | {Md(s.LastNote)} | {Tried(s.Key)} |");
            }
            return sb.ToString();
        }
    }

    sealed class ProposalFile
    {
        [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
        [JsonPropertyName("untrusted")] public bool Untrusted { get; set; } = true;
        [JsonPropertyName("baseRevision")] public long BaseRevision { get; set; }
        [JsonPropertyName("generated_at")] public DateTime GeneratedUtc { get; set; }
        [JsonPropertyName("items")] public List<ProposalItem> Items { get; set; } = new();
    }

    sealed class ProposalItem
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("winget_id")] public string WingetId { get; set; } = "";
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("arch")] public string Arch { get; set; } = "";
        /// <summary>healthy / transient / suspect / same_artifact / new_artifact / needs_review / retire_candidate</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("outcome")] public string Outcome { get; set; } = "";
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("note")] public string Note { get; set; } = "";
        [JsonPropertyName("tried")] public List<string> Tried { get; set; } = new();
        [JsonPropertyName("candidate_version")] public string CandidateVersion { get; set; } = "";
        [JsonPropertyName("candidate_url")] public string CandidateUrl { get; set; } = "";
        [JsonPropertyName("candidate_sha256")] public string CandidateSha256 { get; set; } = "";
        [JsonPropertyName("evidence")] public string Evidence { get; set; } = "";
    }

    sealed class Args
    {
        readonly Dictionary<string, string> _kv = new(StringComparer.Ordinal);

        public static Args Parse(IEnumerable<string> argv)
        {
            var a = new Args(); string key = null;
            foreach (var s in argv)
            {
                if (s.StartsWith("--", StringComparison.Ordinal)) { if (key != null) a._kv[key] = ""; key = s.Substring(2); }
                else if (key != null) { a._kv[key] = s; key = null; }
                else throw new ArgumentException("tham số lạ: " + s);
            }
            if (key != null) a._kv[key] = "";
            return a;
        }

        public bool Has(string k) => _kv.ContainsKey(k);
        public string Get(string k, string def = "") => _kv.TryGetValue(k, out var v) && v.Length > 0 ? v : def;
        public int Int(string k, int def) => int.TryParse(Get(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0 ? v : def;
    }
}
