using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SysInfoWpf.Services.Licensing;

namespace SysInfoWpf.Services.SoftwareCenter.Trust
{
    /// <summary>
    /// GĐ5: kiểm "payloadB64.chữ-ký-B64" (ECDSA P-256/SHA-256, chữ ký R||S 64 byte) — cùng định dạng catalog / license.
    /// Dùng chung cho catalog, freshness.json, root.json. Chỉ trả payload SAU khi chữ ký khớp một khóa trong ring.
    /// </summary>
    public static class SignedBlob
    {
        public enum Error { None, NoKey, BadEnvelope, BadSignature, CryptoFailed }

        public static Error Verify(string envelope, LicenseKeyRing ring, int maxPayloadBytes, out byte[] payload, out string kid)
        {
            payload = null; kid = null;
            if (ring == null || ring.Count == 0) return Error.NoKey;
            envelope = (envelope ?? "").Trim();
            if (envelope.Length > maxPayloadBytes * 4L / 3 + 100) return Error.BadEnvelope;
            int dot = envelope.LastIndexOf('.');
            if (dot <= 0 || dot == envelope.Length - 1) return Error.BadEnvelope;
            byte[] body, sig;
            try { body = Convert.FromBase64String(envelope.Substring(0, dot)); sig = Convert.FromBase64String(envelope.Substring(dot + 1)); }
            catch (FormatException) { return Error.BadEnvelope; }
            if (sig.Length != 64 || body.Length == 0 || body.Length > maxPayloadBytes) return Error.BadEnvelope;

            bool cryptoFailed = false;
            foreach (var kv in ring.All)
            {
                try
                {
                    using var ec = ECDsa.Create();
                    ec.ImportFromPem(kv.Value);
                    if (ec.VerifyData(body, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) { payload = body; kid = kv.Key; return Error.None; }
                }
                catch (CryptographicException) { cryptoFailed = true; }
                catch (ArgumentException) { cryptoFailed = true; }
            }
            return cryptoFailed ? Error.CryptoFailed : Error.BadSignature;
        }

        /// <summary>Payload đã verify -> JSON object có "alg":"ES256" và "kid" khớp khóa đã verify. Sai => null.</summary>
        public static JsonDocument ParsePayload(byte[] payload, string verifiedKid, string expectedTyp, out string why)
        {
            why = "";
            string json;
            try { json = new UTF8Encoding(false, true).GetString(payload); }
            catch (DecoderFallbackException) { why = "utf-8"; return null; }
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 }); }
            catch (JsonException) { why = "json"; return null; }
            var r = doc.RootElement;
            bool ok = r.ValueKind == JsonValueKind.Object
                && Str(r, "alg") == SignedEnvelope.ExpectedAlg
                && Str(r, "kid") == verifiedKid
                && (expectedTyp == null || Str(r, "typ") == expectedTyp);
            if (!ok) { why = "alg / kid / typ"; doc.Dispose(); return null; }
            return doc;
        }

        public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        internal static string Str(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

        internal static long Num(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;

        /// <summary>PEM public key P-256 hợp lệ? (không nhận khóa riêng, không nhận đường cong khác).</summary>
        public static bool IsP256PublicPem(string pem)
        {
            if (string.IsNullOrWhiteSpace(pem) || pem.Contains("PRIVATE KEY", StringComparison.Ordinal)) return false;
            try
            {
                using var ec = ECDsa.Create();
                ec.ImportFromPem(pem);
                return ec.KeySize == 256 && ShopLicenseKeys.KidFromPem(pem).Length > 0;
            }
            catch (Exception) { return false; }
        }
    }
}
