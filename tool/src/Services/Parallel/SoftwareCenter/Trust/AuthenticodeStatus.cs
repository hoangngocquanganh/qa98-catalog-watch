namespace SysInfoWpf.Services.SoftwareCenter.Trust
{
    /// <summary>Loại lỗi chữ ký Authenticode (GĐ5 §4) — để job / lịch sử ghi đúng: chưa ký ≠ đã thu hồi ≠ lỗi chuỗi khác.</summary>
    public enum SignatureFailure { None, NoSignature, Revoked, ChainError }

    public static class AuthenticodeStatus
    {
        public const int TrustENoSignature = unchecked((int)0x800B0100);      // TRUST_E_NOSIGNATURE
        public const int TrustESubjectFormUnknown = unchecked((int)0x800B0003); // TRUST_E_SUBJECT_FORM_UNKNOWN (tệp không mang được chữ ký)
        public const int TrustEProviderUnknown = unchecked((int)0x800B0001);
        public const int CertERevoked = unchecked((int)0x800B010C);           // CERT_E_REVOKED
        public const int TrustEExplicitDistrust = unchecked((int)0x800B0111);  // chứng chỉ bị đánh dấu không tin (Windows chặn tường minh)

        /// <summary>Mã trả về của WinVerifyTrust -> loại lỗi. 0 = đạt. Hết hạn chứng chỉ KHÔNG xét ở đây: để WinVerifyTrust (có timestamp RFC 3161) quyết định.</summary>
        public static SignatureFailure Classify(int hresult) => hresult switch
        {
            0 => SignatureFailure.None,
            TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown => SignatureFailure.NoSignature,
            CertERevoked or TrustEExplicitDistrust => SignatureFailure.Revoked,
            _ => SignatureFailure.ChainError,
        };
    }
}
