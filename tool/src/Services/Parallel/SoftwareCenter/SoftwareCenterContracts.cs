using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SysInfoWpf.Services.SoftwareCenter
{
    // HỢP ĐỒNG CHUNG cho các phiên song song Software Center:
    //   softcenter-app     : danh mục, hàng đợi, giao diện (gọi 2 interface dưới, không tự tải/cài/phát hiện)
    //   softcenter-install : ISoftwareInstaller (tải + kiểm URL/host/sha256/Authenticode/signer + adapter allowlist)
    //   softcenter-detect  : ISoftwareDetector (Uninstall registry CHỈ ĐỌC / MSI product code / winget id, so phiên bản)
    // Chỉ được THÊM vào file này, ghi chú "thêm bởi <phiên>". Không đổi / xóa thành viên có sẵn.

    /// <summary>Trạng thái gói trên máy. Đã cài KHÔNG có nghĩa là có bản quyền.</summary>
    public enum SoftwareInstallState { Unknown, NotInstalled, Installed, UpdateAvailable, NewerThanCatalog }

    public sealed class SoftwareDetectResult
    {
        public SoftwareInstallState State = SoftwareInstallState.Unknown;
        public string InstalledVersion = "";      // "" nếu không biết
        public string MatchedBy = "";             // "registry" / "msi" / "winget" / "" — chỉ để ghi log kỹ thuật
        public string TechLine = "";
    }

    public interface ISoftwareDetector
    {
        /// <summary>Chỉ đọc. Không bao giờ ghi registry / chạy trình cài. Lỗi đọc => State = Unknown, không ném.</summary>
        SoftwareDetectResult Detect(SoftwarePackage package, SoftwareVersion version);
    }

    public enum SoftwareInstallStage { Queued, Resolving, Downloading, Verifying, WaitingForUser, Installing, VerifyingInstall, Done, Failed, Cancelled }

    /// <summary>Mã lỗi ổn định để giao diện hiện lý do song ngữ. Chỉ THÊM ở cuối.</summary>
    public enum SoftwareInstallError
    {
        None,
        NotAllowedByAction,     // gói không phải APPROVED_* (OFFICIAL_PAGE_ONLY, BLOCKED…) => chỉ mở trang chính thức
        NeedsClientUpdate,      // loại trình cài / hành động client chưa hỗ trợ
        WrongArchitecture,
        NoInternet,
        SourceUnavailable,      // nguồn chính + mọi nguồn dự phòng đã duyệt đều hỏng => hiện trang chính chủ
        HostNotAllowed,         // URL cuối (sau redirect) không thuộc allowed_hosts hoặc không phải HTTPS
        NotAnInstaller,         // tải về là HTML / trang đăng nhập / CAPTCHA
        SizeMismatch,
        HashMismatch,
        BadSignature,           // Authenticode không hợp lệ
        SignerMismatch,         // ký hợp lệ nhưng không đúng signer_subject
        UnsafeArchive,          // ZIP slip / đường dẫn tuyệt đối / quá lớn
        LowDiskSpace,
        UacDeclined,
        UserCancelled,
        InstallerFailed,        // trình cài trả mã lỗi
        RebootRequired,
        NotDetectedAfterInstall,
        WingetMissing,
        Unknown,
        InstallerTimedOut,      // thêm bởi nhánh A: trình cài chạy quá trần thời gian => đã giết cả cây tiến trình
        DownloadTimedOut,       // GĐ1: lượt tải vượt trần tổng (server nhỏ giọt); tệp tải dở được giữ để Thử lại tải tiếp
        Unsigned,               // GĐ5: tệp KHÔNG có chữ ký Authenticode (TRUST_E_NOSIGNATURE) — khác BadSignature (lỗi chuỗi / chữ ký hỏng)
        CertRevoked,            // GĐ5: chứng chỉ ký ĐÃ BỊ THU HỒI (CERT_E_REVOKED) — cảnh báo mức cao nhất
        KillSwitchBlocked,      // GĐ5: bản này bị chặn khẩn cấp trong freshness.json (kill-switch)
        CatalogNotFresh,        // GĐ5: freshness hết hạn / thiếu / sai => dừng cài mới, vẫn cho xem
    }

    public sealed class SoftwareInstallProgress
    {
        public SoftwareInstallStage Stage;
        public long? BytesDone, BytesTotal;       // null = không biết => giao diện để progress không xác định, KHÔNG bịa %
        public string DetailVi = "", DetailEn = "";
        public string TechLine = "";
    }

    public sealed class SoftwareInstallResult
    {
        public SoftwareInstallError Error = SoftwareInstallError.Unknown;
        public int? ExitCode;
        public bool RebootRequired;
        public string UsedUrl = "";               // URL cuối thực sự dùng (chính hoặc dự phòng)
        public bool UsedFallback;
        public string InstalledPath = "";         // ZIP portable: thư mục đã giải nén
        public string DownloadedPath = "";
        /// <summary>GĐ5: loại lỗi chữ ký Authenticode khi kiểm (None nếu đạt / không cần kiểm) — job / lịch sử ghi đúng loại.</summary>
        public Trust.SignatureFailure SignatureFailure;
        public bool FromCache;                  // GĐ1: dùng lại bộ cài trong cache (đã kiểm lại hash + chữ ký), không tải        // thêm bởi nhánh A: APPROVED_DIRECT_DOWNLOAD (chỉ tải) => thư mục chứa tệp đã kiểm, không chạy gì
        public string TechLog = "";
    }

    public sealed class SoftwareInstallRequest
    {
        /// <summary>GĐ1: chỉ tải + kiểm hash/chữ ký vào cache (tải sẵn trong lúc gói trước đang cài), không chạy gì. Cần CacheRoot.</summary>
        public bool PrefetchOnly;
        public SoftwarePackage Package;
        public SoftwareVersion Version;
        public string WorkDir = "";               // thư mục tạm của phiên tải; trình cài tự tạo/dọn trong đó
        public string PortableRoot = "";          // ZIP portable: thư mục người dùng đã đồng ý (VD %LOCALAPPDATA%\Programs\QA98Apps)
        public bool UserConsented;                // người dùng đã bấm "Xác nhận & Tải/Cài" sau khi xem license / chi phí
        public bool AcceptedTermsInApp;           // khách đã đọc điều khoản + tick đồng ý trong hộp điều khoản QA98 => cài im lặng (như winget --accept-package-agreements)
    }

    public interface ISoftwareInstaller
    {
        /// <summary>Hỗ trợ được không, trước khi đưa vào hàng đợi (dùng để hiện nút Cài hay chỉ nút Trang chính thức).</summary>
        SoftwareInstallError CanInstall(SoftwarePackage package, SoftwareVersion version);

        /// <summary>Tải + kiểm tra lại toàn bộ ngay trước khi chạy + cài. Không ném: lỗi trả qua Result.Error.</summary>
        Task<SoftwareInstallResult> InstallAsync(SoftwareInstallRequest request, IProgress<SoftwareInstallProgress> progress, CancellationToken ct);
    }
}
