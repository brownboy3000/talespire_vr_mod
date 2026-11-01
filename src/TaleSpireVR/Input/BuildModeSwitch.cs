using Bounce.Singletons;

namespace TaleSpireVR
{
    /// <summary>
    /// Build Mode on / off for the mod's controls: the left stick click and the hotkey row's Build Mode and Build Grid
    /// buttons. They don't send TaleSpire's B key, which can silently do nothing:
    ///  - TaleSpire ignores all its hotkeys while a text box has focus (one clicked in the menu - the library's search,
    ///    say - stays focused after you look away), so here typing ends first (VRKeyboard.Done);
    ///  - its toggle only works from its two plain tools: with a slab, tile or prop still on the pointer, or after
    ///    another build tool (selection, picking up), B does nothing. Here what is on the pointer is put down (each
    ///    tool's own Back, as A / right-click does) and Build Mode is left or entered from the plain tool.
    /// </summary>
    internal static class BuildModeSwitch
    {
        public static void Toggle(string why) => Set(!BoardBuilder.InBuilderMode, why);

        public static void Set(bool on, string why)
        {
            try
            {
                if (!BoardSessionManager.InBoard || BoardSessionManager.IsLoading || !LocalClient.IsInGmMode
                    || !SingletonBehaviour<BoardToolManager>.HasInstance) return;
                if (DiceHand.InHand) { Plugin.Log.LogInfo($"Build Mode ({why}): not while dice are in hand"); return; }
                VRKeyboard.Done();
                if (on == BoardBuilder.InBuilderMode) return;
                var tools = SingletonBehaviour<BoardToolManager>.Instance;
                if (on)
                {
                    // TaleSpire enters it from its plain board tool (the ruler, a creature's menu... step back first).
                    if (!tools.GetTool<DefaultBoardTool>().IsActiveTool) tools.SwitchToTool<DefaultBoardTool>();
                    tools.SwitchToTool<DefaultBuildingTool>();
                }
                else
                {
                    // Back out of whatever build tool is on (dropping what it holds) to the plain one, which leaves.
                    for (int i = 0; i < 4 && BoardBuilder.InBuilderMode && !(BoardToolManager.CurrentTool is DefaultBuildingTool); i++)
                        BoardToolManager.CurrentTool?.Back();
                    if (BoardToolManager.CurrentTool is DefaultBuildingTool plain) plain.Back();
                    if (BoardBuilder.InBuilderMode) tools.SwitchToTool<DefaultBoardTool>();
                }
                Plugin.Log.LogInfo($"Build Mode {(BoardBuilder.InBuilderMode ? "on" : "off")} ({why}; tool {BoardToolManager.CurrentTool?.GetType().Name})");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Build Mode ({why}) failed: " + e.Message); }
        }
    }
}
