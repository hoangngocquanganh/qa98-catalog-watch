using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SysInfoWpf.Services.SoftwareCenter.Trust
{
    /// <summary>Một nguồn phát cặp catalog + freshness. FreshnessUrl rỗng = nguồn chưa phát freshness (chế độ chuyển tiếp, như cPanel hiện tại).</summary>
    public sealed class CatalogSource
    {
        public string Name = "", CatalogUrl = "", FreshnessUrl = "";
    }

    /// <summary>Kết quả 1 lần tải cặp từ 1 nguồn (envelope đã bóc wrapper {"envelope": …}).</summary>
    public sealed class PairFetch
    {
        /// <summary>HTTP status của catalog; 0 = lỗi mạng / timeout.</summary>
        public int Status;
        /// <summary>null khi 304 / lỗi.</summary>
        public string Catalog;
        /// <summary>null = không tải được / nguồn không có freshness.</summary>
        public string Freshness;
        public string Etag = "", Error = "";
        public int RetryAfterSec;
    }

    /// <summary>ETag đã lưu GẮN với nguồn đã cấp nó: 304 chỉ có nghĩa với cache tải từ đúng nguồn đó.</summary>
    public sealed class CatalogMeta
    {
        public string Source = "", Etag = "";
    }

    public sealed class PairOutcome
    {
        public CatalogLoadResult Result;
        /// <summary>Nguồn đã cấp cặp được nhận (hoặc 304 khớp cache). Rỗng = dùng cache / bản quick.</summary>
        public string Source = "";
        /// <summary>Meta mới cần lưu; null = giữ meta cũ (không nhận cặp nào).</summary>
        public CatalogMeta NewMeta;
    }

    /// <summary>
    /// Danh sách nguồn có thứ tự: Pages (nếu đã cấu hình) rồi cPanel. Mỗi lần thử lấy MỘT cặp từ CÙNG một nguồn (không trộn catalog nguồn này với freshness nguồn kia).
    /// Lỗi mạng / 5xx / cặp tải dở: thử lại 1 lần sau backoff ngắn có jitter, rồi sang nguồn sau. 429 / cặp bị từ chối: sang nguồn sau ngay.
    /// Nguồn hỏng được nhớ trong phiên (backoff tăng dần, có trần) để mở cửa sổ nhiều lần không gọi lại liên tục. Một request cho cả danh mục.
    /// </summary>
    public sealed class CatalogSourcePicker
    {
        public const int MaxTriesPerSource = 2;
        public static readonly TimeSpan RetryDelayMax = TimeSpan.FromSeconds(2), DownMin = TimeSpan.FromMinutes(1), DownMax = TimeSpan.FromMinutes(15);

        /// <summary>Khoá tải dùng chung: nhiều cửa sổ / tự cập nhật gọi cùng lúc => chỉ 1 lượt gọi mạng.</summary>
        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, (int Fails, long Until)> _down = new Dictionary<string, (int, long)>(StringComparer.Ordinal);

        public Func<double> Random01 = () => Random.Shared.NextDouble();
        public Func<TimeSpan, Task> Delay = d => Task.Delay(d);

        /// <summary>Pages trước (bỏ qua khi base rỗng), cPanel sau. Chỉ nhận https.</summary>
        public static List<CatalogSource> Ordered(string pagesBaseUrl, string cpanelCatalogUrl, string cpanelFreshnessUrl)
        {
            var list = new List<CatalogSource>();
            string b = (pagesBaseUrl ?? "").Trim().TrimEnd('/');
            if (SoftwareCatalogParser.IsHttps(b + "/"))
                list.Add(new CatalogSource { Name = "pages", CatalogUrl = b + "/catalog.json", FreshnessUrl = b + "/freshness.json" });
            if (SoftwareCatalogParser.IsHttps(cpanelCatalogUrl))
                list.Add(new CatalogSource { Name = "cpanel", CatalogUrl = cpanelCatalogUrl, FreshnessUrl = SoftwareCatalogParser.IsHttps(cpanelFreshnessUrl) ? cpanelFreshnessUrl : "" });
            return list;
        }

        public bool IsDown(string source, long nowUnix) => _down.TryGetValue(source, out var d) && nowUnix < d.Until;

        void MarkDown(string source, long nowUnix, int retryAfterSec)
        {
            int fails = (_down.TryGetValue(source, out var d) ? d.Fails : 0) + 1;
            double sec = Math.Min(DownMax.TotalSeconds, DownMin.TotalSeconds * Math.Pow(2, Math.Min(fails - 1, 10))) * (0.75 + 0.25 * Random01());
            sec = Math.Min(DownMax.TotalSeconds, Math.Max(sec, retryAfterSec));
            _down[source] = (fails, nowUnix + (long)Math.Ceiling(sec));
        }

        TimeSpan RetryDelay(int attempt) => TimeSpan.FromMilliseconds(Math.Min(RetryDelayMax.TotalMilliseconds, 500 * Math.Pow(2, attempt)) * (0.5 + 0.5 * Random01()));

        /// <param name="quick">Gọi TRONG khoá: trả kết quả (cache vừa hỏi máy chủ gần đây) thì không gọi mạng; null = phải tải.</param>
        /// <param name="fetch">(nguồn, etag cho If-None-Match hoặc "") => một cặp từ CHÍNH nguồn đó.</param>
        /// <param name="load">(catalog, lỗi, freshness) => <see cref="SoftwareCatalogStore.Load(string, string, Licensing.LicenseKeyRing, long, string)"/>.</param>
        public async Task<PairOutcome> LoadAsync(IReadOnlyList<CatalogSource> sources, CatalogMeta meta, bool hasCache, Func<long> nowUnix,
            Func<CatalogLoadResult> quick, Func<CatalogSource, string, Task<PairFetch>> fetch, Func<string, string, string, CatalogLoadResult> load)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var q = quick?.Invoke();
                if (q != null) return new PairOutcome { Result = q };
                meta ??= new CatalogMeta();
                var errors = new List<string>();
                CatalogLoadResult rejected = null;
                foreach (var s in sources)
                {
                    if (IsDown(s.Name, nowUnix())) { errors.Add(s.Name + ": skipped (failed recently)"); continue; }
                    bool useEtag = hasCache && meta.Etag.Length > 0 && meta.Source == s.Name, retried304 = false;
                    int retryAfter = 0;
                    for (int t = 0; t < MaxTriesPerSource; t++)
                    {
                        var f = await fetch(s, useEtag ? meta.Etag : "").ConfigureAwait(false) ?? new PairFetch { Error = "no response" };
                        bool pairIncomplete = s.FreshnessUrl.Length > 0 && string.IsNullOrWhiteSpace(f.Freshness);
                        if (f.Status == 304 && !pairIncomplete)
                        {
                            var r = load(null, "", f.Freshness);
                            if (r?.Trust?.Ok == true && r.FromCache) { _down.Remove(s.Name); return new PairOutcome { Result = r, Source = s.Name, NewMeta = meta }; }
                            // 304 mà cache không dùng được => hỏi lại KHÔNG kèm ETag (không tính lượt)
                            if (!retried304) { retried304 = true; useEtag = false; t--; continue; }
                            errors.Add(s.Name + ": 304 without usable cache");
                            break;
                        }
                        if (f.Status == 200 && !string.IsNullOrWhiteSpace(f.Catalog) && !pairIncomplete)
                        {
                            var r = load(f.Catalog, "", f.Freshness);
                            if (r != null && r.Committed) { _down.Remove(s.Name); return new PairOutcome { Result = r, Source = s.Name, NewMeta = new CatalogMeta { Source = s.Name, Etag = f.Etag ?? "" } }; }
                            rejected = r;   // chữ ký sai / phát lại bản cũ / cặp lệch: thử lại cùng nguồn không giúp gì
                            errors.Add(s.Name + ": " + (r?.OnlineError ?? "rejected"));
                            break;
                        }
                        string why = f.Status == 0 ? f.Error : f.Status is 200 or 304 ? "freshness missing (incomplete pair)" : "HTTP " + f.Status;
                        errors.Add(s.Name + ": " + why);
                        if (f.Status == 429) { retryAfter = f.RetryAfterSec; break; }
                        bool transient = f.Status == 0 || f.Status >= 500 || f.Status is 200 or 304;
                        if (!transient || t + 1 >= MaxTriesPerSource) break;
                        await Delay(RetryDelay(t)).ConfigureAwait(false);
                    }
                    MarkDown(s.Name, nowUnix(), retryAfter);
                }
                // Không nguồn nào cho cặp hợp lệ: kết quả của cặp bị từ chối (đã rơi về cache / chỉ xem) hoặc cache
                return new PairOutcome { Result = rejected ?? load(null, string.Join("; ", errors), null) };
            }
            finally { _gate.Release(); }
        }
    }
}
