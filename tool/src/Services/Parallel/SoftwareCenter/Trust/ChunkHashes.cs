using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace SysInfoWpf.Services.SoftwareCenter.Trust
{
    /// <summary>
    /// GĐ5: hash SHA-256 theo đoạn 4 MiB (chunk_sha256 trong catalog đã ký) để tải tiếp an toàn: kiểm đoạn cũ trên đĩa trước khi nối tiếp,
    /// đoạn sai thì tải lại đúng đoạn đó. Phán quyết cuối vẫn là hash toàn tệp. Dùng chung client + công cụ phát hành.
    /// </summary>
    public static class ChunkHashes
    {
        public const int ChunkSize = 4 * 1024 * 1024;

        public static int ExpectedCount(long size) => size <= 0 ? 0 : (int)((size + ChunkSize - 1) / ChunkSize);

        /// <summary>Danh sách có dùng được cho tệp size byte không (đúng số đoạn, mỗi phần tử 64 hex thường).</summary>
        public static bool Usable(IReadOnlyList<string> chunks, long size)
        {
            if (chunks == null || size <= 0 || chunks.Count != ExpectedCount(size)) return false;
            foreach (var c in chunks) if (c == null || c.Length != 64 || !IsLowerHex(c)) return false;
            return true;
        }

        public static List<string> Compute(Stream s)
        {
            var list = new List<string>();
            var buf = new byte[ChunkSize];
            s.Position = 0;
            while (true)
            {
                int n = s.ReadAtLeast(buf, ChunkSize, throwOnEndOfStream: false);
                if (n <= 0) break;
                list.Add(Convert.ToHexString(SHA256.HashData(buf.AsSpan(0, n))).ToLowerInvariant());
                if (n < ChunkSize) break;
            }
            return list;
        }

        /// <summary>Chỉ số các đoạn TRỌN VẸN trong [0, upto) có hash sai. upto phải là bội số của ChunkSize.</summary>
        public static List<int> BadChunks(string file, long upto, IReadOnlyList<string> chunks)
        {
            var bad = new List<int>();
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buf = new byte[ChunkSize];
            int count = (int)Math.Min(upto / ChunkSize, chunks.Count);
            for (int i = 0; i < count; i++)
            {
                fs.Position = (long)i * ChunkSize;
                int n = fs.ReadAtLeast(buf, ChunkSize, throwOnEndOfStream: false);
                if (n != ChunkSize || !string.Equals(Convert.ToHexString(SHA256.HashData(buf.AsSpan(0, n))).ToLowerInvariant(), chunks[i], StringComparison.Ordinal)) bad.Add(i);
            }
            return bad;
        }

        public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        static bool IsLowerHex(string s) { foreach (char ch in s) if (!(ch >= '0' && ch <= '9' || ch >= 'a' && ch <= 'f')) return false; return true; }
    }
}
