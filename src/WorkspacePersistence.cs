using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using ADOFAI;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class WorkspacePersistence
    {
        private const string Magic = "ADOFAI-MTE-WORKSPACE";
        private const int FormatVersion = 2;
        private const int OldestSupportedFormatVersion = 1;
        private const int BackupGenerations = 12;

        internal static bool CanPersist(scnEditor editor)
        {
            return !string.IsNullOrWhiteSpace(GetChartPath(editor));
        }

        // rotateHistory is intentionally false for ordinary metadata/autosave writes.
        // Immutable TrackSlot.Data snapshots only advance historical generations on
        // explicit snapshot-set changes (add / replace / delete).
        internal static void Save(
            scnEditor editor,
            IList<TrackSlot> tracks,
            int nextAutoTagId,
            bool rotateHistory = false)
        {
            string chartPath = GetChartPath(editor);
            if (string.IsNullOrWhiteSpace(chartPath) || tracks == null || tracks.Count == 0) return;

            string path = GetWorkspacePath(chartPath);
            string temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            try
            {
                WriteWorkspace(temp, chartPath, tracks, nextAutoTagId);

                // A periodic autosave of an unchanged immutable snapshot set must not
                // consume recovery generations.
                if (File.Exists(path) && FilesEquivalent(path, temp)) return;

                if (!File.Exists(path))
                {
                    File.Move(temp, path);
                    return;
                }

                if (rotateHistory)
                {
                    RotateBackups(path);
                    File.Copy(path, BackupPath(path, 0), true);
                }

                bool replaced = false;
                try
                {
                    File.Replace(temp, path, null, true);
                    replaced = true;
                }
                catch { }

                if (!replaced)
                {
                    File.Delete(path);
                    File.Move(temp, path);
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static void WriteWorkspace(
            string path,
            string chartPath,
            IList<TrackSlot> tracks,
            int nextAutoTagId)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(NormalizePath(chartPath));
                writer.Write(Math.Max(1, nextAutoTagId));
                writer.Write(tracks.Count);

                for (int i = 0; i < tracks.Count; i++)
                {
                    TrackSlot track = tracks[i];
                    if (track == null || track.Data == null)
                        throw new InvalidOperationException("MTE workspace contains an empty track at index " + i + ".");

                    writer.Write(track.Name ?? string.Empty);
                    writer.Write(track.CursorFloor);
                    writer.Write(track.RegionStartFloor);
                    writer.Write(track.PlanetATag ?? string.Empty);
                    writer.Write(track.PlanetBTag ?? string.Empty);
                    writer.Write(track.PivotIsA);
                    writer.Write((int)track.WrapMode);
                    writer.Write(track.WrapEveryTiles);
                    writer.Write(track.WrapEveryBeats);
                    writer.Write(track.RepeatCount);
                    writer.Write(track.ReuseRepeatPath);
                    writer.Write(track.WrapTilesText ?? string.Empty);
                    writer.Write(track.WrapBeatsText ?? string.Empty);
                    writer.Write(track.RepeatCountText ?? string.Empty);
                    writer.Write(track.LayoutOffsetX);
                    writer.Write(track.LayoutOffsetY);
                    writer.Write(track.AppliedLayoutOffsetX);
                    writer.Write(track.AppliedLayoutOffsetY);
                    writer.Write(track.LayoutOffsetXText ?? string.Empty);
                    writer.Write(track.LayoutOffsetYText ?? string.Empty);

                    // v2 paging metadata is retained even while the experimental paging
                    // UI is disabled, so opening 0.17 workspaces never discards anything.
                    writer.Write(track.DynamicPagingEnabled);
                    writer.Write(Math.Max(2, track.PageTiles));
                    writer.Write(track.PageTilesText ?? "64");

                    writer.Write(track.Data.Encode() ?? string.Empty);
                }
                writer.Flush();
                stream.Flush(true);
            }
        }

        private static bool FilesEquivalent(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a);
                var fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;

                const int BufferSize = 64 * 1024;
                byte[] ba = new byte[BufferSize];
                byte[] bb = new byte[BufferSize];
                using (var sa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    while (true)
                    {
                        int ra = sa.Read(ba, 0, ba.Length);
                        int rb = sb.Read(bb, 0, bb.Length);
                        if (ra != rb) return false;
                        if (ra == 0) return true;
                        for (int i = 0; i < ra; i++)
                            if (ba[i] != bb[i]) return false;
                    }
                }
            }
            catch
            {
                // Failure to compare is not a reason to skip a real save.
                return false;
            }
        }

        internal static bool TryLoad(scnEditor editor, out List<TrackSlot> tracks, out int nextAutoTagId, out string message)
        {
            tracks = new List<TrackSlot>();
            nextAutoTagId = 1;
            message = string.Empty;

            string chartPath = GetChartPath(editor);
            if (string.IsNullOrWhiteSpace(chartPath)) return false;

            string path = GetWorkspacePath(chartPath);
            Exception firstError = null;

            // Primary first, then newest -> oldest historical backups. A readable zero-track
            // file is treated as suspicious rather than masking older recoverable work.
            for (int candidate = -1; candidate < BackupGenerations; candidate++)
            {
                string candidatePath = candidate < 0 ? path : BackupPath(path, candidate);
                if (!File.Exists(candidatePath)) continue;

                try
                {
                    var loaded = new List<TrackSlot>();
                    int loadedNextTag;
                    int loadedVersion;
                    LoadFile(candidatePath, chartPath, loaded, out loadedNextTag, out loadedVersion);
                    if (loaded.Count == 0)
                    {
                        if (firstError == null)
                            firstError = new InvalidDataException("Autosave contained zero tracks.");
                        continue;
                    }

                    tracks = loaded;
                    nextAutoTagId = loadedNextTag;
                    string source = candidate < 0
                        ? "autosave"
                        : (candidate == 0 ? "backup autosave" : "backup autosave generation " + (candidate + 1));
                    message = "Recovered " + tracks.Count + " MTE track(s) from " + source + "."
                        + (candidate >= 0 ? " A newer workspace copy was unavailable or unusable." : string.Empty)
                        + (loadedVersion < FormatVersion
                            ? " Workspace format migrated from v" + loadedVersion + " to v" + FormatVersion + "."
                            : string.Empty);
                    return true;
                }
                catch (Exception ex)
                {
                    if (firstError == null) firstError = ex;
                }
            }

            if (firstError != null)
                message = "MTE autosave copies exist but none could be read: " + firstError.Message;
            return false;
        }

        private static void RotateBackups(string primaryPath)
        {
            try
            {
                for (int i = BackupGenerations - 1; i >= 1; i--)
                {
                    string source = BackupPath(primaryPath, i - 1);
                    string destination = BackupPath(primaryPath, i);
                    if (File.Exists(source)) File.Copy(source, destination, true);
                }
            }
            catch
            {
                // Backup rotation failure must never prevent the newest good primary
                // from being copied to .bak before an explicit destructive operation.
            }
        }

        private static string BackupPath(string primaryPath, int generation)
        {
            if (generation <= 0) return primaryPath + ".bak";
            return primaryPath + ".bak." + generation;
        }

        private static void LoadFile(
            string path,
            string currentChartPath,
            IList<TrackSlot> output,
            out int nextAutoTagId,
            out int loadedVersion)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (!string.Equals(reader.ReadString(), Magic, StringComparison.Ordinal))
                    throw new InvalidDataException("Unknown MTE workspace header.");
                int version = reader.ReadInt32();
                loadedVersion = version;
                if (version < OldestSupportedFormatVersion || version > FormatVersion)
                    throw new InvalidDataException("Unsupported MTE workspace format " + version + ".");

                string savedPath = reader.ReadString();
                if (!string.Equals(NormalizePath(currentChartPath), savedPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Autosave belongs to a different chart.");

                nextAutoTagId = Math.Max(1, reader.ReadInt32());
                int count = reader.ReadInt32();
                if (count < 0 || count > 1024) throw new InvalidDataException("Invalid MTE track count.");

                for (int i = 0; i < count; i++)
                {
                    string name = reader.ReadString();
                    int cursorFloor = reader.ReadInt32();
                    int regionStartFloor = reader.ReadInt32();
                    string planetA = reader.ReadString();
                    string planetB = reader.ReadString();
                    bool pivot = reader.ReadBoolean();
                    int wrapMode = reader.ReadInt32();
                    int wrapTiles = reader.ReadInt32();
                    double wrapBeats = reader.ReadDouble();
                    int repeatCount = reader.ReadInt32();
                    bool reuse = reader.ReadBoolean();
                    string wrapTilesText = reader.ReadString();
                    string wrapBeatsText = reader.ReadString();
                    string repeatCountText = reader.ReadString();
                    double offsetX = reader.ReadDouble();
                    double offsetY = reader.ReadDouble();
                    double appliedX = reader.ReadDouble();
                    double appliedY = reader.ReadDouble();
                    string offsetXText = reader.ReadString();
                    string offsetYText = reader.ReadString();

                    bool dynamicPaging = false;
                    int pageTiles = 64;
                    string pageTilesText = "64";
                    if (version >= 2)
                    {
                        dynamicPaging = reader.ReadBoolean();
                        pageTiles = Math.Max(2, reader.ReadInt32());
                        pageTilesText = reader.ReadString();
                    }

                    string encoded = reader.ReadString();
                    LevelData data = DecodeLevelData(encoded);
                    var track = new TrackSlot(name, data, cursorFloor)
                    {
                        RegionStartFloor = regionStartFloor,
                        PlanetATag = planetA,
                        PlanetBTag = planetB,
                        PivotIsA = pivot,
                        WrapMode = Enum.IsDefined(typeof(CompactWrapMode), wrapMode) ? (CompactWrapMode)wrapMode : CompactWrapMode.Tiles,
                        WrapEveryTiles = Math.Max(1, wrapTiles),
                        WrapEveryBeats = wrapBeats > 0.0 ? wrapBeats : 16.0,
                        RepeatCount = Math.Max(1, repeatCount),
                        ReuseRepeatPath = reuse,
                        WrapTilesText = string.IsNullOrEmpty(wrapTilesText) ? "32" : wrapTilesText,
                        WrapBeatsText = string.IsNullOrEmpty(wrapBeatsText) ? "16" : wrapBeatsText,
                        RepeatCountText = string.IsNullOrEmpty(repeatCountText) ? "1" : repeatCountText,
                        LayoutOffsetX = offsetX,
                        LayoutOffsetY = offsetY,
                        AppliedLayoutOffsetX = appliedX,
                        AppliedLayoutOffsetY = appliedY,
                        LayoutOffsetXText = string.IsNullOrEmpty(offsetXText) ? "0" : offsetXText,
                        LayoutOffsetYText = string.IsNullOrEmpty(offsetYText) ? "0" : offsetYText,
                        DynamicPagingEnabled = dynamicPaging,
                        PageTiles = pageTiles,
                        PageTilesText = string.IsNullOrEmpty(pageTilesText) ? pageTiles.ToString() : pageTilesText
                    };
                    if (track.DynamicPagingEnabled) track.WrapMode = CompactWrapMode.Off;
                    output.Add(track);
                }
            }
        }

        private static LevelData DecodeLevelData(string encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded))
                throw new InvalidDataException("Stored track contains empty ADOFAI level JSON.");

            Dictionary<string, object> dictionary;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 4096 };
                dictionary = serializer.Deserialize<Dictionary<string, object>>(encoded);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Stored track is not valid ADOFAI level JSON.", ex);
            }

            if (dictionary == null)
                throw new InvalidDataException("Stored track is not valid ADOFAI level JSON.");

            var data = new LevelData();
            data.Setup();
            LoadResult result;
            data.Decode(dictionary, out result);
            return data;
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

        private static string GetWorkspacePath(string chartPath)
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ADOFAIMultiTileEditor",
                "workspaces");
            return Path.Combine(root, HashPath(NormalizePath(chartPath)) + ".mtews");
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
