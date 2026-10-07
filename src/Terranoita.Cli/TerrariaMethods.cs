using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Terranoita.Cli
{
    /// <summary>
    /// tncli tr-methods &lt;Terraria.exe&gt; &lt;Full.Type.Name&gt; [regex]: method signatures of a Terraria type, read from the
    /// file's metadata (no loading), for writing Harmony patches against the player's Terraria version.
    /// </summary>
    static class TerrariaMethods
    {
        public static int Run(string exe, string typeName, string filter)
        {
            using (var fs = File.OpenRead(exe))
            using (var pe = new PEReader(fs))
            {
                var md = pe.GetMetadataReader();
                var rx = new Regex(filter ?? ".", RegexOptions.IgnoreCase);
                var provider = new Names(md);
                foreach (var th in md.TypeDefinitions)
                {
                    var t = md.GetTypeDefinition(th);
                    string full = Full(md, t);
                    if (full != typeName)
                        continue;
                    foreach (var fh in t.GetFields())
                    {
                        var f = md.GetFieldDefinition(fh);
                        string name = md.GetString(f.Name);
                        if (rx.IsMatch(name))
                            Console.WriteLine("field " + ((f.Attributes & System.Reflection.FieldAttributes.Static) != 0 ? "static " : "") +
                                              f.DecodeSignature(provider, null) + " " + name);
                    }
                    foreach (var mh in t.GetMethods())
                    {
                        var m = md.GetMethodDefinition(mh);
                        string name = md.GetString(m.Name);
                        if (!rx.IsMatch(name))
                            continue;
                        var sig = m.DecodeSignature(provider, null);
                        var pnames = m.GetParameters().Select(p => md.GetParameter(p)).Where(p => p.SequenceNumber > 0)
                                      .OrderBy(p => p.SequenceNumber).Select(p => md.GetString(p.Name)).ToArray();
                        var ps = sig.ParameterTypes.Select((pt, i) => pt + " " + (i < pnames.Length ? pnames[i] : "p" + i));
                        bool isStatic = (m.Attributes & System.Reflection.MethodAttributes.Static) != 0;
                        Console.WriteLine((isStatic ? "static " : "") + sig.ReturnType + " " + name + "(" + string.Join(", ", ps) + ")");
                    }
                }
            }
            return 0;
        }

        static string Full(MetadataReader md, TypeDefinition t)
        {
            string name = md.GetString(t.Name);
            if (t.GetDeclaringType().IsNil)
            {
                string ns = md.GetString(t.Namespace);
                return ns.Length > 0 ? ns + "." + name : name;
            }
            return Full(md, md.GetTypeDefinition(t.GetDeclaringType())) + "+" + name;
        }

        sealed class Names : ISignatureTypeProvider<string, object>
        {
            readonly MetadataReader _md;
            public Names(MetadataReader md) { _md = md; }
            public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
            public string GetByReferenceType(string e) => "ref " + e;
            public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
            public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
            public string GetGenericMethodParameter(object c, int i) => "!!" + i;
            public string GetGenericTypeParameter(object c, int i) => "!" + i;
            public string GetModifiedType(string m, string u, bool r) => u;
            public string GetPinnedType(string e) => e;
            public string GetPointerType(string e) => e + "*";
            public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString().ToLowerInvariant();
            public string GetSZArrayType(string e) => e + "[]";
            public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => r.GetString(r.GetTypeDefinition(h).Name);
            public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => r.GetString(r.GetTypeReference(h).Name);
            public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => "spec";
        }
    }
}
