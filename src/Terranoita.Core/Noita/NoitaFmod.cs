using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Terranoita.Noita
{
    /// <summary>
    /// Noita's own sounds, played through the FMOD Studio runtime that ships with the player's Noita
    /// (fmod.dll, fmodstudio.dll: FMOD 2.1.5, 32-bit) from its data/audio/Desktop banks. Nothing is copied into the mod.
    /// Positions are in Noita pixels, the units Noita's events are designed for. x86 processes only.
    /// </summary>
    public sealed class NoitaFmod : IDisposable
    {
        const uint FmodVersion = 0x00020105;   // must match the dlls (2.1.5)

        [StructLayout(LayoutKind.Sequential)]
        struct Vec { public float X, Y, Z; }

        [StructLayout(LayoutKind.Sequential)]
        struct Attributes3D { public Vec Position, Velocity, Forward, Up; }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibrary(string path);

        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_System_Create(out IntPtr system, uint headerVersion);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_System_Initialize(IntPtr system, int maxChannels, uint studioFlags, uint flags, IntPtr extra);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_System_Release(IntPtr system);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_System_Update(IntPtr system);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_System_LoadBankFile(IntPtr system, byte[] file, uint flags, out IntPtr bank);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_System_GetEvent(IntPtr system, byte[] path, out IntPtr desc);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_System_GetBus(IntPtr system, byte[] path, out IntPtr bus);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_System_SetListenerAttributes(IntPtr system, int listener, ref Attributes3D attributes, IntPtr attenuation);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_Bus_SetVolume(IntPtr bus, float volume);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_Bank_GetEventCount(IntPtr bank, out int count);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_Bank_GetEventList(IntPtr bank, IntPtr[] array, int capacity, out int count);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_EventDescription_GetPath(IntPtr desc, byte[] path, int size, out int retrieved);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_EventDescription_CreateInstance(IntPtr desc, out IntPtr instance);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_EventInstance_Set3DAttributes(IntPtr instance, ref Attributes3D attributes);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_EventInstance_Start(IntPtr instance);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_EventInstance_Release(IntPtr instance);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_EventInstance_Stop(IntPtr instance, int mode);
        [DllImport("fmodstudio.dll")] static extern int FMOD_Studio_EventInstance_SetVolume(IntPtr instance, float volume);

        IntPtr _system;
        readonly Dictionary<string, IntPtr> _events = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        readonly List<IntPtr> _banks = new List<IntPtr>();
        IntPtr _master;

        public static NoitaFmod Open(string noitaDir, IEnumerable<string> banks, Action<string> log)
        {
            if (IntPtr.Size != 4)
                throw new PlatformNotSupportedException("Noita's FMOD is 32-bit");
            // load the player's own copies by full path, so DllImport("fmodstudio.dll") binds to them
            foreach (var dll in new[] { "fmod.dll", "fmodstudio.dll" })
                if (LoadLibrary(Path.Combine(noitaDir, dll)) == IntPtr.Zero)
                    throw new DllNotFoundException(Path.Combine(noitaDir, dll) + " (error " + Marshal.GetLastWin32Error() + ")");
            var f = new NoitaFmod();
            Check(FMOD_Studio_System_Create(out f._system, FmodVersion), "System_Create");
            Check(FMOD_Studio_System_Initialize(f._system, 64, 0, 0, IntPtr.Zero), "System_Initialize");
            string dir = Path.Combine(noitaDir, "data", "audio", "Desktop");
            foreach (var name in new List<string> { "Master Bank.bank", "Master Bank.strings.bank" }.ConcatSafe(banks))
            {
                int r = FMOD_Studio_System_LoadBankFile(f._system, Utf8(Path.Combine(dir, name)), 0, out var bank);
                if (r == 0)
                    f._banks.Add(bank);
                else
                    log?.Invoke("FMOD: bank " + name + " not loaded (error " + r + ")");
            }
            FMOD_Studio_System_GetBus(f._system, Utf8("bus:/"), out f._master);
            return f;
        }

        static void Check(int result, string what)
        {
            if (result != 0)
                throw new InvalidOperationException("FMOD " + what + " failed with error " + result);
        }

        static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

        /// <summary>Every event path in the loaded banks ("event:/animals/zombie/...").</summary>
        public List<string> EventPaths()
        {
            var list = new List<string>();
            var buf = new byte[512];
            foreach (var bank in _banks)
            {
                if (FMOD_Studio_Bank_GetEventCount(bank, out int n) != 0 || n == 0)
                    continue;
                var arr = new IntPtr[n];
                FMOD_Studio_Bank_GetEventList(bank, arr, n, out int got);
                for (int i = 0; i < got; i++)
                    if (FMOD_Studio_EventDescription_GetPath(arr[i], buf, buf.Length, out int len) == 0 && len > 1)
                        list.Add(Encoding.UTF8.GetString(buf, 0, len - 1));
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public bool Has(string path) => Event(path) != IntPtr.Zero;

        IntPtr Event(string path)
        {
            if (string.IsNullOrEmpty(path))
                return IntPtr.Zero;
            if (!path.StartsWith("event:/", StringComparison.Ordinal))
                path = "event:/" + path;
            if (!_events.TryGetValue(path, out var desc))
            {
                if (FMOD_Studio_System_GetEvent(_system, Utf8(path), out desc) != 0)
                    desc = IntPtr.Zero;
                _events[path] = desc;
            }
            return desc;
        }

        static Attributes3D At(float x, float y) => new Attributes3D
        {
            Position = new Vec { X = x, Y = -y, Z = 0 },     // FMOD: y up; games: y down
            Forward = new Vec { Z = 1 },
            Up = new Vec { Y = 1 },
        };

        /// <summary>Fire and forget a Noita event at a position in Noita pixels.</summary>
        public bool Play(string path, float x, float y)
        {
            var desc = Event(path);
            if (desc == IntPtr.Zero || FMOD_Studio_EventDescription_CreateInstance(desc, out var inst) != 0)
                return false;
            var a = At(x, y);
            FMOD_Studio_EventInstance_Set3DAttributes(inst, ref a);
            FMOD_Studio_EventInstance_Start(inst);
            FMOD_Studio_EventInstance_Release(inst);     // freed by FMOD when it finishes
            return true;
        }

        /// <summary>A long event (music) that plays until stopped; IntPtr.Zero if it is not in the banks.</summary>
        public IntPtr Start(string path)
        {
            var desc = Event(path);
            if (desc == IntPtr.Zero || FMOD_Studio_EventDescription_CreateInstance(desc, out var inst) != 0)
                return IntPtr.Zero;
            FMOD_Studio_EventInstance_Start(inst);
            return inst;
        }

        public void SetVolume(IntPtr instance, float volume)
        {
            if (instance != IntPtr.Zero)
                FMOD_Studio_EventInstance_SetVolume(instance, volume);
        }

        /// <summary>Stop with its fade out (FMOD_STUDIO_STOP_ALLOWFADEOUT) and free it.</summary>
        public void Stop(IntPtr instance)
        {
            if (instance == IntPtr.Zero)
                return;
            FMOD_Studio_EventInstance_Stop(instance, 0);
            FMOD_Studio_EventInstance_Release(instance);
        }

        /// <summary>Once per frame: where the listener is (Noita pixels) and the overall volume (0..1).</summary>
        public void Update(float listenerX, float listenerY, float volume)
        {
            var a = At(listenerX, listenerY);
            FMOD_Studio_System_SetListenerAttributes(_system, 0, ref a, IntPtr.Zero);
            if (_master != IntPtr.Zero)
                FMOD_Studio_Bus_SetVolume(_master, volume);
            FMOD_Studio_System_Update(_system);
        }

        public void Dispose()
        {
            if (_system != IntPtr.Zero)
                FMOD_Studio_System_Release(_system);
            _system = IntPtr.Zero;
        }
    }

    static class EnumerableExt
    {
        public static IEnumerable<T> ConcatSafe<T>(this IEnumerable<T> a, IEnumerable<T> b)
        {
            foreach (var x in a) yield return x;
            if (b != null)
                foreach (var x in b) yield return x;
        }
    }
}
