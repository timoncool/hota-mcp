namespace HotaMcp;

/// What a player takes in at a glance the moment a screen opens, written out in words.
///
/// A человек opening a town sees at once that there are two heroes, two rows of troops, a spent or
/// unspent building for today and what the dwellings offer — and knows where to click. An agent
/// reading a flat list of ids sees none of that and pays for it in a string of small questions.
/// The briefing removes those questions: it names what is on the screen, whose it is, what is
/// pending, and which action key answers each of them.
/// The artefact cells of the exchange window. Each hero has nineteen worn slots in the game's own
/// order and a row of five visible backpack cells; a cell holds an artefact when it is drawn at
/// all, and the picture's frame is the artefact. While an artefact is on the cursor, the cells it
/// may go to are drawn with frame 144.
internal static class ExchangeArtifacts
{
    public static readonly string[] Slots=["голова","плечи","шея","правая рука","левая рука","торс","правое кольцо","левое кольцо",
        "ноги","разное 1","разное 2","разное 3","разное 4","баллиста","тележка","палатка","катапульта","книга","разное 5"];
    public const int Highlight=144;
    public static IEnumerable<(int Id,string Slot,string Name)> Worn(List<UiElement> items,int first)=>
        items.Where(i=>i.Id>=first&&i.Id<first+19&&i.Width==44&&i.Frame!=Highlight).OrderBy(i=>i.Id)
            .Select(i=>(i.Id,Slots[i.Id-first],GameReference.Artifact(i.Frame)??$"артефакт с картинкой {i.Frame}"));
    public static IEnumerable<(int Id,string Name)> Pack(List<UiElement> items,int first)=>
        items.Where(i=>i.Id>=first&&i.Id<first+5&&i.Width==44&&i.Frame!=Highlight).OrderBy(i=>i.Id)
            .Select(i=>(i.Id,GameReference.Artifact(i.Frame)??$"артефакт с картинкой {i.Frame}"));
}

