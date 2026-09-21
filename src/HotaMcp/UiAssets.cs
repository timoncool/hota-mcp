namespace HotaMcp;

/// What the pictures on the buttons mean.
///
/// Most buttons in this game carry no caption: their meaning lives in the image file the control
/// draws. Addressing them by control id works but says nothing — «контрол 12» is not an answer to
/// "which button is that". This table gives every picture the name a player would use, so a button
/// can be found and named by what it does instead of by a number.
///
/// Entries are added only after the button has been pressed in play and its effect seen. A picture
/// nobody has identified stays absent rather than being guessed at.
internal static class UiAssets
{
    private static readonly Dictionary<string,string> Names=new(StringComparer.OrdinalIgnoreCase)
    {
        // Dialog answers.
        ["iokay.def"]="ОК",
        ["icancel.def"]="Отмена",
        ["ICN6432.def"]="Отмена",
        // Main menu.
        ["mmenung.def"]="Новая игра",
        ["mmenulg.def"]="Загрузить игру",
        ["mmenuhs.def"]="Рекорды",
        ["mmenucr.def"]="Создатели",
        ["mmenuqt.def"]="Выход из игры",
        // Game type.
        ["gtsingl.def"]="Одиночный сценарий",
        ["gtmulti.def"]="Многопользовательская игра",
        ["gtcampn.def"]="Кампания",
        ["gttutor.def"]="Обучение",
        ["gtback.def"]="Назад",
        // Scenario selection.
        ["scnrbeg.def"]="НАЧАТЬ — запустить выбранный сценарий",
        ["scnrback.def"]="ВЫЙТИ из выбора сценария",
        ["scnrsav.def"]="Сохранить игру",
        ["scnrlod.def"]="Загрузить игру",
        // Tavern.
        ["TPTav01.def"]="Нанять выбранного героя",
        ["TPTav02.def"]="Гильдия Воров",
        // Adventure map sidebar.
        ["iam001.def"]="Закончить ход",
        ["iam014.def"]="Обмен армиями между рядами",
        ["iam015.def"]="Выход из города",
        // Town screen.
        ["tsbtns.def"]="Кнопка ряда города",
        ["townhrtd.def"]="Знамя гарнизона",
        // Creature card.
        ["iBUY30.def"]="Купить",
        // Combat bar.
        ["icm001.def"]="Настройки боя",
        ["icm002.def"]="Сдаться",
        ["icm003.def"]="Отступить",
        ["icm004.def"]="Автобой",
        ["icm005.def"]="Книга заклинаний",
        ["icm006.def"]="Ждать",
        ["icm007.def"]="Защищаться",
        ["icm011.def"]="Настройки битвы",
        ["icm012.def"]="Начать бой (тактическая фаза)",
        // Hero exchange: the picture names say what each button does.
        ["SwCMR.def"]="Переместить все войска правому герою",
        ["SwCML.def"]="Переместить все войска левому герою",
        ["SwAMR_M.def"]="Переместить все артефакты правому герою",
        ["SwAML_M.def"]="Переместить все артефакты левому герою",
        ["SwXCh.def"]="Обменять армии героев местами",
        ["mov1rm.def"]="Передать одно существо правому герою",
        ["mov1lm.def"]="Передать одно существо левому герою",
        // Random map panel.
        ["RanWeak.def"]="Слабые монстры",
        ["RanNorm.def"]="Монстры обычной силы",
        ["RanStrg.def"]="Сильные монстры",
        ["RanRand.def"]="Случайно",
    };

    /// The name of the picture a control draws, or null when that picture has not been identified
    /// in play yet. A missing name is an honest gap, not a reason to invent one.
    public static string? Name(string? asset)=>
        asset is not null&&Names.TryGetValue(asset,out var name)?name:null;
}
