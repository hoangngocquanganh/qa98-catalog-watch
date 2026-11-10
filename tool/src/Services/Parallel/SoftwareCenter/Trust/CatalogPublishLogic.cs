using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SysInfoWpf.Services.Licensing;

namespace SysInfoWpf.Services.SoftwareCenter.Trust
{
    /// <summary>
    /// GĐ5 — lõi công cụ phát hành (Tools/CatalogPublish dùng lại qua Compile Link; nằm ở đây để SysInfo.Tests kiểm được K8).
    /// KHÔNG chứa khóa riêng: khóa do công cụ đọc từ đường dẫn tham số rồi truyền ECDsa vào. App người dùng không bao giờ gọi Sign*.
    /// Luật: in diff hash / signer so với catalog trước, chỉ ký khi người duyệt nhập ĐÚNG mã xác nhận của đúng diff đó; revision = trước + 1.
    /// </summary>
    public static class CatalogPublishLogic
    {
        public const long DefaultCatalogValiditySec = 7L * 24 * 3600;
        /// <summary>GĐ3: trường quản trị lớp tạm ẩn ở gốc nguồn / payload (availability_disabled: bool, availability_revoked_kids: string[]).</summary>
        public static readonly string[] AdminFields = { "availability_disabled", "availability_revoked_kids" };

        public sealed class Request
        {
            /// <summary>Nguồn chưa ký: {"categories":[...], "packages":[...]}.</summary>
            public string SourceJson = "";
            /// <summary>Envelope catalog đã ký lần trước (null/rỗng = lần đầu, khi đó phải có FirstRevision).</summary>
            public string PreviousEnvelope;
            public long FirstRevision;
            public int Schema = 1;
            public long Now;
            public long CatalogValiditySec = DefaultCatalogValiditySec;
            public long FreshnessValiditySec = CatalogFreshness.DefaultValiditySec;
            /// <summary>Mảng kill-switch JSON (tùy chọn): [{"package":"slug","version":"1.2"|"*","min_version":..,"max_version":..,"reason_vi":..,"reason_en":..}].</summary>
            public string BlockedJson;
        }

        public sealed class Plan
        {
            public string Error = "";
            public long Revision;
            public List<string> Diff = new List<string>();
            public List<string> Warnings = new List<string>();
            /// <summary>Mã người duyệt phải gõ lại để ký. Đổi bất kỳ hash / signer / nguồn nào => mã khác.</summary>
            public string Token = "";
        }

        public sealed class Output
        {
            public long Revision;
            public string CatalogEnvelope = "", FreshnessEnvelope = "", CatalogSha256 = "";
        }

        // ---------------------------------------------------------------- Kế hoạch (không ký)

        public static Plan MakePlan(Request r)
        {
            var plan = new Plan();
            JsonObject src;
            try { src = JsonNode.Parse(r.SourceJson ?? "") as JsonObject; }
            catch (JsonException ex) { plan.Error = "source json: " + ex.Message; return plan; }
            if (src == null || src["packages"] is not JsonArray || src["categories"] is not JsonArray) { plan.Error = "source must have categories[] and packages[]"; return plan; }
            if (r.Schema < SoftwareCatalogParser.SupportedSchema || r.Schema > SoftwareCatalogParser.MaxSupportedSchema) { plan.Error = "schema " + r.Schema; return plan; }

            JsonElement prevBody = default; long prevRev = 0;
            JsonDocument prevDoc = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(r.PreviousEnvelope))
                {
                    prevDoc = DecodeUnverified(r.PreviousEnvelope);
                    if (prevDoc == null) { plan.Error = "previous catalog envelope unreadable"; return plan; }
                    var root = prevDoc.RootElement;
                    prevRev = SignedBlob.Num(root, "revision");
                    prevBody = root.TryGetProperty("catalog", out var cb) ? cb : root;
                    if (prevRev <= 0) { plan.Error = "previous revision missing"; return plan; }
                    plan.Revision = prevRev + 1;
                }
                else
                {
                    if (r.FirstRevision <= 0) { plan.Error = "no previous catalog: FirstRevision required"; return plan; }
                    plan.Revision = r.FirstRevision;
                }

                // Tự kiểm bằng ĐÚNG parser của client: gói / phiên bản mang trường tham số tự do => không ký
                var probe = BuildCatalogPayload(src, r.Schema, plan.Revision, Math.Max(1, r.Now), Math.Max(2, r.Now + r.CatalogValiditySec), "0000000000000000");
                var warn = new List<string>();
                var parsed = SoftwareCatalogParser.Parse(probe, warn);
                foreach (var w in warn) { if (w.Contains("forbidden", StringComparison.Ordinal)) plan.Error = "refused: " + w; plan.Warnings.Add(w); }
                foreach (var p in parsed.Packages)
                    foreach (var v in p.Versions)
                        if (v.Invalid.Length > 0)
                        {
                            plan.Warnings.Add($"{p.Slug} {v.Version} {v.ArchRaw}: {v.Invalid}");
                            if (v.Invalid.StartsWith("forbidden", StringComparison.Ordinal)) plan.Error = $"refused: {p.Slug} {v.Version}: {v.Invalid}";
                        }
                if (plan.Error.Length > 0) return plan;
                if (r.BlockedJson != null && (JsonNode.Parse(r.BlockedJson) is not JsonArray)) { plan.Error = "blocked must be a JSON array"; return plan; }

