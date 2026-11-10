using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysInfoWpf.Services.SoftwareCenter.Health
{
    // =====================================================================================================================
    // Danh sách gói TẠM ẨN: bot dò link (Tools/CatalogWatch) công bố kết quả, server QA98 áp luật ẩn và ký.
    // - link_health.json (HealthList, KHÔNG ký): định dạng cũ, bot vẫn dựng được; client KHÔNG còn áp (GĐ3).
    // - availability.json (AvailabilityDoc, ký bằng KHOÁ VẬN HÀNH riêng — AvailabilityKeys, không nằm trong key ring catalog/freshness/root):
    //   client chỉ áp khi chữ ký đúng, còn hạn (TTL ≤ 24 giờ), sequence không lùi, khớp đúng revision/hash catalog đang dùng, dưới trần số mục.
    // Cả hai CHỈ có quyền ẩn: không thêm gói, không đổi link / hash / phiên bản / trust. Bộ cài vẫn chỉ đến từ catalog đã ký.
    // Docs/AVAILABILITY-VA-THUNG-RAC.md.
    // =====================================================================================================================

    public sealed class HealthEntry
    {
        [JsonPropertyName("winget_id")] public string WingetId { get; set; } = "";
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        /// <summary>Phiên bản đã duyệt bị lỗi; rỗng = mọi phiên bản. Catalog đổi sang bản khác thì mục cũ tự hết tác dụng.</summary>
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("since")] public DateTime SinceUtc { get; set; }
        [JsonPropertyName("fails")] public int Fails { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; } = "";
    }

    public sealed class HealthList
    {
        public const int Schema = 1;
        /// <summary>Danh sách ẩn quá 25 % danh mục => coi là bot hỏng / bị phá: bỏ qua cả danh sách, hiện mọi gói như chưa có bot.</summary>
        public const double MaxHiddenFraction = 0.25;
        public const int MaxBytes = 1024 * 1024;

        [JsonPropertyName("schema")] public int SchemaVersion { get; set; } = Schema;
        [JsonPropertyName("generated_at")] public DateTime GeneratedUtc { get; set; }
        [JsonPropertyName("catalog_revision")] public long CatalogRevision { get; set; }
        [JsonPropertyName("hidden")] public List<HealthEntry> Hidden { get; set; } = new();

        static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

        /// <summary>Đọc an toàn: lỗi / quá lớn / schema lạ => null (app hiện mọi gói như không có danh sách).</summary>
        public static HealthList Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes) return null;
            try
            {
                var h = JsonSerializer.Deserialize<HealthList>(json);
                if (h == null || h.SchemaVersion != Schema) return null;
                h.Hidden = (h.Hidden ?? new()).Where(e => e != null && ((e.WingetId ?? "").Length > 0 || (e.Slug ?? "").Length > 0)).ToList();
                foreach (var e in h.Hidden) { e.WingetId ??= ""; e.Slug ??= ""; e.Version ??= ""; e.Reason ??= ""; e.Note ??= ""; }
                return h;
            }
            catch (Exception) { return null; }
        }

        public string ToJson() => JsonSerializer.Serialize(this, Opt);
    }

    public static class HealthFilter
    {
        public static bool Matches(HealthEntry e, string wingetId, string slug, string version)
        {
            bool idHit = e.WingetId.Length > 0 && string.Equals(e.WingetId, wingetId ?? "", StringComparison.OrdinalIgnoreCase)
                      || e.Slug.Length > 0 && string.Equals(e.Slug, slug ?? "", StringComparison.OrdinalIgnoreCase);
            return idHit && (e.Version.Length == 0 || string.Equals(e.Version, version ?? "", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Các khoá (winget id / slug, phiên bản) của gói cần ẩn. Trả rỗng khi không có danh sách, hoặc danh sách ẩn quá
        /// <see cref="HealthList.MaxHiddenFraction"/> số gói (<paramref name="ignored"/> = true để ghi log).
        /// </summary>
        public static List<T> Hidden<T>(IReadOnlyCollection<T> apps, HealthList list, Func<T, (string WingetId, string Slug, string Version)> key, out bool ignored)
        {
            ignored = false;
            if (list == null || list.Hidden.Count == 0 || apps == null || apps.Count == 0) return new List<T>();
            var hit = apps.Where(a => { var k = key(a); return list.Hidden.Any(e => Matches(e, k.WingetId, k.Slug, k.Version)); }).ToList();
            if (hit.Count > apps.Count * HealthList.MaxHiddenFraction) { ignored = true; return new List<T>(); }
            return hit;
        }
    }

    // ================================ phía bot (Tools/CatalogWatch) ================================

    public enum ProbeOutcome
    {
        /// <summary>Link trả đúng file (đã kiểm SHA-256 trong lượt này hoặc dấu vân tay ETag/dung lượng không đổi so với lần kiểm hash gần nhất).</summary>
        Ok,
        /// <summary>Lỗi có thể tự hết: timeout, 429, 5xx, lỗi mạng của bot.</summary>
        Transient,
        /// <summary>Link chết thật: 404/410, trả trang HTML, chuyển hướng ra host không hợp lệ, manifest winget của bản đã duyệt không còn.</summary>
        Broken,
        /// <summary>Link sống nhưng file khác bộ cài đã duyệt (SHA-256 / dung lượng lệch) — tải về sẽ bị app chặn.</summary>
        HashMismatch,
    }

    public sealed class VariantState
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("winget_id")] public string WingetId { get; set; } = "";
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("hidden")] public bool Hidden { get; set; }
        [JsonPropertyName("fails")] public int ConsecutiveFails { get; set; }
        [JsonPropertyName("oks")] public int ConsecutiveOks { get; set; }
        [JsonPropertyName("first_fail")] public DateTime? FirstFailUtc { get; set; }
        [JsonPropertyName("last_ok")] public DateTime? LastOkUtc { get; set; }
        [JsonPropertyName("last_reason")] public string LastReason { get; set; } = "";
        [JsonPropertyName("last_note")] public string LastNote { get; set; } = "";
        /// <summary>Dấu vân tay lần kiểm hash đạt gần nhất (ETag|Last-Modified|Content-Length): giống thì không cần tải lại cả file.</summary>
        [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
        [JsonPropertyName("hashed_at")] public DateTime? HashedUtc { get; set; }
        /// <summary>SHA-256 của artifact đã duyệt (khoá biến thể trong availability.json). Rỗng = catalog không ghim hash.</summary>
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        /// <summary>Lượt lỗi gần nhất ĐƯỢC ĐẾM (lượt cách lượt trước &lt; 6 giờ không đếm thêm).</summary>
        [JsonPropertyName("last_fail")] public DateTime? LastFailUtc { get; set; }
    }

    public static class LinkHealthLogic
    {
        /// <summary>Link chết / sai file: 3 lượt lỗi liên tiếp, mỗi lượt cách lượt được đếm trước ≥ 6 giờ, mới ẩn.</summary>
        public const int BrokenFailsToHide = 3;
        public static readonly TimeSpan BrokenMinGap = TimeSpan.FromHours(6);
        /// <summary>Đã ẩn: đạt 2 lượt liên tiếp (có kiểm hash) mới hiện lại.</summary>
        public const int OksToRestore = 2;
        /// <summary>Kiểm lại toàn bộ hash ít nhất mỗi 7 ngày dù dấu vân tay không đổi.</summary>
        public static readonly TimeSpan RehashEvery = TimeSpan.FromDays(7);

        public static string KeyOf(string wingetId, string slug, string version)
            => ((wingetId ?? "").Length > 0 ? "w:" + wingetId : "s:" + slug).ToLowerInvariant() + "|" + (version ?? "");

        /// <summary>Có cần tải cả file để kiểm hash không (chưa từng kiểm / dấu vân tay đổi / quá hạn / đang bị ẩn).</summary>
        public static bool NeedsFullHash(VariantState s, string fingerprint, DateTime nowUtc)
            => s.HashedUtc == null || s.Hidden || s.Fingerprint.Length == 0 || fingerprint.Length == 0
               || !string.Equals(s.Fingerprint, fingerprint, StringComparison.Ordinal) || nowUtc - s.HashedUtc.Value >= RehashEvery;

        /// <summary>Cập nhật trạng thái sau một lượt dò. Trả true nếu trạng thái ẩn/hiện đổi.</summary>
        public static bool Step(VariantState s, ProbeOutcome o, DateTime nowUtc, string reason = "", string note = "")
        {
            bool was = s.Hidden;
            if (o == ProbeOutcome.Ok)
            {
                // Bỏ ẩn chỉ khi nguồn đã ký HIỆN TẠI phục vụ đúng artifact 2 lượt liền (link mới vẫn phải qua catalog đã ký)
                s.ConsecutiveFails = 0; s.FirstFailUtc = null; s.LastFailUtc = null; s.LastOkUtc = nowUtc;
                s.ConsecutiveOks++;
                if (s.Hidden && s.ConsecutiveOks >= OksToRestore) { s.Hidden = false; s.LastReason = ""; s.LastNote = ""; }
                return was != s.Hidden;
            }
            s.ConsecutiveOks = 0;
            // Lỗi tạm (timeout / 403 / 429 / 5xx / mạng của runner): chỉ đưa vào kiểm lại, KHÔNG BAO GIỜ đếm để ẩn
            if (o == ProbeOutcome.Transient) return false;
            if (s.LastFailUtc == null || nowUtc - s.LastFailUtc.Value >= BrokenMinGap) { s.ConsecutiveFails++; s.LastFailUtc = nowUtc; }
            s.FirstFailUtc ??= nowUtc;
            s.LastReason = reason.Length > 0 ? reason : o.ToString();
            s.LastNote = note ?? "";
            if (s.ConsecutiveFails >= BrokenFailsToHide) s.Hidden = true;
            return was != s.Hidden;
        }

        /// <summary>Dựng payload availability.json (chỉ mục đang ẩn, khoá đúng artifact đã duyệt) (mẫu tham chiếu cho cron PHP của server; test ký bằng <see cref="AvailabilityList.Sign"/>).</summary>
        public static AvailabilityDoc BuildAvailability(IEnumerable<VariantState> states, long catalogRevision, string catalogSha256, long sequence, long nowUnix, long ttlSec = AvailabilityList.MaxTtlSec) => new AvailabilityDoc
        {
            CatalogRevision = catalogRevision, CatalogSha256 = catalogSha256 ?? "", Sequence = sequence,
            GeneratedAt = nowUnix, ExpiresAt = nowUnix + Math.Min(ttlSec, AvailabilityList.MaxTtlSec),
            Hidden = states.Where(s => s.Hidden && s.Version.Length > 0).OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => new AvailabilityEntry
            {
                WingetId = s.WingetId, Slug = s.Slug, Version = s.Version, Sha256 = s.Sha256 ?? "", Reason = Short(s.LastReason, AvailabilityList.MaxReason),
                Since = new DateTimeOffset(DateTime.SpecifyKind(s.FirstFailUtc ?? DateTime.UnixEpoch, DateTimeKind.Utc)).ToUnixTimeSeconds(),
                Note = Short(System.Text.RegularExpressions.Regex.Replace(s.LastNote ?? "", @"\S*://\S*", "[url]"), AvailabilityList.MaxNote),   // payload cấm URL
            }).ToList(),
        };

        static string Short(string s, int max) => (s ?? "").Length <= max ? s ?? "" : s.Substring(0, max);

        /// <summary>Dựng link_health.json từ trạng thái (chỉ mục đang ẩn), sắp xếp ổn định để diff git gọn.</summary>
        public static HealthList Build(IEnumerable<VariantState> states, long catalogRevision, DateTime nowUtc) => new HealthList
        {
            GeneratedUtc = nowUtc, CatalogRevision = catalogRevision,
            Hidden = states.Where(s => s.Hidden).OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => new HealthEntry
            {
                WingetId = s.WingetId, Slug = s.Slug, Version = s.Version, Reason = s.LastReason,
                SinceUtc = s.FirstFailUtc ?? nowUtc, Fails = s.ConsecutiveFails, Note = s.LastNote,
            }).ToList(),
        };
    }

    // ================================ availability.json (ký bằng khoá vận hành) ================================

    /// <summary>
    /// Public key VẬN HÀNH (P-256 PEM) ký availability.json — mảng RIÊNG, không bao giờ thêm vào key ring catalog / freshness / root.
    /// Mảng rỗng = tính năng tắt (chưa có khoá thật). Thu hồi khoá: catalog đã ký ghi availability_revoked_kids (không cần phát bản app mới).
    /// </summary>
    public static class AvailabilityKeys
    {
        public static readonly string[] PublicKeyPems = { };
    }

    /// <summary>Một mục tạm ẩn = khoá biến thể của ĐÚNG một artifact đã duyệt (winget_id / slug + version + sha256 nếu có).</summary>
    public sealed class AvailabilityEntry
    {
        [JsonPropertyName("winget_id")] public string WingetId { get; set; } = "";
        [JsonPropertyName("slug")] public string Slug { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("since")] public long Since { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; } = "";
    }

    public sealed class AvailabilityDoc
    {
        [JsonPropertyName("typ")] public string Typ { get; set; } = AvailabilityList.Typ;
        [JsonPropertyName("schema")] public int Schema { get; set; } = AvailabilityList.Schema;
        [JsonPropertyName("catalog_revision")] public long CatalogRevision { get; set; }
        [JsonPropertyName("catalog_sha256")] public string CatalogSha256 { get; set; } = "";
        [JsonPropertyName("sequence")] public long Sequence { get; set; }
        [JsonPropertyName("generated_at")] public long GeneratedAt { get; set; }
        [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
        [JsonPropertyName("hidden")] public List<AvailabilityEntry> Hidden { get; set; } = new();

        public string ToPayloadJson() => JsonSerializer.Serialize(this);
    }

    /// <summary>Catalog đang dùng (đã verify) — availability chỉ áp khi khớp. Disabled / RevokedKids lấy từ catalog đã ký (đường tin cậy chính).</summary>
    public sealed class AvailabilityContext
    {
        public long CatalogRevision;
        public string CatalogSha256 = "";
        public int PackageCount;
        public bool Disabled;
        public IReadOnlyCollection<string> RevokedKids = Array.Empty<string>();
    }

    public enum AvailabilityError { None, Off, AdminDisabled, BadEnvelope, UnknownKid, RevokedKid, BadSignature, BadPayload, ForbiddenField, NotYetValid, Expired, TtlTooLong, Replay, CatalogMismatch, TooMany }

    public sealed class AvailabilityResult
    {
        public bool Ok => Error == AvailabilityError.None;
        public AvailabilityError Error;
        public string Detail = "";
        public string Kid = "";
        public AvailabilityDoc Doc;
    }

    /// <summary>
    /// Phong bì GIỐNG catalog.php: {"ok":true,"alg":"ES256","kid","revision","etag","envelope":"base64(payload).base64(R||S 64 byte)"} — server QA98 (PHP openssl)
    /// ký bằng KHOÁ VẬN HÀNH riêng. Chỉ "envelope" được tin; các trường bọc ngoài chỉ là gợi ý. Payload: typ "software_availability", alg "ES256",
    /// kid = khoá đã verify, theo DANH SÁCH TRẮNG trường: trường lạ (url, version thay thế, hash mới, khoá…) => từ chối cả bản
    /// (tinh thần ForbiddenFields của catalog). Chuỗi chứa "://" bị cấm. Đặc tả + vector test: Docs/AVAILABILITY-VA-THUNG-RAC.md.
    /// </summary>
    public static class AvailabilityList
    {
        public const string Typ = "software_availability";
        public const int Schema = 1;
        public const int MaxBytes = 256 * 1024, MaxReason = 64, MaxNote = 300, MaxId = 128;
        public const long MaxTtlSec = 24L * 3600, MaxClockSkewSec = 10 * 60;

        static readonly HashSet<string> TopFields = new(StringComparer.Ordinal) { "typ", "alg", "kid", "schema", "catalog_revision", "catalog_sha256", "sequence", "generated_at", "expires_at", "hidden" };
        static readonly HashSet<string> EntryFields = new(StringComparer.Ordinal) { "winget_id", "slug", "version", "sha256", "reason", "since", "note" };
        static readonly System.Text.RegularExpressions.Regex HexRe = new("^[0-9a-f]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>Ký payload bằng khoá riêng vận hành (test / công cụ; bản thật do server PHP ký). Tự ghi alg + kid vào payload. App người dùng không gọi.</summary>
        public static string Sign(string payloadJson, string privatePem)
        {
            using var k = System.Security.Cryptography.ECDsa.Create();
            k.ImportFromPem(privatePem);
            if (k.KeySize != 256) throw new ArgumentException("availability key must be ECDSA P-256");
            string kid = SysInfoWpf.Services.Licensing.ShopLicenseKeys.KidFromPem(k.ExportSubjectPublicKeyInfoPem());
            var o = System.Text.Json.Nodes.JsonNode.Parse(payloadJson)!.AsObject();
            o["alg"] = "ES256"; o["kid"] = kid;
            byte[] p = System.Text.Encoding.UTF8.GetBytes(o.ToJsonString());
            byte[] sig = k.SignData(p, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return new System.Text.Json.Nodes.JsonObject
            {
                ["ok"] = true, ["alg"] = "ES256", ["kid"] = kid,
                ["revision"] = o["catalog_revision"]?.DeepClone(), ["etag"] = SysInfoWpf.Services.SoftwareCenter.Trust.SignedBlob.Sha256Hex(p).Substring(0, 16),
                ["envelope"] = Convert.ToBase64String(p) + "." + Convert.ToBase64String(sig),
            }.ToJsonString();
        }

        /// <summary>
        /// Kiểm đủ: khoá bật, quản trị không tắt, chữ ký khớp khoá vận hành ghim + kid chưa thu hồi, danh sách trắng trường, hạn (TTL ≤ 24 giờ),
        /// sequence ≥ <paramref name="minSequence"/> (chống phát lại), khớp revision / sha catalog, trần số mục. Không bao giờ ném.
        /// </summary>
        public static AvailabilityResult Verify(string text, IReadOnlyCollection<string> pinnedPems, AvailabilityContext ctx, long nowUnix, long minSequence)
        {
            var res = new AvailabilityResult();
            if (pinnedPems == null || pinnedPems.Count == 0) return Fail(res, AvailabilityError.Off, "no availability key pinned");
            if (ctx == null) return Fail(res, AvailabilityError.CatalogMismatch, "no catalog");
            if (ctx.Disabled) return Fail(res, AvailabilityError.AdminDisabled, "availability_disabled in signed catalog");
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxBytes * 2) return Fail(res, AvailabilityError.BadEnvelope, "size");
            string envelope = text.Trim(), hintKid = "";
            if (envelope.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    using var w = JsonDocument.Parse(envelope);
                    if (w.RootElement.ValueKind != JsonValueKind.Object) return Fail(res, AvailabilityError.BadEnvelope, "wrapper");
                    envelope = Str(w.RootElement, "envelope"); hintKid = Str(w.RootElement, "kid");
                }
                catch (JsonException) { return Fail(res, AvailabilityError.BadEnvelope, "wrapper json"); }
            }
            if (envelope.Length == 0) return Fail(res, AvailabilityError.BadEnvelope, "no envelope");
            var ring = SysInfoWpf.Services.SoftwareCenter.Trust.CatalogRootChain.RingOf(pinnedPems);
            var se = SysInfoWpf.Services.SoftwareCenter.Trust.SignedBlob.Verify(envelope, ring, MaxBytes, out byte[] payload, out string kid);
            if (se == SysInfoWpf.Services.SoftwareCenter.Trust.SignedBlob.Error.BadEnvelope) return Fail(res, AvailabilityError.BadEnvelope, "envelope format / size");
            if (se != SysInfoWpf.Services.SoftwareCenter.Trust.SignedBlob.Error.None)
                return hintKid.Length > 0 && !ring.All.Any(kv => kv.Key == hintKid)
                    ? Fail(res, AvailabilityError.UnknownKid, "kid " + hintKid)
                    : Fail(res, AvailabilityError.BadSignature, "signature");
            res.Kid = kid;
            if (ctx.RevokedKids != null && ctx.RevokedKids.Contains(kid, StringComparer.OrdinalIgnoreCase)) return Fail(res, AvailabilityError.RevokedKid, "kid " + kid + " revoked by catalog");
            // Chỉ từ đây mới đọc payload
            var d = new AvailabilityDoc();
            try
            {
                using var doc = JsonDocument.Parse(new System.Text.UTF8Encoding(false, true).GetString(payload), new JsonDocumentOptions { MaxDepth = 8 });
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object) return Fail(res, AvailabilityError.BadPayload, "not object");
                foreach (var f in r.EnumerateObject()) if (!TopFields.Contains(f.Name)) return Fail(res, AvailabilityError.ForbiddenField, "field '" + f.Name + "'");
                if (Str(r, "typ") != Typ || Str(r, "alg") != "ES256" || Str(r, "kid") != kid || Num(r, "schema") != Schema) return Fail(res, AvailabilityError.BadPayload, "typ / alg / kid / schema");
                d.CatalogRevision = Num(r, "catalog_revision"); d.CatalogSha256 = Str(r, "catalog_sha256").ToLowerInvariant();
                d.Sequence = Num(r, "sequence"); d.GeneratedAt = Num(r, "generated_at"); d.ExpiresAt = Num(r, "expires_at");
                if (d.CatalogRevision <= 0 || d.Sequence <= 0 || d.GeneratedAt <= 0 || d.ExpiresAt <= d.GeneratedAt
                    || d.CatalogSha256.Length > 0 && !HexRe.IsMatch(d.CatalogSha256)) return Fail(res, AvailabilityError.BadPayload, "fields");
                if (!r.TryGetProperty("hidden", out var hid) || hid.ValueKind != JsonValueKind.Array) return Fail(res, AvailabilityError.BadPayload, "hidden");
                foreach (var e in hid.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) return Fail(res, AvailabilityError.BadPayload, "entry");
                    foreach (var f in e.EnumerateObject())
                    {
                        if (!EntryFields.Contains(f.Name)) return Fail(res, AvailabilityError.ForbiddenField, "entry field '" + f.Name + "'");
                        if (f.Value.ValueKind == JsonValueKind.String && (f.Value.GetString() ?? "").Contains("://", StringComparison.Ordinal)) return Fail(res, AvailabilityError.ForbiddenField, "url in '" + f.Name + "'");
                    }
                    var x = new AvailabilityEntry
                    {
                        WingetId = Str(e, "winget_id"), Slug = Str(e, "slug"), Version = Str(e, "version"), Sha256 = Str(e, "sha256").ToLowerInvariant(),
                        Reason = Str(e, "reason"), Since = Num(e, "since"), Note = Str(e, "note"),
                    };
                    if (x.WingetId.Length == 0 && x.Slug.Length == 0 || x.WingetId.Length > MaxId || x.Slug.Length > MaxId || x.Version.Length == 0 || x.Version.Length > MaxId
                        || x.Sha256.Length > 0 && !HexRe.IsMatch(x.Sha256) || x.Reason.Length > MaxReason || x.Note.Length > MaxNote)
                        return Fail(res, AvailabilityError.BadPayload, "entry key / length");
                    d.Hidden.Add(x);
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is System.Text.DecoderFallbackException || ex is InvalidOperationException) { return Fail(res, AvailabilityError.BadPayload, "payload json"); }

            if (d.ExpiresAt - d.GeneratedAt > MaxTtlSec) return Fail(res, AvailabilityError.TtlTooLong, "ttl > 24h");
            if (d.GeneratedAt > nowUnix + MaxClockSkewSec) return Fail(res, AvailabilityError.NotYetValid, "generated_at in the future");
            if (nowUnix >= d.ExpiresAt) return Fail(res, AvailabilityError.Expired, "expired");
            if (d.Sequence < minSequence) return Fail(res, AvailabilityError.Replay, $"sequence {d.Sequence} < {minSequence}");
            if (d.CatalogRevision != ctx.CatalogRevision || d.CatalogSha256.Length > 0 && !string.Equals(d.CatalogSha256, ctx.CatalogSha256 ?? "", StringComparison.OrdinalIgnoreCase))
                return Fail(res, AvailabilityError.CatalogMismatch, $"catalog revision {d.CatalogRevision} vs {ctx.CatalogRevision} / sha256");
            if (d.Hidden.Count > ctx.PackageCount * HealthList.MaxHiddenFraction) return Fail(res, AvailabilityError.TooMany, $"{d.Hidden.Count} hidden > {HealthList.MaxHiddenFraction:P0} of {ctx.PackageCount}");
            res.Doc = d;
            return res;
        }

        /// <summary>
        /// Chọn bản áp: bản vừa tải nếu đạt, không thì bản đã lưu (endpoint lỗi / bản mới hỏng => giữ bản hợp lệ gần nhất tới khi nó hết hạn).
        /// Không bản nào đạt => Accepted null = bỏ lớp tạm ẩn. FetchedError = lý do từ chối bản vừa tải (để báo quản trị).
        /// </summary>
        public static (AvailabilityResult Accepted, bool FromFetched, AvailabilityResult FetchedError) Pick(string fetched, string cached, IReadOnlyCollection<string> pinnedPems, AvailabilityContext ctx, long nowUnix, long minSequence)
        {
            AvailabilityResult bad = null;
            if (!string.IsNullOrWhiteSpace(fetched))
            {
                var r = Verify(fetched, pinnedPems, ctx, nowUnix, minSequence);
                if (r.Ok) return (r, true, null);
                bad = r;
            }
            if (!string.IsNullOrWhiteSpace(cached) && cached != fetched)
            {
                var r = Verify(cached, pinnedPems, ctx, nowUnix, minSequence);
                if (r.Ok) return (r, false, bad);
            }
            return (null, false, bad);
        }

        /// <summary>Đúng artifact: (winget id hoặc slug) + version, và sha256 nếu mục có ghi (catalog đổi file => mục cũ hết tác dụng).</summary>
        public static bool Matches(AvailabilityEntry e, string wingetId, string slug, string version, string sha256)
        {
            bool idHit = e.WingetId.Length > 0 && string.Equals(e.WingetId, wingetId ?? "", StringComparison.OrdinalIgnoreCase)
                      || e.Slug.Length > 0 && string.Equals(e.Slug, slug ?? "", StringComparison.OrdinalIgnoreCase);
            return idHit && string.Equals(e.Version, version ?? "", StringComparison.OrdinalIgnoreCase)
                   && (e.Sha256.Length == 0 || string.Equals(e.Sha256, sha256 ?? "", StringComparison.OrdinalIgnoreCase));
        }

        public static AvailabilityEntry Find(AvailabilityDoc doc, (string WingetId, string Slug, string Version, string Sha256) k)
            => doc?.Hidden.FirstOrDefault(e => Matches(e, k.WingetId, k.Slug, k.Version, k.Sha256));

        public static List<T> Hidden<T>(IEnumerable<T> apps, AvailabilityDoc doc, Func<T, (string WingetId, string Slug, string Version, string Sha256)> key)
            => doc == null || doc.Hidden.Count == 0 || apps == null ? new List<T>() : apps.Where(a => Find(doc, key(a)) != null).ToList();

        static AvailabilityResult Fail(AvailabilityResult r, AvailabilityError e, string d) { r.Error = e; r.Detail = d; r.Doc = null; return r; }
        static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
        static long Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long x) ? x : 0;
    }
}
