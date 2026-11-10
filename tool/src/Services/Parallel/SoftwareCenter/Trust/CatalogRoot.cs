using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SysInfoWpf.Services.Licensing;

namespace SysInfoWpf.Services.SoftwareCenter.Trust
{
    /// <summary>
    /// GĐ5 — neo tin cậy duy nhất của danh mục: public key ROOT (ECDSA P-256) ghim cứng trong binary.
    /// Root (giữ OFFLINE) chỉ ký root.json = danh sách khóa catalog được tin + ngưỡng (1-trên-N) + khóa root kế tiếp.
    /// Quy trình tạo root / điền khóa / phát hành: Docs/GD5-RUNBOOK-XOAY-KHOA.md.
    /// </summary>
    public static class CatalogRootKeys
    {
        // ================= CHỖ TRỐNG — CHỦ DỰ ÁN ĐIỀN =================
        // Chưa có khóa root thật => CHẾ ĐỘ CHUYỂN TIẾP: client tiếp tục tin danh sách khóa catalog cũ
        // (SoftwareCatalogKeys.PublicKeyPems) đúng như bản 3.0.x đang chạy ngoài thực tế.
        // Điền đồng thời CẢ HAI mảng dưới (public key root + chuỗi root.json đã ký) — test GD5_TrustTests chặn bản phát hành điền thiếu.
        /// <summary>PEM public key ROOT (P-256). Rỗng = chưa cấp root (chuyển tiếp).</summary>
        public static readonly string[] PinnedRootPems = { };

        /// <summary>Chuỗi root.json đã ký (envelope, theo thứ tự version tăng dần) đóng gói trong bản app này.</summary>
        public static readonly string[] BundledRootChain = { };
        // ===============================================================

        public static bool Provisioned => PinnedRootPems.Length > 0;
    }

    public sealed class RootDoc
    {
        public int Version;
        public long IssuedAt;
        public int Threshold;
        public List<string> RootKeyPems = new List<string>();
        public List<string> CatalogKeyPems = new List<string>();
    }

    public sealed class RootChainResult
    {
        public bool Ok => Error.Length == 0;
        public string Error = "";
        public RootDoc Root;
        /// <summary>Khóa catalog root cuối cùng liệt kê (kid -> PEM). Lỗi => ring RỖNG (Verify trả NoKey: fail-closed).</summary>
        public LicenseKeyRing CatalogRing = new LicenseKeyRing();
    }

    /// <summary>
    /// Xác thực chuỗi root kiểu TUF: root[0] ký bởi khóa root ghim; root[n+1] có version = n+1 và ký bởi một khóa trong
    /// root_keys của root[n] (xoay root theo chuỗi). Lỗi bất kỳ mắt xích nào => FAIL-CLOSED cả chuỗi (không bao giờ trả tập khóa rỗng rồi cho qua).
    /// </summary>
    public static class CatalogRootChain
    {
        public const string Typ = "qa98_root";
        public const int MaxRootBytes = 64 * 1024;
        public const int MaxKeys = 8;

        public static RootChainResult Resolve(IReadOnlyCollection<string> pinnedRootPems, IEnumerable<string> chain, int minVersion = 0)
        {
            var res = new RootChainResult();
            var trusted = RingOf(pinnedRootPems);
            if (trusted.Count == 0) return Fail(res, "no pinned root key");
            RootDoc prev = null;
            int i = 0;
            foreach (var env in chain ?? Array.Empty<string>())
            {
                var e = SignedBlob.Verify(env, trusted, MaxRootBytes, out var payload, out var kid);
                if (e != SignedBlob.Error.None) return Fail(res, $"root[{i}] {e}");
                using var doc = SignedBlob.ParsePayload(payload, kid, Typ, out string why);
                if (doc == null) return Fail(res, $"root[{i}] {why}");
                var d = Parse(doc.RootElement, out why);
                if (d == null) return Fail(res, $"root[{i}] {why}");
                if (prev == null ? d.Version < 1 : d.Version != prev.Version + 1) return Fail(res, $"root[{i}] version {d.Version}");
                if (prev != null && d.IssuedAt < prev.IssuedAt) return Fail(res, $"root[{i}] issued_at older");
                trusted = RingOf(d.RootKeyPems);
                prev = d; i++;
            }
            if (prev == null) return Fail(res, "empty root chain");
            if (prev.Version < minVersion) return Fail(res, $"root version {prev.Version} < {minVersion}");
            res.Root = prev;
            res.CatalogRing = RingOf(prev.CatalogKeyPems);
            if (res.CatalogRing.Count == 0) return Fail(res, "no catalog key");
            return res;
        }

        static RootDoc Parse(JsonElement r, out string why)
        {
            why = "";
            var d = new RootDoc
            {
                Version = (int)Math.Min(int.MaxValue, SignedBlob.Num(r, "version")),
                IssuedAt = SignedBlob.Num(r, "issued_at"),
                Threshold = (int)Math.Min(int.MaxValue, SignedBlob.Num(r, "threshold")),
                RootKeyPems = Pems(r, "root_keys"),
                CatalogKeyPems = Pems(r, "catalog_keys"),
            };
            // Ngưỡng > 1 cần nhiều chữ ký trên catalog — client này chưa hỗ trợ => từ chối (không hạ ngưỡng âm thầm).
            if (d.Threshold != 1) { why = "threshold " + d.Threshold; return null; }
            if (d.IssuedAt <= 0) { why = "issued_at"; return null; }
            if (d.RootKeyPems == null || d.RootKeyPems.Count == 0) { why = "root_keys"; return null; }
            if (d.CatalogKeyPems == null || d.CatalogKeyPems.Count == 0) { why = "catalog_keys"; return null; }
            return d;
        }

        /// <summary>Mảng PEM; một phần tử sai => null (cả tài liệu không hợp lệ, không bỏ qua lặng lẽ).</summary>
        static List<string> Pems(JsonElement r, string name)
        {
            if (!r.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() > MaxKeys) return null;
            var l = new List<string>();
            foreach (var x in a.EnumerateArray())
            {
                string pem = x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "";
                if (!SignedBlob.IsP256PublicPem(pem)) return null;
                l.Add(pem);
            }
            return l;
        }

        public static LicenseKeyRing RingOf(IEnumerable<string> pems)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pem in pems ?? Array.Empty<string>())
            {
                string kid = ShopLicenseKeys.KidFromPem(pem);
                if (kid.Length > 0 && !pem.Contains("PRIVATE KEY", StringComparison.Ordinal)) d[kid] = pem;
            }
            return new LicenseKeyRing(d);
        }

        static RootChainResult Fail(RootChainResult r, string why) { r.Error = why; r.Root = null; r.CatalogRing = new LicenseKeyRing(); return r; }
    }
}