                using (var nd = JsonDocument.Parse(src.ToJsonString()))
                    plan.Diff = Diff(prevDoc == null ? (JsonElement?)null : prevBody, nd.RootElement);
                foreach (var f in AdminFields)
                {
                    string o = prevDoc != null && prevDoc.RootElement.TryGetProperty(f, out var pv) ? pv.GetRawText() : "(none)";
                    string n = src[f]?.ToJsonString() ?? "(none)";
                    if (o != n) plan.Diff.Add($"~ {f}: {o} -> {n}");
                }
                plan.Token = Token(plan.Revision, plan.Diff, r.SourceJson, r.BlockedJson);
                return plan;
            }
            catch (JsonException ex) { plan.Error = "json: " + ex.Message; return plan; }
            finally { prevDoc?.Dispose(); }
        }

        /// <summary>Ký catalog + freshness. confirm phải đúng <see cref="Plan.Token"/> — sai/thiếu => ném, không ra tệp nào.</summary>
        public static Output Sign(Request r, Plan plan, string confirm, ECDsa key)
        {
            if (plan == null || plan.Error.Length > 0) throw new InvalidOperationException("plan has errors: " + plan?.Error);
            if (string.IsNullOrEmpty(plan.Token) || !string.Equals((confirm ?? "").Trim(), plan.Token, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("not confirmed: reviewer must type the confirmation code shown with the diff");
            if (key == null) throw new ArgumentNullException(nameof(key));
            // Kế hoạch phải đúng với nguồn hiện tại (không ký một nguồn đã bị sửa sau khi duyệt)
            var again = MakePlan(r);
            if (again.Token != plan.Token) throw new InvalidOperationException("source changed after review");

            string kid = KidOf(key);
            var src = (JsonObject)JsonNode.Parse(r.SourceJson);
            long issued = r.Now, expires = r.Now + Math.Min(r.CatalogValiditySec, SoftwareCatalogTrust.MaxValiditySec);
            string catalogJson = BuildCatalogPayload(src, r.Schema, plan.Revision, issued, expires, kid);
            byte[] catalogBytes = Encoding.UTF8.GetBytes(catalogJson);
            string sha = SignedBlob.Sha256Hex(catalogBytes);
            var fresh = new JsonObject
            {
                ["typ"] = CatalogFreshness.Typ, ["alg"] = SignedEnvelope.ExpectedAlg, ["kid"] = kid,
                ["catalog_revision"] = plan.Revision, ["issued_at"] = issued,
                ["expires_at"] = issued + Math.Min(r.FreshnessValiditySec, CatalogFreshness.MaxValiditySec),
                ["catalog_sha256"] = sha,
                ["blocked"] = r.BlockedJson == null ? new JsonArray() : JsonNode.Parse(r.BlockedJson),
            };
            return new Output
            {
                Revision = plan.Revision, CatalogSha256 = sha,
                CatalogEnvelope = SignBytes(catalogBytes, key),
                FreshnessEnvelope = SignBytes(Encoding.UTF8.GetBytes(fresh.ToJsonString()), key),
            };
        }

        /// <summary>
        /// Phát hành lại CHỈ freshness cho catalog đang có (gia hạn định kỳ trước khi hết 10 ngày, hoặc thêm kill-switch khẩn cấp).
        /// Mã xác nhận tính từ (revision, sha256 catalog, danh sách chặn) — cùng luật "không ký âm thầm".
        /// </summary>
        public static string FreshnessToken(string catalogEnvelope, string blockedJson)
        {
            var (rev, sha) = CatalogIdentity(catalogEnvelope);
            return Token(rev, new[] { "freshness-only", sha }, "", blockedJson);
        }

        public static string SignFreshnessOnly(string catalogEnvelope, string blockedJson, long now, long validitySec, string confirm, ECDsa key)
        {
            if (!string.Equals((confirm ?? "").Trim(), FreshnessToken(catalogEnvelope, blockedJson), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("not confirmed: reviewer must type the confirmation code");
            var (rev, sha) = CatalogIdentity(catalogEnvelope);
            if (rev <= 0) throw new InvalidOperationException("catalog envelope unreadable");
            if (blockedJson != null && JsonNode.Parse(blockedJson) is not JsonArray) throw new InvalidOperationException("blocked must be a JSON array");
            var fresh = new JsonObject
            {
                ["typ"] = CatalogFreshness.Typ, ["alg"] = SignedEnvelope.ExpectedAlg, ["kid"] = KidOf(key),
                ["catalog_revision"] = rev, ["issued_at"] = now, ["expires_at"] = now + Math.Min(validitySec, CatalogFreshness.MaxValiditySec),
                ["catalog_sha256"] = sha, ["blocked"] = blockedJson == null ? new JsonArray() : JsonNode.Parse(blockedJson),
            };
            return SignBytes(Encoding.UTF8.GetBytes(fresh.ToJsonString()), key);
        }

        /// <summary>(revision, SHA-256 payload) của một envelope catalog (dạng thô hoặc {"envelope":...}).</summary>
        public static (long Revision, string Sha256) CatalogIdentity(string catalogEnvelope)
        {
            string e = UnwrapEnvelope(catalogEnvelope);
            int dot = e.LastIndexOf('.');
            if (dot <= 0) return (0, "");
            try
            {
                byte[] payload = Convert.FromBase64String(e.Substring(0, dot));
                using var d = JsonDocument.Parse(payload);
                return (SignedBlob.Num(d.RootElement, "revision"), SignedBlob.Sha256Hex(payload));
            }
            catch (Exception ex) when (ex is FormatException || ex is JsonException) { return (0, ""); }
        }

        /// <summary>Nhận envelope thô hoặc tệp JSON {"envelope":"..."} (catalog.php / catalog.json / freshness.json).</summary>
        public static string UnwrapEnvelope(string text)
        {
            string e = (text ?? "").Trim();
            if (!e.StartsWith("{", StringComparison.Ordinal)) return e;
            try { using var w = JsonDocument.Parse(e); return SignedBlob.Str(w.RootElement, "envelope"); }
            catch (JsonException) { return ""; }
        }

        // ---------------------------------------------------------------- root.json

        /// <summary>Tài liệu root kế tiếp (version = trước + 1) ký bằng khóa ROOT hiện tại. Trả envelope (nối vào cuối chuỗi root.json).</summary>
        public static string SignRoot(int version, long issuedAt, IReadOnlyCollection<string> rootKeyPems, IReadOnlyCollection<string> catalogKeyPems, ECDsa rootKey)
        {
            if (version < 1 || rootKeyPems == null || rootKeyPems.Count == 0 || catalogKeyPems == null || catalogKeyPems.Count == 0) throw new ArgumentException("root needs version >= 1, root_keys and catalog_keys");
            if (rootKeyPems.Concat(catalogKeyPems).Any(p => !SignedBlob.IsP256PublicPem(p))) throw new ArgumentException("every key must be a P-256 PUBLIC key PEM");
            var doc = new JsonObject
            {
                ["typ"] = CatalogRootChain.Typ, ["alg"] = SignedEnvelope.ExpectedAlg, ["kid"] = KidOf(rootKey),
                ["version"] = version, ["issued_at"] = issuedAt, ["threshold"] = 1,
                ["root_keys"] = new JsonArray(rootKeyPems.Select(p => (JsonNode)JsonValue.Create(p.Trim())).ToArray()),
                ["catalog_keys"] = new JsonArray(catalogKeyPems.Select(p => (JsonNode)JsonValue.Create(p.Trim())).ToArray()),
            };
            return SignBytes(Encoding.UTF8.GetBytes(doc.ToJsonString()), rootKey);
        }

        // ---------------------------------------------------------------- tiện ích

        public static string BuildCatalogPayload(JsonObject src, int schema, long revision, long issued, long expires, string kid)
        {
            var o = new JsonObject
            {
                ["typ"] = "software_catalog", ["alg"] = SignedEnvelope.ExpectedAlg, ["kid"] = kid,
                ["schema_version"] = schema, ["revision"] = revision, ["issued_at"] = issued, ["expires_at"] = expires,
                ["requires_freshness"] = true,   // client mới: danh mục này phải đi kèm freshness.json; client 3.0.x bỏ qua trường lạ
                ["catalog"] = new JsonObject { ["categories"] = src["categories"].DeepClone(), ["packages"] = src["packages"].DeepClone() },
            };
            // GĐ3: quản trị lớp tạm ẩn qua đường tin cậy chính (tùy chọn, thiếu = mặc định)
            foreach (var f in AdminFields) if (src[f] != null) o[f] = src[f].DeepClone();
            return o.ToJsonString();
        }

        public static string SignBytes(byte[] payload, ECDsa key)
            => Convert.ToBase64String(payload) + "." + Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        public static string KidOf(ECDsa key) => ShopLicenseKeys.KidFromPem(key.ExportSubjectPublicKeyInfoPem());

        /// <summary>Đọc payload KHÔNG kiểm chữ ký — chỉ để so diff với bản trước (bản trước đã được client kiểm khi phát hành).</summary>
        public static JsonDocument DecodeUnverified(string envelope)
        {
            try
            {
                string e = UnwrapEnvelope(envelope);
                int dot = e.LastIndexOf('.');
                return dot > 0 ? JsonDocument.Parse(Convert.FromBase64String(e.Substring(0, dot))) : null;
            }
            catch (Exception ex) when (ex is FormatException || ex is JsonException) { return null; }
        }

        public static string Token(long revision, IEnumerable<string> diff, string source, string blocked)
        {
            string all = revision + "\n" + string.Join("\n", diff) + "\n" + SignedBlob.Sha256Hex(Encoding.UTF8.GetBytes(source ?? "")) + "\n" + (blocked ?? "");
            return SignedBlob.Sha256Hex(Encoding.UTF8.GetBytes(all)).Substring(0, 8).ToUpperInvariant();
        }

        static readonly string[] Watched = { "sha256", "size", "size_bytes", "signer_subject", "signer_thumbprint", "url", "installer_type", "installer_kind" };

        /// <summary>Diff theo (gói, phiên bản, kiến trúc, loại): + thêm, - bỏ, ~ đổi hash / dung lượng / signer / URL / loại trình cài.</summary>
        public static List<string> Diff(JsonElement? prevBody, JsonElement newBody)
        {
            var before = Versions(prevBody); var after = Versions(newBody);
            var lines = new List<string>();
            foreach (var k in after.Keys.Except(before.Keys).OrderBy(x => x, StringComparer.Ordinal))
                lines.Add($"+ {k}  sha256={Field(after[k], "sha256")} signer={Field(after[k], "signer_subject")} size={Size(after[k])}");
            foreach (var k in before.Keys.Except(after.Keys).OrderBy(x => x, StringComparer.Ordinal))
                lines.Add($"- {k}");
            foreach (var k in after.Keys.Intersect(before.Keys).OrderBy(x => x, StringComparer.Ordinal))
                foreach (var f in Watched)
                {
                    string o = f.StartsWith("size", StringComparison.Ordinal) ? Size(before[k]) : Field(before[k], f);
                    string n = f.StartsWith("size", StringComparison.Ordinal) ? Size(after[k]) : Field(after[k], f);
                    if (f == "size_bytes" || o == n) continue;
                    lines.Add($"~ {k}  {f}: {(o.Length == 0 ? "(none)" : o)} -> {(n.Length == 0 ? "(none)" : n)}");
                }
            return lines;
        }

        static Dictionary<string, JsonElement> Versions(JsonElement? body)
        {
            var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (body is not JsonElement b || !b.TryGetProperty("packages", out var pk) || pk.ValueKind != JsonValueKind.Array) return d;
            foreach (var p in pk.EnumerateArray())
            {
                string slug = SignedBlob.Str(p, "slug");
                if (!p.TryGetProperty("versions", out var vs) || vs.ValueKind != JsonValueKind.Array) continue;
                foreach (var v in vs.EnumerateArray())
                {
                    string type = SignedBlob.Str(v, "installer_type");
                    if (type.Length == 0) type = SignedBlob.Str(v, "installer_kind");
                    d[$"{slug}/{SignedBlob.Str(v, "version")}/{SignedBlob.Str(v, "arch")}/{type}"] = v.Clone();
                }
            }
            return d;
        }

        static string Field(JsonElement v, string f) => f == "sha256" ? SignedBlob.Str(v, f).ToLowerInvariant() : SignedBlob.Str(v, f);

        static string Size(JsonElement v)
        {
            long n = SignedBlob.Num(v, "size_bytes");
            if (n <= 0) n = SignedBlob.Num(v, "size");
            return n > 0 ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        }
    }

    /// <summary>
    /// GĐ3 — duyệt đề xuất của bot (proposals.json, DỮ LIỆU KHÔNG TIN CẬY) trên máy quản trị: diff 5 nhóm, thùng rác chờ duyệt
    /// catalog-removed-pending.json (nhập lại không trùng, giữ lịch sử để khôi phục) và draft nguồn cho lệnh sign hiện có (vẫn bắt mã xác nhận).
    /// Không tự đổi hash / thêm bản: chỉ áp vào draft (a) đổi URL CÙNG file đã kiểm SHA-256 từ tệp cục bộ, (b) xóa / khôi phục người duyệt chỉ định.
    /// Đặc tả: Docs/AVAILABILITY-VA-THUNG-RAC.md.
    /// </summary>
    public static class CatalogReview
    {
        // Công cụ quản trị (console) in tiếng Việt; vế tiếng Anh giữ theo luật song ngữ của dự án.
        static string L(string vi, string en) => vi;

        public static readonly string GSameFile = L("Đổi URL cùng file", "Same-file URL change"), GNewArtifact = L("Đổi hash/bản", "Hash / version change"),
            GHidden = L("Tạm ẩn chờ duyệt", "Temporarily hidden, pending review"), GRemove = L("Xóa khỏi catalog", "Remove from catalog"), GRestore = L("Khôi phục", "Restore");
        public static readonly string[] Groups = { GSameFile, GNewArtifact, GHidden, GRemove, GRestore };
        public static readonly string[] Kinds = { "same_artifact", "new_artifact", "needs_review", "retire_candidate", "healthy", "transient", "suspect" };
        public const int MaxItems = 20000;

        public sealed class Input
        {
            public string ProposalsJson = "", AvailabilityJson, CatalogEnvelope = "", RemovedPendingJson;
            public long Now;
            /// <summary>SHA-256 (hex thường) của các tệp người duyệt đã tải về và kiểm (Program: hash + Authenticode bằng mã hiện có).</summary>
            public HashSet<string> VerifiedSha256 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Remove = new HashSet<string>(StringComparer.OrdinalIgnoreCase), Restore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class Output
        {
            public string Error = "";
            public long CatalogRevision, BaseRevision;
            public Dictionary<string, List<string>> Diff = Groups.ToDictionary(g => g, _ => new List<string>());
            public List<string> NeedsReprobe = new List<string>(), Warnings = new List<string>();
            public int Transient, Healthy;
            public string RemovedPendingJson = "", DraftSourceJson = "";
            /// <summary>Số thay đổi thật đã áp vào draft (0 = draft giống catalog hiện tại).</summary>
            public int DraftChanges;
        }

        sealed class Item { public string Key = "", Kind = "", WingetId = "", Slug = "", Version = "", ShaOld = "", UrlNew = "", ShaNew = "", Note = ""; public List<string> Evidence = new List<string>(); }

        static readonly Regex HexRe = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        static readonly Regex QueryRe = new Regex(@"(https?://[^\s?#]+)[?#]\S*", RegexOptions.CultureInvariant);

        /// <summary>Khoá biến thể — PHẢI giống LinkHealthLogic.KeyOf (bot dùng hàm đó; tệp Health không biên dịch vào CatalogPublish).</summary>
        public static string KeyOf(string wingetId, string slug, string version)
            => ((wingetId ?? "").Length > 0 ? "w:" + wingetId : "s:" + slug).ToLowerInvariant() + "|" + (version ?? "");

        /// <summary>Chuỗi không tin cậy => bỏ query / fragment (token có hạn) + ký tự điều khiển, cắt độ dài. Dùng cho MỌI thứ in ra / ghi xuống.</summary>
        public static string Clean(string s, int max = 300)
        {
            s = QueryRe.Replace(s ?? "", "$1?[query removed]");
            s = new string(s.Where(c => c >= ' ' && c != '\u007f').ToArray()).Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        public static Output Run(Input inp)
        {
            var o = new Output();
            string now = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, inp.Now)).ToString("u", System.Globalization.CultureInfo.InvariantCulture);

            // ---- catalog hiện tại (Program đã verify chữ ký nếu truyền --pub) ----
            var (rev, _) = CatalogPublishLogic.CatalogIdentity(inp.CatalogEnvelope);
            JsonObject root;
            try
            {
                string e = CatalogPublishLogic.UnwrapEnvelope(inp.CatalogEnvelope);
                root = JsonNode.Parse(Convert.FromBase64String(e.Substring(0, e.LastIndexOf('.')))) as JsonObject;
            }
            catch (Exception ex) when (ex is FormatException || ex is JsonException || ex is ArgumentException) { root = null; }
            var body = root?["catalog"] as JsonObject ?? root;
            if (rev <= 0 || body?["packages"] is not JsonArray packages || body["categories"] is not JsonArray) { o.Error = "catalog unreadable"; return o; }
            o.CatalogRevision = rev;

            // ---- proposals (không tin cậy) ----
            var items = new List<Item>();
            try
            {
                using var pd = JsonDocument.Parse(inp.ProposalsJson ?? "", new JsonDocumentOptions { MaxDepth = 16 });
                var pr = pd.RootElement;
                o.BaseRevision = SignedBlob.Num(pr, "baseRevision");
                if (!pr.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() > MaxItems) { o.Error = "proposals: items[] missing / too many"; return o; }
                foreach (var x in arr.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.Object) continue;
                    var it = new Item
                    {
                        Key = Clean(SignedBlob.Str(x, "key"), 200), Kind = SignedBlob.Str(x, "kind"), WingetId = Clean(SignedBlob.Str(x, "winget_id"), 128),
                        Slug = Clean(SignedBlob.Str(x, "slug"), 64), Version = Clean(SignedBlob.Str(x, "version"), 64),
                        ShaOld = SignedBlob.Str(x, "sha256_old").ToLowerInvariant(), UrlNew = SignedBlob.Str(x, "url_new"), ShaNew = SignedBlob.Str(x, "sha256_new").ToLowerInvariant(),
                        Note = Clean(SignedBlob.Str(x, "note")),
                    };
                    if (x.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Array)
                        it.Evidence = ev.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => Clean(v.GetString())).Where(v => v.Length > 0).Take(20).ToList();
                    if (!Kinds.Contains(it.Kind) || it.WingetId.Length == 0 && it.Slug.Length == 0 || it.Version.Length == 0) { o.Warnings.Add(L($"bỏ mục đề xuất sai định dạng: {Clean(it.Key + " " + it.Kind, 120)}", $"dropped malformed proposal item: {Clean(it.Key + " " + it.Kind, 120)}")); continue; }
                    if (it.ShaOld.Length > 0 && !HexRe.IsMatch(it.ShaOld) || it.ShaNew.Length > 0 && !HexRe.IsMatch(it.ShaNew)) { o.Warnings.Add(L($"bỏ mục sha256 sai định dạng: {it.Key}", $"dropped item with malformed sha256: {it.Key}")); continue; }
                    if (it.UrlNew.Length > 0 && (!SoftwareCatalogParser.IsHttps(it.UrlNew) || it.UrlNew.IndexOfAny(new[] { '?', '#' }) >= 0 || it.UrlNew.Length > 2048))
                    { o.Warnings.Add(L($"bỏ url_new không phải https cố định (có query / fragment): {it.Key}", $"dropped url_new that is not a fixed https URL (query / fragment): {it.Key}")); it.UrlNew = ""; }
                    if (it.Key.Length == 0) it.Key = KeyOf(it.WingetId, it.Slug, it.Version);
                    items.Add(it);
                }
            }
            catch (JsonException ex) { o.Error = "proposals json: " + ex.Message; return o; }
            bool drift = o.BaseRevision != rev;
            if (drift) o.Warnings.Add(L($"proposals dựng trên revision {o.BaseRevision}, catalog hiện tại {rev}: so lại từng biến thể theo sha256_old, lệch => cần dò lại",
                               $"proposals built on revision {o.BaseRevision}, current catalog {rev}: each variant re-checked by sha256_old, mismatch => re-probe"));

            var records = LoadRecords(inp.RemovedPendingJson, o.Warnings);

            foreach (var it in items)
            {
                if (it.Kind == "transient") { o.Transient++; continue; }
                var (pkg, ver) = Find(packages, it, drift);
                if (ver == null && it.Kind != "healthy")
                {
                    o.NeedsReprobe.Add(drift ? L($"{it.Key} ({it.Kind}): không khớp biến thể ở revision {rev} (sha256_old) — yêu cầu bot dò lại", $"{it.Key} ({it.Kind}): no matching variant at revision {rev} (sha256_old) — ask the bot to re-probe")
                                            : L($"{it.Key} ({it.Kind}): không có biến thể trong catalog — yêu cầu bot dò lại", $"{it.Key} ({it.Kind}): variant not in the catalog — ask the bot to re-probe"));
                    continue;
                }
                string curSha = ver != null ? SignedBlob.Str(ToEl(ver), "sha256").ToLowerInvariant() : "";
                switch (it.Kind)
                {
                    case "same_artifact" when it.UrlNew.Length > 0 && it.ShaNew.Length > 0 && it.ShaNew == curSha:
                        bool checkedLocal = inp.VerifiedSha256.Contains(curSha);
                        o.Diff[GSameFile].Add($"{it.Key}  url: {Clean((string)ver["url"])} -> {Clean(it.UrlNew)}  sha256={curSha}  {(checkedLocal ? L("[đã kiểm file cục bộ: khớp sha256]", "[local file checked: sha256 matches]") : L("[CHƯA kiểm file — tải về rồi truyền --file, chưa đưa vào draft]", "[file NOT checked — download it and pass --file; not in the draft]"))}");
                        if (checkedLocal) { SwapUrl(ver, it.UrlNew, o); o.DraftChanges++; }
                        break;
                    case "same_artifact":
                    case "new_artifact":
                    case "needs_review":
                        o.Diff[GNewArtifact].Add($"{it.Key}  [{it.Kind}] sha256: {(curSha.Length > 0 ? curSha : "(none)")} -> {(it.ShaNew.Length > 0 ? it.ShaNew : "(?)")}  url_new: {(it.UrlNew.Length > 0 ? Clean(it.UrlNew) : "(none)")}  " + L("— sửa nguồn bằng tay sau khi kiểm file (hash, signer), lệnh sign sẽ hiện diff", "— edit the source by hand after checking the file (hash, signer); sign will show the diff"));
                        break;
                    case "suspect":
                        Upsert(records, it, pkg, ver, "suspect", now, rev, o);
                        o.Diff[GHidden].Add(L($"{it.Key}  [nghi mất nguồn] {it.Note}", $"{it.Key}  [suspected source loss] {it.Note}"));
                        break;
                    case "retire_candidate":
                        Upsert(records, it, pkg, ver, "retire_candidate", now, rev, o);
                        o.Diff[GRemove].Add($"{it.Key}  {it.Note}  {(inp.Remove.Contains(it.Key) ? L("[XÓA trong draft]", "[REMOVED in draft]") : L($"[chờ duyệt — truyền --remove {it.Key} để xóa]", $"[pending — pass --remove {it.Key} to remove]"))}");
                        break;
                    case "healthy":
                        o.Healthy++;
                        if (records.TryGetValue(it.Key, out var rec) && Status(rec) is "pending" or "removed")
                        {
                            o.Diff[GRestore].Add(Status(rec) == "removed"
                                ? L($"{it.Key}  nguồn đã ký phục vụ lại đúng file — đã xóa khỏi catalog: truyền --restore {it.Key}", $"{it.Key}  signed source serves the right file again — removed from catalog: pass --restore {it.Key}")
                                : L($"{it.Key}  nguồn đã ký phục vụ lại đúng file", $"{it.Key}  signed source serves the right file again"));
                            if (Status(rec) == "pending") SetStatus(rec, "restored", now, rev, L("bot: nguồn đã ký phục vụ lại", "bot: signed source serves again"));
                        }
                        break;
                }
            }

            // ---- availability.json hiện hành (không tin cậy, chỉ để ghi nhận chờ duyệt) ----
            foreach (var (it, why) in AvailabilityItems(inp.AvailabilityJson, rev, o.Warnings))
            {
                var (pkg, ver) = Find(packages, it, false);
                Upsert(records, it, pkg, ver, "availability:" + why, now, rev, o);
                o.Diff[GHidden].Add(L($"{it.Key}  [đang tạm ẩn trên máy khách] {why} {it.Note}", $"{it.Key}  [hidden on clients now] {why} {it.Note}"));
            }

            // ---- xóa / khôi phục do người duyệt chỉ định ----
            foreach (var key in inp.Remove.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!records.TryGetValue(key, out var rec)) { o.Warnings.Add(L($"--remove {key}: không có trong hàng chờ (cần đề xuất retire_candidate / suspect trước)", $"--remove {key}: not in the queue (needs a retire_candidate / suspect proposal first)")); continue; }
                var it = new Item { Key = key, WingetId = Str(rec, "winget_id"), Slug = Str(rec, "slug"), Version = Str(rec, "version"), ShaOld = Str(rec, "sha256") };
                var (pkg, ver) = Find(packages, it, false);
                if (ver == null) { o.Warnings.Add(L($"--remove {key}: biến thể không còn trong catalog hiện tại", $"--remove {key}: variant no longer in the current catalog")); continue; }
                ((JsonArray)pkg["versions"]).Remove(ver);
                if (((JsonArray)pkg["versions"]).Count == 0) { packages.Remove(pkg); rec["scope"] = "package"; }
                SetStatus(rec, "removed", now, rev + 1, L("người duyệt: xóa khỏi catalog (hiệu lực khi revision mới được ký + phát hành)", "reviewer: remove from catalog (effective once the new revision is signed and published)"));
                if (!o.Diff[GRemove].Any(l => l.StartsWith(key + " ", StringComparison.Ordinal))) o.Diff[GRemove].Add(L($"{key}  [XÓA trong draft]", $"{key}  [REMOVED in draft]"));
                o.DraftChanges++;
            }
            foreach (var key in inp.Restore.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!records.TryGetValue(key, out var rec) || rec["original"] is not JsonObject orig) { o.Warnings.Add(L($"--restore {key}: không có bản ghi gốc trong thùng rác", $"--restore {key}: no original record in the trash")); continue; }
                if (Status(rec) == "removed")
                {
                    string slug = Str(rec, "slug");
                    var pkg = packages.OfType<JsonObject>().FirstOrDefault(p => SignedBlob.Str(ToEl(p), "slug") == slug);
                    if (pkg == null && rec["original_package"] is JsonObject op) { pkg = (JsonObject)op.DeepClone(); pkg["versions"] = new JsonArray(); packages.Add(pkg); }
                    if (pkg == null) { o.Warnings.Add(L($"--restore {key}: thiếu gói gốc", $"--restore {key}: original package missing")); continue; }
                    if (pkg["versions"] is not JsonArray vs) pkg["versions"] = vs = new JsonArray();
                    vs.Add(orig.DeepClone());
                    o.DraftChanges++;
                }
                SetStatus(rec, "restored", now, rev + 1, L("người duyệt: khôi phục", "reviewer: restore"));
                o.Diff[GRestore].Add(L($"{key}  [KHÔI PHỤC trong draft từ bản ghi cũ]", $"{key}  [RESTORED in draft from the old record]"));
            }

            o.RemovedPendingJson = new JsonObject
            {
                ["schema"] = 1,
                ["items"] = new JsonArray(records.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => (JsonNode)k.Value.DeepClone()).ToArray()),
            }.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var draft = new JsonObject { ["categories"] = body["categories"].DeepClone(), ["packages"] = packages.DeepClone() };
            foreach (var f in CatalogPublishLogic.AdminFields) if (root[f] != null) draft[f] = root[f].DeepClone();
            o.DraftSourceJson = draft.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return o;
        }

        // ---------------------------------------------------------------- nội bộ

        /// <summary>Biến thể theo (slug | winget id) + version (+ sha256_old). strictSha = catalog đã đổi so với baseRevision: BẮT BUỘC khớp sha256_old.</summary>
        static (JsonObject Pkg, JsonObject Ver) Find(JsonArray packages, Item it, bool strictSha)
        {
            if (strictSha && it.ShaOld.Length == 0) return (null, null);
            foreach (var p in packages.OfType<JsonObject>())
            {
                var pe = ToEl(p);
                if (it.Slug.Length > 0 && SignedBlob.Str(pe, "slug") != it.Slug) continue;
                if (p["versions"] is not JsonArray vs) continue;
                foreach (var v in vs.OfType<JsonObject>())
                {
                    var ve = ToEl(v);
                    string wid = SignedBlob.Str(ve, "winget_id"); if (wid.Length == 0) wid = SignedBlob.Str(pe, "winget_id");
                    if (it.Slug.Length == 0 && !string.Equals(wid, it.WingetId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(SignedBlob.Str(ve, "version"), it.Version, StringComparison.OrdinalIgnoreCase)) continue;
                    if (it.ShaOld.Length > 0 && SignedBlob.Str(ve, "sha256").ToLowerInvariant() != it.ShaOld) continue;
                    return (p, v);
                }
            }
            return (null, null);
        }

        static JsonElement ToEl(JsonNode n) { using var d = JsonDocument.Parse(n.ToJsonString()); return d.RootElement.Clone(); }

        static void SwapUrl(JsonObject ver, string url, Output o)
        {
            ver["url"] = url;
            string host = new Uri(url).Host.ToLowerInvariant();
            if (ver["allowed_hosts"] is JsonArray hosts && !hosts.Any(h => string.Equals((string)h, host, StringComparison.OrdinalIgnoreCase)))
            {
                hosts.Add(host);
                o.Warnings.Add(L($"draft thêm host {host} vào allowed_hosts — người duyệt kiểm đây là host chính chủ", $"draft adds host {host} to allowed_hosts — reviewer must confirm it is the official host"));
            }
        }

        static IEnumerable<(Item It, string Why)> AvailabilityItems(string text, long rev, List<string> warnings)
        {
            var list = new List<(Item, string)>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            try
            {
                // cùng phong bì catalog.php: {"envelope":"b64.b64"} hoặc envelope trần (không kiểm chữ ký: chỉ ghi nhận chờ duyệt)
                using var pd = CatalogPublishLogic.DecodeUnverified(text) ?? throw new FormatException("availability envelope");
                long arev = SignedBlob.Num(pd.RootElement, "catalog_revision");
                if (arev != rev) warnings.Add(L($"availability.json cho revision {arev}, catalog hiện tại {rev} — máy khách sẽ không áp bản này", $"availability.json is for revision {arev}, current catalog {rev} — clients will not apply it"));
                if (pd.RootElement.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.Array)
                    foreach (var e in h.EnumerateArray().Take(MaxItems))
                    {
                        var it = new Item
                        {
                            WingetId = Clean(SignedBlob.Str(e, "winget_id"), 128), Slug = Clean(SignedBlob.Str(e, "slug"), 64), Version = Clean(SignedBlob.Str(e, "version"), 64),
                            ShaOld = SignedBlob.Str(e, "sha256").ToLowerInvariant(), Note = Clean(SignedBlob.Str(e, "note")),
                        };
                        if (it.WingetId.Length == 0 && it.Slug.Length == 0 || it.Version.Length == 0 || it.ShaOld.Length > 0 && !HexRe.IsMatch(it.ShaOld)) continue;
                        it.Key = KeyOf(it.WingetId, it.Slug, it.Version);
                        list.Add((it, Clean(SignedBlob.Str(e, "reason"), 64)));
                    }
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException) { warnings.Add(L("availability.json không đọc được — bỏ qua", "availability.json unreadable — skipped")); }
            return list;
        }

        static Dictionary<string, JsonObject> LoadRecords(string json, List<string> warnings)
        {
            var d = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return d;
            try
            {
                if (JsonNode.Parse(json)?["items"] is JsonArray arr)
                    foreach (var r in arr.OfType<JsonObject>())
                    {
                        string k = Str(r, "key");
                        if (k.Length > 0) d[k] = (JsonObject)r.DeepClone();
                    }
            }
            catch (JsonException) { throw new InvalidOperationException(L("catalog-removed-pending.json hỏng — không ghi đè (sửa hoặc khôi phục bản sao trước)", "catalog-removed-pending.json is corrupt — not overwriting (fix it or restore a copy first)")); }
            return d;
        }

        /// <summary>Thêm / cập nhật bản ghi KHÔNG trùng: bằng chứng + nguồn đã thử gộp phân biệt; lịch sử chỉ thêm khi đổi trạng thái; giữ nguyên bản gốc để khôi phục.</summary>
        static void Upsert(Dictionary<string, JsonObject> records, Item it, JsonObject pkg, JsonObject ver, string reason, string now, long rev, Output o)
        {
            if (!records.TryGetValue(it.Key, out var rec))
            {
                var pe = pkg != null ? ToEl(pkg) : default;
                var skeleton = pkg?.DeepClone() as JsonObject;
                skeleton?.Remove("versions");
                rec = new JsonObject
                {
                    ["key"] = it.Key, ["winget_id"] = it.WingetId, ["slug"] = pkg != null ? SignedBlob.Str(pe, "slug") : it.Slug,
                    ["name"] = pkg != null ? Clean(SignedBlob.Str(pe, "name_en").Length > 0 ? SignedBlob.Str(pe, "name_en") : SignedBlob.Str(pe, "name_vi"), 120) : "",
                    ["scope"] = "variant", ["version"] = it.Version,
                    ["arch"] = ver != null ? SignedBlob.Str(ToEl(ver), "arch") : "",
                    ["sha256"] = ver != null ? SignedBlob.Str(ToEl(ver), "sha256").ToLowerInvariant() : it.ShaOld,
                    ["first_seen"] = now, ["updated_at"] = now, ["sources_tried"] = new JsonArray(), ["evidence"] = new JsonArray(),
                    ["reason"] = reason, ["note"] = it.Note, ["status"] = "pending", ["revision"] = rev,
                    ["original"] = ver?.DeepClone(), ["original_package"] = skeleton,
                    ["history"] = new JsonArray($"{now} pending ({reason}) rev {rev}"),
                };
                records[it.Key] = rec;
            }
            else if (Status(rec) == "restored")
                SetStatus(rec, "pending", now, rev, L($"bot: mất nguồn lại ({reason})", $"bot: source lost again ({reason})"));
            bool changed = false;
            var sources = new List<string>();
            if (ver != null)
            {
                sources.Add(Clean((string)ver["url"]));
                if (ver["fallbacks"] is JsonArray fb) sources.AddRange(fb.OfType<JsonObject>().Select(f => Clean((string)f["url"])));
            }
            if (it.UrlNew.Length > 0) sources.Add(Clean(it.UrlNew));
            changed |= Union(Arr(rec, "sources_tried"), sources);
            changed |= Union(Arr(rec, "evidence"), it.Evidence);
            if (it.Note.Length > 0 && Str(rec, "note") != it.Note) { rec["note"] = it.Note; changed = true; }
            if (changed) rec["updated_at"] = now;
        }

        static bool Union(JsonArray arr, IEnumerable<string> add)
        {
            bool changed = false;
            foreach (var s in add.Where(s => !string.IsNullOrEmpty(s)))
                if (!arr.Any(x => (string)x == s)) { arr.Add(s); changed = true; }
            return changed;
        }

        static void SetStatus(JsonObject rec, string status, string now, long rev, string why)
        {
            if (Status(rec) == status) return;
            rec["status"] = status; rec["revision"] = rev; rec["updated_at"] = now;
            if (rec["history"] is not JsonArray h) rec["history"] = h = new JsonArray();
            h.Add($"{now} {status} rev {rev}: {why}");
        }

        static JsonArray Arr(JsonObject r, string k) { if (r[k] is not JsonArray a) r[k] = a = new JsonArray(); return a; }
        static string Status(JsonObject r) => Str(r, "status");
        static string Str(JsonObject r, string k) => r[k] is JsonValue v && v.TryGetValue(out string s) ? s : "";
    }
}
