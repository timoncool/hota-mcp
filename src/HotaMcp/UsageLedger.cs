using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HotaMcp;

/// What a game costs, kept with the game itself: every call to the bridge with the game day it was
/// made on, and every model request of the sessions that play it with the cost its client
/// reported, stamped with the same day. One file per game.
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

    private static void Append(string root,string game,object record)
    {
        string file=LedgerFile(root,game);
        string line=JsonSerializer.Serialize(record);
        lock(gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file,line+"\n");
        }
    }

    public static void Record(string root,int player,string call,int[] day,long bytes,long milliseconds,string? refused)
    {
        string game=CurrentGame(root);
        Append(root,game,new{time=DateTimeOffset.Now,player,game,call,day,bytes,ms=milliseconds,refused});
    }

    private static string Text(JsonElement value)=>value.ValueKind==JsonValueKind.Object
        ?value.EnumerateObject().Select(p=>p.Value.ValueKind==JsonValueKind.String?p.Value.GetString()??"":p.Value.GetRawText()).FirstOrDefault()??""
        :"";

    private static Dictionary<string,string> Attributes(JsonElement owner)
    {
        var result=new Dictionary<string,string>();
        // An attribute with no key or no value names nothing; the fields that matter are checked
        // by name where they are used.
        if(owner.ValueKind==JsonValueKind.Object&&owner.TryGetProperty("attributes",out var list)&&list.ValueKind==JsonValueKind.Array)
            foreach(var a in list.EnumerateArray())
                if(a.ValueKind==JsonValueKind.Object&&a.TryGetProperty("key",out var key)&&key.ValueKind==JsonValueKind.String
                   &&a.TryGetProperty("value",out var value))
                    result[key.GetString()!]=Text(value);
        return result;
    }

    /// A session plays this game once it calls the bridge: an MCP tool of a `hota` server, or a
    /// command addressed to the bridge — its HTTP routes, its port, or the Python client
    /// (`bridge.py`, `import bridge`).
    private static bool CallsBridge(Dictionary<string,string> a)
    {
        static bool Addressed(string text)=>text.Contains("/bridge/",StringComparison.Ordinal)||text.Contains("bridge.py",StringComparison.Ordinal)
            ||text.Contains("import bridge",StringComparison.Ordinal)||text.Contains(":18773",StringComparison.Ordinal);
        if(a.TryGetValue("tool_input",out var input)&&Addressed(input))return true;
        if(!a.TryGetValue("tool_parameters",out var parameters)||parameters.Length==0)return false;
        // Claude Code cuts long values short, and a cut parameter string is no longer JSON; its
        // text still names the server or the command.
        JsonDocument json;
        try{json=JsonDocument.Parse(parameters);}
        catch(JsonException){return Addressed(parameters)||parameters.Contains("\"mcp_server_name\":\"hota",StringComparison.OrdinalIgnoreCase);}
        using(json)
        {
            var r=json.RootElement;
            if(r.ValueKind!=JsonValueKind.Object)return false;
            if(r.TryGetProperty("mcp_server_name",out var server)&&server.ValueKind==JsonValueKind.String
               &&server.GetString()!.StartsWith("hota",StringComparison.OrdinalIgnoreCase))return true;
            return r.TryGetProperty("bash_command",out var command)&&command.ValueKind==JsonValueKind.String&&Addressed(command.GetString()!);
        }
    }

    /// OTLP JSON leaves out empty lists, so a missing one holds nothing.
    private static IEnumerable<JsonElement> Items(JsonElement owner,string name)=>
        owner.ValueKind==JsonValueKind.Object&&owner.TryGetProperty(name,out var list)&&list.ValueKind==JsonValueKind.Array?list.EnumerateArray():[];

    // Requests already filed, by session and the client's own event counter: an export the client
    // resends after a lost answer is not counted twice. Resends come within minutes, so the most
    // recent ids are enough.
    private static readonly HashSet<string> filedEvents=[];
    private static readonly Queue<string> filedOrder=new();

    private static void Filed(string id)
    {
        filedEvents.Add(id);filedOrder.Enqueue(id);
        while(filedOrder.Count>5000)filedEvents.Remove(filedOrder.Dequeue());
    }

    /// When the request was made: Claude Code's own event.timestamp, else the record's OTLP time.
    private static DateTimeOffset RequestTime(Dictionary<string,string> a,JsonElement log)
    {
        if(a.TryGetValue("event.timestamp",out var stamp)&&DateTimeOffset.TryParse(stamp,CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed))
            return parsed;
        foreach(string field in new[]{"timeUnixNano","observedTimeUnixNano"})
            if(log.TryGetProperty(field,out var nano)&&long.TryParse(nano.ValueKind==JsonValueKind.String?nano.GetString():nano.GetRawText(),out long ns)&&ns>0)
                return DateTimeOffset.FromUnixTimeMilliseconds(ns/1_000_000);
        throw new InvalidOperationException("api_request carries neither event.timestamp nor an OTLP time");
    }

    private static long Number(Dictionary<string,string> a,string key)=>
        a.TryGetValue(key,out var v)&&long.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out long n)?n:0;

    /// An OTLP/HTTP JSON log export from Claude Code's telemetry. `tool_result` events mark the
    /// sessions that call the bridge; `api_request` events of those sessions are filed under the
    /// current game with their `cost_usd` and token counts, on the side and day `at` names for the
    /// moment of the request. The whole export is checked before anything is written, so a bad
    /// record fails it without leaving half of it filed. Returns the requests filed.
    public static int Ingest(string root,Func<DateTimeOffset,(int Player,int[] Day)> at,JsonElement export)
    {
        string sessionsFile=Path.Combine(root,"usage","otel-sessions.json");
        lock(gate)
        {
            var sessions=File.Exists(sessionsFile)
                ?JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(sessionsFile))??throw new InvalidOperationException($"{sessionsFile} is empty")
                :new HashSet<string>();
            int known=sessions.Count;
            string game=CurrentGame(root);
            var rows=new List<(string Event,object Row)>();
            foreach(var resource in Items(export,"resourceLogs"))
            {
                var shared=Attributes(resource.TryGetProperty("resource",out var r)?r:default);
                foreach(var scope in Items(resource,"scopeLogs"))
                foreach(var log in Items(scope,"logRecords"))
                {
                    var a=Attributes(log);
                    foreach(var (k,v) in shared)a.TryAdd(k,v);
                    if(!a.TryGetValue("session.id",out var session)||!a.TryGetValue("event.name",out var name))continue;
                    if(name=="tool_result"&&CallsBridge(a)){sessions.Add(session);continue;}
                    if(name!="api_request"||!sessions.Contains(session))continue;
                    if(!a.TryGetValue("cost_usd",out var costText)||!decimal.TryParse(costText,NumberStyles.Float,CultureInfo.InvariantCulture,out decimal cost))
                        throw new InvalidOperationException($"api_request without a numeric cost_usd: «{costText}»");
                    var time=RequestTime(a,log);
                    string id=$"{session}#"+(a.GetValueOrDefault("request_id")??a.GetValueOrDefault("event.sequence")
                        ??$"{time:O}/{costText}/{a.GetValueOrDefault("input_tokens")}/{a.GetValueOrDefault("output_tokens")}");
                    var (player,day)=at(time);
                    rows.Add((id,new{time,player,game,kind="cost",day,session,model=a.GetValueOrDefault("model",""),
                        cost,input=Number(a,"input_tokens"),cacheWrite=Number(a,"cache_creation_tokens"),cacheRead=Number(a,"cache_read_tokens"),
                        output=Number(a,"output_tokens")}));
                }
            }
            int filed=0;
            foreach(var (id,row) in rows)
            {
                if(filedEvents.Contains(id))continue;
                Append(root,game,row);
                Filed(id);
                filed++;
            }
            if(sessions.Count!=known)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sessionsFile)!);
                File.WriteAllText(sessionsFile,JsonSerializer.Serialize(sessions));
            }
            return filed;
        }
    }

    private sealed record Call(DateTimeOffset Time,int Player,string Day,long Bytes,bool Refused);
    private sealed record Request(DateTimeOffset Time,int Player,string Day,string Model,decimal Cost,long Input,long CacheWrite,long CacheRead,long Output);

    private static string Money(decimal value)=>value.ToString("0.00",CultureInfo.InvariantCulture);

    private static string Label(JsonElement day)
    {
        int[] d=day.EnumerateArray().Select(v=>v.GetInt32()).ToArray();
        return d.Length==3?$"м{d[2]} н{d[1]} д{d[0]}":"меню";
    }

    /// Per player and game day: bridge calls, refusals, bytes answered, model requests, tokens and
    /// dollars; the total cost of the game at the top.
    public static string Report(string root,string? game)
    {
        game??=CurrentGame(root);
        string file=LedgerFile(root,game);
        if(!File.Exists(file))throw new InvalidOperationException($"No usage ledger for game «{game}» ({file})");
        var calls=new List<Call>();
        var requests=new List<Request>();
        foreach(string line in File.ReadLines(file))
        {
            using var json=JsonDocument.Parse(line);
            var r=json.RootElement;
            var time=r.GetProperty("time").GetDateTimeOffset();
            int player=r.GetProperty("player").GetInt32();
            string day=Label(r.GetProperty("day"));
            if(r.TryGetProperty("kind",out var kind)&&kind.GetString()=="cost")
            {
                requests.Add(new(time,player,day,r.GetProperty("model").GetString()??"",r.GetProperty("cost").GetDecimal(),
                    r.GetProperty("input").GetInt64(),r.GetProperty("cacheWrite").GetInt64(),r.GetProperty("cacheRead").GetInt64(),r.GetProperty("output").GetInt64()));
                continue;
            }
            calls.Add(new(time,player,day,r.GetProperty("bytes").GetInt64(),r.TryGetProperty("refused",out var refused)&&refused.ValueKind==JsonValueKind.String));
        }
        if(calls.Count==0&&requests.Count==0)throw new InvalidOperationException($"Usage ledger for «{game}» is empty");
        var text=new StringBuilder();
        text.AppendLine($"Партия: {game}");
        text.AppendLine(requests.Count>0
            ?$"Стоимость: ${Money(requests.Sum(q=>q.Cost))} ({requests.Count} запросов к модели: {string.Join(", ",requests.Select(q=>q.Model).Distinct())})"
            :"Стоимость неизвестна: запросов к модели в журнале нет (телеметрия Claude Code не включена или сессия не вызывала мост).");
        foreach(int player in calls.Select(c=>c.Player).Concat(requests.Select(q=>q.Player)).Distinct().Order())
        {
            var mine=calls.Where(c=>c.Player==player).ToList();
            var spent=requests.Where(q=>q.Player==player).ToList();
            text.AppendLine();
            text.AppendLine($"Игрок {player}: ${Money(spent.Sum(q=>q.Cost))}; вызовов моста {mine.Count}, отказов {mine.Count(c=>c.Refused)}, ответы моста {mine.Sum(c=>c.Bytes)/1024} КБ");
            text.AppendLine("День | $ | запросов | вход | запись кэша | чтение кэша | выход | вызовы | отказы | КБ ответов");
            var days=mine.Select(c=>(c.Time,c.Day)).Concat(spent.Select(q=>(q.Time,q.Day))).OrderBy(x=>x.Time).Select(x=>x.Day).Distinct();
            foreach(string day in days)
            {
                var c=mine.Where(x=>x.Day==day).ToList();
                var q=spent.Where(x=>x.Day==day).ToList();
                text.AppendLine($"{day} | {Money(q.Sum(x=>x.Cost))} | {q.Count} | {q.Sum(x=>x.Input)} | {q.Sum(x=>x.CacheWrite)} | {q.Sum(x=>x.CacheRead)} | {q.Sum(x=>x.Output)} | "
                    +$"{c.Count} | {c.Count(x=>x.Refused)} | {c.Sum(x=>x.Bytes)/1024}");
            }
        }
        return text.ToString();
    }
}
