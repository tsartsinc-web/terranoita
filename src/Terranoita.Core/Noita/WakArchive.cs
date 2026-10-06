using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Terranoita.Noita
{
    /// <summary>
    /// Read-only access to Noita's data/data.wak, read from the player's own Noita install.
    /// Layout (community-documented, to be confirmed on a real file):
    ///   u32 0, u32 fileCount, u32 tocEnd, u32 0,
    ///   then fileCount entries of { u32 offset, u32 size, u32 nameLength, name bytes (UTF-8, '/' separators) }.
    /// Offsets are absolute from the start of the file.
    /// </summary>
    public sealed class WakArchive : IDisposable
    {
        public struct Entry
        {
            public string Path;
            public uint Offset;
            public uint Size;
        }

        readonly Stream _stream;
        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<Entry> Entries => _entries.Values;
        public int Count => _entries.Count;

        public WakArchive(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            ReadToc();
        }

        public static WakArchive Open(string path) =>
            new WakArchive(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));

        void ReadToc()
        {
            long length = _stream.Length;
            var r = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
            _stream.Position = 0;
            r.ReadUInt32();
            uint count = r.ReadUInt32();
            uint tocEnd = r.ReadUInt32();
            r.ReadUInt32();
            if (tocEnd > length || count > 1000000)
                throw new InvalidDataException($"Not a Noita .wak archive (count {count}, toc end {tocEnd}, length {length}).");
            for (uint i = 0; i < count; i++)
            {
                uint offset = r.ReadUInt32();
                uint size = r.ReadUInt32();
                uint nameLength = r.ReadUInt32();
                if (nameLength == 0 || nameLength > 4096 || _stream.Position + nameLength > tocEnd)
                    throw new InvalidDataException($"Bad name length {nameLength} at entry {i}.");
                string name = Encoding.UTF8.GetString(r.ReadBytes((int)nameLength)).Replace('\\', '/');
                if ((long)offset + size > length)
                    throw new InvalidDataException($"Entry '{name}' points past the end of the archive.");
                _entries[name] = new Entry { Path = name, Offset = offset, Size = size };
            }
        }

        public bool Contains(string path) => _entries.ContainsKey(Normalize(path));

        public bool TryRead(string path, out byte[] data)
        {
            data = null;
            if (!_entries.TryGetValue(Normalize(path), out var e))
                return false;
            data = new byte[e.Size];
            lock (_stream)
            {
                _stream.Position = e.Offset;
                int read = 0;
                while (read < data.Length)
                {
                    int n = _stream.Read(data, read, data.Length - read);
                    if (n <= 0)
                        throw new EndOfStreamException(e.Path);
                    read += n;
                }
            }
            return true;
        }

        public byte[] Read(string path) =>
            TryRead(path, out var data) ? data : throw new FileNotFoundException("Not in data.wak: " + path, path);

        public string ReadText(string path) => DecodeText(Read(path));

        public bool TryReadText(string path, out string text)
        {
            text = TryRead(path, out var data) ? DecodeText(data) : null;
            return text != null;
        }

        static string DecodeText(byte[] data)
        {
            int start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(data, start, data.Length - start);
        }

        static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

        public void Dispose() => _stream.Dispose();
    }

    /// <summary>
    /// Noita's files: the data.wak archive plus loose files that ship next to it (data/ folder in the game).
    /// Loose files win, matching how the game resolves them.
    /// </summary>
    public sealed class NoitaFiles : IDisposable
    {
        readonly string _gameDir;
        readonly WakArchive _wak;

        public WakArchive Archive => _wak;

        public NoitaFiles(string gameDir)
        {
            _gameDir = gameDir;
            string wak = System.IO.Path.Combine(gameDir, "data", "data.wak");
            if (!File.Exists(wak))
                throw new FileNotFoundException("Noita's data.wak not found. Is this the Noita folder? " + gameDir, wak);
            _wak = WakArchive.Open(wak);
        }

        public bool TryRead(string path, out byte[] data)
        {
            string loose = System.IO.Path.Combine(_gameDir, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (File.Exists(loose))
            {
                data = File.ReadAllBytes(loose);
                return true;
            }
            return _wak.TryRead(path, out data);
        }

        public bool TryReadText(string path, out string text)
        {
            text = null;
            if (!TryRead(path, out var data))
                return false;
            int start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            text = System.Text.Encoding.UTF8.GetString(data, start, data.Length - start);
            return true;
        }

        public void Dispose() => _wak.Dispose();
    }
}
