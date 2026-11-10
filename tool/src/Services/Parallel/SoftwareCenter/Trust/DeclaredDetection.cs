using System.Collections.Generic;
using System.Linq;

namespace SysInfoWpf.Services.SoftwareCenter.Trust
{
    /// <summary>Kết quả 1 lần dò. ReadOk = false: không đọc được nguồn => không được kết luận "chưa cài".</summary>
    public sealed class ProbeAnswer
    {
        public bool ReadOk = true, Found;
        public string Version = "";
        public static ProbeAnswer NotFound => new ProbeAnswer();
        public static ProbeAnswer Unreadable => new ProbeAnswer { ReadOk = false };
        public static ProbeAnswer At(string version) => new ProbeAnswer { Found = true, Version = version ?? "" };
    }

    /// <summary>Nguồn dò máy thật (chỉ ĐỌC). Máy thật: WindowsDeclaredDetectionProbe (Services/SoftwareInstallService.cs); test: giả.</summary>
    public interface IDeclaredDetectionProbe
    {
        /// <summary>ARP (Uninstall) HKLM 64 + WOW6432Node + HKCU: theo tên khóa (nếu có) hoặc DisplayName bắt đầu bằng chuỗi + Publisher chứa chuỗi.</summary>
        ProbeAnswer Arp(string displayName, string publisher, string keyName);
        /// <summary>Windows Installer: sản phẩm liên quan theo UpgradeCode (MsiEnumRelatedProducts) — đổi ProductCode sau major upgrade vẫn nhận ra.</summary>
        ProbeAnswer MsiUpgradeCode(string upgradeCode);
        /// <summary>Inno: khóa Uninstall "{AppId}_is1".</summary>
        ProbeAnswer InnoUninstall(string appId);
        /// <summary>Squirrel: %LOCALAPPDATA%\folder có Update.exe + thư mục app-x.y.z (lấy bản cao nhất).</summary>
        ProbeAnswer Squirrel(string folder);
        /// <summary>Tệp dưới gốc đã biết (ProgramFiles / ProgramFilesX86 / LocalAppData / ProgramData) + đọc file version.</summary>
        ProbeAnswer File(string root, string relativePath);
    }

    /// <summary>
    /// GĐ5 §2: nhận diện đã cài theo tín hiệu khai báo trong catalog đã ký, thứ tự nguồn tin cậy của nghiên cứu:
    /// ARP (kể cả Inno unins) → MSI theo UpgradeCode → Squirrel (per-user) → file version. Trung thực: đọc thiếu => Unknown, không suy ra "chưa cài".
    /// </summary>
    public static class DeclaredDetection
    {
        static int Rank(DetectionType t) => t switch
        {
            DetectionType.Arp => 0, DetectionType.Unins => 1, DetectionType.MsiUpgradeCode => 2, DetectionType.Squirrel => 3, _ => 4,
        };

        public static SoftwareDetectResult Evaluate(IReadOnlyList<DetectionSignal> signals, string catalogVersion, IDeclaredDetectionProbe probe)
        {
            var r = new SoftwareDetectResult();
            if (signals == null || signals.Count == 0 || probe == null) { r.TechLine = "no declared detection"; return r; }
            bool allRead = true;
            foreach (var s in signals.OrderBy(x => Rank(x.Type)))
            {
                ProbeAnswer a;
                try
                {
                    a = s.Type switch
                    {
                        DetectionType.Arp => probe.Arp(s.DisplayName, s.Publisher, s.Key),
                        DetectionType.Unins => probe.InnoUninstall(s.AppId),
                        DetectionType.MsiUpgradeCode => probe.MsiUpgradeCode(s.UpgradeCode),
                        DetectionType.Squirrel => probe.Squirrel(s.Folder),
                        _ => probe.File(s.Root, s.Path),
                    } ?? ProbeAnswer.Unreadable;
                }
                catch (System.Exception) { a = ProbeAnswer.Unreadable; }
                if (!a.ReadOk) { allRead = false; continue; }
                if (!a.Found) continue;
                r.MatchedBy = "declared:" + s.Type;
                r.InstalledVersion = a.Version;
                if (s.MinVersion.Length > 0 && a.Version.Length > 0 && SoftwareCenterPlanner.CompareVersion(a.Version, s.MinVersion) < 0)
                    r.State = SoftwareInstallState.UpdateAvailable;
                else if (a.Version.Length == 0 || string.IsNullOrEmpty(catalogVersion)) r.State = SoftwareInstallState.Installed;
                else
                {
                    int c = SoftwareCenterPlanner.CompareVersion(a.Version, catalogVersion);
                    r.State = c < 0 ? SoftwareInstallState.UpdateAvailable : c > 0 ? SoftwareInstallState.NewerThanCatalog : SoftwareInstallState.Installed;
                }
                return r;
            }
            r.State = allRead ? SoftwareInstallState.NotInstalled : SoftwareInstallState.Unknown;
            r.TechLine = allRead ? "declared: not found" : "declared: some sources unreadable";
            return r;
        }
    }
}
