using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SysInfoWpf.Services.Licensing;
using SysInfoWpf.Services.SoftwareCenter.Trust;

namespace SysInfoWpf.Services.SoftwareCenter
{
    /// <summary>
    /// Khóa công khai ký danh mục phần mềm — RIÊNG, không dùng chung khóa license (ShopLicenseKeys).
    /// Lý do: khóa danh mục nằm ở luồng xuất bản của web admin (đổi thường xuyên, nhiều người thao tác) — lộ khóa đó
    /// không được phép kéo theo giả mạo được license, và ngược lại. Xoay khóa: xem docs/software-catalog-schema.md §4.
    /// Chỉ nhúng khóa công khai. Khóa riêng được giữ ngoài thư mục web trên máy chủ.
    /// </summary>
    public static class SoftwareCatalogKeys
    {
        /// <summary>Danh sách PEM public key P-256 đã ghim (khóa đang dùng + khóa kế tiếp khi xoay). kid tính từ PEM như license.</summary>
        public static readonly string[] PublicKeyPems =
        {
            """
            -----BEGIN PUBLIC KEY-----
            MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAESkAtgtsKvOx6XsiZJUtrZoiNUkI0
            BDfycgohGVNtz0JUysbZs5ZJhOr/vbnnwfuADVYva4Q6H7uBgZK7m/S6pw==
            -----END PUBLIC KEY-----
            """,
            // Khóa kế tiếp (tạo 2026-11-09, khóa riêng chỉ ở server: ~/.qa98-catalog-signing/private_next.pem).
            // Server vẫn ký bằng khóa đầu; khi đổi sang khóa này, bản app đã ghim cả hai vẫn verify được.
            """
            -----BEGIN PUBLIC KEY-----
            MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE1sfmCYp2b2gxCpPpYRDZSW6tt5EZ
            pTTAE1grrQVcrt3dJ3a5u9s6oD0Mmz02iDUcTdGdUV2xndwMgcOaJVZGAA==
            -----END PUBLIC KEY-----
            """,
        };

        /// <summary>
        /// GĐ5: tập khóa catalog được tin. Đã cấp root (<see cref="CatalogRootKeys.Provisioned"/>) => đọc từ chuỗi root.json đóng gói đã xác thực
        /// bằng khóa ROOT ghim cứng; lỗi => ring RỖNG (Verify trả NoKey — FAIL-CLOSED, không nạp danh mục).
        /// Chưa cấp root => CHẾ ĐỘ CHUYỂN TIẾP: dùng <see cref="PublicKeyPems"/> như bản 3.0.x.
        /// </summary>
        public static LicenseKeyRing Ring()
        {
            if (!CatalogRootKeys.Provisioned) return CatalogRootChain.RingOf(PublicKeyPems);
            var r = CatalogRootChain.Resolve(CatalogRootKeys.PinnedRootPems, CatalogRootKeys.BundledRootChain);
            if (r.Ok) return r.CatalogRing;
            Diag.Log("SoftwareCenter", "Error", "root.json khong hop le, khong nap danh muc: " + r.Error, null);
            return new LicenseKeyRing();
        }
    }

    public enum CatalogTrustError { None, NoKey, BadEnvelope, BadSignature, CryptoFailed, BadPayload, UnsupportedSchema, NotYetValid, Expired, ValidityTooLong, Rollback }

    public sealed class CatalogTrustResult
    {
        public bool Ok => Error == CatalogTrustError.None;
        public CatalogTrustError Error;
        public string Detail = "";
        public SoftwareCatalog Catalog;
        public List<string> Warnings = new List<string>();
        /// <summary>GĐ5: SHA-256 (hex thường) của payload đã verify — so với catalog_sha256 trong freshness.json (chống trộn bản).</summary>
        public string PayloadSha256 = "";
    }

