using System;
using System.Collections.Generic;
using System.Linq;

namespace SysInfoWpf.Services.SoftwareCenter
{
    public enum Availability
    {
        Installable,        // có phiên bản hợp lệ, trình cài (ISoftwareInstaller) nhận
        OfficialPageOnly,   // chỉ mở trang chính thức
        SourceUnavailable,  // mọi nguồn tải đã duyệt đều hỏng
        NeedsClientUpdate,  // loại trình cài / hành động mà bản QA98 này chưa hỗ trợ
        WrongArch,          // không có bản cho kiến trúc máy này
        NoValidVersion,     // không có phiên bản hợp lệ / trình cài từ chối vì lý do khác
        Module,             // dẫn sang mô-đun riêng (Office Manager)
        Hidden,             // BLOCKED
    }

    public sealed class PackageView
    {
        public SoftwarePackage Package;
        public SoftwareVersion Chosen;         // phiên bản sẽ dùng (null nếu không tải/cài được)
        public string CatalogVersion = "";     // phiên bản mới nhất hợp với máy (chỉ để hiển thị)
        public Availability Availability;
        /// <summary>Lý do trình cài từ chối (CanInstall), None nếu nhận.</summary>
        public SoftwareInstallError InstallerSays = SoftwareInstallError.None;
        public SoftwareDetectResult Detect = new SoftwareDetectResult();
        public bool IsInstalled => Detect.State is SoftwareInstallState.Installed or SoftwareInstallState.NewerThanCatalog;
        /// <summary>Được đưa vào hàng đợi "Tải/Cài" không. OFFICIAL_PAGE_ONLY / user-driven / BLOCKED / lỗi / đã cài => false.</summary>
        public bool CanQueue => Chosen != null && Availability == Availability.Installable && !IsInstalled;
    }

    /// <summary>
    /// Quyết định hiển thị / cho vào hàng đợi từ danh mục + kết quả của 2 phiên khác:
    /// <see cref="ISoftwareDetector"/> (đã cài chưa) và <see cref="ISoftwareInstaller.CanInstall"/> (cài được không).
    /// Lớp này không tải, không cài, không đọc registry.
    /// </summary>
    public static class SoftwareCenterPlanner
    {
        public static bool IsAutoAction(PackageAction a)
            => a == PackageAction.ApprovedDirectDownload || a == PackageAction.ApprovedInteractiveInstall || a == PackageAction.ApprovedUnattendedInstall;

