using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace Mizu.Updating
{
    // Shared by the app preflight and the independent .NET Framework updater.
    public static class PackageArchive
    {
        public static void CheckTree(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("更新路径不能包含符号链接或目录联接。");
                current = Path.GetDirectoryName(current) ?? "";
            }
        }
        public static string Under(string root, string relative)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("更新包包含越界路径。");
            return full;
        }
        public static void CheckExecutable(string path, string runtime)
        {
            using (var stream = File.OpenRead(path)) CheckExecutable(stream, runtime);
        }
        public static void CheckExecutable(Stream stream, string runtime)
        {
            using (var data = new MemoryStream())
            {
                // Zip entry streams cannot seek. The PE header must be near the start.
                var block = new byte[4096]; int read = stream.Read(block, 0, block.Length);
                while (read < block.Length) { int n = stream.Read(block, read, block.Length - read); if (n == 0) break; read += n; }
                if (read < 64 || block[0] != 'M' || block[1] != 'Z') throw new IOException("更新包中的程序无效。");
                int offset = BitConverter.ToInt32(block, 0x3c);
                if (offset < 64 || offset > read - 6 || BitConverter.ToUInt32(block, offset) != 0x00004550)
                    throw new IOException("更新包中的 PE 头无效。");
                ushort machine = BitConverter.ToUInt16(block, offset + 4);
                ushort expected = runtime == "win-x64" ? (ushort)0x8664 : runtime == "win-arm64" ? (ushort)0xaa64 : (ushort)0x14c;
                if (!String.IsNullOrEmpty(runtime) && machine != expected) throw new IOException("更新包架构不匹配。");
            }
        }
        public static void Inspect(string path, string runtime)
        {
            using (var zip = ZipFile.OpenRead(path)) Validate(zip, runtime);
        }
        private static void Validate(ZipArchive zip, string runtime)
        {
            if (runtime != "win-x64" && runtime != "win-x86" && runtime != "win-arm64") throw new IOException("无效更新架构。");
            string prefix = "Mizu-" + runtime + "/";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            if (zip.Entries.Count > 10000) throw new IOException("更新包文件过多。");
            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName;
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.IndexOf('\\') >= 0 ||
                    ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & 0x400) != 0)
                    throw new IOException("更新包包含无效路径或链接。");
                string relative = name.Substring(prefix.Length);
                if (relative.Length == 0) continue;
                string normalized = relative.TrimEnd('/');
                foreach (string part in normalized.Split('/'))
                    if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") ||
                        part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                        Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                        throw new IOException("更新包包含无效文件名。");
                if (!seen.Add(normalized)) throw new IOException("更新包包含重复文件。");
                if (entry.Length > 512L * 1024 * 1024 || total > 1024L * 1024 * 1024 - entry.Length)
                    throw new IOException("更新包解压大小过大。");
                total += entry.Length;
            }
            foreach (string required in new[] { "Mizu.exe", "app/Mizu.exe", "app/Mizu.dll", "app/Mizu.runtimeconfig.json", "app/Mizu.Updater.exe", "app/mizu-package.json" })
                if (zip.GetEntry(prefix + required) == null) throw new IOException("更新包不完整。");
            var executable = zip.GetEntry(prefix + "app/Mizu.exe");
            if (executable == null) throw new IOException("更新包程序缺失。");
            using (var stream = executable.Open()) CheckExecutable(stream, runtime);
        }
        public static string Extract(string path, string runtime, string destination)
        {
            CheckTree(destination);
            if (Directory.Exists(destination)) throw new IOException("更新暂存目录已存在。");
            using (var zip = ZipFile.OpenRead(path))
            {
                Validate(zip, runtime); Directory.CreateDirectory(destination);
                string prefix = "Mizu-" + runtime + "/";
                foreach (var entry in zip.Entries)
                {
                    string relative = entry.FullName.Substring(prefix.Length);
                    if (relative.Length == 0) continue;
                    string target = Under(destination, relative.Replace('/', Path.DirectorySeparatorChar));
                    if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    string parent = Path.GetDirectoryName(target) ?? "";
                    if (parent.Length == 0) throw new IOException("无效的解压目录。");
                    Directory.CreateDirectory(parent);
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] block = new byte[65536]; long written = 0; int count;
                        while ((count = input.Read(block, 0, block.Length)) > 0) {
                            written += count;
                            if (written > entry.Length) throw new IOException("更新包解压长度超出声明。");
                            output.Write(block, 0, count);
                        }
                        if (written != entry.Length) throw new IOException("更新包解压文件不完整。");
                    }
                }
            }
            return destination;
        }
    }
}
