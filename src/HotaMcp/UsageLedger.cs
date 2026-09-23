using System.Text;
using System.Text.Json;

namespace HotaMcp;

/// What a game costs the controller: every call to the bridge with the game day it was made on,
/// the size of the answer and how long it took, one file per game. Tokens are the harness's to
/// know, not the bridge's; the report joins the harness's own record to the days of the game.
internal static class UsageLedger
{
    private static readonly object gate=new();

    private static string GameFile(string root)=>Path.Combine(root,"current-game.txt");

    public static string CurrentGame(string root)=>File.Exists(GameFile(root))?File.ReadAllText(GameFile(root)).Trim():"без-партии";

    public static void StartGame(string root,string? scenario)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(GameFile(root),$"{DateTime.Now:yyyyMMdd-HHmmss} {scenario??"партия"}");
    }

    private static string LedgerFile(string root,string game)
    {
        string safe=string.Concat(game.Select(c=>Path.GetInvalidFileNameChars().Contains(c)||c==' '?'_':c));
        return Path.Combine(root,"usage",safe+".jsonl");
    }

    public static void Record(string root,int player,string call,int[] day,long bytes,long milliseconds,string? refused)
    {
        string game=CurrentGame(root);
        string file=LedgerFile(root,game);
        string line=JsonSerializer.Serialize(new{time=DateTimeOffset.Now,player,game,call,day,bytes,ms=milliseconds,refused});
        lock(gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file,line+"\n");
        }
    }

    private sealed record Call(DateTimeOffset Time,string Day,long Bytes,bool Refused);

    /// A table per game day: calls, refusals, bytes the bridge answered with, and — when harness
    /// transcripts are given — the tokens spent between the first and the last call of the game.
    public static string Report(string root,string? game,IReadOnlyList<string> transcripts)
    {
        game??=CurrentGame(root);
        string file=LedgerFile(root,game);
        if(!File.Exists(file))throw new InvalidOperationException($"No usage ledger for game «{game}» ({file})");
        var calls=new List<Call>();
        foreach(string line in File.ReadLines(file))
        {
            using var json=JsonDocument.Parse(line);
            var r=json.RootElement;
            int[] day=r.GetProperty("day").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
            string label=day.Length==3?$"м{day[2]} н{day[1]} д{day[0]}":"меню";
            calls.Add(new(r.GetProperty("time").GetDateTimeOffset(),label,r.GetProperty("bytes").GetInt64(),
                r.TryGetProperty("refused",out var refused)&&refused.ValueKind==JsonValueKind.String));
        }
        if(calls.Count==0)throw new InvalidOperationException($"Usage ledger for «{game}» is empty");
        var days=calls.Select(c=>c.Day).Distinct().ToList();
        var tokens=days.ToDictionary(d=>d,_=>new long[5]);
        DateTimeOffset from=calls[0].Time.AddMinutes(-1),to=calls[^1].Time.AddMinutes(1);
        foreach(string transcript in transcripts)
        {
            // One assistant message is written as several lines, each repeating its usage; the
            // message id keeps it counted once.
            var seen=new HashSet<string>();
            foreach(string line in File.ReadLines(transcript))
            {
                if(!line.Contains("\"usage\"",StringComparison.Ordinal))continue;
                using var json=JsonDocument.Parse(line);
                var r=json.RootElement;
                if(!r.TryGetProperty("timestamp",out var stamp)||!r.TryGetProperty("message",out var message)
                   ||message.ValueKind!=JsonValueKind.Object||!message.TryGetProperty("usage",out var usage))continue;
                var time=stamp.GetDateTimeOffset();
                if(time<from||time>to)continue;
                if(message.TryGetProperty("id",out var id)&&!seen.Add(id.GetString()??""))continue;
                string day=calls.LastOrDefault(c=>c.Time<=time)?.Day??calls[0].Day;
                long Get(string name)=>usage.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Number?v.GetInt64():0;
                var t=tokens[day];
                t[0]+=Get("input_tokens");t[1]+=Get("cache_creation_input_tokens");t[2]+=Get("cache_read_input_tokens");
                t[3]+=Get("output_tokens");t[4]++;
            }
        }
        var text=new StringBuilder();
        text.AppendLine($"Партия: {game}");
        text.AppendLine($"Вызовов моста: {calls.Count}, отказов: {calls.Count(c=>c.Refused)}, ответы моста: {calls.Sum(c=>c.Bytes)/1024} КБ");
        bool withTokens=transcripts.Count>0;
        text.AppendLine(withTokens
            ?"День | вызовы | отказы | КБ ответов | вход | запись кэша | чтение кэша | выход | сообщений"
            :"День | вызовы | отказы | КБ ответов");
        foreach(string day in days)
        {
            var list=calls.Where(c=>c.Day==day).ToList();
            var t=tokens[day];
            text.AppendLine(withTokens
                ?$"{day} | {list.Count} | {list.Count(c=>c.Refused)} | {list.Sum(c=>c.Bytes)/1024} | {t[0]} | {t[1]} | {t[2]} | {t[3]} | {t[4]}"
                :$"{day} | {list.Count} | {list.Count(c=>c.Refused)} | {list.Sum(c=>c.Bytes)/1024}");
        }
        if(withTokens)
        {
            var sum=Enumerable.Range(0,5).Select(i=>tokens.Values.Sum(t=>t[i])).ToArray();
            text.AppendLine($"Итого токенов: вход {sum[0]}, запись кэша {sum[1]}, чтение кэша {sum[2]}, выход {sum[3]}; сообщений {sum[4]}");
        }
        return text.ToString();
    }
}
