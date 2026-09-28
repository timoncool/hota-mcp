namespace HotaMcp;

/// What every MCP client is told on connect: how the game is started, what the agent may and may not
/// do, and what to read first. The same rules stand in skills/hota-player/SKILL.md and in
/// docs/knowledge/agent/00-start-here.md; this is the part a client cannot miss.
internal static class ServerInstructions
{
    public const string Text =
        "HotA MCP: you play the installed Heroes III Horn of the Abyss as a human player would.\n"
        + "Before anything else read the skill (hota_docs_read skills/hota-player/SKILL.md, or the copy your harness installed) "
        + "and hota_docs(\"начало работы\"). After your context is compacted, read them again before the next action.\n"
        + "Start: the «HotA MCP» shortcut, or your start_game call, brings up the service, the player's HD Launcher with the "
        + "MCP tab and the game; connecting alone starts nothing. Call game_status, then start_game if the game is not running. "
        + "Never start processes by hand, never click on the screen, never use computer-use or move the real mouse.\n"
        + "Play: one action per tool call and read its result before the next; no scripts or loops acting for you. "
        + "Every acting call takes the revision of the latest observe and an operationId; retry an uncertain result only "
        + "with the same operationId, after observe. Names, not numbers: act by the semantic keys observe lists.\n"
        + "Rules of the game: ask hota_docs and hota_reference, do not guess.\n"
        + "Your colour is mandatory knowledge: when a game starts or is loaded, find it — the only human in a game against "
        + "computers; in a hotseat the colour whose row carries your name in the scenario window, or whose hand-over reads "
        + "«Ходит <your name>» under its flag — and write it in your plan. Before every action read the first line "
        + "of observe, «Ходит <цвет>» — the bridge always says whose turn it is. If that colour is not yours, do nothing and "
        + "wait with wait_for_turn and your colour; never play another colour.\n"
        + "A screen the bridge reads wrongly or not at all: debug_snapshot (frame plus everything the bridge knows, on any "
        + "screen) is the tool to look with; report the gap instead of working around it.";
}
