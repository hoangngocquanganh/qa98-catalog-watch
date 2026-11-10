// SPDX-License-Identifier: LicenseRef-QA98-Proprietary
// Bản quyền QA98 — xác minh envelope ký số (thuần logic, ZERO phụ thuộc WPF / Win32).
//
// ĐỊNH DẠNG ENVELOPE:  payloadB64 "." signatureB64
//  - payload: UTF-8 JSON, phải chứa "alg": "ES256" và "kid".
//  - signature: base64 của chữ ký ECDSA P-256 / SHA-256 dạng R||S (P1363, đúng 64 byte).
//    KHÔNG dùng DER (độ dài biến đổi) — DER của OpenSSL / VersionPolicy là 1 định dạng
//    khác; nhầm 2 dạng này là lỗi đã từng gây fail verify.
//
// QUY TẮC SẮT:
//  1. Verify chữ ký TRƯỚC khi tin bất kỳ field nào trong payload.
//  2. "kid" trong payload là KHÔNG ĐÁNG TIN trước khi verify: thử từng khóa đã pin
//     trong keyring; khóa nào verify được thì payload.kid PHẢI khớp kid của khóa đó.
//  3. Không tải public key từ server rồi tin nó. Keyring chỉ đến từ binary (hằng
//     ShopLicenseKeys.PublicKeyPem) hoặc từ test (khóa sinh tạm trong test, không ship).
//  4. Keyring RỖNG => mọi envelope đều bị từ chối (fail-closed).

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SysInfoWpf.Services.Licensing
{
    /// <summary>
    /// Keyring các public key đã pin (kid -> PEM). Chỉ chứa PUBLIC key — không bao giờ private key.
    /// </summary>
    public sealed class LicenseKeyRing
    {
        readonly Dictionary<string, string> _pemByKid;

        public LicenseKeyRing() { _pemByKid = new Dictionary<string, string>(StringComparer.Ordinal); }

        public LicenseKeyRing(IDictionary<string, string> pemByKid)
        {
            _pemByKid = new Dictionary<string, string>(StringComparer.Ordinal);
            if (pemByKid != null)
                foreach (var kv in pemByKid)
                    if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                        _pemByKid[kv.Key.Trim()] = kv.Value;
        }

        public int Count => _pemByKid.Count;

        public IEnumerable<KeyValuePair<string, string>> All => _pemByKid;

        public static LicenseKeyRing FromPem(string kid, string pem)
            => new LicenseKeyRing(new Dictionary<string, string> { { kid ?? "", pem ?? "" } });
    }

    /// <summary>Kết quả verify envelope: payload JSON (chỉ tin được khi Ok == true) + lý do.</summary>
    public sealed class EnvelopeResult
    {
        public bool Ok;
        public string Reason = "";
        /// <summary>Không chạy được phép kiểm tra chữ ký (bộ mã hóa Windows lỗi hoặc khóa đã pin hỏng), khác với chữ ký sai. Vẫn là Ok = false.</summary>
        public bool CryptoFailed;
        /// <summary>Payload JSON thô (UTF-8). Chỉ có nghĩa khi Ok == true.</summary>
        public string PayloadJson = "";
        public string Kid = "";
    }

    /// <summary>Xác minh envelope ký ECDSA P-256 / SHA-256 (R||S P1363).</summary>
    public static class SignedEnvelope
    {
        public const string ExpectedAlg = "ES256";

        /// <summary>
        /// Verify envelope. Trả payload JSON khi chữ ký khớp 1 khóa trong keyring VÀ
        /// payload.kid khớp đúng kid của khóa đó. Mọi trường hợp khác => Ok = false.
        /// </summary>
        public static EnvelopeResult Verify(string envelope, LicenseKeyRing keyring)
        {
            var fail = new EnvelopeResult { Ok = false };
            try
            {
                if (string.IsNullOrWhiteSpace(envelope)) { fail.Reason = "Envelope rỗng."; return fail; }
                if (keyring == null || keyring.Count == 0) { fail.Reason = "Keyring rỗng — chưa có public key đã pin."; return fail; }
                int dot = envelope.LastIndexOf('.');
                if (dot <= 0 || dot == envelope.Length - 1) { fail.Reason = "Envelope sai định dạng (thiếu payload.signature)."; return fail; }
                string payloadB64 = envelope.Substring(0, dot);
                string sigB64 = envelope.Substring(dot + 1);
                byte[] payload, sig;
                try { payload = Convert.FromBase64String(payloadB64); sig = Convert.FromBase64String(sigB64); }
                catch (FormatException) { fail.Reason = "Envelope không phải base64 hợp lệ."; return fail; }
                if (sig.Length != 64) { fail.Reason = "Chữ ký phải dài đúng 64 byte (R||S P1363)."; return fail; }
                if (payload.Length == 0 || payload.Length > 65536) { fail.Reason = "Payload sai kích thước."; return fail; }

                // Thử từng khóa đã pin (kid trong payload chưa đáng tin trước khi verify).
                bool cryptoFailed = false;
                foreach (var kv in keyring.All)
                {
                    bool sigOk;
                    try
                    {
                        using (var ec = ECDsa.Create())
                        {
                            ec.ImportFromPem(kv.Value);
                            sigOk = ec.VerifyData(payload, sig, HashAlgorithmName.SHA256,
                                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                        }
                    }
                    catch (CryptographicException) { cryptoFailed = true; continue; }
                    catch (Exception) { continue; }
                    if (!sigOk) continue;
                    // Chữ ký khớp khóa này => giờ mới được đọc payload, và kid phải khớp khóa đó.
                    string json;
                    try { json = Encoding.UTF8.GetString(payload); }
                    catch (Exception) { fail.Reason = "Payload không phải UTF-8."; return fail; }
                    string kid = "", alg = "";
                    try
                    {
                        using (var doc = JsonDocument.Parse(json))
                        {
                            var r = doc.RootElement;
                            if (r.ValueKind != JsonValueKind.Object) { fail.Reason = "Payload không phải JSON object."; return fail; }
                            if (r.TryGetProperty("kid", out var k) && k.ValueKind == JsonValueKind.String) kid = k.GetString() ?? "";
                            if (r.TryGetProperty("alg", out var a) && a.ValueKind == JsonValueKind.String) alg = a.GetString() ?? "";
                        }
                    }
                    catch (JsonException) { fail.Reason = "Payload không phải JSON hợp lệ."; return fail; }
                    if (!string.Equals(alg, ExpectedAlg, StringComparison.Ordinal))
                    { fail.Reason = "alg không phải ES256 (từ chối alg lạ / alg=none)."; return fail; }
                    if (!string.Equals(kid, kv.Key, StringComparison.Ordinal))
                    { fail.Reason = "kid trong payload không khớp khóa đã verify."; return fail; }
                    return new EnvelopeResult { Ok = true, PayloadJson = json, Kid = kid };
                }
                // Vẫn từ chối (fail-closed), nhưng không đổ cho chữ ký sai khi thực ra chưa kiểm tra được.
                fail.CryptoFailed = cryptoFailed;
                fail.Reason = cryptoFailed
                    ? "Không kiểm tra được chữ ký vì lỗi bộ mã hóa của Windows hoặc khóa đã pin bị hỏng."
                    : "Chữ ký không khớp bất kỳ public key nào đã pin.";
                return fail;
            }
            catch (Exception ex) { fail.Reason = "Lỗi verify: " + ex.Message; return fail; }
        }

        /// <summary>
        /// Ký payload (CHỈ DÙNG CHO TEST / tool ký của server — app production KHÔNG BAO GIỜ ký).
        /// Test sinh khóa tạm trong bộ nhớ, ký, rồi verify lại bằng public key tương ứng.
        /// </summary>
        public static string SignForTest(string payloadJson, ECDsa signer)
        {
            if (signer == null) throw new ArgumentNullException(nameof(signer));
            byte[] payload = Encoding.UTF8.GetBytes(payloadJson ?? "");
            byte[] sig = signer.SignData(payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return Convert.ToBase64String(payload) + "." + Convert.ToBase64String(sig);
        }
    }
}
