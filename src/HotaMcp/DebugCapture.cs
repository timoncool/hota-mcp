using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace HotaMcp;

public sealed record CaptureResult(string Path,int Width,int Height,string Format,string Source);

/// <summary>
/// Explicit developer capture only. Never the desktop: either the game's own drawing surface, or
/// the game window alone, rendered into our own bitmap without bringing it to the front.
///
/// The launcher offers seven renderers (16- and 32-bit DirectDraw, GDI and OpenGL), and they do not
/// agree on the surface: pixel size differs, rows carry padding of their own, and the 16-bit modes
/// differ again between 5-6-5 and 5-5-5. So the format is derived from what the surface reports
/// rather than assumed, and when there is no readable surface at all the window itself is asked to
/// draw a copy of what it is showing.
/// </summary>
internal static class DebugCapture
{
    public static CaptureResult Save(WindowsGame game,int player,string directory)
    {
        uint owner=game.U32(0x69ccfc);
        // Only a screen of a running game shows what one player may hide from another; menus and
        // setup windows before a game belong to nobody.
        uint top=game.U32(game.U32(0x6992d0)+0x54);
        bool inGame=top!=0&&GameReader.NameOf(game.U32(top)) is string name&&GameReader.InGameScreens.Contains(name);
        // An ally's screen is shared anyway; only a rival's turn is private.
        uint teamsAt=game.U32(0x699538)+0x1f86c+0xc;
        byte[] teams=game.Read(teamsAt,9);
        int active=game.I32(0x69ccf4);
        bool ally=teams[0]==1&&active is >=0 and <8&&teams[1+active]<8&&teams[1+active]==teams[1+player];
        if(inGame&&!ally&&owner!=0&&(game.I32(0x69ccf4)!=player||owner!=game.U32(0x699538)+0x20ad0+(uint)player*0x168))
            throw new InvalidOperationException("Capture denied for another player's context");
        try{return FromSurface(game,owner,directory);}
        catch(InvalidOperationException e)when(e.Data.Contains("fallback"))
        {
            return FromWindow(game,directory,(string)e.Data["fallback"]!);
        }
    }

    private static CaptureResult FromSurface(WindowsGame game,uint owner,string directory)
    {
        uint manager=game.U32(0x6992d0),surface=game.U32(manager+0x40);
        if(surface<0x10000)throw Fallback("the game exposes no drawing surface");
        byte[] header=game.Read(surface+0x24,16);
        int width=BitConverter.ToInt32(header,0),height=BitConverter.ToInt32(header,4),stride=BitConverter.ToInt32(header,8);
        uint pixels=BitConverter.ToUInt32(header,12);
        if(width<320||width>8192||height<240||height>8192)throw Fallback($"surface reports {width}x{height}");
        if(pixels<0x10000)throw Fallback("surface holds no pixel buffer");
        // Rows are padded: the 32-bit GDI surface reports 3212 bytes for 800 pixels. The pixel size
        // follows from the row fitting, not from an exact stride.
        int bytesPerPixel=stride>=width*4&&stride<width*4+64?4:stride>=width*2&&stride<width*2+64?2:0;
        if(bytesPerPixel==0||(long)stride*height>64*1024*1024)
            throw Fallback($"surface stride {stride} does not match {width} pixels");

        byte[][] rows=new byte[height][];
        for(int y=0;y<height;y++)rows[y]=game.Read(checked(pixels+(uint)(y*stride)),stride);
        if(game.U32(manager+0x40)!=surface||!game.Read(surface+0x24,16).SequenceEqual(header)||game.U32(0x69ccfc)!=owner)
            throw new InvalidOperationException("Capture surface or player changed; discard frame");

        bool fiveFiveFive=bytesPerPixel==2&&IsFiveFiveFive(rows,width);
        byte[] rgb=new byte[checked((width*3+1)*height)];
        for(int y=0;y<height;y++)
        {
            byte[] row=rows[y];
            int target=y*(width*3+1)+1;
            for(int x=0;x<width;x++)
            {
                int source=x*bytesPerPixel;
                if(bytesPerPixel==4)
                {
                    rgb[target++]=row[source+2];rgb[target++]=row[source+1];rgb[target++]=row[source];
                    continue;
                }
                int value=row[source]|row[source+1]<<8;
                if(fiveFiveFive)
                {
                    rgb[target++]=(byte)(((value>>10)&31)*255/31);
                    rgb[target++]=(byte)(((value>>5)&31)*255/31);
                    rgb[target++]=(byte)((value&31)*255/31);
                }
                else
                {
                    rgb[target++]=(byte)(((value>>11)&31)*255/31);
                    rgb[target++]=(byte)(((value>>5)&63)*255/63);
                    rgb[target++]=(byte)((value&31)*255/31);
                }
            }
        }
        string format=bytesPerPixel==4?"bgra32":fiveFiveFive?"rgb555":"rgb565";
        return Write(directory,width,height,rgb,"game_framebuffer_"+format);
    }