internal static class ScreenBriefing
{
    public static List<string> Build(string screen,int[] date,int[] resources,List<TownView> towns,
        List<HeroView> roster,HeroView? selected,SideView? side,string? selectedStack,BuildOffer? offer,int openTown,string? foreignHero,List<string> foreignArmy,List<ForeignHero> foreignHeroes,List<UiElement> items,CombatView? combat=null,int[]? sidebar=null)
    {
        var lines=new List<string>();
        if(side is not null)
            lines.Add(side.Yours
                ?$"Ход твой, играешь за {side.Colour}. День {Part(date,0)}, неделя {Part(date,1)}, месяц {Part(date,2)}." + (Part(date,0)=="1"?" Первый день недели: в городах появился прирост существ, мельницы и водяные колёса снова дают ресурс.":"")
                :$"Сейчас ходит {side.ActiveColour}, а ты играешь за {side.Colour} — не действуй за чужой цвет.");
        foreach(var enemy in foreignHeroes)
        {
            if(side?.Allies.Contains(enemy.Owner)==true)
            {
                lines.Add($"Союзник: герой {enemy.Name} ({Colour(enemy.Owner)}) на клетке {enemy.Position[0]},{enemy.Position[1]}"
                    +$"{NearestTown(enemy.Position,towns," — от твоего города ")}. Его герои и города не цель и не угроза.");
                continue;
            }
            lines.Add($"ТРЕВОГА: чужой герой {enemy.Name} ({Colour(enemy.Owner)}) виден на клетке "
                +$"{enemy.Position[0]},{enemy.Position[1]}{NearestTown(enemy.Position,towns," — от твоего города ")}. Посмотреть его войско — наведи на него inspect_tile "
                +"или открой карточку правым щелчком; при угрозе городу переходи в состояние обороны.");
        }
        if(date.Length>2&&screen!="combat")
        {
            int left=8-date[0];
            // The hall pays every morning and the dwellings fill on the first day of a week; both
            // are known in advance, so planning a purchase does not need a turn to find out.
            int income=towns.Sum(t=>t.Buildings.Contains(13)?4000:t.Buildings.Contains(12)?2000
                :t.Buildings.Contains(11)?1000:t.Buildings.Contains(10)?500:0);
            lines.Add($"Завтра утром придёт {income} золота с ратуш"
                +(towns.Count>0?" плюс дневная добыча шахт (их список и общий доход — экран game:kingdom)":"")
                +$". До конца недели {left} " +(left==1?"день":left<5?"дня":"дней")
                +", прирост существ придёт в первый день новой недели — до него имеет смысл достроить жилища.");
        }
        if(screen=="enemy_hero_card")
        {
            lines.Add($"Карточка чужого героя {foreignHero}. Войско: "
                +(foreignArmy.Count>0?string.Join(", ",foreignArmy):"не прочитано")+".");
            lines.Add("Размер отряда игра показывает вилкой, а не числом — точное число даёт только заклинание Видения "
                +"или существо Разбойник в армии. Закрыть карточку — screen:close.");
        }
        if(screen=="message")
            lines.Add("На экране сообщение игры, и пока оно висит, ничего другого сделать нельзя: "
                +"ни походить, ни открыть город. Текст лежит в Elements; закрой его действием "
                +"message:accept, а вопрос с двумя кнопками — message:confirm или message:decline.");
        if(screen=="message")RewardBrief(lines,items);
        if(screen=="mage_guild")
        {
            var taught=items.Where(i=>i.Id is >=40 and <70&&i.Width==83&&i.Frame>0).OrderBy(i=>i.Id)
                .Select(i=>GameReference.Spell(i.Frame)??$"заклинание с картинкой {i.Frame}").ToList();
            lines.Add("Гильдия магов. Заклинания в ней: "+(taught.Count>0?string.Join(", ",taught):"не видно")
                +". Герой с книгой и нужной Мудростью выучивает их сам, войдя в гильдию. Описание — inspect_element по свитку; выйти — screen:close.");
        }
        if(screen=="backpack")
        {
            var carried=items.Where(i=>i.Id is >=2000 and <2064).OrderBy(i=>i.Id)
                .Select(i=>GameReference.Artifact(i.Frame)??$"артефакт с картинкой {i.Frame}").ToList();
            lines.Add("Рюкзак героя целиком: "+(carried.Count>0?string.Join(", ",carried):"пусто")
                +". Описание — inspect_element по картинке артефакта; закрыть — backpack:close.");
        }
        if(screen=="level_up")
        {
            var offers=items.Where(i=>i.Id is 2010 or 2011).Select(i=>ScreenActions.LevelSkill(items,i)+(ScreenActions.LevelSkillChosen(items,i)?" (выбран)":"")).ToList();
            var said=items.Where(i=>i.Id<2000&&!string.IsNullOrWhiteSpace(i.Text)&&(i.Text.Contains("уровн")||i.Text.Contains('+'))).Select(i=>i.Text!.Trim().Replace('\n',' '));
            lines.Add("Повышение уровня. "+string.Join(" ",said)+(offers.Count>0?$" На выбор: {string.Join(" или ",offers)} — level:choose:<навык>, затем level:accept. Что брать под роль героя — hota_docs(\"повышение уровня что брать\").":" Навыков на выбор нет — level:accept."));
        }
        if(screen=="battle_result")BattleResultBrief(lines,items);
        if(screen=="marketplace")
        {
            string Txt(int id)=>items.FirstOrDefault(i=>i.Id==id)?.Text?.Trim()??"";
            lines.Add("Рынок. В казне: "+string.Join(", ",Enumerable.Range(0,7).Select(i=>$"{ResourceNames[i]} {Txt(35+i)}"))+".");
            var rates=Enumerable.Range(0,7).Where(i=>Txt(77+i).Length>0).Select(i=>$"{ResourceNames[i]} {Txt(77+i)}").ToList();
            lines.Add(rates.Count>0
                ?$"Курс за выбранный слева ресурс, как пишет игра: «1/10» — получишь 1 за 10 отданных, одно число — сколько получишь за 1: {string.Join(", ",rates)}."
                :"Курсы появятся под ресурсами справа, когда выбран ресурс слева (market:give:<ресурс>).");
            lines.Add("Порядок: market:give:<что отдать> → market:get:<что получить> → market:max или ползунок → market:trade.");
        }
        if(screen=="waiting")
        {
            var said=items.Where(i=>!string.IsNullOrWhiteSpace(i.Text)).Select(i=>i.Text!.Trim().Replace('\n',' ')).ToList();
            if(said.Count>0)lines.Add("На экране сообщение: «"+string.Join(" ",said)+"»");
            lines.Add("Экран сейчас принадлежит игроку, который ходит: его кнопок в наблюдении нет, и ничего нажимать нельзя — "
                +"мост откажет с NOT_YOUR_TURN. Твои герои, города и ресурсы ниже — твои. Когда ход перейдёт к тебе, observe это покажет.");
            if(side is not null&&side.Participants.Count>0)lines.Add("Участники: "+string.Join("; ",side.Participants)+".");
        }
                if(screen=="combat"&&combat is not null)CombatBrief(lines,combat);
        if(screen=="town")TownBrief(lines,resources,towns,roster,selectedStack,openTown);
        if(screen=="exchange")ExchangeBrief(lines,items,roster);
        if(screen=="adventure")AdventureBrief(lines,resources,towns,roster,selected,sidebar,side);
        if(screen=="building_confirmation"&&offer is not null)
        {
            lines.Add($"{offer.Title}. {offer.Effect}");
            lines.Add($"Цена: {string.Join(", ",offer.Price)}. {offer.Conditions}");
            lines.Add(offer.CanBuy
                ?"Купить — building:buy, отказаться — building:cancel. Постройка тратит дневной лимит города."
                :$"Построить сейчас нельзя: {offer.Blocked}. Выход — building:cancel.");
        }
        // Not the rules themselves — those stay out of observations — but where to ask for them.
        bool siege=combat?.Stacks.Any(s=>s.Name.StartsWith("Катапульт",StringComparison.Ordinal))==true;
        string topic=screen switch
        {
            "combat" when siege=>"как устроена осада стены башни ров ворота",
            "combat"=>"боевая математика урон ответный удар",
            "adventure"=>"машина состояний партии",
            "town" or "town_hall" or "building_confirmation"=>"порядок строительства",
            "town_fort" or "recruitment"=>"найм существ прирост недели выкуп",
            "marketplace"=>"рынок курсы обмена",
            "level_up"=>"повышение уровня что брать",
            "hero_screen"=>"вторичные навыки какие брать",
            "exchange" or "split_army"=>"как переносить отряды между героем и гарнизоном",
            "mage_guild" or "spellbook"=>"заклинания школы магии",
            "tavern"=>"найм героя в таверне",
            "creature_card"=>"оценка боя сила армии",
            "enemy_hero_card"=>"вражеский герой сила армии",
            "kingdom_overview"=>"доход королевства золото в день",
            "puzzle_map"=>"карта загадок грааль",
            "world_view"=>"карта мира разведка",
            "scenario_selection" or "scenario_info" or "game_type"=>"условия победы и поражения",
            "main_menu"=>"главное меню новая игра загрузка",
            "save_game" or "load_game"=>"сохранение и загрузка партии",
            _=>"реестр экранов как выйти",
        };
        lines.Add($"Правила и расчёты для этого экрана — в справочнике: hota_docs(\"{topic}\"); карточка существа, "
            +"артефакта, заклинания или объекта — hota_reference(<имя>). Не по памяти.");
        return lines;
    }

