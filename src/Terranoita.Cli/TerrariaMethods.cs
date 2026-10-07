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
                        if (Environment.GetEnvironmentVariable("TN_IL") == "1" && m.RelativeVirtualAddress != 0)
                            DumpIl(md, pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes(), provider);
                    }
                }
            }
            return 0;
        }

        static System.Collections.Generic.Dictionary<short, System.Reflection.Emit.OpCode> _ops;

        /// <summary>The method's IL as opcodes with the members, strings and numbers they use (enough to read what it does).</summary>
        static void DumpIl(MetadataReader md, byte[] il, Names provider)
        {
            if (_ops == null)
            {
                _ops = new System.Collections.Generic.Dictionary<short, System.Reflection.Emit.OpCode>();
                foreach (var f in typeof(System.Reflection.Emit.OpCodes).GetFields())
                    if (f.GetValue(null) is System.Reflection.Emit.OpCode op)
                        _ops[op.Value] = op;
            }
            int i = 0;
            while (i < il.Length)
            {
                int at = i;
                short code = il[i++];
                if (code == 0xFE)
                    code = (short)(0xFE00 | il[i++]);
                if (!_ops.TryGetValue(code, out var op))
                {
                    Console.WriteLine("  ?? " + code);
                    return;
                }
                string arg = "";
                switch (op.OperandType)
                {
                    case System.Reflection.Emit.OperandType.InlineNone: break;
                    case System.Reflection.Emit.OperandType.ShortInlineBrTarget: arg = "-> " + (i + 1 + (sbyte)il[i]); i += 1; break;
                    case System.Reflection.Emit.OperandType.ShortInlineI:
                    case System.Reflection.Emit.OperandType.ShortInlineVar: arg = il[i].ToString(); i += 1; break;
                    case System.Reflection.Emit.OperandType.InlineVar: arg = BitConverter.ToInt16(il, i).ToString(); i += 2; break;
                    case System.Reflection.Emit.OperandType.InlineI: arg = BitConverter.ToInt32(il, i).ToString(); i += 4; break;
                    case System.Reflection.Emit.OperandType.InlineBrTarget: arg = "-> " + (i + 4 + BitConverter.ToInt32(il, i)); i += 4; break;
                    case System.Reflection.Emit.OperandType.ShortInlineR: arg = BitConverter.ToSingle(il, i).ToString(); i += 4; break;
                    case System.Reflection.Emit.OperandType.InlineI8: arg = BitConverter.ToInt64(il, i).ToString(); i += 8; break;
                    case System.Reflection.Emit.OperandType.InlineR: arg = BitConverter.ToDouble(il, i).ToString(); i += 8; break;
                    case System.Reflection.Emit.OperandType.InlineSwitch: int n = BitConverter.ToInt32(il, i); i += 4 + 4 * n; arg = "switch " + n; break;
                    case System.Reflection.Emit.OperandType.InlineString: arg = "\"" + md.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, i) & 0xFFFFFF)) + "\""; i += 4; break;
                    default: arg = Token(md, BitConverter.ToInt32(il, i)); i += 4; break;
                }
                Console.WriteLine("  " + at.ToString("X4") + " " + op.Name + " " + arg);
            }
        }

        static string Token(MetadataReader md, int token)
        {
            try
            {
                var h = MetadataTokens.EntityHandle(token);
                switch (h.Kind)
                {
                    case HandleKind.MethodDefinition:
                        var m = md.GetMethodDefinition((MethodDefinitionHandle)h);
                        return Full(md, md.GetTypeDefinition(m.GetDeclaringType())) + "::" + md.GetString(m.Name);
                    case HandleKind.FieldDefinition:
                        var f = md.GetFieldDefinition((FieldDefinitionHandle)h);
                        return Full(md, md.GetTypeDefinition(f.GetDeclaringType())) + "::" + md.GetString(f.Name);
                    case HandleKind.MemberReference:
                        var r = md.GetMemberReference((MemberReferenceHandle)h);
                        string parent = r.Parent.Kind == HandleKind.TypeReference ? md.GetString(md.GetTypeReference((TypeReferenceHandle)r.Parent).Name) : "?";
                        return parent + "::" + md.GetString(r.Name);
                    case HandleKind.TypeDefinition:
                        return Full(md, md.GetTypeDefinition((TypeDefinitionHandle)h));
                    case HandleKind.TypeReference:
                        return md.GetString(md.GetTypeReference((TypeReferenceHandle)h).Name);
                    case HandleKind.MethodSpecification:
                        var ms = md.GetMethodSpecification((MethodSpecificationHandle)h);
                        return "spec " + Token(md, MetadataTokens.GetToken(ms.Method));
                    default:
                        return h.Kind.ToString();
                }
            }
            catch { return "tok " + token.ToString("X8"); }
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
