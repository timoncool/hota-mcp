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
            if(items.Any(i=>i.Id==30722&&i.Interactive))actions.Add(new("split:confirm","Подтвердить разделение отряда"));
            if(items.Any(i=>i.Id==30721&&i.Interactive))actions.Add(new("split:decline","Отменить разделение, отряд останется целым"));
        }
        if(screen=="hero_screen")actions.Add(new("hero:close","Закрыть экран героя (Esc)"));
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
                actions.Add(new($"fort:recruit:{tier}",built
                    ?$"Нанять {name.Text} (уровень {tier+1}, {dwelling}): {available}, прирост {growth}"
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
            actions.Add(new("exchange:done","Закрыть окно обмена (ОК)"));
        }
        if(screen=="level_up")
        {
            // The level-up screen offers its skills as two picture buttons with a label under each.
            foreach(var icon in items.Where(i=>i.Id is 2010 or 2011))
            {
                var label=items.FirstOrDefault(i=>i.Id==icon.Id-3);
                actions.Add(new($"level:choose:{icon.Id}",
                    "Выбрать навык при повышении уровня: "+(label?.Text?.Replace('\n',' ')??"вариант "+(icon.Id-2009))));
            }
            if(items.Any(i=>i.Id==30722))actions.Add(new("level:accept","Подтвердить выбор навыка"));
        }

        if(screen=="main_menu")
        {
            if(items.Any(i=>i.Id==101&&i.Interactive))actions.Add(new("menu:new","Новая игра"));
            if(items.Any(i=>i.Id==102&&i.Interactive))actions.Add(new("menu:load","Загрузить игру"));
        }
        if(screen=="game_type"&&items.Any(i=>i.Id==104&&i.Interactive))actions.Add(new("menu:back","Главное меню"));
        if(screen=="game_type"&&items.Any(i=>i.Id==100&&i.Interactive))actions.Add(new("menu:single","Одиночная игра"));
        if(screen=="scenario_selection"&&items.Any(i=>i.Id==188&&i.Interactive))actions.Add(new("scenario:back","Выйти из выбора сценария"));
        if(screen=="scenario_selection"&&items.Any(i=>i.Id==186&&i.Interactive))actions.Add(new("scenario:start","Начать партию с текущими настройками"));
        if(screen=="scenario_selection")
        {
            foreach(var (id,key) in new[]{(128,"scenario:maps"),(129,"scenario:players"),(130,"scenario:random")})
                if(items.Any(i=>i.Id==id&&i.Interactive))actions.Add(new(key,items.Single(i=>i.Id==id).Text!));
        }
        if(screen=="adventure")foreach(var town in towns)actions.Add(new($"town:open:{town.Id}",$"Открыть город: {town.Name}"));
        if(screen=="adventure")actions.Add(new("hero:select","Перейти к следующему своему герою на карте (штатная клавиша H)"));
        if(screen=="adventure"&&hero is not null)actions.Add(new("hero:move","Переместить героя по проложенному пути (штатная клавиша M)"));
        if(screen=="adventure")
        {
            // The sidebar hero list: pressing a portrait selects that hero, and pressing the
            // portrait of the hero already selected opens his own screen with skills, spells and
            // artefacts. Occupied slots are the ones the game keeps visible.
            for(int slot=0;slot<5;slot++)
            {
                var portrait=items.FirstOrDefault(i=>i.Id==15+slot);
                if(portrait is null||!portrait.Interactive)continue;
                actions.Add(new($"hero:sheet:{slot}",$"Открыть экран героя из списка, место {slot+1}"));
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
                actions.Add(new($"town:building:{building}",$"Войти в постройку города номер {building}"));
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
            if(currentTown is not null&&currentTown.GarrisonHero>=0)actions.Add(new("hero:out","Вытащить гарнизонного героя на карту: клик по портрету героя, затем клик по строке ниже"));
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
                        $"Отдать «{name}» x{count} снизу вверх, в {upper}, на свободную клетку. Сливать не с чем, поэтому игра откроет экран split_army и спросит, сколько перенести: split:confirm переносит выставленное, split:decline отменяет целиком. Случай: оставить городу охрану или освободить слот у героя."));
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
                        $"Забрать «{name}» x{count} сверху вниз, из {upper} в {lower}, на свободную клетку. Сливать не с чем, поэтому игра спросит, сколько перенести (экран split_army). Случай: забрать недельный прирост перед походом."));
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
                string state=picture?.Frame switch
                {
                    0=>"уже построено",
                    2=>"можно построить",
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
                        $"Сложность: {names[level]} (фигура {level+1} из 5, слева направо от лёгкой к трудной)"));
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
            foreach(string id in combat.AttackableTargets)
            {
                var victim=combat.Stacks.Single(s=>s.Id==id);
                actions.Add(new("combat:attack:"+id,
                    $"Атаковать: {victim.Name} ({victim.Count}) — стрелок бьёт с места, ближний бой требует подойти; законность проверяет игра"));
            }
            }
        }
        return actions;
    }
}