    private static string Colour(int index)=>index switch
    {
        0=>"красный",1=>"синий",2=>"коричневый",3=>"зелёный",
        4=>"оранжевый",5=>"фиолетовый",6=>"бирюзовый",7=>"розовый",_=>"игрок "+index
    };

    private static string Part(int[] date,int index)=>index<date.Length?date[index].ToString():"?";

    /// The battlefield as a player reads it at a glance: whose move it is, where every stack
    /// stands, and which enemies the moving stack can hit this turn.
    private static void CombatBrief(List<string> lines,CombatView combat)
    {
        string Where(CombatStack s)=>s.Hexes.Length>1?$"клетки {string.Join("-",s.Hexes)}":$"клетка {s.Hex}";
        string Traits(CombatStack s)=>string.Join("",new[]{s.Flying?", летает":"",s.Shooter?", стреляет":"",s.Wide?", занимает две клетки":""});
        // The creature card as a player reads it on a right click, plus the stack's state this round.
        string Card(CombatStack s)
        {
            var parts=new List<string>{Where(s),$"атака {s.Attack}, защита {s.Defence}",$"урон {s.DamageMin}–{s.DamageMax}",
                $"здоровье {s.TopHealth}/{s.HealthEach} у верхнего",$"скорость {s.Speed}"};
            if(s.Shots is int shots)parts.Add($"выстрелов {shots}");
            if(!s.WarMachine)parts.Add($"ответных ударов {s.Retaliations}");
            if(s.Morale!=0)parts.Add($"мораль {s.Morale:+0;-0}");
            if(s.Luck!=0)parts.Add($"удача {s.Luck:+0;-0}");
            string traits=Traits(s).TrimStart(',',' ');
            if(traits.Length>0)parts.Add(traits);
            parts.AddRange(s.Abilities);
            if(s.WarMachine)parts.Add("военная машина");
            if(s.Summoned)parts.Add("призван в бою");
            if(s.Acted)parts.Add("в этом раунде уже ходил");
            else if(s.Waited)parts.Add("ждёт");
            if(s.Defending)parts.Add("в защите");
            // Who stands next to whom, said outright: models read a stated neighbour far more
            // reliably than they work it out from a drawn grid.
            var around=s.Around().ToHashSet();
            var touching=combat.Stacks.Where(o=>o.Id!=s.Id&&o.Hexes.Any(around.Contains))
                .Select(o=>$"{o.Name} [{o.Id}]"+(o.Side==combat.OwnSide?" (твой)":" (враг)")).ToList();
            if(touching.Count>0)parts.Add("вплотную: "+string.Join(", ",touching));
            if(s.Shooter&&!s.WarMachine&&combat.Stacks.Any(o=>o.Side!=s.Side&&o.Hexes.Any(around.Contains)))
                parts.Add("противник вплотную — выстрела не будет, только ближний бой");
            if(s.Effects.Length>0)parts.Add("действует: "+string.Join(", ",s.Effects));
            return $"{s.Name} {s.Count} [{s.Id}] ({string.Join(", ",parts)})";
        }
        string Line(IEnumerable<CombatStack> list)=>string.Join("; ",list.Select(Card));
        var active=combat.Stacks.FirstOrDefault(s=>s.Id==combat.ActiveStack);
        lines.Add($"Бой, раунд {combat.Round+1}. "+(combat.OwnTurn&&active is not null
            ?$"Ходит твой отряд: {active.Name} {active.Count} ({Where(active)}, скорость {active.Speed}{Traits(active)})."
            :"Сейчас ходит противник: действий нет, наблюдай, пока ход не вернётся."));
        if(combat.SinceLastMove.Length>0)lines.Add("С твоего прошлого хода: "+string.Join("; ",combat.SinceLastMove)+".");
        lines.Add("Твои отряды: "+Line(combat.Stacks.Where(s=>s.Side==combat.OwnSide))+".");
        lines.Add("Враги: "+Line(combat.Stacks.Where(s=>s.Side!=combat.OwnSide))+".");
        LossesBrief(lines,combat);
        if(combat.Field is {Siege:true} field)
            lines.Add("Осада"+(field.Moat?", перед стеной ров":"")+". Стена: "
                +string.Join(", ",field.Walls.Select(w=>w.HitPoints>0?$"{w.Name} — прочность {w.HitPoints}":$"{w.Name} — разрушена"))+".");
        lines.AddRange(FieldMap(combat));
        if(!combat.OwnTurn||active is null)return;
        if(active.Speed==0)
        {
            lines.Add("Ходящий отряд не двигается (военная машина): цель выбирает combat:attack:<отряд>.");
            return;
        }
        var reach=new HashSet<int>(combat.ReachableHexes);
        var near=new List<string>();var far=new List<string>();
        foreach(var enemy in combat.Stacks.Where(s=>s.Side!=combat.OwnSide))
        {
            var around=enemy.Around().ToList();
            bool touching=active.Hexes.Any(around.Contains);
            if(touching||around.Any(reach.Contains))near.Add($"{enemy.Name} {enemy.Count} [{enemy.Id}]"+(touching?" (уже вплотную)":""));
            else far.Add($"{enemy.Name} {enemy.Count} [{enemy.Id}]");
        }
        lines.Add((near.Count>0?"Ближним боем в этот ход достаёшь: "+string.Join(", ",near)+". ":"Ближним боем в этот ход не достать никого. ")
            +(far.Count>0?"Не достаёшь: "+string.Join(", ",far)+". ":"")
            +(active.Shooter?"Отряд стреляет — выстрел бьёт любую цель: combat:attack:<отряд>.":"Удар — combat:attack:<отряд> или с выбранной клетки combat:attack:<отряд>:from:<клетка>."));
    }

