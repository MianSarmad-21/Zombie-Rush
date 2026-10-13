using System.IO.Compression;
using System.Text;

// Minimal binary FBX reader (node tree + properties)
class FbxNode {
  public string Name; public List<object> Props = new(); public List<FbxNode> Kids = new();
  public FbxNode Kid(string n) => Kids.FirstOrDefault(k => k.Name == n);
  public IEnumerable<FbxNode> All(string n) => Kids.Where(k => k.Name == n);
}

static class Fbx {
  public static FbxNode Load(string path) {
    var data = File.ReadAllBytes(path);
    using var br = new BinaryReader(new MemoryStream(data));
    br.BaseStream.Position = 23; uint ver = br.ReadUInt32();
    var root = new FbxNode { Name = "root" };
    while (true) { var n = ReadNode(br, ver); if (n == null) break; root.Kids.Add(n); if (br.BaseStream.Position >= data.Length - 200) break; }
    return root;
  }
  static FbxNode ReadNode(BinaryReader br, uint ver) {
    long end, num, len;
    if (ver >= 7500) { end = (long)br.ReadUInt64(); num = (long)br.ReadUInt64(); len = (long)br.ReadUInt64(); }
    else { end = br.ReadUInt32(); num = br.ReadUInt32(); len = br.ReadUInt32(); }
    int nl = br.ReadByte();
    if (end == 0) return null;
    var n = new FbxNode { Name = Encoding.ASCII.GetString(br.ReadBytes(nl)) };
    for (int i = 0; i < num; i++) n.Props.Add(ReadProp(br));
    while (br.BaseStream.Position < end) { var k = ReadNode(br, ver); if (k == null) break; n.Kids.Add(k); }
    br.BaseStream.Position = end;
    return n;
  }
  static object ReadProp(BinaryReader br) {
    char t = (char)br.ReadByte();
    switch (t) {
      case 'Y': return br.ReadInt16(); case 'C': return br.ReadByte() != 0; case 'I': return br.ReadInt32();
      case 'F': return br.ReadSingle(); case 'D': return br.ReadDouble(); case 'L': return br.ReadInt64();
      case 'S': case 'R': { int l = br.ReadInt32(); var b = br.ReadBytes(l); return t == 'S' ? Encoding.UTF8.GetString(b) : b; }
      case 'f': case 'd': case 'l': case 'i': case 'b': {
        int count = br.ReadInt32(), enc = br.ReadInt32(), clen = br.ReadInt32(); var raw = br.ReadBytes(clen);
        if (enc == 1) { using var ms = new MemoryStream(raw, 2, raw.Length - 2); using var z = new DeflateStream(ms, CompressionMode.Decompress); using var o = new MemoryStream(); z.CopyTo(o); raw = o.ToArray(); }
        switch (t) {
          case 'f': { var a = new float[count]; Buffer.BlockCopy(raw, 0, a, 0, count * 4); return a; }
          case 'd': { var a = new double[count]; Buffer.BlockCopy(raw, 0, a, 0, count * 8); return a; }
          case 'l': { var a = new long[count]; Buffer.BlockCopy(raw, 0, a, 0, count * 8); return a; }
          case 'i': { var a = new int[count]; Buffer.BlockCopy(raw, 0, a, 0, count * 4); return a; }
          default: return raw;
        }
      }
      default: throw new Exception("bad prop " + t);
    }
  }
  public static void Dump(FbxNode n, int d, int maxD, TextWriter w) {
    if (d > maxD) return;
    var ps = string.Join(", ", n.Props.Take(5).Select(p => p is Array a ? $"{p.GetType().Name}[{a.Length}]" : p is string s ? $"\"{(s.Length > 60 ? s[..60] : s)}\"" : p?.ToString()));
    w.WriteLine(new string(' ', d * 2) + n.Name + " (" + ps + ")");
    if (n.Name == "Properties70" && d > 4) return;
    int c = 0; foreach (var k in n.Kids) { if (++c > 40) { w.WriteLine(new string(' ', d * 2 + 2) + "..."); break; } Dump(k, d + 1, maxD, w); }
  }
}
