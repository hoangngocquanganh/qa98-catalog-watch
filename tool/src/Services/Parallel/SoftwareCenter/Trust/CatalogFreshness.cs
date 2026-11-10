using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SysInfoWpf.Services.Licensing;

namespace SysInfoWpf.Services.SoftwareCenter.Trust
{
    /// <summary>
    /// Trạng thái độ mới của danh mục đang dùng:
    /// Transition = chưa từng có freshness (danh mục v1 hiện hành) => giữ hành vi cũ;
    /// Ok = freshness hợp lệ, khớp danh mục, còn hạn; Expired / Missing / Invalid => DỪNG CÀI MỚI, vẫn cho xem.
    /// </summary>
    public enum FreshnessState { Transition, Ok, Expired, Missing, Invalid }

    /// <summary>Kill-switch: cấm cài khẩn cấp 1 gói (mọi bản / 1 bản / dải bản). Không ghi version / min / max = mọi bản.</summary>
    public sealed class FreshnessBlock
    {
        public string Package = "", Version = "", MinVersion = "", MaxVersion = "", ReasonVi = "", ReasonEn = "";

        public bool Matches(string slug, string version)
        {
            if (!string.Equals(slug, Package, StringComparison.Ordinal)) return false;
            if (Version.Length == 0 && MinVersion.Length == 0 && MaxVersion.Length == 0 || Version == "*") return true;
            string v = version ?? "";
            if (Version.Length > 0) return string.Equals(v, Version, StringComparison.OrdinalIgnoreCase) || v.Length > 0 && SoftwareCenterPlanner.CompareVersion(v, Version) == 0;
            if (v.Length == 0) return true;   // không biết bản => chặn cho chắc
            return (MinVersion.Length == 0 || SoftwareCenterPlanner.CompareVersion(v, MinVersion) >= 0)
                && (MaxVersion.Length == 0 || SoftwareCenterPlanner.CompareVersion(v, MaxVersion) <= 0);
        }
    }

    public sealed class FreshnessDoc
    {
        public long CatalogRevision, IssuedAt, ExpiresAt;
        public string CatalogSha256 = "";
        public List<FreshnessBlock> Blocked = new List<FreshnessBlock>();
    }

    public sealed class FreshnessResult
    {
        public bool Ok => Error == CatalogTrustError.None;
        public CatalogTrustError Error;
        public string Detail = "";
        public FreshnessDoc Doc;
    }

    /// <summary>freshness.json (gộp snapshot + timestamp của TUF), ký bằng khóa catalog. Đặc tả: Docs/GD5-SCHEMA-V2.md §3.</summary>
    public static class CatalogFreshness
    {
        public const string Typ = "catalog_freshness";
        public const int MaxBytes = 256 * 1024, MaxBlocked = 500;
        public const long DefaultValiditySec = 10L * 24 * 3600, MaxValiditySec = 14L * 24 * 3600;
        /// <summary>Giá trị SoftwareVersion.Invalid của bản bị kill-switch (engine trả KillSwitchBlocked).</summary>
        public const string KillSwitchInvalid = "BLOCKED_BY_FRESHNESS";