    /// What each side has lost since the fight began, priced by the game's AI Value — the same
    /// measure the game's own AI weighs armies with.
    private static void LossesBrief(List<string> lines,CombatView combat)
    {
        if(combat.Losses.Length==0)return;
        string Side(bool own)
        {
            var list=combat.Losses.Where(l=>(l.Side==combat.OwnSide)==own).ToList();
            return list.Count==0?"нет":string.Join(", ",list.Select(l=>$"{l.Name} {l.Lost}"))+$" (ценность {list.Sum(l=>l.ValueLost)})";
        }
        lines.Add($"Потери с начала боя — твои: {Side(true)}; врага: {Side(false)}.");
    }

    /// The battlefield drawn in text, the way a player takes it in at a glance: eleven rows of
    /// fifteen hexes, every other row shifted half a hex to the right, as on screen.
    private static IEnumerable<string> FieldMap(CombatView combat)
    {
        var symbol=new Dictionary<string,char>();
        const string own="123456789abcdefghijkl",enemy="ABCDEFGHIJKLMNOPQRSTU";
        int o=0,e=0;
        foreach(var s in combat.Stacks)symbol[s.Id]=s.Side==combat.OwnSide?own[Math.Min(o++,own.Length-1)]:enemy[Math.Min(e++,enemy.Length-1)];
        var at=new Dictionary<int,char>();
        foreach(var s in combat.Stacks)foreach(int h in s.Hexes)at[h]=symbol[s.Id];
        var field=combat.Field;
        void Mark(IEnumerable<int>? hexes,char c){if(hexes is null)return;foreach(int h in hexes)at.TryAdd(h,c);}
        Mark(field?.Obstacles,'#');Mark(field?.ForceFields,'#');Mark(field?.FireWalls,'!');Mark(field?.Quicksand,'~');Mark(field?.LandMines,'*');
        Mark(combat.ReachableHexes,'o');
        yield return "Карта поля: клетка = строка×17 + столбец; цифры — твои отряды, буквы — враги; "
            +(combat.OwnTurn?"o — куда ходящий отряд встанет в этот ход, ":"")+"# — препятствие, ! — огонь, ~ — твой зыбучий песок, * — твоя мина, . — свободно.";
        yield return "     "+string.Join(" ",Enumerable.Range(1,15).Select(c=>(c%10).ToString()));
        for(int row=0;row<11;row++)
            yield return $"{row,2} "+(row%2==0?"  ":" ")+string.Join(" ",Enumerable.Range(1,15).Select(c=>at.TryGetValue(row*17+c,out char ch)?ch:'.'));
        yield return "Обозначения: "+string.Join("; ",combat.Stacks.Select(s=>$"{symbol[s.Id]} — {s.Name} {s.Count} [{s.Id}]"+(s.Id==combat.ActiveStack?", ходит":"")))+".";
    }

