namespace HotaMcp;

/// The catalogue of semantic actions the adapter publishes for the screen currently on display.
/// Every key listed here must have a matching row in GameCommands.
internal static class ScreenActions
{
    public static List<AvailableAction> Build(WindowsGame game,int player,string screen,
        List<UiElement> items,List<TownView> towns,HeroView? hero,List<HeroView> roster,SaveList? saves,ScenarioSetup? setup,CombatView? combat,string? selected)
    {
        var actions=new List<AvailableAction>();
        if(screen=="message"&&items.Count(i=>i.Interactive)==1&&items.Any(i=>i.Id==30722&&i.Asset=="iokay.def"&&i.Interactive))actions.Add(new("message:accept","Подтвердить прочитанное сообщение"));
        if(screen=="message"&&items.Count(i=>i.Interactive)==2&&items.Any(i=>i.Id==30725&&i.Asset=="iokay.def"&&i.Interactive)&&items.Any(i=>i.Id==30726&&i.Asset=="icancel.def"&&i.Interactive))actions.Add(new("message:confirm","Согласиться с вопросом текущего диалога"));
        if(actions.Any(a=>a.Key=="message:confirm"))actions.Add(new("message:decline","Отказаться от действия в текущем диалоге"));
        // The game asks one question that must never be answered out of habit: ending a turn while
        // heroes can still walk. Movement does not carry over, so a blind «да» throws away part of
        // the day. The question is given its own keys so it cannot be confirmed by a loop that
        // clears dialogs.
        if(screen=="message"&&items.Any(i=>i.Text is not null&&i.Text.Contains("ещё могут ходить",StringComparison.Ordinal)))
        {
            actions.RemoveAll(a=>a.Key is "message:confirm" or "message:decline");
            actions.Add(new("turn:end:anyway","ДА, закончить ход, хотя у героев остались очки хода — они сгорят"));
            actions.Add(new("turn:end:cancel","НЕТ, вернуться и дойти оставшимися ходами"));
        }
        if(screen=="message")
        {
            // A reward dialog offers two pictures side by side with a number under each and the
            // word «или» between them: a treasure chest trading gold for experience, a campfire,
            // a scholar. The choice is made by pressing the picture, and until now it had no name
            // — the agent had to press a bare control id it could not interpret.
            var choices=items.Where(i=>i.Text is not null&&int.TryParse(i.Text.Trim(),out _)
                    &&items.Any(o=>o.Text?.Trim()=="или"))
                .OrderBy(i=>i.X).ToList();
            if(choices.Count==2)
            {
                // A number alone is not an answer: 1000 of what? The dialog's own text names the
                // two rewards in the order the pictures stand, so the left choice is the first
                // thing it mentions and the right one the second. A treasure chest reads «забрать
                // золото или ... опытом», and the left picture is indeed the gold pile.
                string story=items.FirstOrDefault(i=>i.Text is not null&&i.Text.Length>40)?.Text??"";
                string[] known=["золот","опыт","камн","кристалл","ртут","сер","древесин","руд"];
                string[] pretty=["золото","опыт","кристаллы","кристаллы","ртуть","сера","дерево","руда"];
                var named=new List<string>();
                foreach(var (stem,index) in known.Select((stem,index)=>(stem,index)))
                {
                    int at=story.IndexOf(stem,StringComparison.OrdinalIgnoreCase);
                    if(at>=0)named.Add($"{at}|{pretty[index]}");
                }
                var order=named.Select(n=>n.Split('|')).OrderBy(n=>int.Parse(n[0]))
                    .Select(n=>n[1]).Distinct().ToList();
                for(int side=0;side<choices.Count;side++)
                {
                    var choice=choices[side];
                    var picture=items.Where(i=>i.Text is null&&Math.Abs(i.X-choice.X)<20&&i.Y<choice.Y)
                        .OrderByDescending(i=>i.Y).FirstOrDefault();
                    if(picture is null)continue;
                    string what=side<order.Count?order[side]:"вариант";
                    actions.Add(new($"reward:take:{what}",
                        $"Взять {what} — {choice.Text!.Trim()}. Второй вариант тогда пропадёт; "
                        +"после выбора подтверди message:accept."));
                }
            }
        }
        if(screen=="creature_card")
        {
            // The game reuses this dialog for the creature card of a stack. Its two small buttons
            // are the arrows that upgrade the stack and the crossed circle that dismisses it; the
            // price of the upgrade is a hover hint, so it is not in the label here.
            if(items.Any(i=>i.Id==300&&i.Interactive))
                actions.Add(new("army:upgrade","Улучшить этот отряд за золото (кнопка со стрелками)"));
            if(items.Any(i=>i.Id==30723&&i.Interactive))
                actions.Add(new("army:dismiss","Распустить этот отряд — необратимо"));
            if(items.Any(i=>i.Id==30722&&i.Interactive))
                actions.Add(new("army:close","Закрыть карточку отряда"));
            actions.Add(new("split:cancel","Закрыть карточку (Esc)"));
        }
        if(screen=="split_army")
        {
            // Dropping a stack on an empty slot of the other row asks how to divide it: a slider
            // between two halves and the game's own confirm. Cancelling leaves the stack whole.
            if(items.Any(i=>i.Id==5)&&items.Any(i=>i.Id==4))
                actions.Add(new("split:amount:<n>",$"Сколько отделить в новую клетку, например split:amount:15. Сейчас: остаётся {items.First(i=>i.Id==4).Text?.Trim()}, отделяется {items.First(i=>i.Id==5).Text?.Trim()}"));
            if(items.Any(i=>i.Id==30722&&i.Interactive))actions.Add(new("split:confirm","Подтвердить разделение отряда"));
            if(items.Any(i=>i.Id==30721&&i.Interactive))actions.Add(new("split:decline","Отменить разделение, отряд останется целым"));
        }
        if(screen=="hero_screen")actions.Add(new("hero:close","Закрыть экран героя (Esc)"));
        if(screen=="backpack")actions.Add(new("backpack:close","Закрыть рюкзак (Esc)"));
        if(screen=="kingdom_overview")actions.Add(new("kingdom:close","Закрыть обзор королевства"));
        if(screen=="town_fort")
        {
            // The castle screen shows every tier at once: name, dwelling, how many are available
            // and the weekly growth. Pressing a tier's dwelling opens its recruitment.
            for(int tier=0;tier<7;tier++)
            {
                var name=items.FirstOrDefault(i=>i.Id==25+tier);
                if(name is null||string.IsNullOrWhiteSpace(name.Text))continue;
                var available=items.FirstOrDefault(i=>i.Id==33+tier)?.Text?.Trim();
                var growth=items.FirstOrDefault(i=>i.Id==129+tier)?.Text?.Trim();
                var dwelling=items.FirstOrDefault(i=>i.Id==9+tier)?.Text?.Trim();
                if(items.All(i=>i.Id!=1+tier))continue;
                bool built=!string.IsNullOrWhiteSpace(available);
                int? price=GameReference.CreatureGold(name.Text);
                int.TryParse(new string((available??"").Where(char.IsDigit).ToArray()),out int stock);
                string cost=price is int p?$", цена {p} золота за одного, весь запас {p*stock}":", цена не прочитана";
                actions.Add(new($"fort:recruit:{tier}",built
                    ?$"Нанять {name.Text} (уровень {tier+1}, {dwelling}): {available}, прирост {growth}{cost}"
                    :$"{name.Text} (уровень {tier+1}): жилище {dwelling} не построено"));
            }
        }
        if(screen=="tavern")
        {
            // The tavern offers up to two heroes: portraits 5 and 6, the price under the chosen
            // one at 4, the hire button at 11 and the exit at 30720. Hiring was not published at
            // all, so a bridge with a town and no hero had no way to get one.
            var price=items.FirstOrDefault(i=>i.Id==4)?.Text?.Trim();
            var who=items.FirstOrDefault(i=>i.Id==7)?.Text?.ReplaceLineEndings(" ").Trim();
            foreach(int slot in new[]{5,6})
                if(items.Any(i=>i.Id==slot&&i.Interactive))
                    actions.Add(new($"tavern:select:{slot-4}",$"Выбрать героя {slot-4} из предложенных"));
            // Hiring is control 12, the button under the price; control 11 beside it opens the
            // Thieves Guild and is not the way in. The hire button goes dead while a hero already
            // stands in the town as a visitor: a town holds one visiting hero and one garrison
            // hero, and the tavern needs the visitor slot free.
            if(items.Any(i=>i.Id==11&&i.Interactive))
                actions.Add(new("tavern:thieves","Открыть Гильдию Воров: сведения о соперниках"));
            // The two portraits are the two candidates; pressing one selects him, and the hire
            // button then hires whoever is selected.
            foreach(var (id,side) in new[]{(5,"слева"),(6,"справа")})
                if(items.Any(i=>i.Id==id))actions.Add(new($"tavern:pick:{side}",$"Выбрать кандидата {side} для найма"));
            bool free=items.Any(i=>i.Id==12&&i.Interactive);
            if(!free&&items.Any(i=>i.Id==12))
                actions.Add(new("tavern:hire",
                    "Нанять нельзя: место гостя в городе занято. Сначала выведи героя на карту "
                    +"(hero:out) или посади его в гарнизон (town:lead), потом нанимай."));
            if(free)
                actions.Add(new("tavern:hire",
                    $"Нанять выбранного героя{(who is null?"":$" ({who})")}{(price is null?"":$" за {price} золота")}. "
                    +"Герой появится в городе как гость со своей небольшой армией."));
        }
        if(screen=="enemy_hero_card")actions.Add(new("screen:close","Закрыть карточку чужого героя"));
        if(screen is "adventure_options" or "world_view" or "puzzle_map" or "scenario_info"
            or "thieves_guild" or "marketplace" or "mage_guild" or "town_fort")
            actions.Add(new("screen:close","Закрыть окно и вернуться"));
        if(screen=="exchange")
        {
            // Two heroes meeting on the map share one window: the left one is the hero who walked
            // in, the right one stood there. Each row is seven cells — pictures 13..19 and 20..26,
            // counts 65..71 and 72..78 — and the picture's frame is the creature, two ahead of the
            // type the game stores, the same offset the town rows use.
            string Named(int picture,int count)
            {
                var image=items.FirstOrDefault(i=>i.Id==picture&&i.Frame>0);
                var number=items.FirstOrDefault(i=>i.Id==count&&!string.IsNullOrWhiteSpace(i.Text));
                return image is null||number is null?"":GameReference.Creature(image.Frame-2);
            }
            string Count(int count)=>items.FirstOrDefault(i=>i.Id==count)?.Text?.Trim()??"";
            // The window is also the only place both heroes are shown side by side: names and
            // classes at 87 and 88, the four primary skills in two columns — 3..6 on the left and
            // 8..11 on the right — and experience and mana under each portrait at 81/83 and 82/84.
            string Value(int id)=>items.FirstOrDefault(i=>i.Id==id)?.Text?.Trim()??"?";
            actions.Add(new("exchange:compare",
                $"Сравнить героев: слева {Value(87)} — атака {Value(3)}, защита {Value(4)}, сила магии {Value(5)}, "
                +$"знание {Value(6)}, опыт {Value(81)}, мана {Value(83)}; справа {Value(88)} — атака {Value(8)}, "
                +$"защита {Value(9)}, сила магии {Value(10)}, знание {Value(11)}, опыт {Value(82)}, мана {Value(84)}. "
                +"Это чтение, ничего не нажимается: названия вторичных навыков читает inspect_element по их значкам."));
            for(int slot=0;slot<7;slot++)
            {
                var mine=Named(13+slot,65+slot);
                if(mine.Length>0)
                    actions.Add(new($"exchange:give:{mine}",
                        $"Отдать «{mine}» x{Count(65+slot)} второму герою (левый ряд → правый). "
                        +"Если у него уже есть такой отряд, они сольются."));
                var theirs=Named(20+slot,72+slot);
                if(theirs.Length>0)
                    actions.Add(new($"exchange:take:{theirs}",
                        $"Забрать «{theirs}» x{Count(72+slot)} у второго героя (правый ряд → левый). "
                        +"Если у тебя уже есть такой отряд, они сольются."));
            }
            // Under every cell sits a single arrow: it hands over exactly one creature from that
            // stack. Controls 430..436 pass one to the right, 440..446 one to the left. It is how
            // a scout is given a token stack without opening the split dialog.
            for(int slot=0;slot<7;slot++)
            {
                var mine=Named(13+slot,65+slot);
                if(mine.Length>0&&items.Any(i=>i.Id==430+slot))
                    actions.Add(new($"exchange:one:right:{mine}",
                        $"Передать ОДНОГО «{mine}» правому герою (стрелка под отрядом). Случай: дать разведчику символический отряд."));
                var theirs=Named(20+slot,72+slot);
                if(theirs.Length>0&&items.Any(i=>i.Id==440+slot))
                    actions.Add(new($"exchange:one:left:{theirs}",
                        $"Забрать ОДНОГО «{theirs}» себе (стрелка под отрядом правого героя)."));
            }
            // The specialty icon sits beside each portrait — 105 on the left, 106 on the right — and
            // the secondary skills run along the row under it. A left press opens the game's own
            // explanation of what the icon means; it is the only way to read a specialty, and a
            // specialty decides which hero should carry the army.
            if(items.Any(i=>i.Id==105))actions.Add(new("exchange:specialty:left",
                "Прочитать специализацию левого героя: откроется пояснение игры, закрывается message:accept"));
            if(items.Any(i=>i.Id==106))actions.Add(new("exchange:specialty:right",
                "Прочитать специализацию правого героя: откроется пояснение игры, закрывается message:accept"));
            // Six buttons between the rows do wholesale moves. Their pictures name them: SwCMR and
            // SwCML move every stack to one hero, SwXCh swaps the two armies outright, and the
            // pair below does the same for artefacts.
            if(items.Any(i=>i.Id==400))actions.Add(new("exchange:army:right","Отдать ВСЁ войско правому герою одной кнопкой"));
            if(items.Any(i=>i.Id==402))actions.Add(new("exchange:army:left","Забрать ВСЁ войско левому герою одной кнопкой"));
            if(items.Any(i=>i.Id==401))actions.Add(new("exchange:army:swap","Обменять армии героев местами целиком"));
            if(items.Any(i=>i.Id==450))actions.Add(new("exchange:artifacts:right","Отдать все артефакты правому герою"));
            if(items.Any(i=>i.Id==452))actions.Add(new("exchange:artifacts:left","Забрать все артефакты левому герою"));
            // An artefact both heroes carry is addressed with the side it leaves from.
            var owned=new[]{("слева","левого героя правому",27,89),("справа","правого героя левому",46,94)}
                .SelectMany(side=>ExchangeArtifacts.Worn(items,side.Item3).Where(a=>a.Slot!="книга").Select(a=>a.Name)
                    .Concat(ExchangeArtifacts.Pack(items,side.Item4).Select(a=>a.Name)).Distinct()
                    .Select(name=>(Side:side.Item1,Who:side.Item2,Name:name))).ToList();
            foreach(var a in owned)
            {
                bool both=owned.Count(o=>o.Name==a.Name)>1;
                actions.Add(new($"exchange:artifact:{a.Name}"+(both?$"@{a.Side}":""),$"Передать «{a.Name}» от {a.Who}"));
            }
            if(items.Any(i=>i.Id==8000))actions.Add(new("exchange:backpack:слева","Открыть весь рюкзак левого героя (в ряду внизу видно только пять клеток)"));
            if(items.Any(i=>i.Id==8001))actions.Add(new("exchange:backpack:справа","Открыть весь рюкзак правого героя (в ряду внизу видно только пять клеток)"));
            actions.Add(new("exchange:done","Закрыть окно обмена (ОК)"));
        }
        if(screen=="level_up")
        {
            // The level-up screen offers its skills as two picture buttons with a label under each.
            // Each offer is named by its picture: the frame is the skill and its level.
            foreach(var icon in items.Where(i=>i.Id is 2010 or 2011))
            {
                var label=items.FirstOrDefault(i=>i.Id==icon.Id-3);
                string skill=GameReference.SkillFromFrame(icon.Frame)??$"навык с картинкой {icon.Frame}";
                actions.Add(new($"level:choose:{skill}",
                    "Выбрать навык при повышении уровня: "+(label?.Text?.Replace('\n',' ')??skill)+(icon.Selected?" — выбран сейчас":"")));
            }
            if(items.Any(i=>i.Id==30722))actions.Add(new("level:accept","Подтвердить выбор навыка"));
        }

        if(screen=="main_menu")
        {
            // Five buttons, each drawing its own picture: new game, load, high scores, credits and
            // quit. Only the first two were published, so the rest of the menu did not exist for
            // the agent at all.
            if(items.Any(i=>i.Id==101&&i.Interactive))actions.Add(new("menu:new","Новая игра"));
            if(items.Any(i=>i.Id==102&&i.Interactive))actions.Add(new("menu:load","Загрузить игру"));
            if(items.Any(i=>i.Id==103&&i.Interactive))actions.Add(new("menu:highscores","Рекорды: таблица лучших результатов"));
            if(items.Any(i=>i.Id==104&&i.Interactive))actions.Add(new("menu:credits","Создатели игры"));
            if(items.Any(i=>i.Id==105&&i.Interactive))actions.Add(new("menu:quit","Выход из игры — игра закроется"));
        }
        if(screen=="game_type")
        {
            // Five choices here: single scenario, multiplayer, campaign, tutorial and back. Only
            // two were published, so campaigns and multiplayer were invisible to the agent.
            if(items.Any(i=>i.Id==100&&i.Interactive))actions.Add(new("menu:single","Одиночный сценарий"));
            if(items.Any(i=>i.Id==102&&i.Interactive))actions.Add(new("menu:multiplayer","Многопользовательская игра: сеть, hotseat"));
            if(items.Any(i=>i.Id==101&&i.Interactive))actions.Add(new("menu:campaign","Кампания"));
            if(items.Any(i=>i.Id==103&&i.Interactive))actions.Add(new("menu:tutorial","Обучение"));
            if(items.Any(i=>i.Id==104&&i.Interactive))actions.Add(new("menu:back","Назад в главное меню"));
        }
        if(screen=="marketplace")
        {
            // The market is two columns of the same seven resources: what the kingdom holds on
            // the left, what can be had for it on the right. Pressing a resource on the left
            // chooses what to give; pressing one on the right chooses what to get, and the rate
            // then stands under every resource on the right.
            string[] res=["дерево","ртуть","руда","сера","кристаллы","самоцветы","золото"];
            for(int i=0;i<7;i++)
            {
                actions.Add(new($"market:give:{res[i]}",$"Рынок: отдать {res[i]} (выбрать слева)"));
                actions.Add(new($"market:get:{res[i]}",$"Рынок: получить {res[i]} (выбрать справа)"));
            }
            if(items.Any(i=>i.Id==7))actions.Add(new("market:max","Рынок: поставить максимальное количество"));
            if(items.Any(i=>i.Id==6))actions.Add(new("market:amount:<n>","Рынок: поставить количество выбранного слева ресурса к обмену, например market:amount:12; сколько получишь — число справа внизу"));
            if(items.Any(i=>i.Id==5))actions.Add(new("market:trade","Рынок: совершить обмен выбранного количества"));
            actions.Add(new("market:close","Закрыть рынок"));
        }
        if(screen=="popup_choice")
        {
            // The expansion opens several different popups over the setup screen and they all share
            // one class, so each is recognised by what stands in it. A list of captions — the timer
            // type, the random map template — is chosen by its own words. The starting town and
            // starting hero are grids of bare pictures: their cells carry no caption at all, and
            // the game names them only when the right button is held over one. The grid lays the
            // factions out in the order the game numbers them, and one town's sixteen heroes follow
            // the same numbering, so every cell can be offered by name.
            foreach(var row in items.Where(i=>!string.IsNullOrWhiteSpace(i.Text)&&i.Interactive))
                actions.Add(new($"выбрать:{row.Text!.Trim()}",$"Выбрать в открытом списке: {row.Text!.Trim()}"));
            if(items.Any(i=>i.Id==999&&i.Interactive))
                actions.Add(new("выбор:город:случайный","Стартовый город — случайный (кубик)"));
            foreach(var cell in items.Where(i=>i.Id is >=1000 and <=1011&&i.Interactive))
                actions.Add(new($"выбор:город:{GameReference.Faction(cell.Id-1000)}",
                    $"Стартовый город: {GameReference.Faction(cell.Id-1000)}"));
            if(items.Any(i=>i.Id==2999&&i.Interactive))
                actions.Add(new("выбор:герой:случайный","Стартовый герой — случайный (кубик)"));
            // The hero grid holds the roster of the town already chosen for this row, and that
            // town is the one cell of the town grid drawn in its pressed picture.
            // The chosen cell is marked by a frame the game draws over it: a separate control that
            // stands exactly on the picked picture. Which town that is, is read from what lies
            // under the mark.
            var mark=items.FirstOrDefault(i=>i.Id==5000);
            var chosen=mark is null?null:items.FirstOrDefault(i=>i.Id is >=1000 and <=1011
                &&Math.Abs(i.X-mark.X)<8&&Math.Abs(i.Y-mark.Y)<8);
            foreach(var cell in items.Where(i=>i.Id is >=3000 and <3016&&i.Interactive))
            {
                string name=chosen is null?$"место {cell.Id-2999}"
                    :GameReference.Hero((chosen.Id-1000)*16+cell.Id-3000);
                actions.Add(new($"выбор:герой:{name}",chosen is null
                    ?$"Стартовый герой на месте {cell.Id-2999}: город ряда ещё не выбран, поэтому имя не читается"
                    :$"Стартовый герой: {name}"));
            }
            // The team agreements dialog puts each colour's flag into one of the team boxes.
            foreach(var cell in items.Where(i=>i.Id is 100 or 101 or 110 or 111&&i.Interactive))
                actions.Add(new($"команда:{(cell.Id<110?1:2)}:{(cell.Id%10)+1}",
                    $"Команда {(cell.Id<110?1:2)}, место {(cell.Id%10)+1}"));
            if(items.Any(i=>i.Asset=="CAMPCHK.def"&&i.Interactive))
                actions.Add(new("popup:подтвердить","Подтвердить и закрыть окно"));
            if(items.Any(i=>i.Asset=="CAMPCAN.def"&&i.Interactive))
                actions.Add(new("popup:отменить","Закрыть окно, ничего не меняя"));
        }
        if(screen=="scenario_selection"&&items.Any(i=>i.Id==188&&i.Interactive))actions.Add(new("scenario:back","Выйти из выбора сценария"));
        if(screen=="scenario_selection"&&items.Any(i=>i.Id==186&&i.Interactive))actions.Add(new("scenario:start","Начать партию с текущими настройками"));
        if(screen=="scenario_selection")
        {
            // The row of buttons over the list filters it by map size; «Все» clears the filter.
            // They are ordinary controls, each drawing its own picture.
            foreach(var (id,label) in new[]{(137,"S — маленькие"),(138,"M — средние"),(139,"L — большие"),
                (140,"XL — очень большие"),(3000,"H — огромные"),(3001,"XH — сверхогромные"),
                (3002,"G — гигантские"),(141,"все размеры")})
                if(items.Any(i=>i.Id==id&&i.Interactive))
                    actions.Add(new($"scenario:filter:{id}",$"Показать в списке только {label}"));
            // The players panel gives one row per colour, and every row is the same five things:
            // the flag, a human/AI switch, and three choices with arrows either side — starting
            // town, starting hero and starting bonus. Each column is a block of ids: the switch at
            // 207 plus the row, town arrows at 215 and 223, hero at 231 and 239, bonus at 247 and
            // 255, with the chosen value written under each picture.
            string[] colours=["красный","синий","коричневый","зелёный","оранжевый","фиолетовый","бирюзовый","розовый"];
            // Three panels share this screen and reuse the same control numbers: the rows of the
            // random map settings stand exactly where the player rows stand. Offering the player
            // rows while another panel is shown would change the wrong thing silently.
            for(int slot=0;setup?.Panel=="players"&&slot<8;slot++)
            {
                if(items.All(i=>i.Id!=345+slot))continue;
                string who=items.FirstOrDefault(i=>i.Id==345+slot)?.Text?.Trim()??"?";
                string colour=slot<colours.Length?colours[slot]:$"игрок {slot+1}";
                if(items.Any(i=>i.Id==207+slot&&i.Interactive))
                    actions.Add(new($"setup:{colour}:кто",
                        $"{colour}: переключить, кто играет — сейчас «{who}»"));
                foreach(var (what,left,right) in new[]{("город",215,223),("герой",231,239),("бонус",247,255)})
                {
                    // The picture between the two arrows opens the whole grid at once — every town
                    // or every hero of that town — which is how a player picks one by sight
                    // instead of stepping through them one arrow press at a time.
                    var row=items.FirstOrDefault(i=>i.Id==left+slot);
                    int column=what=="город"?176:what=="герой"?252:328;
                    var picture=row is null?null:items.FirstOrDefault(i=>Math.Abs(i.X-column)<6&&Math.Abs(i.Y-row.Y)<8);
                    if(picture is not null&&what!="бонус")
                        actions.Add(new($"setup:{colour}:{what}:выбрать",
                            $"{colour}: открыть выбор — весь список, что можно поставить в «{what}»"));
                    string now=items.FirstOrDefault(i=>i.Id==(what=="город"?353:what=="герой"?361:369)+slot)?.Text?.Trim()??"";
                    if(items.Any(i=>i.Id==left+slot&&i.Interactive))
                        actions.Add(new($"setup:{colour}:{what}:назад",
                            $"{colour}: предыдущий стартовый {what}{(now.Length>0?$" (сейчас {now})":"")}"));
                    if(items.Any(i=>i.Id==right+slot&&i.Interactive))
                        actions.Add(new($"setup:{colour}:{what}:вперёд",
                            $"{colour}: следующий стартовый {what}{(now.Length>0?$" (сейчас {now})":"")}"));
                    // A town can be asked for by name: the adapter steps the row itself and checks
                    // every step against what the game calls the picture, so the agent never has to
                    // count arrow presses.
                    if(what=="город"&&items.Any(i=>i.Id==right+slot&&i.Interactive))
                        foreach(string faction in GameReference.Factions)
                            actions.Add(new($"setup:{colour}:город:{faction}",
                                $"{colour}: поставить стартовым городом {faction}"));
                }
            }
            foreach(var (id,key) in new[]{(128,"scenario:maps"),(129,"scenario:players"),(130,"scenario:random")})
                if(items.Any(i=>i.Id==id&&i.Interactive))actions.Add(new(key,items.Single(i=>i.Id==id).Text!));
        }
        if(screen=="adventure")
        {
            // The town list works like the hero list: each place holds the town the player's own
            // list puts there, and the town is asked for by its name.
            var owned=GameReader.SidebarTowns(game,player);
            for(int slot=0;slot<owned.Length&&slot<5;slot++)
            {
                var town=towns.FirstOrDefault(t=>t.Id==owned[slot]);
                if(town is null||items.All(i=>i.Id!=32+slot))continue;
                actions.Add(new($"town:open:{town.Name}",$"Открыть город {town.Name} (место {slot+1} в списке городов справа)"));
            }
        }
        if(screen=="adventure")actions.Add(new("hero:select","Перейти к следующему своему герою на карте (штатная клавиша H)"));
        if(screen=="adventure"&&hero is not null)actions.Add(new("hero:move","Переместить героя по проложенному пути (штатная клавиша M)"));
        if(screen=="adventure")
        {
            // The sidebar hero list: pressing a portrait selects that hero, and pressing the
            // portrait of the hero already selected opens his own screen with skills, spells and
            // artefacts. Occupied slots are the ones the game keeps visible.
            // Each place is named by the hero standing in it, read from the player's own list,
            // so the agent asks for a hero by name and never counts portraits.
            var list=GameReader.SidebarHeroes(game,player);
            for(int slot=0;slot<5;slot++)
            {
                var portrait=items.FirstOrDefault(i=>i.Id==15+slot);
                if(portrait is null||!portrait.Interactive||list[slot]<0)continue;
                string name=roster.FirstOrDefault(h=>h.Id==list[slot])?.Name??$"герой №{list[slot]}";
                bool current=hero?.Id==list[slot];
                if(!current)actions.Add(new($"hero:pick:{name}",$"Выбрать героя {name} (место {slot+1} в списке справа)"));
                actions.Add(new($"hero:sheet:{name}",$"Открыть экран героя {name}: навыки, заклинания, артефакты"));
            }
        }
        if(screen=="adventure"&&hero is not null)
        {
            // Manual, Section IV: the arrow keys move the current hero one step. This needs no
            // route planning and no cursor, so it is the plain way to walk.
            foreach(var (key,label) in new[]{
                ("north","на север"),("south","на юг"),("west","на запад"),("east","на восток"),
                ("northwest","на северо-запад"),("northeast","на северо-восток"),
                ("southwest","на юго-запад"),("southeast","на юго-восток")})
                actions.Add(new($"hero:step:{key}",$"Шаг героя {label} (штатная клавиша-стрелка)"));
            actions.Add(new("hero:sleep","Усыпить героя, чтобы он не предлагался в этом ходу (Z)"));
            actions.Add(new("hero:wake","Разбудить героя (W)"));
        }
        if(screen=="adventure")
        {
            foreach(var (key,label) in new[]{("north","вверх"),("south","вниз"),("west","влево"),("east","вправо")})
                actions.Add(new($"view:scroll:{key}",$"Прокрутить карту {label} (Ctrl+стрелка)"));
            actions.Add(new("game:kingdom","Обзор королевства: все герои, города, шахты и доход (K)"));
            actions.Add(new("game:world_view","Просмотр мира: вся известная карта с фильтрами (V)"));
            actions.Add(new("game:marketplace","Рынок королевства: обмен ресурсов (B)"));
            actions.Add(new("game:thieves_guild","Гильдия воров: сведения о соперниках (G)"));
            actions.Add(new("game:puzzle","Карта-загадка обелисков (P)"));
            actions.Add(new("game:adventure_options","Меню карты: просмотр мира, загадка, копать, сведения (A)"));
            actions.Add(new("game:quest_log","Журнал заданий (Q)"));
            actions.Add(new("game:scenario_info","Сведения о сценарии (I)"));
        }
        if(screen=="adventure"&&items.Any(i=>i.Id==12&&i.Asset=="iam001.def"&&i.Interactive))actions.Add(new("turn:end","Закончить ход; игра может запросить подтверждение"));
        if(screen=="town")
        {
            int townId=game.Read(game.U32(game.U32(0x69954c)+0x38),1)[0];
            var currentTown=towns.Single(t=>t.Id==townId);
            if(currentTown.Buildings.Contains(5))actions.Add(new("town:tavern","Открыть таверну"));
            // Every building that is actually built can be entered by pressing it, the way a
            // player does. The names the game itself uses come from its own building table.
            foreach(int building in currentTown.Buildings.Where(b=>b is not (>=30 and <=36) and not 5).OrderBy(b=>b))
                actions.Add(new($"town:building:{building}",$"Войти: {GameReference.Building(building,currentTown.Type)}"));
            for(int level=0;level<7;level++)if(currentTown.Buildings.Contains(30+level))actions.Add(new($"town:recruit:{level}",$"Открыть найм существ уровня {level+1}"));
            actions.Add(new("town:construction","Открыть зал совета"));
            actions.Add(new("town:close","Вернуться на карту"));
            if(currentTown is not null)actions.Add(new("town:lead","Соединить армию героя с гарнизоном: портрет, затем знамя"));
            if(currentTown is not null)actions.Add(new("town:banner","Клик по знамени гарнизона (переключить гарнизонного героя)"));
            if(currentTown is not null)actions.Add(new("hero:switch","Переключиться между гарнизонным героем и посетителем города (Space)"));
            if(towns.Count>1)
            {
                actions.Add(new("town:previous","Предыдущий город (стрелка вверх)"));
                actions.Add(new("town:next","Следующий город (стрелка вниз)"));
            }
            // Bringing the garrison hero out needs the visitor's place free: with a visitor there
            // the same gesture simply swaps the two heroes, which is what hero:switch is for.
            if(currentTown is not null&&currentTown.GarrisonHero>=0&&currentTown.VisitingHero<0)
                actions.Add(new("hero:out","Вытащить гарнизонного героя на карту: клик по портрету героя, затем клик по строке ниже"));
            // The town shows two rows of seven slots. The lower one is the visiting hero's army;
            // the upper one belongs to the garrison hero when a hero stands there, and to the town
            // garrison otherwise — the player sees one row either way, so the actions must not
            // care which of the two it is.
            var visitingHero=roster.FirstOrDefault(h=>h.Id==currentTown?.VisitingHero);
            var garrisonHero=roster.FirstOrDefault(h=>h.Id==currentTown?.GarrisonHero);
            int[] heroTypes=visitingHero?.ArmyTypes??[];
            int[] heroCounts=visitingHero?.ArmyCounts??[];
            int[] garrisonTypes=garrisonHero?.ArmyTypes??currentTown?.GarrisonTypes??[];
            int[] garrisonCounts=garrisonHero?.ArmyCounts??currentTown?.GarrisonCounts??[];
            string upper=garrisonHero is null?"гарнизон города":$"ряд гарнизонного героя {garrisonHero.Name}";
            string lower=visitingHero is null?"нижний ряд":$"ряд героя {visitingHero.Name}";
            string Named(int[] types,int[] counts,int slot)=>
                slot<types.Length&&slot<counts.Length&&types[slot]>=0&&counts[slot]>0
                    ?GameReference.Creature(types[slot]):"";
            // The same creature can stand in two slots of one row; then the name alone is not an
            // address and the slot is appended, the way the player tells them apart by position.
            string Address(int[] types,int[] counts,int slot)
            {
                var name=Named(types,counts,slot);
                return Enumerable.Range(0,7).Count(other=>Named(types,counts,other)==name)>1
                    ?$"{name}#{slot}":name;
            }
            int FindSlot(int[] types,int[] counts,int type)=>Enumerable.Range(0,7)
                .FirstOrDefault(slot=>slot<types.Length&&slot<counts.Length&&types[slot]==type&&counts[slot]>0,-1);
            bool garrisonFull=Enumerable.Range(0,7).All(slot=>Named(garrisonTypes,garrisonCounts,slot).Length>0);
            bool heroFull=Enumerable.Range(0,7).All(slot=>Named(heroTypes,heroCounts,slot).Length>0);
            for(int slot=0;slot<7;slot++)
            {
                var name=Named(heroTypes,heroCounts,slot);
                if(name.Length==0)continue;
                string address=Address(heroTypes,heroCounts,slot);
                int count=heroCounts[slot];
                actions.Add(new($"army:open:h{slot}",
                    $"Открыть карточку отряда «{name}» x{count} из нижнего ряда ({lower}). На карточке улучшение за золото и роспуск; армию она не двигает. Случай: узнать, во что и почём улучшается отряд."));
                int twin=FindSlot(garrisonTypes,garrisonCounts,heroTypes[slot]);
                if(twin>=0)
                    actions.Add(new($"army:give:{address}",
                        $"Отдать «{name}» x{count} снизу вверх, в {upper}: там уже стоит такой же отряд ({garrisonCounts[twin]}), отряды сольются в один на {count+garrisonCounts[twin]}. Случай: герой уходит налегке, войско остаётся держать город."));
                else if(!garrisonFull)
                    actions.Add(new($"army:give:{address}",
                        $"Отдать «{name}» x{count} снизу вверх, в {upper}, на свободную клетку, весь отряд целиком. Часть отряда — army:split-give:{address}. Случай: освободить слот у героя."));
                if(!garrisonFull&&count>1)
                    actions.Add(new($"army:split-give:{address}",
                        $"Отделить часть «{name}» x{count} в {upper} на свободную клетку: откроется окно разделения, там split:amount:<n> и split:confirm. Случай: оставить городу охрану, не отдавая весь отряд."));
            }
            for(int slot=0;slot<7;slot++)
            {
                var name=Named(garrisonTypes,garrisonCounts,slot);
                if(name.Length==0)continue;
                string address=Address(garrisonTypes,garrisonCounts,slot);
                int count=garrisonCounts[slot];
                actions.Add(new($"army:open:g{slot}",
                    $"Открыть карточку отряда «{name}» x{count} из верхнего ряда ({upper}). На карточке улучшение за золото и роспуск. Случай: улучшить войско, пришедшее с недельным приростом."));
                int twin=FindSlot(heroTypes,heroCounts,garrisonTypes[slot]);
                if(twin>=0)
                {
                    int mine=heroCounts[twin];
                    bool lastOfKeeper=garrisonHero is not null&&garrisonCounts.Count(c=>c>0)==1;
                    actions.Add(new($"army:merge:{address}",
                        $"Объединить «{name}» в один отряд у героя: {mine} внизу ({lower}) плюс {count} наверху ({upper}) равно {mine+count}."
                        +(lastOfKeeper
                            ?$" Но это единственный отряд {garrisonHero!.Name}, а герой не может остаться без войска, поэтому перейдёт {count-1}, а одно существо останется наверху."
                            :"")
                        +" Случай: перед выходом из города собрать войско в один кулак, чтобы оно било одним ударом, а не двумя мелкими."));
                    actions.Add(new($"army:take:{address}",
                        $"То же самое другими словами: забрать «{name}» x{count} сверху вниз, к герою, где отряд сольётся с его собственными {mine}."
                        +(garrisonHero is not null&&garrisonCounts.Count(c=>c>0)==1
                            ?$" Это единственный отряд {garrisonHero.Name}, а герой не может остаться без войска — одно существо останется наверху."
                            :"")));
                }
                else if(!heroFull)
                    actions.Add(new($"army:take:{address}",
                        $"Забрать «{name}» x{count} сверху вниз, из {upper} в {lower}, на свободную клетку, весь отряд целиком. Часть отряда — army:split-take:{address}. Случай: забрать недельный прирост перед походом."));
                if(!heroFull&&count>1)
                    actions.Add(new($"army:split-take:{address}",
                        $"Взять часть «{name}» x{count} из {upper} к герою на свободную клетку: откроется окно разделения, там split:amount:<n> и split:confirm. Случай: взять в поход часть гарнизона, оставив городу охрану."));
            }
            // Two stacks of one creature in the same row waste a slot and a blow.
            foreach(var row in new[]{(Types:heroTypes,Counts:heroCounts,Row:"h",Where:lower),
                                     (Types:garrisonTypes,Counts:garrisonCounts,Row:"g",Where:upper)})
                for(int slot=0;slot<7;slot++)
                {
                    var name=Named(row.Types,row.Counts,slot);
                    if(name.Length==0)continue;
                    int other=Enumerable.Range(slot+1,6-slot).FirstOrDefault(o=>Named(row.Types,row.Counts,o)==name,-1);
                    if(other<0)continue;
                    actions.Add(new($"army:join:{row.Row}{slot}+{other}",
                        $"Слить два отряда «{name}» внутри одного ряда ({row.Where}): {row.Counts[slot]} и {row.Counts[other]} станут одним отрядом на {row.Counts[slot]+row.Counts[other]} и освободят слот. Случай: после найма или приёма подкрепления одно существо оказалось в двух клетках."));
                }
            if(selected is not null)
                actions.Add(new("army:deselect",
                    $"Снять выделение с отряда ({selected}). Пока отряд выделен, щелчок по другой клетке переносит его туда; действия переноса снимают выделение сами, это на случай, когда его надо просто сбросить."));
            if(currentTown is not null)for(int slot=0;slot<7;slot++)
                if(currentTown.GarrisonCounts.Length>slot&&currentTown.GarrisonCounts[slot]>0)
                    actions.Add(new($"town:take:{slot}",$"Передать отряд из гарнизона герою: {currentTown.GarrisonCounts[slot]} существ в слоте {slot+1}"));
        }
        if(screen=="town_hall")
        {
            // The hall colours each row by state; the picture next to the label carries it.
            foreach(var item in items.Where(i=>i.Id>=600&&i.Id<618))
            {
                int slot=item.Id-600;
                var picture=items.FirstOrDefault(i=>i.Id==400+slot);
                // What the player sees on this screen is a colour under each picture: green is a
                // building that can be put up now, gold one that already stands, red with a cross
                // one that cannot be started. These were named the wrong way round, so every row
                // the adapter called buildable was in fact the one the game refuses.
                string state=picture?.Frame switch
                {
                    0=>"уже построено",
                    1=>"можно построить",
                    2=>"построить нельзя",
                    3=>"построить нельзя",
                    null=>"состояние неизвестно",
                    _=>$"состояние {picture.Frame}",
                };
                actions.Add(new($"building:inspect:{slot}",$"{item.Text??"Здание"} — {state}"));
            }
            actions.Add(new("construction:close","Вернуться в город"));
        }
        if(screen=="building_confirmation")
        {
            if(items.Any(i=>i.Id==30722&&i.Interactive))actions.Add(new("building:buy","Построить указанное здание за показанную цену"));
            if(items.Any(i=>i.Id==30721&&i.Interactive))actions.Add(new("building:cancel","Отменить покупку"));
        }
        if(screen=="save_game")actions.Add(new("save:confirm","Сохранить игру; может открыться запрос имени"));
        if(screen=="load_game"&&saves is not null)
        {
            foreach(var entry in saves.Entries)
            {
                if(entry.Folder)
                {
                    if(entry.Width>0)actions.Add(new($"load:open:{entry.Index}",$"Открыть папку сохранений: {entry.Name}"));
                    continue;
                }
                actions.Add(new($"load:select:{entry.Index}",$"Выбрать сохранение: {entry.Name}"));
            }
            if(items.Any(i=>i.Id==186&&i.Asset=="scnrlod.def"&&i.Interactive))actions.Add(new("load:confirm","Загрузить выбранное сохранение"));
            if(items.Any(i=>i.Id==188&&i.Interactive))actions.Add(new("load:back","Выйти в главное меню"));
        }
        if(screen=="recruitment")
        {
            if(items.Any(i=>i.Id==532&&i.Interactive))actions.Add(new("recruit:max","Выбрать максимум доступных для найма существ"));
            if(items.Any(i=>i.Id==30722&&i.Interactive))actions.Add(new("recruit:buy","Нанять выбранное количество за указанную цену"));
            actions.Add(new("recruit:cancel","Отменить найм"));
        }
        if(screen=="system_options"&&items.Any(i=>i.Id==106&&i.Interactive))actions.Add(new("game:save","Открыть сохранение игры"));
        if(screen=="system_options"&&items.Any(i=>i.Id==102&&i.Asset=="soload.def"&&i.Interactive))actions.Add(new("game:load","Открыть загрузку игры"));
        if(screen=="system_options"&&items.Any(i=>i.Id==108&&i.Asset=="somain.def"&&i.Interactive))actions.Add(new("game:main_menu","Выйти в главное меню через штатный вопрос игры"));
        if(screen=="tavern")
        {
            actions.Add(new("tavern:close","Выйти из таверны"));
        }
        if(screen=="battle_result"&&items.Any(i=>i.Id==30722&&i.Interactive))actions.Add(new("battle:accept","Принять результат боя"));
        if(screen=="spellbook")
        {
            actions.Add(new("spellbook:close","Закрыть книгу"));
            actions.Add(new("spellbook:adventure","Показать заклинания карты приключений (A)"));
            actions.Add(new("spellbook:combat","Показать боевые заклинания (C)"));
            foreach(var label in items.Where(i=>i.Id>=749&&i.Id<=772&&!string.IsNullOrEmpty(i.Text)))
                if(items.Any(i=>i.Id==label.Id-280&&i.Interactive))actions.Add(new($"spellbook:select:{label.Id-280}",label.Text!));
        }
        if(screen=="scenario_selection")
        {
            // The five chess pieces under «Уровень сложности» are controls 107 to 111, easiest
            // first. The chosen one is the only one whose state carries the selection bit, and the
            // choice decides starting resources and how hard the opponents play — it was not
            // published at all, so a game could start on the hardest setting unnoticed.
            string[] names=["самый лёгкий","лёгкий","обычный","трудный","самый трудный"];
            for(int level=0;level<5;level++)
                if(items.Any(i=>i.Id==107+level))
                    actions.Add(new($"scenario:difficulty:{level+1}",
                        $"Сложность: {names[level]} (фигура {level+1} из 5, слева направо от лёгкой к трудной)"
                        +(items.Any(i=>i.Id==107+level&&i.Selected)?" — ВЫБРАНА СЕЙЧАС":"")));
        }
        if(setup is not null)foreach(var choice in setup.Fields.SelectMany(f=>f.Choices).Where(c=>c.Enabled&&!c.Selected))actions.Add(new(choice.Action,choice.Label));
        if(combat?.OwnTurn==true)
        {
            // The game announces target mode in its status line, but the wording changes the moment
            // the cursor crosses a stack: «Выберите цель заклинания» becomes «Направить <спелл> на
            // <отряд>». Both mean the same thing — a spell is waiting for a target — and missing
            // the second one left a cast hanging with no way to finish it.
            if(items.Any(i=>i.Id==2005&&i.Text is not null
                &&(i.Text.Contains("цель заклинания",StringComparison.Ordinal)
                   ||i.Text.StartsWith("Направить",StringComparison.Ordinal))))
            {
                foreach(var stack in combat.Stacks)actions.Add(new("spell:target:"+stack.Id,"Выбрать цель заклинания: "+stack.Name+"; допустимость проверяет игра"));
                actions.Add(new("spell:cancel","Отменить выбор цели"));
            }
            else
            {
            if(items.Any(i=>i.Id==2008&&i.Interactive))actions.Add(new("combat:spellbook","Открыть книгу заклинаний"));
            if(items.Any(i=>i.Id==2009&&i.Interactive))actions.Add(new("combat:wait","Ждать"));
            if(items.Any(i=>i.Id==2010&&i.Interactive))actions.Add(new("combat:defend","Защищаться"));
            if(items.Any(i=>i.Id==2002&&i.Interactive))actions.Add(new("combat:retreat","Отступить: сохранить героя, потерять армию"));
            if(items.Any(i=>i.Id==2004&&i.Interactive))actions.Add(new("combat:auto","Автобой: игра сама разыгрывает бой за обе стороны"));
            actions.Add(new("combat:surrender","Сдаться: сохранить героя и армию за золото (S)"));
            actions.Add(new("combat:options","Настройки боя (O)"));
            foreach(int hex in combat.ReachableHexes)actions.Add(new($"combat:move:{hex}",$"Переместиться на клетку {hex}"));
            // Whether a blow can land this turn is exactly what the shaded hexes show a player: a
            // melee attacker must be able to step next to the defender. Each target says so, so a
            // stack that cannot reach is not sent on a blow that cannot happen.
            var reach=new HashSet<int>(combat.ReachableHexes);
            foreach(string id in combat.AttackableTargets)
            {
                var victim=combat.Stacks.Single(s=>s.Id==id);
                var around=victim.Around().ToList();
                bool inReach=around.Any(reach.Contains)
                    ||combat.Stacks.Any(s=>s.Id==combat.ActiveStack&&s.Hexes.Any(around.Contains));
                actions.Add(new("combat:attack:"+id,
                    $"Атаковать: {victim.Name} ({victim.Count}) — "
                    +(inReach?"ближний бой дотянется в этот ход; сторону удара мост выберет ближайшую"
                        :"ближним боем в этот ход НЕ дотянуться; сработает только выстрел, если ходящий отряд стреляет")));
                // Where the blow comes from matters — a flank, a stack that retaliates, a
                // neighbour that would be hit too — so every side the attacker can strike from is
                // offered by the hex it would stand on and its direction from the defender.
                foreach(int side in around.Where(reach.Contains))
                    actions.Add(new($"combat:attack:{id}:from:{side}",
                        $"Атаковать {victim.Name} ({victim.Count}) {Deliveries.SideName(Deliveries.Facing(victim,side),side)}, встав на клетку {side}"));
            }
            }
        }
        return actions;
    }
}
