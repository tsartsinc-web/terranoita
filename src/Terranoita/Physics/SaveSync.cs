using System;
using System.IO;

namespace Terranoita.Game.Physics
{
    /// <summary>
    /// Terraria autosaves the world on a ThreadPool thread (WorldGen.saveAndPlay -> WorldFile.SaveWorld) while the game
    /// keeps updating. What we save (liquids, placed tiles, toxic ground) changes under this lock on the main thread;
    /// the save takes a snapshot under it and writes the file outside it.
    /// </summary>
    static class SaveSync
    {
        public static readonly object Gate = new object();

        /// <summary>Writes path + ".tmp" and swaps it in: a crash or an error mid-save never leaves a cut file.</summary>
        public static void WriteAtomic(string path, Action<BinaryWriter> write)
        {
            string tmp = path + ".tmp";
            using (var w = new BinaryWriter(File.Create(tmp)))
                write(w);
            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }
            try { File.Replace(tmp, path, null); }
            catch (IOException)
            {
                // some file systems refuse Replace: delete and move (the old file is gone only after the new one is complete)
                File.Delete(path);
                File.Move(tmp, path);
            }
        }

        /// <summary>A file we could not read is kept aside as .bad (never overwritten by a later save, never read again).</summary>
        public static void SetAside(string path)
        {
            try
            {
                string bad = path + ".bad";
                if (File.Exists(bad))
                    File.Delete(bad);
                File.Move(path, bad);
                Entry.Log("kept the unreadable file as " + Path.GetFileName(bad));
            }
            catch (Exception ex) { Entry.Error("set aside " + Path.GetFileName(path), ex); }
        }
    }
}
