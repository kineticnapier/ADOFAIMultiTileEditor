using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class WorkspaceDeletion
    {
        private const int BackupGenerations = 12;

        internal static void ArchiveAndDelete(scnEditor editor)
        {
            string chartPath = GetChartPath(editor);
            if (string.IsNullOrWhiteSpace(chartPath)) return;

            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ADOFAIMultiTileEditor",
                "workspaces");
            string key = HashPath(NormalizePath(chartPath));
            string primary = Path.Combine(root, key + ".mtews");
            if (!File.Exists(primary)) return;

            // Intentional deletion must not be interpreted as a corrupt/missing primary
            // and auto-restored from .bak on the next launch. Move one known-good copy
            // outside the auto-recovery namespace first, then clear primary generations.
            string trash = Path.Combine(root, "trash");
            Directory.CreateDirectory(trash);
            string archived = Path.Combine(
                trash,
                key + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".mtews");
            File.Copy(primary, archived, true);

            TryDelete(primary);
            TryDelete(primary + ".bak");
            for (int i = 1; i < BackupGenerations; i++)
                TryDelete(primary + ".bak." + i);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string GetChartPath(scnEditor editor)
        {
            string path = ADOBase.levelPath;
            if (!string.IsNullOrWhiteSpace(path)) return path;
            try
            {
                if (editor != null && editor.customLevel != null && !string.IsNullOrWhiteSpace(editor.customLevel.levelPath))
                    return editor.customLevel.levelPath;
            }
            catch { }
            return string.Empty;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return Path.GetFullPath(path).Trim().ToLowerInvariant(); }
            catch { return path.Trim().ToLowerInvariant(); }
        }

        private static string HashPath(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