        static readonly Regex SlugRe = new Regex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
        static readonly Regex HexRe = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Kiểm chữ ký + cấu trúc + chống rollback (revision &lt; sàn, issued_at cũ hơn bản đã nhận => Rollback; issued_at ở tương lai => NotYetValid).
        /// KHÔNG từ chối vì hết hạn: bên gọi đánh dấu Expired (dừng cài mới, vẫn cho xem).
        /// </summary>
        public static FreshnessResult Verify(string envelope, LicenseKeyRing ring, long nowUnix, long minRevision, long minIssuedAt)
        {
            var res = new FreshnessResult();
            var e = SignedBlob.Verify(envelope, ring, MaxBytes, out var payload, out var kid);
            if (e != SignedBlob.Error.None)
                return Fail(res, e switch { SignedBlob.Error.NoKey => CatalogTrustError.NoKey, SignedBlob.Error.BadEnvelope => CatalogTrustError.BadEnvelope,
                    SignedBlob.Error.CryptoFailed => CatalogTrustError.CryptoFailed, _ => CatalogTrustError.BadSignature }, "freshness " + e);
            using var doc = SignedBlob.ParsePayload(payload, kid, Typ, out string why);
            if (doc == null) return Fail(res, CatalogTrustError.BadPayload, "freshness " + why);
            var r = doc.RootElement;
            var d = new FreshnessDoc
            {
                CatalogRevision = SignedBlob.Num(r, "catalog_revision"),
                IssuedAt = SignedBlob.Num(r, "issued_at"),
                ExpiresAt = SignedBlob.Num(r, "expires_at"),
                CatalogSha256 = SignedBlob.Str(r, "catalog_sha256").ToLowerInvariant(),
            };
            if (d.CatalogRevision <= 0 || d.IssuedAt <= 0 || d.ExpiresAt <= d.IssuedAt || !HexRe.IsMatch(d.CatalogSha256))
                return Fail(res, CatalogTrustError.BadPayload, "freshness fields");
            if (d.ExpiresAt - d.IssuedAt > MaxValiditySec) return Fail(res, CatalogTrustError.ValidityTooLong, "freshness validity too long");
            if (d.IssuedAt > nowUnix + SoftwareCatalogTrust.MaxClockSkewSec) return Fail(res, CatalogTrustError.NotYetValid, "freshness issued_at in the future");
            if (r.TryGetProperty("blocked", out var bl))
            {
                if (bl.ValueKind != JsonValueKind.Array || bl.GetArrayLength() > MaxBlocked) return Fail(res, CatalogTrustError.BadPayload, "blocked");
                foreach (var b in bl.EnumerateArray())
                {
                    var x = new FreshnessBlock
                    {
                        Package = SignedBlob.Str(b, "package"), Version = SignedBlob.Str(b, "version"),
                        MinVersion = SignedBlob.Str(b, "min_version"), MaxVersion = SignedBlob.Str(b, "max_version"),
                        ReasonVi = SignedBlob.Str(b, "reason_vi"), ReasonEn = SignedBlob.Str(b, "reason_en"),
                    };
                    // Mục chặn sai cấu trúc => cả freshness không hợp lệ (kill-switch không được phép "bỏ qua lặng lẽ")
                    if (!SlugRe.IsMatch(x.Package)) return Fail(res, CatalogTrustError.BadPayload, "blocked package");
                    d.Blocked.Add(x);
                }
            }
            if (d.CatalogRevision < minRevision) return Fail(res, CatalogTrustError.Rollback, $"freshness revision {d.CatalogRevision} < {minRevision}");
            if (d.IssuedAt < minIssuedAt) return Fail(res, CatalogTrustError.Rollback, $"freshness issued_at {d.IssuedAt} < {minIssuedAt}");
            res.Doc = d;
            return res;
        }

        /// <summary>Đánh dấu các bản bị kill-switch: Invalid = <see cref="KillSwitchInvalid"/> (planner / Hub / engine đều bỏ qua bản Invalid).</summary>
        public static int ApplyBlocked(SoftwareCatalog c, IReadOnlyCollection<FreshnessBlock> blocks)
        {
            int n = 0;
            if (c == null || blocks == null || blocks.Count == 0) return 0;
            foreach (var p in c.Packages)
                foreach (var v in p.Versions)
                    if (blocks.Any(b => b.Matches(p.Slug, v.Version))) { v.Invalid = KillSwitchInvalid; n++; }
            return n;
        }

        /// <summary>Dừng cài mới (freshness hết hạn / thiếu / sai): gói vẫn hiện + nhận diện đã cài, chỉ còn nút trang chính thức.</summary>
        public static void FreezeInstalls(SoftwareCatalog c)
        {
            if (c == null) return;
            foreach (var p in c.Packages)
            {
                p.InstallFrozen = true;
                if (SoftwareCenterPlanner.IsAutoAction(p.Action)) p.Action = PackageAction.OfficialPageOnly;
            }
        }

        static FreshnessResult Fail(FreshnessResult r, CatalogTrustError e, string d) { r.Error = e; r.Detail = d; r.Doc = null; return r; }
    }
}