    /// Fort, citadel and castle are one building rebuilt in place; the key is whichever stands.
    private static string FortKey(TownView town)=>new[]{9,8,7}.Where(town.Buildings.Contains)
        .Select(b=>$"town:building:{b} ({GameReference.Building(b,town.Type)})").FirstOrDefault()
        ??"форта нет — найм по жилищам town:recruit:<уровень>";

    private static string Stacks(int[] types,int[] counts)
    {
        var parts=types.Zip(counts).Where(s=>s.First>=0&&s.Second>0)
            .Select(s=>$"{GameReference.Creature(s.First)} x{s.Second}").ToList();
        return parts.Count==0?"пусто":string.Join(", ",parts);
    }

    private static void TownBrief(List<string> lines,int[] resources,List<TownView> towns,
        List<HeroView> roster,string? selectedStack,int openTown)
    {
        // With two towns the screen shows one of them, and the game keeps which one in its own
        // town manager. Taking the first of the list was showing the wrong town's garrison,
        // buildings and daily limit the moment a second town was captured.
        var town=towns.FirstOrDefault(t=>t.Id==openTown)??towns.FirstOrDefault();
        if(town is null){lines.Add("Экран города открыт, но город не прочитан.");return;}
        lines.Add($"Город {town.Name}. Золото {(resources.Length>6?resources[6]:0)}, дерево {Res(resources,0)}, руда {Res(resources,2)}, "
            +$"ртуть {Res(resources,1)}, сера {Res(resources,3)}, кристаллы {Res(resources,4)}, самоцветы {Res(resources,5)}.");
        var keeper=roster.FirstOrDefault(h=>h.Id==town.GarrisonHero);
        var guest=roster.FirstOrDefault(h=>h.Id==town.VisitingHero);
        lines.Add(keeper is not null
            ?$"Верхний ряд — гарнизонный герой {keeper.Name}: {Stacks(keeper.ArmyTypes,keeper.ArmyCounts)}. Остаётся в городе и держит оборону."
            :$"Верхний ряд — гарнизон города: {Stacks(town.GarrisonTypes,town.GarrisonCounts)}. Гарнизонного героя нет.");
        lines.Add(guest is not null
            ?$"Нижний ряд — герой-гость {guest.Name}: {Stacks(guest.ArmyTypes,guest.ArmyCounts)}, ходов {guest.Movement} из {guest.MaxMovement}. Может выйти из города."
            :"Нижний ряд пуст: героя-гостя в городе нет. Нанять его можно в таверне (town:tavern).");
        if(keeper is not null&&guest is not null)
            lines.Add("Два героя в городе: отряды между ними переносятся действиями army:give, army:take и army:merge по имени существа, "
                +"а army:join сливает два отряда одного существа внутри одного ряда.");
        lines.Add("Построено: "+string.Join(", ",town.Built)+".");
        lines.Add(town.BuiltToday
            ?"Постройка на сегодня уже потрачена — в этом городе сегодня больше ничего не построить."
            :"Постройка на сегодня не потрачена: town:construction открывает зал совета со списком и ценами.");
        var offers=new List<string>();
        for(int tier=0;tier<7;tier++)
        {
            int ready=town.Recruitable.Select(row=>tier<row.Length?row[tier]:0).Sum();
            if(ready>0)offers.Add($"уровень {tier+1}: {ready}");
        }
        lines.Add(offers.Count>0
            ?$"К найму готовы — {string.Join(", ",offers)}. Весь список сразу с ценами и приростом: {FortKey(town)}, оттуда fort:recruit:<уровень>."
            :"К найму сейчас никого: прирост приходит в первый день недели, в другие дни жилища отвечают «Доступно 0» — это норма, а не сбой.");
        if(selectedStack is not null)
            lines.Add($"Внимание: на экране выделен отряд ({selectedStack}). Следующий щелчок по другой клетке перенесёт его туда. "
                +"Действия переноса снимают выделение сами; сбросить вручную — army:deselect.");
    }

