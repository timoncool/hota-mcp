using System.IO.Compression;
using System.Text;

namespace HotaMcp;

/// <summary>
/// Reads the game's own LOD archives.
///
/// The reference the agent asks questions against is taken from the installed game rather than
/// copied into this repository: the tables inside HotA_lng.lod already carry the HotA rules for the
/// version the player actually has, so the answers cannot drift from the running game, and nothing
/// of the publisher's content is redistributed.
/// </summary>
internal sealed class LodArchive
{
    private readonly record struct Entry(uint Offset,uint Size,uint Compressed);

    private readonly string path;
    private readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> Names=>entries.Keys;

    private LodArchive(string path){this.path=path;}

    private static FileStream OpenShared(string path)=>
        new(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);

    public static LodArchive? Open(string path)
    {
        if(!File.Exists(path))return null;
        var archive=new LodArchive(path);
        // The running game keeps its archives open, so share the handle instead of failing.
        using var file=OpenShared(path);
        using var reader=new BinaryReader(file);
        if(reader.ReadUInt32()!=0x00444f4c)return null; // "LOD\0"
        reader.ReadUInt32();
        uint count=reader.ReadUInt32();
        if(count is 0 or >65536)return null;
        file.Position=0x5c;
        byte[] table=reader.ReadBytes((int)count*32);
        if(table.Length!=count*32)return null;
        for(int i=0;i<count;i++)
        {
            int at=i*32;
            int end=Array.IndexOf(table,(byte)0,at,16);
            string name=Encoding.Latin1.GetString(table,at,(end<0?at+16:end)-at);
            if(name.Length==0)continue;
            archive.entries[name]=new(BitConverter.ToUInt32(table,at+16),
                BitConverter.ToUInt32(table,at+20),BitConverter.ToUInt32(table,at+28));
        }
        return archive;
    }

    public bool Contains(string name)=>entries.ContainsKey(name);

    /// Returns one archived text file decoded from the game's own code page, or null when absent.
    public string? ReadText(string name)
    {
        if(!entries.TryGetValue(name,out var entry))return null;
        if(entry.Size>16*1024*1024)return null;
        using var file=OpenShared(path);
        file.Position=entry.Offset;
        byte[] raw=new byte[entry.Compressed!=0?entry.Compressed:entry.Size];
        if(file.Read(raw,0,raw.Length)!=raw.Length)return null;
        if(entry.Compressed!=0)
        {
            using var packed=new MemoryStream(raw);
            using var inflate=new ZLibStream(packed,CompressionMode.Decompress);
            using var plain=new MemoryStream();
            inflate.CopyTo(plain,81920);
            raw=plain.ToArray();
        }
        return Encoding.GetEncoding(1251).GetString(raw);
    }
}