        /// <summary>Các WinGet ID được phép cài / nâng cấp từ mọi điểm vào (kể cả Trung tâm ứng dụng): chỉ gói trong danh mục đã ký,
        /// mức cho tự động, có bản WinGet hợp lệ. Gói ngoài danh mục => chỉ tra cứu.</summary>
        public static HashSet<string> ApprovedWingetIds(SoftwareCatalog catalog)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in catalog?.Packages ?? new List<SoftwarePackage>())
            {
                if (!IsAutoAction(p.Action) || p.Module.Length > 0) continue;
                foreach (var v in p.Versions)
                    if (v.Type == InstallerType.Winget && v.Invalid.Length == 0 && SoftwareCatalogParser.IsSafeWingetId(v.WingetId)) ids.Add(v.WingetId);
            }
            return ids;
        }

        /// <summary>Hạng phù hợp kiến trúc (nhỏ = tốt hơn); -1 = không chạy được trên máy này.</summary>
        public static int ArchRank(CpuArch pkg, CpuArch machine)
        {
            if (pkg == machine) return 0;
            if (pkg == CpuArch.Neutral) return 1;
            return (machine, pkg) switch
            {
                (CpuArch.X64, CpuArch.X86) => 2,
                (CpuArch.Arm64, CpuArch.X64) => 2,   // Windows 11 ARM giả lập x64
                (CpuArch.Arm64, CpuArch.X86) => 3,
                _ => -1,
            };
        }

        /// <summary>So sánh phiên bản theo từng cụm số ("24.09" &lt; "24.10", "1.2" == "1.2.0").</summary>
        public static int CompareVersion(string a, string b)
        {
            static long[] P(string s) => (s ?? "").Split(new[] { '.', '-', '_', ' ', '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => long.TryParse(new string(x.TakeWhile(char.IsDigit).ToArray()), out long n) ? n : 0).ToArray();
            var x = P(a); var y = P(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                long l = i < x.Length ? x[i] : 0, r = i < y.Length ? y[i] : 0;
                if (l != r) return l < r ? -1 : 1;
            }
            return 0;
        }

        /// <summary>Phiên bản mới nhất, hợp lệ, chạy được trên máy; cùng phiên bản thì ưu tiên kiến trúc gốc.</summary>
        public static SoftwareVersion Pick(SoftwarePackage p, CpuArch machine)
            => p.Versions.Where(v => v.Invalid.Length == 0 && ArchRank(v.Arch, machine) >= 0)
                .OrderByDescending(v => v, Comparer<SoftwareVersion>.Create((l, r) => CompareVersion(l.Version, r.Version)))
                .ThenBy(v => ArchRank(v.Arch, machine))
                .FirstOrDefault();

        /// <summary>Còn nguồn tải nào không BROKEN (nguồn chính hoặc dự phòng). WINGET không có URL.</summary>
        public static bool HasLiveSource(SoftwareVersion v)
            => v.Type == InstallerType.Winget || v.Status != SourceStatus.Broken || v.Fallbacks.Any(f => f.Status != SourceStatus.Broken);

        public static PackageView Evaluate(SoftwarePackage p, CpuArch machine, ISoftwareDetector detector, ISoftwareInstaller installer)
        {
            var view = new PackageView { Package = p };
            if (p.Action == PackageAction.Blocked) { view.Availability = Availability.Hidden; return view; }
            if (p.Module.Length > 0) { view.Availability = Availability.Module; return view; }

            var chosen = Pick(p, machine);
            view.CatalogVersion = chosen?.Version ?? "";
            try { view.Detect = detector?.Detect(p, chosen ?? p.Versions.FirstOrDefault()) ?? new SoftwareDetectResult(); }
            catch (Exception ex) { view.Detect = new SoftwareDetectResult { TechLine = ex.Message }; }   // hợp đồng: không ném; phòng hờ

            if (!IsAutoAction(p.Action))
            {
                // OFFICIAL_PAGE_ONLY, OFFICIAL_DOWNLOAD_USER_DRIVEN, hành động lạ: chỉ mở trang chính thức, không bao giờ tải/cài.
                view.Availability = p.Action == PackageAction.Unknown ? Availability.NeedsClientUpdate : Availability.OfficialPageOnly;
                return view;
            }
            if (chosen == null)
            {
                view.Availability = p.Versions.Any(v => v.Invalid.Length == 0) ? Availability.WrongArch
                    : p.Versions.Any(v => v.Invalid == "NEEDS_CLIENT_UPDATE") ? Availability.NeedsClientUpdate
                    : Availability.NoValidVersion;
                return view;
            }
            if (!HasLiveSource(chosen)) { view.Availability = Availability.SourceUnavailable; return view; }

            SoftwareInstallError can;
            try { can = installer?.CanInstall(p, chosen) ?? SoftwareInstallError.NeedsClientUpdate; }
            catch (Exception) { can = SoftwareInstallError.Unknown; }
            view.InstallerSays = can;
            view.Availability = can switch
            {
                SoftwareInstallError.None => Availability.Installable,
                SoftwareInstallError.NotAllowedByAction => Availability.OfficialPageOnly,
                SoftwareInstallError.NeedsClientUpdate => Availability.NeedsClientUpdate,
                SoftwareInstallError.WrongArchitecture => Availability.WrongArch,
                SoftwareInstallError.SourceUnavailable => Availability.SourceUnavailable,
                _ => Availability.NoValidVersion,
            };
            if (view.Availability == Availability.Installable) view.Chosen = chosen;
            return view;
        }
    }
}