    /// <summary>
    /// Xác minh danh mục: CÙNG cơ chế ký với license (QA98.Licensing/SignedEnvelope: payloadB64 "." chữ ký ECDSA P-256/SHA-256 dạng R||S 64 byte,
    /// payload có "alg":"ES256" + "kid" khớp khóa đã verify). Chỉ khác: giới hạn payload lớn hơn (danh mục > 64 KB) nên verify lại ở đây theo đúng quy tắc đó.
    /// Sau chữ ký: schema, issued_at / expires_at (có giới hạn), revision không được lùi (chống phát lại danh mục cũ).
    /// </summary>
    public static class SoftwareCatalogTrust
    {
        public const int MaxPayloadBytes = 4 * 1024 * 1024;
        public const long MaxClockSkewSec = 10 * 60;
        public const long MaxValiditySec = 31L * 24 * 3600;

        public static CatalogTrustResult Verify(string envelope, LicenseKeyRing ring, long nowUnix, long minRevision)
            => VerifyCore(envelope, ring, nowUnix, minRevision, allowExpired: false);

        internal static long CachedRevision(string envelope, LicenseKeyRing ring, long nowUnix)
            => VerifyCore(envelope, ring, nowUnix, 0, allowExpired: true).Catalog?.Revision ?? 0;

