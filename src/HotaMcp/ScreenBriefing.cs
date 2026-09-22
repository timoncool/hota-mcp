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
        List<HeroView> roster,HeroView? selected,SideView? side,string? selectedStack,BuildOffer? offer,int openTown,string? foreignHero,List<string> foreignArmy,List<ForeignHero> foreignHeroes,List<UiElement> items)
    {
        var lines=new List<string>();
        if(side is not null)
            lines.Add(side.Yours
                ?$"Ход твой, играешь за {side.Colour}. День {Part(date,0)}, неделя {Part(date,1)}, месяц {Part(date,2)}." + (Part(date,0)=="1"?" Первый день недели: в городах появился прирост существ, мельницы и водяные колёса снова дают ресурс.":"")
                :$"Сейчас ходит {side.ActiveColour}, а ты играешь за {side.Colour} — не действуй за чужой цвет.");
        foreach(var enemy in foreignHeroes)
            lines.Add($"ТРЕВОГА: чужой герой {enemy.Name} ({Colour(enemy.Owner)}) виден на клетке "
                +$"{enemy.Position[0]},{enemy.Position[1]}. Посмотреть его войско — наведи на него inspect_tile "
                +"или открой карточку правым щелчком; при угрозе городу переходи в состояние обороны.");
        if(date.Length>2)
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
        if(screen=="town")TownBrief(lines,resources,towns,roster,selectedStack,openTown);
        if(screen=="exchange")ExchangeBrief(lines,items,roster);
        if(screen=="adventure")AdventureBrief(lines,resources,towns,roster,selected);
        if(screen=="building_confirmation"&&offer is not null)
        {
            lines.Add($"{offer.Title}. {offer.Effect}");
            lines.Add($"Цена: {string.Join(", ",offer.Price)}. {offer.Conditions}");
            lines.Add(offer.CanBuy
                ?"Купить — building:buy, отказаться — building:cancel. Постройка тратит дневной лимит города."
                :$"Построить сейчас нельзя: {offer.Blocked}. Выход — building:cancel.");
        }
        return lines;
    }

    private static string Colour(int index)=>index switch
    {
        0=>"красный",1=>"синий",2=>"коричневый",3=>"зелёный",
        4=>"оранжевый",5=>"фиолетовый",6=>"бирюзовый",7=>"розовый",_=>"игрок "+index
    };

    private static string Part(int[] date,int index)=>index<date.Length?date[index].ToString():"?";

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
            ?$"К найму готовы — {string.Join(", ",offers)}. Весь список сразу с ценами и приростом: town:building:7 (форт), оттуда fort:recruit:<уровень>."
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
        lines.Add($"Слева: атака {Text(3)}, защита {Text(4)}, сила магии {Text(5)}, знание {Text(6)}; "
            +$"опыт {Text(81)}, мана {Text(83)}. Специальность: {Specialty(105)}. Навыки: {Skills(200)}.");
        lines.Add($"Справа: атака {Text(8)}, защита {Text(9)}, сила магии {Text(10)}, знание {Text(11)}; "
            +$"опыт {Text(82)}, мана {Text(84)}. Специальность: {Specialty(106)}. Навыки: {Skills(208)}.");
        lines.Add("Специальность решает, кто из двоих главный: она растёт с каждым уровнем и "
            +"привязана к герою навсегда. Сведи армию тому, чья специальность работает на твою армию.");
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