    /// Two heroes side by side, written out the way the screen reads to a player.
    ///
    /// The screen itself shows only bare numbers in two columns and rows of small pictures: which
    /// number is attack and which is knowledge, whose column is whose, and what each picture means
    /// are all things a player knows by sight and an agent cannot guess. Without this the choice of
    /// a main hero came down to pressing one of the two transfer buttons to find out which side is
    /// which.
    private static void ExchangeBrief(List<string> lines,List<UiElement> items,List<HeroView> roster)
    {
        string Text(int id)=>items.FirstOrDefault(i=>i.Id==id)?.Text?.Trim()??"?";
        string Skills(int first)=>string.Join(", ",Enumerable.Range(first,8)
            .Select(id=>GameReference.SkillFromFrame(items.FirstOrDefault(i=>i.Id==id)?.Frame??0))
            .Where(name=>name is not null)) is {Length:>0} list?list:"навыков нет";
        lines.Add($"Обмен героев. Слева {Text(87)}, справа {Text(88)}. "
            +"Кнопки переноса названы по стороне: exchange:army:left отдаёт всё войско левому, "
            +"exchange:army:right — правому, exchange:army:swap меняет армии местами.");
        // The name line reads «Имя, уровень N, Класс»; the hero it names is the one whose
        // specialty is wanted, and the roster knows his number.
        string Specialty(int icon)
        {
            int frame=items.FirstOrDefault(i=>i.Id==icon)?.Frame??-1;
            return GameReference.Specialty(frame)
                ??$"картинка специальности №{frame} в таблице игры не найдена";
        }
        // Morale and luck are pictures beside the experience: frame 3 is zero, each step either side one point.
        string Mood(int id)=>items.FirstOrDefault(i=>i.Id==id) is {} icon?(icon.Frame-3).ToString("+0;-0;0"):"?";
        lines.Add($"Слева: атака {Text(3)}, защита {Text(4)}, сила магии {Text(5)}, знание {Text(6)}; "
            +$"опыт {Text(81)}, мана {Text(83)}, мораль {Mood(107)}, удача {Mood(109)}. Специальность: {Specialty(105)}. Навыки: {Skills(200)}.");
        lines.Add($"Справа: атака {Text(8)}, защита {Text(9)}, сила магии {Text(10)}, знание {Text(11)}; "
            +$"опыт {Text(82)}, мана {Text(84)}, мораль {Mood(108)}, удача {Mood(110)}. Специальность: {Specialty(106)}. Навыки: {Skills(208)}.");
        foreach(var (side,doll,pack) in new[]{("Слева",27,89),("Справа",46,94)})
        {
            var worn=ExchangeArtifacts.Worn(items,doll).Select(a=>$"{a.Slot} — {a.Name}").ToList();
            var carried=ExchangeArtifacts.Pack(items,pack).Select(a=>a.Name).ToList();
            lines.Add($"{side} артефакты: {(worn.Count>0?string.Join("; ",worn):"нет")}. Рюкзак (видимая часть): {(carried.Count>0?string.Join(", ",carried):"пусто")}.");
        }
        lines.Add("Передать один артефакт соседу — exchange:artifact:<название>: игра сама кладёт его в подходящий слот, а если слот занят — в рюкзак. Описание любого — inspect_element по его клетке.");
        lines.Add("Специальность решает, кто из двоих главный: она растёт с каждым уровнем и "
            +"привязана к герою навсегда. Сведи армию тому, чья специальность работает на твою армию.");
    }

