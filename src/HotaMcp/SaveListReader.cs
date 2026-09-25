using System.Text;

namespace HotaMcp;

public record SaveEntry(int Index,string Name,string ScenarioFile,bool Folder,bool Selected,int X,int Y,int Width,int Height);
public record SaveList(string Kind,List<SaveEntry> Entries,int SelectedIndex,string Coverage);

/// <summary>
/// Reads the own save browser (load/save game) of the running game. The entries belong to the
/// dialog itself; file rows are ordinary dialog text controls, so a row click uses their
/// reported bounds. Nothing is written to the game.
/// </summary>
internal sealed class SaveListReader(WindowsGame game)
{
    private const int EntrySize=0xca4;
    private const int NameOffset=0x33d;
    public SaveList Read(uint dialog,string kind)
    {
        uint first=game.U32(dialog+0x1054),last=game.U32(dialog+0x1058),cap=game.U32(dialog+0x105c);
        if(first<0x10000||first>last||last>cap||(last-first)%EntrySize!=0||(last-first)/EntrySize>512)
            throw new InvalidOperationException("Save list layout unsupported");
        int count=(int)((last-first)/EntrySize);
        int selected=game.I32(dialog+0x374);
        var rows=ReadRows(dialog);
        // The visible rows show the entries from the list's scroll position on, not from the first.
        int top=game.I32(dialog+0x370);
        if(top<0||top>=Math.Max(count,1))throw new InvalidOperationException("Save list scroll position unsupported");
        var entries=new List<SaveEntry>();
        for(int index=0;index<count;index++)
        {
            uint entry=checked(first+(uint)index*EntrySize);
            // The file name runs on past the 13-byte DOS field for long names: «партия3-день49.GM1».
            string name=game.Text(entry+NameOffset,200)??"";
            // A save is .GM1 for a single game and .GM2 for a hotseat or network one.
            bool folder=!System.Text.RegularExpressions.Regex.IsMatch(name,@"\.GM\d$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string label=folder?FolderLabel(entry):Path.GetFileNameWithoutExtension(name);
            var row=index>=top&&index-top<rows.Count?rows[index-top]:(X:0,Y:0,Width:0,Height:0);
            entries.Add(new(index,label,name,folder,index==selected,row.X,row.Y,row.Width,row.Height));
        }
        string coverage=count>rows.Count
            ?$"{count} entries, {rows.Count} visible rows; a file row below the view is reached by load:select, which walks the selection with the arrow keys"
            :"Files and folders are read from the browser's own entry list";
        return new(kind,entries,selected,coverage);
    }
    private string FolderLabel(uint entry)
    {
        // Folder rows carry the save directory marker of the interface; only the plain name is published.
        const string marker="<HD3FMDir>";
        uint window=entry+0x400;
        byte[] bytes=game.Read(window,0x400);
        int found=bytes.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(marker));
        if(found<0)return "папка сохранений";
        int end=Array.IndexOf(bytes,(byte)0,found+marker.Length);
        string value=Encoding.GetEncoding(1251).GetString(bytes,found+marker.Length,(end<0?bytes.Length:end)-found-marker.Length);
        return value.TrimStart('\\','/');
    }
    private List<(int X,int Y,int Width,int Height)> ReadRows(uint dialog)
    {
        uint start=game.U32(dialog+0x34),end=game.U32(dialog+0x38);
        if(end<start||end-start>8192||(end-start)%4!=0)throw new InvalidOperationException("Invalid UI list");
        int dx=game.I32(dialog+0x18),dy=game.I32(dialog+0x1c);
        var rows=new List<(int,int,int,int)>();
        for(uint slot=start;slot<end;slot+=4)
        {
            uint item=game.U32(slot);byte[] b=game.Read(item,0x30);
            if(BitConverter.ToUInt32(b,4)!=dialog)throw new InvalidOperationException("UI changed while reading");
            if(BitConverter.ToUInt32(b,0)!=0x642dc0)continue;
            int width=BitConverter.ToUInt16(b,0x1c),height=BitConverter.ToUInt16(b,0x1e);
            if(width!=314||height!=25)continue;
            rows.Add((dx+BitConverter.ToInt16(b,0x18),dy+BitConverter.ToInt16(b,0x1a),width,height));
        }
        rows.Sort((left,right)=>left.Item2.CompareTo(right.Item2));
        return rows;
    }
}
