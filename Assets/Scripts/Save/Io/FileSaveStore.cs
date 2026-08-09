using System;
using System.IO;

namespace Pho.Save.Io
{
    /// <summary>
    /// Real ISaveStore backed by the filesystem. Constructed with a plain
    /// rootPath string rather than Application.persistentDataPath directly
    /// -- Pho.Save cannot reference UnityEngine, so Pho.Core is responsible
    /// for constructing this with Application.persistentDataPath and handing
    /// it to the save wiring.
    ///
    /// Write() sequence (architecture.md section 4 "IO safety", literal):
    /// write to a temp file -> flush to disk -> rotate any existing primary
    /// to .bak -> atomically move the temp file into place. This means a
    /// crash mid-write never corrupts the previous primary or backup: the
    /// temp file is the only thing that can be left half-written, and it is
    /// never read as a save.
    /// </summary>
    public sealed class FileSaveStore : ISaveStore
    {
        readonly string _rootPath;

        public FileSaveStore(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath))
            {
                throw new ArgumentException("rootPath must be non-empty.", nameof(rootPath));
            }

            _rootPath = rootPath;
        }

        string PrimaryPath(string slot) => Path.Combine(_rootPath, slot + ".sav");
        string BackupPath(string slot) => Path.Combine(_rootPath, slot + ".sav.bak");
        string TempPath(string slot) => Path.Combine(_rootPath, slot + ".sav.tmp");

        public bool TryRead(string slot, out string json) => TryReadFile(PrimaryPath(slot), out json);

        public bool TryReadBackup(string slot, out string json) => TryReadFile(BackupPath(slot), out json);

        static bool TryReadFile(string path, out string json)
        {
            if (!File.Exists(path))
            {
                json = null;
                return false;
            }

            try
            {
                json = File.ReadAllText(path);
                return true;
            }
            catch (IOException)
            {
                json = null;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                json = null;
                return false;
            }
        }

        public void Write(string slot, string json)
        {
            Directory.CreateDirectory(_rootPath);

            var tempPath = TempPath(slot);
            var primaryPath = PrimaryPath(slot);
            var backupPath = BackupPath(slot);

            // 1. Write to a temp file and flush it to disk.
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true); // force OS buffers to disk, not just the StreamWriter buffer -- must run before the stream is disposed
            }

            // 2. Rotate any existing primary into the backup slot.
            if (File.Exists(primaryPath))
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Move(primaryPath, backupPath);
            }

            // 3. Atomically move the temp file into place as the new primary.
            if (File.Exists(primaryPath))
            {
                File.Delete(primaryPath);
            }

            File.Move(tempPath, primaryPath);
        }
    }
}
