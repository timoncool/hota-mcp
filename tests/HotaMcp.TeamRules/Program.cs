using HotaMcp;
using System.Text.Json;

static void Check(bool expected,byte[] header,int player,int colour,string scenario)
{
    bool actual=TeamRelations.AreAllies(header,player,colour);
    if(actual!=expected)throw new Exception($"{scenario}: expected {expected}, got {actual}");
}

byte[] teams=[2,0,1,1,0,255,255,255,255];
Check(true,teams,1,2,"blue and brown on the same team");
Check(false,teams,1,0,"blue and red on opposing teams");
Check(false,teams,1,3,"blue and green on opposing teams");
Check(false,teams,1,1,"a player is not their own ally");
Check(false,teams,1,4,"an absent colour is not an ally");
Check(false,teams,4,1,"an absent player has no allies");
Check(false,[0,0,0,0,0,255,255,255,255],1,2,"without teams, other colours are opponents");
Check(false,[9,0,0,0,0,255,255,255,255],1,2,"invalid team count is rejected");
Check(false,[2,0],1,0,"truncated header is rejected");
Check(false,teams,-1,0,"invalid player is rejected");
Check(false,teams,1,8,"invalid colour is rejected");
Console.WriteLine("PASS team privacy: allies are logged, enemies and absent colours are excluded");

var waiting=CompetitiveObservation.Waiting(1);
using var json=JsonDocument.Parse(JsonSerializer.Serialize(waiting));
var root=json.RootElement;
if(root.GetProperty("Screen").GetString()!="waiting"||root.GetProperty("Player").GetInt32()!=1
   ||root.GetProperty("Date").GetArrayLength()!=0||root.GetProperty("Resources").GetArrayLength()!=0
   ||root.GetProperty("Hero").ValueKind!=JsonValueKind.Null||root.GetProperty("Towns").GetArrayLength()!=0
   ||root.GetProperty("Heroes").GetArrayLength()!=0||root.GetProperty("ForeignHeroes").GetArrayLength()!=0
   ||root.GetProperty("ForeignArmy").GetArrayLength()!=0||root.GetProperty("ForeignHero").ValueKind!=JsonValueKind.Null
   ||root.GetProperty("Actions").GetArrayLength()!=0||root.GetProperty("Elements").GetArrayLength()!=0
   ||root.GetProperty("Side").ValueKind!=JsonValueKind.Null||root.GetProperty("Width").GetInt32()!=0
   ||root.GetProperty("Height").GetInt32()!=0||root.GetProperty("Revision").GetString()!="")
    throw new Exception("Competitive waiting observation exposed game state");
Console.WriteLine("PASS competitive waiting observation contains no other-side state");

var handedOff=CompetitiveObservation.AfterHandoff(new OperationResult("completed","enemy hero moved",
    new Observation("secret",1,[2,1,1],[100],null,"enemy_hero_card",800,600,[])
    {ForeignHero="opponent",ForeignArmy=["army"]}),1);
if(handedOff.Status!="completed"||handedOff.Message.Contains("enemy",StringComparison.OrdinalIgnoreCase)
   ||handedOff.Observation?.Screen!="waiting"||handedOff.Observation.ForeignHero is not null
   ||handedOff.Observation.ForeignArmy.Count!=0)
    throw new Exception("Turn hand-off returned another player's screen");
Console.WriteLine("PASS turn hand-off preserves operation status without enemy details");

if(!CombatTurnRules.OwnActor(1,0,1,1)||CombatTurnRules.OwnActor(1,0,1,0)
   ||CombatTurnRules.OwnActor(1,0,2,1)||CombatTurnRules.OwnActor(1,0,1,-1))
    throw new Exception("Defender combat actor ownership is incorrect");
Console.WriteLine("PASS defender is allowed only when its own combat side acts");

if(VisitStatus.FromHint("Сад Откровения (Посещено)")!="visited"
   ||VisitStatus.FromHint("Сад Откровения (Не посещено)")!="not_visited")
    throw new Exception("Visit status did not follow the selected hero's exact game hint");
if(VisitStatus.FromHint("Сад Откровения: даруется лишь единожды")!="unknown"
   ||VisitStatus.FromHint(null)!="unknown")
    throw new Exception("Unknown visit state was guessed from an object rule or missing hint");
Console.WriteLine("PASS visit state is explicit only for exact game markers");
