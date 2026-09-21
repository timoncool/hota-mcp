namespace HotaMcp;

/// The catalogue of semantic actions the adapter publishes for the screen currently on display.
/// Every key listed here must have a matching row in GameCommands.
internal static class ScreenActions
{
    public static List<AvailableAction> Build(WindowsGame game,int player,string screen,
        List<UiElement> items,List<TownView> towns,HeroView? hero,SaveList? saves,ScenarioSetup? setup,CombatView? combat)
    {
        var actions=new List<AvailableAction>();
        if(screen=="message"&&items.Count(i=>i.Interactive)==1&&items.Any(i=>i.Id==30722&&i.Asset=="iokay.def"&&i.Interactive))actions.Add(new("message:accept","Подтвердить прочитанное сообщение"));
        if(screen=="message"&&items.Count(i=>i.Interactive)==2&&items.Any(i=>i.Id==30725&&i.Asset=="iokay.def"&&i.Interactive)&&items.Any(i=>i.Id==30726&&i.Asset=="icancel.def"&&i.Interactive))actions.Add(new("message:confirm","Согласиться с вопросом текущего диалога"));
        if(actions.Any(a=>a.Key=="message:confirm"))actions.Add(new("message:decline","Отказаться от действия в текущем диалоге"));
        if(screen=="split_stack")actions.Add(new("split:cancel","Закрыть окно отряда (Esc)"));
        if(screen=="hero_screen")actions.Add(new("hero:close","Закрыть экран героя (Esc)"));
        if(screen=="exchange")actions.Add(new("exchange:done","Закрыть окно обмена (ОК)"));
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
        if(screen=="adventure"&&hero is null)actions.Add(new("hero:select","Выбрать своего героя на карте (штатная клавиша H)"));
        if(screen=="adventure"&&hero is not null)actions.Add(new("hero:move","Переместить героя по проложенному пути (штатная клавиша M)"));
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
            actions.Add(new("game:kingdom","Обзор королевства (K)"));
            actions.Add(new("game:quest_log","Журнал заданий (Q)"));
            actions.Add(new("game:scenario_info","Сведения о сценарии (I)"));
        }
        if(screen=="adventure"&&items.Any(i=>i.Id==12&&i.Asset=="iam001.def"&&i.Interactive))actions.Add(new("turn:end","Закончить ход; игра может запросить подтверждение"));
        if(screen=="town")
        {
            int townId=game.Read(game.U32(game.U32(0x69954c)+0x38),1)[0];
            var currentTown=towns.Single(t=>t.Id==townId);
            if(currentTown.Buildings.Contains(5))actions.Add(new("town:tavern","Открыть таверну"));
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
            if(currentTown is not null)for(int slot=0;slot<7;slot++)
                if(currentTown.GarrisonCounts.Length>slot&&currentTown.GarrisonCounts[slot]>0)
                    actions.Add(new($"town:take:{slot}",$"Передать отряд из гарнизона герою: {currentTown.GarrisonCounts[slot]} существ в слоте {slot+1}"));
        }
        if(screen=="town_hall")
        {
            foreach(var item in items.Where(i=>i.Id>=600&&i.Id<618))actions.Add(new($"building:inspect:{item.Id-600}",item.Text??"Описание здания"));
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
            if(items.Any(i=>i.Id==12&&i.Interactive))actions.Add(new("tavern:hire","Нанять выбранного героя за указанную цену"));
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
        if(setup is not null)foreach(var choice in setup.Fields.SelectMany(f=>f.Choices).Where(c=>c.Enabled&&!c.Selected))actions.Add(new(choice.Action,choice.Label));
        if(combat?.OwnTurn==true)
        {
            if(items.Any(i=>i.Id==2005&&i.Text=="Выберите цель заклинания"))
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
            foreach(string id in combat.AttackableTargets)actions.Add(new("combat:attack:"+id,"Атаковать: "+combat.Stacks.Single(s=>s.Id==id).Name));
            }
        }
        return actions;
    }
}