    /// The end of a fight in words: who won, and what each side lost. The losses are rows of
    /// small creature portraits under «Нападающий» and «Обороняющийся», each with the number lost
    /// under it; a small portrait draws the creature number plus two.
    private static void BattleResultBrief(List<string> lines,List<UiElement> items)
    {
        string? verdict=items.FirstOrDefault(i=>i.Text is not null&&i.Text.Contains("опыт",StringComparison.OrdinalIgnoreCase))?.Text?.Replace("\n"," ").Trim();
        if(verdict is not null)lines.Add($"Итог боя: {verdict}");
        var attacker=items.FirstOrDefault(i=>i.Text?.Trim()=="Нападающий");
        var defender=items.FirstOrDefault(i=>i.Text?.Trim()=="Обороняющийся");
        string Losses(int top,int bottom)
        {
            var parts=items.Where(i=>i.Width==32&&i.Height==32&&i.Frame>=2&&i.Text is null&&i.Y>top&&i.Y<bottom)
                .OrderBy(i=>i.X)
                .Select(p=>
                {
                    var n=items.FirstOrDefault(t=>t.Text is not null&&t.Y>p.Y&&t.Y<p.Y+60&&Math.Abs(t.X+t.Width/2-(p.X+p.Width/2))<12);
                    return $"{GameReference.Creature(p.Frame-2)} {n?.Text?.Trim()??"?"}";
                }).ToList();
            return parts.Count==0?"нет":string.Join(", ",parts);
        }
        if(attacker is not null)
            lines.Add($"Потери нападающего: {Losses(attacker.Y,defender?.Y??int.MaxValue)}.");
        if(defender is not null)
            lines.Add($"Потери обороняющегося: {Losses(defender.Y,int.MaxValue)}.");
        lines.Add("Принять итог — battle:accept.");
    }

    private static readonly string[] ResourceNames=["дерево","ртуть","руда","сера","кристаллы","самоцветы","золото"];

    /// What a message hands out, in words. The game draws each reward as a picture with its
    /// amount under it; a resource picture comes from the resource icon file and its frame is the
    /// resource, so «600» and «6» become «золото 600, сера 6».
    private static void RewardBrief(List<string> lines,List<UiElement> items)
    {
        var parts=new List<string>();
        foreach(var picture in items.Where(i=>i.Asset is not null&&i.Asset.StartsWith("resour",StringComparison.OrdinalIgnoreCase)
                    &&i.Frame is >=0 and <7).OrderBy(i=>i.X))
        {
            int centre=picture.X+picture.Width/2;
            var amount=items.Where(t=>t.Text is not null&&t.Y>=picture.Y&&Math.Abs(t.X+t.Width/2-centre)<45
                    &&t.Text.Trim().Split(' ')[0].All(char.IsDigit))
                .OrderBy(t=>t.Y).FirstOrDefault();
            parts.Add($"{ResourceNames[picture.Frame]} {amount?.Text?.Trim()??"?"}");
        }
        // Any other picture in the window — an artefact, a spell, a creature — is named by the
        // caption the game prints under it.
        foreach(var picture in items.Where(i=>i.Asset is not null&&!i.Interactive
                    &&!i.Asset.StartsWith("resour",StringComparison.OrdinalIgnoreCase)).OrderBy(i=>i.X))
        {
            int centre=picture.X+picture.Width/2;
            var caption=items.Where(t=>!string.IsNullOrWhiteSpace(t.Text)&&t.Y>=picture.Y+picture.Height-4&&t.Y<=picture.Y+picture.Height+40
                    &&Math.Abs(t.X+t.Width/2-centre)<45&&!t.Text.Trim().All(c=>char.IsDigit(c)||c==' ')&&t.Text.Trim()!="или")
                .OrderBy(t=>t.Y).FirstOrDefault();
            if(caption is not null&&!parts.Contains(caption.Text!.Trim()))parts.Add(caption.Text!.Trim());
        }
        if(parts.Count>0)lines.Add($"Награда в этом окне: {string.Join(", ",parts)}.");
    }

    private static string Res(int[] resources,int index)=>index<resources.Length?resources[index].ToString():"?";

