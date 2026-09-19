using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace HotaMcp;

public sealed record CaptureResult(string Path,int Width,int Height,string Format,string Source);

// Explicit developer capture only. Reads the game's drawing surface, never the desktop.
internal static class DebugCapture
{
    public static CaptureResult Save(WindowsGame game,int player,string directory)
    {
        uint owner=game.U32(0x69ccfc);
        if(owner!=0&&(game.I32(0x69ccf4)!=player||owner!=game.U32(0x699538)+0x20ad0+(uint)player*0x168))
            throw new InvalidOperationException("Capture denied for another player's context");
        uint manager=game.U32(0x6992d0),surface=game.U32(manager+0x40);
        byte[] header=game.Read(surface+0x24,16);
        int width=BitConverter.ToInt32(header,0),height=BitConverter.ToInt32(header,4),stride=BitConverter.ToInt32(header,8);
        uint pixels=BitConverter.ToUInt32(header,12);
        if(width<640||width>8192||height<480||height>8192)throw new InvalidOperationException("Unsupported capture dimensions");
        int bytesPerPixel=stride==width*4?4:stride==((width*2+3)&~3)?2:0;
        if(bytesPerPixel==0||(long)stride*height>64*1024*1024)throw new InvalidOperationException("Unsupported drawing surface format");
        byte[] rgb=new byte[checked((width*3+1)*height)];
        for(int y=0;y<height;y++)
        {
            byte[] row=game.Read(checked(pixels+(uint)(y*stride)),stride);
            int target=y*(width*3+1)+1;
            for(int x=0;x<width;x++)
            {
                int source=x*bytesPerPixel;
                if(bytesPerPixel==4){rgb[target++]=row[source+2];rgb[target++]=row[source+1];rgb[target++]=row[source];}
                else
                {
                    int value=row[source]|row[source+1]<<8;
                    rgb[target++]=(byte)(((value>>11)&31)*255/31);
                    rgb[target++]=(byte)(((value>>5)&63)*255/63);
                    rgb[target++]=(byte)((value&31)*255/31);
                }
            }
        }
        if(game.U32(manager+0x40)!=surface||!game.Read(surface+0x24,16).SequenceEqual(header)||game.U32(0x69ccfc)!=owner)
            throw new InvalidOperationException("Capture surface or player changed; discard frame");
        Directory.CreateDirectory(directory);
        string path=System.IO.Path.Combine(directory,$"frame-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
        using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        file.Write(new byte[]{137,80,78,71,13,10,26,10});
        byte[] ihdr=new byte[13];BinaryPrimitives.WriteInt32BigEndian(ihdr,width);BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4),height);ihdr[8]=8;ihdr[9]=2;
        Chunk(file,"IHDR",ihdr);
        using var compressed=new MemoryStream();
        using(var zlib=new ZLibStream(compressed,CompressionLevel.Fastest,true))zlib.Write(rgb);
        Chunk(file,"IDAT",compressed.ToArray());Chunk(file,"IEND",[]);
        return new(path,width,height,"image/png",bytesPerPixel==4?"game_framebuffer_bgra32":"game_framebuffer_rgb565");
    }
    private static void Chunk(Stream output,string name,byte[] data)
    {
        byte[] type=Encoding.ASCII.GetBytes(name);Span<byte> number=stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number,data.Length);output.Write(number);output.Write(type);output.Write(data);
        uint crc=0xffffffff;
        foreach(byte b in type.Concat(data)){crc^=b;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}
        BinaryPrimitives.WriteUInt32BigEndian(number,~crc);output.Write(number);
    }
}
