using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SysInfoWpf.Services.SoftwareCenter.Trust
{
    /// <summary>Sàn chống rollback danh mục (GĐ5 §1.2). Mọi trường chỉ được TĂNG.</summary>
    public sealed class CatalogFloor
    {
        /// <summary>
        /// SÀN ĐÓNG GÓI: revision danh mục đang chạy ngoài thực tế lúc phát hành bản app này (đọc từ catalog cache thật ngày 10/11/2026: revision 24).
        /// Client không bao giờ nhận danh mục dưới sàn này kể cả khi mọi nơi lưu bị xóa. Mỗi lần phát hành app: nâng lên revision hiện hành.
        /// </summary>
        public const long MinCatalogRevision = 24;

        public long Revision, CatalogIssuedAt, FreshnessIssuedAt;
        /// <summary>Đã từng thấy freshness.json hợp lệ => từ nay bắt buộc có freshness (không cho lùi về chế độ chuyển tiếp bằng cách giấu file).</summary>
        public bool FreshnessSeen;

        public static CatalogFloor Max(CatalogFloor a, CatalogFloor b)
        {
            a ??= new CatalogFloor(); b ??= new CatalogFloor();
            return new CatalogFloor
            {
                Revision = Math.Max(a.Revision, b.Revision),
                CatalogIssuedAt = Math.Max(a.CatalogIssuedAt, b.CatalogIssuedAt),
                FreshnessIssuedAt = Math.Max(a.FreshnessIssuedAt, b.FreshnessIssuedAt),
                FreshnessSeen = a.FreshnessSeen || b.FreshnessSeen,
            };
        }

        public bool SameAs(CatalogFloor o) => o != null && o.Revision == Revision && o.CatalogIssuedAt == CatalogIssuedAt
            && o.FreshnessIssuedAt == FreshnessIssuedAt && o.FreshnessSeen == FreshnessSeen;

        const string Magic = "QA98CF1";

        /// <summary>Chuỗi lưu: "QA98CF1|rev|catalog_issued|freshness_issued|seen|hmac" — HMAC-SHA256 theo khóa máy (chống sửa tay / hỏng tệp, không phải bí mật).</summary>
        public static string Serialize(CatalogFloor f, byte[] key)
        {
            string body = string.Join("|", Magic, f.Revision.ToString(CultureInfo.InvariantCulture), f.CatalogIssuedAt.ToString(CultureInfo.InvariantCulture),
                f.FreshnessIssuedAt.ToString(CultureInfo.InvariantCulture), f.FreshnessSeen ? "1" : "0");
            return body + "|" + Mac(body, key);
        }

        /// <summary>Sai định dạng / sai HMAC / số âm => null (nguồn đó coi như không có, nguồn khác + sàn đóng gói vẫn giữ).</summary>
        public static CatalogFloor Deserialize(string raw, byte[] key)
        {
            var p = (raw ?? "").Trim().Split('|');
            if (p.Length != 6 || p[0] != Magic) return null;
            string body = string.Join("|", p.Take(5));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Mac(body, key)), Encoding.ASCII.GetBytes(p[5]))) return null;
            if (!long.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out long rev)
                || !long.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ci)
                || !long.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out long fi)
                || (p[4] != "0" && p[4] != "1")) return null;
            return new CatalogFloor { Revision = rev, CatalogIssuedAt = ci, FreshnessIssuedAt = fi, FreshnessSeen = p[4] == "1" };
        }

        static string Mac(string body, byte[] key)
        {
            using var h = new HMACSHA256(key ?? Array.Empty<byte>());
            return Convert.ToHexString(h.ComputeHash(Encoding.ASCII.GetBytes(body))).ToLowerInvariant();
        }
    }

    /// <summary>Một nơi lưu sàn độc lập. Test thay bằng bộ nhớ (không ghi HKLM / ProgramData thật).</summary>
    public interface ICatalogFloorSource
    {
        string Name { get; }
        /// <summary>null = chưa có / hỏng. Được ném (bên gọi bắt + ghi log).</summary>
        CatalogFloor Read();
        /// <summary>Được ném (VD không có quyền ghi HKLM) — bên gọi bỏ qua + ghi log.</summary>
        void Write(CatalogFloor f);
    }

    /// <summary>
    /// Gộp các nơi lưu: đọc = giá trị LỚN NHẤT các nguồn đọc được + sàn đóng gói; ghi = ghi lại mọi nguồn (nguồn bị xóa / sửa tay được khôi phục).
    /// Hai nguồn lệch nhau => ghi Diag.Log. Lỗi quyền ghi => bỏ qua + Diag.Log.
    /// </summary>
    public sealed class DurableCatalogFloor
    {
        readonly ICatalogFloorSource[] _sources;
        readonly long _builtIn;

        public DurableCatalogFloor(long builtInMinRevision, params ICatalogFloorSource[] sources)
        {
            _builtIn = builtInMinRevision;
            _sources = (sources ?? Array.Empty<ICatalogFloorSource>()).Where(s => s != null).ToArray();
        }

        public CatalogFloor Read()
        {
            var got = new List<CatalogFloor>();
            foreach (var s in _sources)
            {
                try { var f = s.Read(); if (f != null) got.Add(f); }
                catch (Exception ex) { Diag.Log("SoftwareCenter", "Warn", "doc san danh muc " + s.Name, ex); }
            }
            var m = got.Aggregate(new CatalogFloor(), CatalogFloor.Max);
            if (got.Count > 1 && got.Any(f => !f.SameAs(got[0])))
                Diag.Log("SoftwareCenter", "Warn", "san danh muc lech giua cac noi luu: " + string.Join(", ", got.Select(f => f.Revision)) + " => dung " + m.Revision, null);
            if (m.Revision < _builtIn) m.Revision = _builtIn;
            return m;
        }

        public void Raise(CatalogFloor candidate)
        {
            var m = CatalogFloor.Max(Read(), candidate);
            foreach (var s in _sources)
            {
                try
                {
                    var cur = s.Read();
                    if (cur == null || !cur.SameAs(m)) s.Write(m);
                }
                catch (Exception ex) { Diag.Log("SoftwareCenter", "Warn", "ghi san danh muc " + s.Name + " (bo qua)", ex); }
            }
        }
    }

    /// <summary>Tệp sàn (máy thật: %ProgramData%\QA98\catalog-floor.dat). Ghi nguyên tử: tệp tạm rồi Move đè.</summary>
    public sealed class FileFloorSource : ICatalogFloorSource
    {
        readonly string _path; readonly byte[] _key;
        public FileFloorSource(string path, byte[] key) { _path = path; _key = key; }
        public string Name => _path;

        public CatalogFloor Read() => File.Exists(_path) ? CatalogFloor.Deserialize(File.ReadAllText(_path, Encoding.ASCII), _key) : null;

        public void Write(CatalogFloor f)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(tmp, CatalogFloor.Serialize(f, _key), Encoding.ASCII); File.Move(tmp, _path, true); }
            finally { try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { } }
        }
    }

    /// <summary>HKLM\SOFTWARE\HNQA\QA98, giá trị "CatalogFloor" (view 64-bit). Sửa cần quyền admin — cùng mức quyền với cài phần mềm.</summary>
    [SupportedOSPlatform("windows")]
    public sealed class RegistryFloorSource : ICatalogFloorSource
    {
        public const string KeyPath = @"SOFTWARE\HNQA\QA98", ValueName = "CatalogFloor";
        readonly byte[] _key;
        public RegistryFloorSource(byte[] key) { _key = key; }
        public string Name => @"HKLM\" + KeyPath;

        public CatalogFloor Read()
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var k = root.OpenSubKey(KeyPath);
            return k?.GetValue(ValueName) is string s ? CatalogFloor.Deserialize(s, _key) : null;
        }

        public void Write(CatalogFloor f)
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var k = root.CreateSubKey(KeyPath, true);
            k.SetValue(ValueName, CatalogFloor.Serialize(f, _key), RegistryValueKind.String);
        }
    }

    [SupportedOSPlatform("windows")]
    public static class CatalogFloorStores
    {
        /// <summary>Máy thật: ProgramData + HKLM + sàn đóng gói <see cref="CatalogFloor.MinCatalogRevision"/>. Chỉ gọi từ app (không gọi trong test).</summary>
        public static DurableCatalogFloor ForMachine()
        {
            byte[] key = MachineKey();
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "QA98", "catalog-floor.dat");
            return new DurableCatalogFloor(CatalogFloor.MinCatalogRevision, new FileFloorSource(file, key), new RegistryFloorSource(key));
        }

        /// <summary>SHA-256("HNQA-CATFLOOR|" + MachineGuid) — cùng cách VersionPolicyStore lấy khóa theo máy (chỉ đọc registry).</summary>
        static byte[] MachineKey()
        {
            string guid = "";
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                guid = k?.GetValue("MachineGuid") as string ?? "";
            }
            catch (Exception ex) { Diag.Log("SoftwareCenter", "Warn", "doc MachineGuid", ex); }
            return SHA256.HashData(Encoding.UTF8.GetBytes("HNQA-CATFLOOR|" + guid));
        }
    }
}
