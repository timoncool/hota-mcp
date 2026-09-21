namespace HotaMcp;

/// What a player takes in at a glance the moment a screen opens, written out in words.
///
/// A человек opening a town sees at once that there are two heroes, two rows of troops, a spent or
/// unspent building for today and what the dwellings offer — and knows where to click. An agent
/// reading a flat list of ids sees none of that and pays for it in a string of small questions.
/// The briefing removes those questions: it names what is on the screen, whose it is, what is
/// pending, and which action key answers each of them.
internal static class ScreenBriefing
{
    public static List<string> Build(string screen,int[] date,int[] resources,List<TownView> towns,
        List<HeroView> roster,HeroView? selected,SideView? side,string? selectedStack)
    {
        var lines=new List<string>();
        if(side is not null)
            lines.Add(side.Yours
                ?$"Ход твой, играешь за {side.Colour}. Месяц {Part(date,0)}, неделя {Part(date,1)}, день {Part(date,2)}."
                :$"Сейчас ходит {side.ActiveColour}, а ты играешь за {side.Colour} — не действуй за чужой цвет.");
        if(screen=="town")TownBrief(lines,resources,towns,roster,selectedStack);
        if(screen=="adventure")AdventureBrief(lines,resources,towns,roster,selected);
        return lines;
    }

    private static string Part(int[] date,int index)=>index<date.Length?date[index].ToString():"?";

    private static string Stacks(int[] types,int[] counts)
    {
        var parts=types.Zip(counts).Where(s=>s.First>=0&&s.Second>0)
            .Select(s=>$"{GameReference.Creature(s.First)} x{s.Second}").ToList();
        return parts.Count==0?"пусто":string.Join(", ",parts);
    }

    private static void TownBrief(List<string> lines,int[] resources,List<TownView> towns,
        List<HeroView> roster,string? selectedStack)
    {
        var town=towns.FirstOrDefault();
        if(town is null){lines.Add("Экран города открыт, но город не прочитан.");return;}
        lines.Add($"Город {town.Name}. Золото {(resources.Length>6?resources[6]:0)}, дерево {Res(resources,0)}, руда {Res(resources,1)}.");
        var keeper=roster.FirstOrDefault(h=>h.Id==town.GarrisonHero);
        var guest=roster.FirstOrDefault(h=>h.Id==town.VisitingHero);
        lines.Add(keeper is not null
            ?$"Верхний ряд — гарнизонный герой {keeper.Name}: {Stacks(keeper.ArmyTypes,keeper.ArmyCounts)}. Он остаётся в городе и держит оборону."
            :$"Верхний ряд — гарнизон города: {Stacks(town.GarrisonTypes,town.GarrisonCounts)}. Гарнизонного героя нет.");
        lines.Add(guest is not null
            ?$"Нижний ряд — герой-гость {guest.Name}: {Stacks(guest.ArmyTypes,guest.ArmyCounts)}, ходов {guest.Movement} из {guest.MaxMovement}. Он может выйти из города."
            :"Нижний ряд пуст: героя-гостя в городе нет. Нанять его можно в таверне (town:tavern).");
        if(keeper is not null&&guest is not null)
            lines.Add("Два героя в городе: отряды между ними переносятся действиями army:give, army:take и army:merge по имени существа, "
                +"а army:join сливает два отряда одного существа внутри одного ряда.");
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
            ?$"К найму готовы — {string.Join(", ",offers)}. Весь список сразу с ценами и приростом: town:building:7 (форт), оттуда fort:recruit:<уровень>."
            :"К найму сейчас никого: прирост приходит в первый день недели, в другие дни жилища отвечают «Доступно 0» — это норма, а не сбой.");
        if(selectedStack is not null)
            lines.Add($"Внимание: на экране выделен отряд ({selectedStack}). Следующий щелчок по другой клетке перенесёт его туда. "
                +"Действия переноса снимают выделение сами; сбросить вручную — army:deselect.");
    }

    private static string Res(int[] resources,int index)=>index<resources.Length?resources[index].ToString():"?";

    private static void AdventureBrief(List<string> lines,int[] resources,List<TownView> towns,
        List<HeroView> roster,HeroView? selected)
    {
        lines.Add($"Карта. Золото {(resources.Length>6?resources[6]:0)}. Героев {roster.Count}, городов {towns.Count}.");
        foreach(var hero in roster)
            lines.Add($"Герой {hero.Name}: клетка {(hero.Position.Length>1?$"{hero.Position[0]},{hero.Position[1]}":"?")}, "
                +$"ходов {hero.Movement} из {hero.MaxMovement}, мана {hero.Mana}, войско: {Stacks(hero.ArmyTypes,hero.ArmyCounts)}"
                +(selected is not null&&selected.Id==hero.Id?" — выбран сейчас.":"."));
        foreach(var town in towns)
            lines.Add($"Город {town.Name}: {(town.BuiltToday?"постройка дня потрачена":"постройка дня свободна")}, "
                +$"гарнизон {Stacks(town.GarrisonTypes,town.GarrisonCounts)}. Открыть — town:open:{town.Id}.");
        lines.Add("Что рядом с выбранным героем — nearby_targets, живой маршрут до одной цели — inspect_target, "
            +"кто стоит на клетке — inspect_cell. День закрывается действием turn:end.");
    }
}