    /// In 5-6-5 the top bit is the high bit of red, so a real frame lights it somewhere. In 5-5-5
    /// that bit is unused and stays clear across the whole surface.
    private static bool IsFiveFiveFive(byte[][] rows,int width)
    {
        for(int y=0;y<rows.Length;y+=7)
            for(int x=0;x<width;x+=5)
                if((rows[y][x*2+1]&0x80)!=0)return false;
        return true;
    }

    /// Asks the window to paint a copy of itself into our bitmap. No activation, no cursor, no
    /// desktop: the source is this one window even when it is behind others.
    private static CaptureResult FromWindow(WindowsGame game,string directory,string why)
    {
        if(!GetClientRect(game.Window,out Rect rect)||IsIconic(game.Window))
            throw new InvalidOperationException($"Cannot capture: {why}, and the game window is unavailable or minimized");
        int width=rect.Right,height=rect.Bottom;
        if(width<320||height<240||(long)width*height>64*1024*1024)
            throw new InvalidOperationException($"Cannot capture: {why}, and the window reports {width}x{height}");
        nint screen=GetDC(0),memory=CreateCompatibleDC(screen);
        var info=new BitmapInfo
        {
            Size=40,Width=width,Height=-height,Planes=1,BitCount=32,Compression=0,
        };
        nint bitmap=CreateDIBSection(memory,ref info,0,out nint bits,0,0);
        try
        {
            if(bitmap==0||bits==0)throw new InvalidOperationException($"Cannot capture: {why}, and no bitmap could be created");
            nint previous=SelectObject(memory,bitmap);
            // PW_RENDERFULLCONTENT: the flag that makes hardware-accelerated windows draw as well.
            bool drawn=PrintWindow(game.Window,memory,2)||PrintWindow(game.Window,memory,0);
            SelectObject(memory,previous);
            if(!drawn)throw new InvalidOperationException($"Cannot capture: {why}, and the window refused to render a copy");
            byte[] raw=new byte[checked(width*4*height)];
            Marshal.Copy(bits,raw,0,raw.Length);
            byte[] rgb=new byte[checked((width*3+1)*height)];
            for(int y=0;y<height;y++)
            {
                int source=y*width*4,target=y*(width*3+1)+1;
                for(int x=0;x<width;x++,source+=4)
                {
                    rgb[target++]=raw[source+2];rgb[target++]=raw[source+1];rgb[target++]=raw[source];
                }
            }
            return Write(directory,width,height,rgb,"game_window_printwindow");
        }
        finally
        {
            if(bitmap!=0)DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0,screen);
        }
    }

    private static InvalidOperationException Fallback(string why)
    {
        var error=new InvalidOperationException(why);
        error.Data["fallback"]=why;
        return error;
    }

    private static CaptureResult Write(string directory,int width,int height,byte[] rgb,string source)
    {
        Directory.CreateDirectory(directory);
        string path=System.IO.Path.Combine(directory,$"frame-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
        using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        file.Write(new byte[]{137,80,78,71,13,10,26,10});
        byte[] ihdr=new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr,width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4),height);
        ihdr[8]=8;ihdr[9]=2;
        Chunk(file,"IHDR",ihdr);
        using var compressed=new MemoryStream();
        using(var zlib=new ZLibStream(compressed,CompressionLevel.Fastest,true))zlib.Write(rgb);
        Chunk(file,"IDAT",compressed.ToArray());
        Chunk(file,"IEND",[]);
        return new(path,width,height,"image/png",source);
    }

    private static void Chunk(Stream output,string name,byte[] data)
    {
        byte[] type=Encoding.ASCII.GetBytes(name);Span<byte> number=stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number,data.Length);output.Write(number);output.Write(type);output.Write(data);
        uint crc=0xffffffff;
        foreach(byte b in type.Concat(data)){crc^=b;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}
        BinaryPrimitives.WriteUInt32BigEndian(number,~crc);output.Write(number);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size,Width,Height;
        public short Planes,BitCount;
        public int Compression,SizeImage,XPelsPerMeter,YPelsPerMeter,ClrUsed,ClrImportant;
    }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window,out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window,nint dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window,nint dc,uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc,nint handle);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint bits,nint section,uint offset);
}