    /// Where a cell lies from the nearest own town, in cells and by the compass, the way a player
    /// reads the map at a glance. Straight-line distance, not a route: the route is the game's.
    private static string NearestTown(int[] at,List<TownView> towns,string prefix,bool toward=false)
    {
        if(at.Length<3)return "";
        var near=towns.Where(t=>t.Position.Length==3&&t.Position[2]==at[2])
            .Select(t=>(Town:t,Cells:Math.Max(Math.Abs(t.Position[0]-at[0]),Math.Abs(t.Position[1]-at[1]))))
            .OrderBy(t=>t.Cells).FirstOrDefault();
        if(near.Town is null)return "";
        if(near.Cells<=2)return $"{prefix}{near.Town.Name}: рядом";
        int dx=(at[0]-near.Town.Position[0])*(toward?-1:1),dy=(at[1]-near.Town.Position[1])*(toward?-1:1);
        string[] compass=["восток","северо-восток","север","северо-запад","запад","юго-запад","юг","юго-восток"];
        int sector=(int)Math.Round(Math.Atan2(-dy,dx)/(Math.PI/4));
        string side=compass[(sector%8+8)%8];
        return $"{prefix}{near.Town.Name}: {near.Cells} кл. на {side}";
    }

    private static void AdventureBrief(List<string> lines,int[] resources,List<TownView> towns,
        List<HeroView> roster,HeroView? selected,int[]? sidebar,SideView? side)
    {
        lines.Add($"Карта. Золото {(resources.Length>6?resources[6]:0)}. Героев {roster.Count}, городов {towns.Count}.");
        if(side is not null&&side.Participants.Count>0)
            lines.Add("Участники: "+string.Join("; ",side.Participants)+"."+(side.Underground?" На карте есть подземный уровень.":" Подземного уровня нет."));
        foreach(var hero in roster)
            lines.Add($"Герой {hero.Name}: клетка {(hero.Position.Length>1?$"{hero.Position[0]},{hero.Position[1]}":"?")}, "
                +$"ходов {hero.Movement} из {hero.MaxMovement}, мана {hero.Mana}{NearestTown(hero.Position,towns,", до своего города ",true)}, войско: {Stacks(hero.ArmyTypes,hero.ArmyCounts)}"
                +(selected is not null&&selected.Id==hero.Id?" — выбран сейчас.":".")
                +(sidebar is not null&&Array.IndexOf(sidebar,hero.Id) is int slot and >=0 and <5
                    ?$" Ход по карточке игры (правый щелчок по полосе хода): inspect_element id:{20+slot}.":""));
        foreach(var town in towns)
        {
            // A hero leading the garrison holds the town's troops as his own army; saying
            // «гарнизон пусто» then was simply wrong.
            var keeper=roster.FirstOrDefault(h=>h.Id==town.GarrisonHero);
            string guard=keeper is not null
                ?$"гарнизон держит герой {keeper.Name}: {Stacks(keeper.ArmyTypes,keeper.ArmyCounts)}"
                :$"гарнизон {Stacks(town.GarrisonTypes,town.GarrisonCounts)}";
            string icon=town.IconCross is null?"":town.IconCross==town.BuiltToday
                ?(town.IconCross==true?" (на значке в панели крест)":" (значок в панели без креста)")
                :$" (РАСХОЖДЕНИЕ: значок в панели {(town.IconCross==true?"с крестом":"без креста")}, а память города — {(town.BuiltToday?"строили":"не строили")})";
            lines.Add($"Город {town.Name}: {(town.BuiltToday?"постройка дня потрачена":"постройка дня свободна")}{icon}, "
                +$"{guard}. Открыть — town:open:{town.Name}.");
        }
        lines.Add("Рамка решений: у партии одно состояние (осмотреться, экономика, накопление, штурм, сведение тиров, "
            +"расширение, оборона), у каждого героя роль (главный берёт охраняемое, сборщик — свободное), "
            +"а день решается деревом по приоритету. Целиком — hota_docs(\"машина состояний партии\"). "
            +"Текущее состояние и активную задачу держи в плане.");
        lines.Add("Чем мерить: бродячий отряд даёт только опыт, шахта — ресурс каждый день, захваченный город — "
            +"и доход, и прирост; поэтому город и шахты важнее драки ради драки.");
        lines.Add("Что рядом с выбранным героем — nearby_targets, живой маршрут до одной цели — inspect_target, "
            +"кто стоит на клетке — inspect_cell. День закрывается действием turn:end.");
    }
}
