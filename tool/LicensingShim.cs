using System;
using System.Security.Cryptography;

namespace SysInfoWpf.Services.Licensing
{
    /// <summary>
    /// Bản tối thiểu của ShopLicenseKeys.KidFromPem (QA98.Licensing/ShopLicenseCore.cs) để repo bot công khai KHÔNG phải chứa mã bản quyền.
    /// Phải giống hệt bản gốc: kid = 16 ký tự hex thường đầu của SHA-256(DER SubjectPublicKeyInfo). Lệch => verify catalog fail-closed (bot dừng, mã 2).
    /// </summary>
    public static class ShopLicenseKeys
    {
        public static string KidFromPem(string pem)
        {
            try
            {
                using var ec = ECDsa.Create();
                ec.ImportFromPem(pem);
                return Convert.ToHexString(SHA256.HashData(ec.ExportSubjectPublicKeyInfo())).ToLowerInvariant().Substring(0, 16);
            }
            catch (Exception) { return ""; }
        }
    }
}