        static CatalogTrustResult VerifyCore(string envelope, LicenseKeyRing ring, long nowUnix, long minRevision, bool allowExpired)
        {
            var res = new CatalogTrustResult();
            switch (SignedBlob.Verify(envelope, ring, MaxPayloadBytes, out byte[] payload, out string verifiedKid))
            {
                case SignedBlob.Error.None: break;
                case SignedBlob.Error.NoKey: return Fail(res, CatalogTrustError.NoKey, "no pinned catalog key");
                case SignedBlob.Error.BadEnvelope: return Fail(res, CatalogTrustError.BadEnvelope, "envelope format / size");
                case SignedBlob.Error.CryptoFailed: return Fail(res, CatalogTrustError.CryptoFailed, "signature");
                default: return Fail(res, CatalogTrustError.BadSignature, "signature");
            }
            res.PayloadSha256 = SignedBlob.Sha256Hex(payload);

            // Chỉ từ đây mới đọc payload.
            string json;
            try { json = new UTF8Encoding(false, true).GetString(payload); }
            catch (DecoderFallbackException) { return Fail(res, CatalogTrustError.BadPayload, "utf-8"); }
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return Fail(res, CatalogTrustError.BadPayload, "not object");
                    string alg = r.TryGetProperty("alg", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : "";
                    string kid = r.TryGetProperty("kid", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : "";
                    if (alg != SignedEnvelope.ExpectedAlg) return Fail(res, CatalogTrustError.BadPayload, "alg");
                    if (kid != verifiedKid) return Fail(res, CatalogTrustError.BadPayload, "kid");
                    if (r.TryGetProperty("schema_version", out _) || r.TryGetProperty("typ", out _) || r.TryGetProperty("catalog", out _))
                    {
                        if (!r.TryGetProperty("typ", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "software_catalog"
                            || !r.TryGetProperty("schema_version", out _) || !r.TryGetProperty("catalog", out var body) || body.ValueKind != JsonValueKind.Object
                            || !body.TryGetProperty("categories", out var cats) || cats.ValueKind != JsonValueKind.Array
                            || !body.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
                            return Fail(res, CatalogTrustError.BadPayload, "catalog type / structure");
                    }
                }
                res.Catalog = SoftwareCatalogParser.Parse(json, res.Warnings);
            }
            catch (JsonException) { return Fail(res, CatalogTrustError.BadPayload, "json"); }

            var c = res.Catalog;
            if (c.Schema < SoftwareCatalogParser.SupportedSchema || c.Schema > SoftwareCatalogParser.MaxSupportedSchema) return Fail(res, CatalogTrustError.UnsupportedSchema, "schema " + c.Schema);
            if (c.Revision <= 0 || c.IssuedAt <= 0 || c.ExpiresAt <= c.IssuedAt) return Fail(res, CatalogTrustError.BadPayload, "revision / time fields");
            if (c.IssuedAt > nowUnix + MaxClockSkewSec) return Fail(res, CatalogTrustError.NotYetValid, "issued_at in the future");
            if (c.ExpiresAt - c.IssuedAt > MaxValiditySec) return Fail(res, CatalogTrustError.ValidityTooLong, "validity too long");
            if (!allowExpired && nowUnix >= c.ExpiresAt) return Fail(res, CatalogTrustError.Expired, "expired");
            if (c.Revision < minRevision) return Fail(res, CatalogTrustError.Rollback, $"revision {c.Revision} < {minRevision}");
            return res;
        }

        static CatalogTrustResult Fail(CatalogTrustResult r, CatalogTrustError e, string d) { r.Error = e; r.Detail = d; r.Catalog = null; return r; }
    }

    public sealed class CatalogLoadResult
    {
        public CatalogTrustResult Trust;
        public bool FromCache;
        /// <summary>Lỗi lần tải mới (mạng / chữ ký...) khi phải dùng bản cache. Rỗng nếu tải mới thành công.</summary>
        public string OnlineError = "";
        public CatalogTrustError OnlineTrustError;
        /// <summary>GĐ5: độ mới của danh mục đang dùng. Khác Transition / Ok => đã dừng cài mới (gói InstallFrozen), vẫn cho xem.</summary>
        public FreshnessState Freshness = FreshnessState.Transition;
        public string FreshnessDetail = "";
        /// <summary>GĐ5 kill-switch đang hiệu lực (dữ liệu cho GĐ2: máy đã cài đúng bản bị chặn => hiện cảnh báo).</summary>
        public List<FreshnessBlock> Blocked = new List<FreshnessBlock>();
        /// <summary>Cặp vừa tải (catalog + freshness) đã kiểm đủ, đã lưu và nâng sàn. false = dùng cache / cặp bị từ chối / không tải.</summary>
        public bool Committed;
    }

    /// <summary>
    /// Bản cuối hợp lệ: lưu NGUYÊN envelope đã ký (sửa tay sẽ hỏng chữ ký) => lần sau verify lại toàn bộ, kể cả hạn dùng.
    /// Bản cache hết hạn => không dùng để cài. Revision của cache là mức sàn: bản tải mới có revision thấp hơn bị từ chối.
    /// </summary>
    public sealed class SoftwareCatalogStore
    {
        readonly string _path, _floorPath;

        /// <summary>Sàn revision lưu cạnh (không trong) thư mục cache: "&lt;thư mục cha&gt;\&lt;tên thư mục cache&gt;-&lt;tên tệp&gt;.floor".
        /// VD cache %LOCALAPPDATA%\QA98\software\catalog.envelope => sàn %LOCALAPPDATA%\QA98\software-catalog.envelope.floor.</summary>
        public SoftwareCatalogStore(string path) : this(path, DefaultFloorPath(path)) { }

        /// <param name="floorPath">Tệp sàn revision monotonic, phải nằm NGOÀI thư mục cache catalog (xóa / thay cache không hạ được sàn).</param>
        public SoftwareCatalogStore(string path, string floorPath) { _path = path; _floorPath = floorPath; }

        static string DefaultFloorPath(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            string parent = Path.GetDirectoryName(dir) ?? dir;
            return Path.Combine(parent, Path.GetFileName(dir) + "-" + Path.GetFileName(path) + ".floor");
        }

        /// <summary>Sàn đã lưu; chưa có / đọc lỗi / hỏng => 0 (lần đầu dùng sàn từ cache như trước).</summary>
        public long ReadFloor()
        {
            try { return File.Exists(_floorPath) && long.TryParse(File.ReadAllText(_floorPath, Encoding.ASCII).Trim(), out long v) && v > 0 ? v : 0; }
            catch (IOException) { return 0; } catch (UnauthorizedAccessException) { return 0; }
        }

        /// <summary>Chỉ tăng, không bao giờ hạ. Ghi nguyên tử (tệp tạm rồi Move đè). Lỗi ghi => bỏ qua (sàn từ cache vẫn còn).</summary>
        void RaiseFloor(long revision)
        {
            if (revision <= ReadFloor()) return;
            string tmp = _floorPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_floorPath));
                File.WriteAllText(tmp, revision.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.ASCII);
                File.Move(tmp, _floorPath, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            finally { try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { } }
        }

        public string ReadCached() { try { return File.Exists(_path) ? File.ReadAllText(_path, Encoding.ASCII) : ""; } catch (IOException) { return ""; } catch (UnauthorizedAccessException) { return ""; } }

        public void Save(string envelope)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, envelope, Encoding.ASCII);
                File.Move(tmp, _path, true);
            }
            finally { File.Delete(tmp); }
        }

        /// <summary>
        /// Gộp kết quả: <paramref name="fetched"/> = envelope vừa tải (null nếu lỗi mạng, kèm <paramref name="fetchError"/>).
        /// Thứ tự: verify cache (lấy mức sàn revision) -> verify bản mới với mức sàn -> hợp lệ thì lưu & dùng; không thì dùng cache nếu còn hạn.
        /// </summary>
        /// <summary>
        /// GĐ5: sàn bền ProgramData + HKLM + sàn đóng gói (<see cref="CatalogFloorStores.ForMachine"/>). null = như trước GĐ5 (chỉ sàn theo tài khoản)
        /// — test dùng nguồn bộ nhớ, không ghi HKLM / ProgramData thật.
        /// </summary>
        public DurableCatalogFloor Durable { get; set; }

        string FreshnessPath => _path + ".freshness";

        public CatalogLoadResult Load(string fetched, string fetchError, LicenseKeyRing ring, long nowUnix) => Load(fetched, fetchError, ring, nowUnix, null);

        /// <param name="fetchedFreshness">GĐ5: envelope freshness.json vừa tải (null = không tải được / máy chủ chưa có — chế độ chuyển tiếp nếu chưa từng thấy).</param>
        public CatalogLoadResult Load(string fetched, string fetchError, LicenseKeyRing ring, long nowUnix, string fetchedFreshness)
        {
            var durable = Durable?.Read();
            string cachedEnv = ReadCached();
            CatalogTrustResult cached = cachedEnv.Length > 0 ? SoftwareCatalogTrust.Verify(cachedEnv, ring, nowUnix, 0) : null;
            long floor = cached?.Catalog?.Revision ?? 0;
            // Cache hết hạn vẫn giữ mức sàn revision (đọc lại không kiểm hạn) để không bị phát lại bản cũ hơn.
            if (cached != null && cached.Error == CatalogTrustError.Expired)
                floor = SoftwareCatalogTrust.CachedRevision(cachedEnv, ring, nowUnix);
            // Sàn = max(sàn đã lưu riêng, sàn từ cache, sàn bền). Cache bị thay bằng bản ký cũ hơn sàn => không dùng (chống phát lại qua cache).
            long stored = Math.Max(ReadFloor(), durable?.Revision ?? 0);
            long minIssued = durable?.CatalogIssuedAt ?? 0;
            if (cached != null && cached.Ok && (cached.Catalog.Revision < stored || cached.Catalog.IssuedAt < minIssued))
                cached = new CatalogTrustResult { Error = CatalogTrustError.Rollback, Detail = $"cached revision {cached.Catalog.Revision} < {stored}" };
            if (floor > stored) RaiseFloor(floor);   // máy đã có cache từ trước khi có tệp sàn: chép sàn sang
            floor = Math.Max(floor, stored);
            if (cached != null && cached.Ok) Durable?.Raise(new CatalogFloor { Revision = cached.Catalog.Revision, CatalogIssuedAt = cached.Catalog.IssuedAt });

            var res = new CatalogLoadResult();
            CatalogTrustResult rejectedPair = null; FreshnessEval rejectedEval = null;
            if (fetched != null)
            {
                var fresh = SoftwareCatalogTrust.Verify(fetched, ring, nowUnix, floor);
                // issued_at đơn điệu: bản ký cũ hơn bản đã nhận => từ chối kể cả cùng revision
                if (fresh.Ok && fresh.Catalog.IssuedAt < minIssued)
                    fresh = new CatalogTrustResult { Error = CatalogTrustError.Rollback, Detail = $"issued_at {fresh.Catalog.IssuedAt} < {minIssued}" };
                if (fresh.Ok)
                {
                    // Kiểm ĐỦ cặp catalog + freshness (chữ ký, revision, hash, hạn) TRƯỚC khi nâng sàn / lưu:
                    // cặp lệch hoặc tải dở không được nâng sàn chặn mất bản tốt đang dùng.
                    var ev = EvalFreshness(fresh, fetchedFreshness, ring, nowUnix, durable);
                    if (ev.State == FreshnessState.Ok || ev.State == FreshnessState.Transition)
                    {
                        RaiseFloor(fresh.Catalog.Revision);
                        Durable?.Raise(new CatalogFloor { Revision = fresh.Catalog.Revision, CatalogIssuedAt = fresh.Catalog.IssuedAt });
                        try { Save(fetched.Trim()); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* vẫn dùng bản mới trong phiên này */ }
                        res.Trust = fresh;
                        res.Committed = true;
                        FinishFreshness(res, ev, commit: true);
                        return res;
                    }
                    rejectedPair = fresh; rejectedEval = ev;
                    res.OnlineError = "freshness " + ev.State + ": " + ev.Detail;
                }
                else
                {
                    res.OnlineTrustError = fresh.Error;
                    res.OnlineError = fresh.Error + ": " + fresh.Detail;
                }
            }
            else res.OnlineError = fetchError ?? "";

            if (cached != null && cached.Ok)
            {
                res.Trust = cached; res.FromCache = true;
                FinishFreshness(res, EvalFreshness(cached, fetchedFreshness, ring, nowUnix, durable), commit: true);
                return res;
            }
            // Cặp mới bị từ chối, không có cache tốt: chỉ cho XEM trong phiên này (không lưu, không nâng sàn), đã dừng cài mới.
            if (rejectedPair != null) { res.Trust = rejectedPair; FinishFreshness(res, rejectedEval, commit: false); return res; }
            // Không có bản nào dùng được: ưu tiên báo lỗi của bản tải mới; mất mạng + không cache => BadEnvelope ("không có danh mục").
            res.Trust = fetched != null
                ? new CatalogTrustResult { Error = res.OnlineTrustError, Detail = res.OnlineError }
                : cached ?? new CatalogTrustResult { Error = CatalogTrustError.BadEnvelope, Detail = res.OnlineError };
            res.FromCache = fetched == null && cached != null;
            return res;
        }

        sealed class FreshnessEval
        {
            public FreshnessState State;
            public string Detail = "";
            public FreshnessDoc Best;
            public string BestFetchedEnv;   // != null => freshness vừa tải khớp catalog, lưu lại khi commit
            public List<FreshnessBlock> Blocked = new List<FreshnessBlock>();
        }

        /// <summary>
        /// GĐ5 freshness (KHÔNG ghi gì): chọn freshness (bản vừa tải, rồi bản đã lưu) ký hợp lệ + không rollback + khớp đúng revision và SHA-256 payload danh mục.
        /// Chưa từng thấy freshness và danh mục không đòi => Transition (giữ hành vi cũ). Đã đòi mà thiếu / sai / hết hạn => dừng cài mới, vẫn cho xem.
        /// Kill-switch gộp từ MỌI freshness hợp lệ (kể cả hết hạn / lệch hash / của cặp bị từ chối) — chặn thừa an toàn hơn bỏ sót.
        /// </summary>
        FreshnessEval EvalFreshness(CatalogTrustResult trust, string fetchedFreshness, LicenseKeyRing ring, long nowUnix, CatalogFloor durable)
        {
            var cat = trust.Catalog;
            durable ??= new CatalogFloor();
            var ev = new FreshnessEval();
            var candidates = new List<(string Env, bool Fetched)>();
            if (!string.IsNullOrWhiteSpace(fetchedFreshness)) candidates.Add((fetchedFreshness.Trim(), true));
            string cachedF = ReadText(FreshnessPath);
            if (cachedF.Length > 0) candidates.Add((cachedF, false));

            bool anyValid = false;
            var why = new List<string>();
            foreach (var (env, isFetched) in candidates)
            {
                var f = CatalogFreshness.Verify(env, ring, nowUnix, Math.Max(durable.Revision, cat.Revision), durable.FreshnessIssuedAt);
                if (!f.Ok) { why.Add(f.Error + ": " + f.Detail); continue; }
                anyValid = true;
                ev.Blocked.AddRange(f.Doc.Blocked);
                if (ev.Best != null) continue;
                if (f.Doc.CatalogRevision != cat.Revision || !string.Equals(f.Doc.CatalogSha256, trust.PayloadSha256, StringComparison.Ordinal))
                { why.Add($"freshness does not match catalog (revision {f.Doc.CatalogRevision} vs {cat.Revision} / sha256)"); continue; }
                ev.Best = f.Doc;
                if (isFetched) ev.BestFetchedEnv = env;
            }
            ev.Detail = string.Join("; ", why);
            bool required = cat.RequiresFreshness || durable.FreshnessSeen || anyValid;
            if (ev.Best != null) ev.State = nowUnix >= ev.Best.ExpiresAt ? FreshnessState.Expired : FreshnessState.Ok;
            else ev.State = !required ? FreshnessState.Transition : candidates.Count == 0 ? FreshnessState.Missing : FreshnessState.Invalid;
            return ev;
        }

        /// <summary>Áp kết quả freshness vào danh mục đang dùng. <paramref name="commit"/> = lưu freshness khớp + nâng sàn freshness (không làm với cặp bị từ chối).</summary>
        void FinishFreshness(CatalogLoadResult res, FreshnessEval ev, bool commit)
        {
            var cat = res.Trust.Catalog;
            res.Freshness = ev.State;
            res.FreshnessDetail = ev.Detail;
            if (commit && ev.Best != null)
            {
                if (ev.BestFetchedEnv != null) WriteText(FreshnessPath, ev.BestFetchedEnv);
                Durable?.Raise(new CatalogFloor { Revision = cat.Revision, FreshnessIssuedAt = ev.Best.IssuedAt, FreshnessSeen = true });
            }
            if (ev.Blocked.Count > 0) { res.Blocked = ev.Blocked; CatalogFreshness.ApplyBlocked(cat, ev.Blocked); }
            if (res.Freshness != FreshnessState.Transition && res.Freshness != FreshnessState.Ok)
            {
                CatalogFreshness.FreezeInstalls(cat);
                Diag.Log("SoftwareCenter", "Warn", "danh muc: freshness " + res.Freshness + " => dung cai moi. " + res.FreshnessDetail, null);
            }
        }

        static string ReadText(string path) { try { return File.Exists(path) ? File.ReadAllText(path, Encoding.ASCII).Trim() : ""; } catch (IOException) { return ""; } catch (UnauthorizedAccessException) { return ""; } }

        static void WriteText(string path, string text)
        {
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(tmp, text, Encoding.ASCII); File.Move(tmp, path, true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            finally { try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { } }
        }
    }
}
