using Bounce.Singletons;

namespace TaleSpireVR
{
    /// <summary>
    /// What you may do and see, by TaleSpire's own rules: Game Master or player, which creatures you control, whose
    /// turn it is in turn-based mode, cutscenes, and which creatures are hidden. The mod asks these before anything that
    /// acts on the board, so it never lets you do more than TaleSpire itself would.
    /// </summary>
    internal static class Roles
    {
        /// <summary>Debug ("roles player|off"): act as a player who controls every creature (to try turn locks alone).</summary>
        public static bool DebugAsPlayer;

        /// <summary>In Game Master mode (TaleSpire's role switch; a player who joined a campaign never is).</summary>
        public static bool IsGm
        {
            get { if (DebugAsPlayer) return false; try { return LocalClient.IsInGmMode; } catch { return false; } }
        }

        public static bool OnBoard
        {
            get { try { return BoardSessionManager.InBoard; } catch { return false; } }
        }

        /// <summary>A Game Master, or not on a board at all (the menus): Game Master features may show.</summary>
        public static bool GmOrOffBoard => !OnBoard || IsGm;

        /// <summary>TaleSpire lets you control this creature (a Game Master controls all of them).</summary>
        public static bool CanControl(CreatureBoardAsset c)
        {
            if (c == null) return false;
            if (DebugAsPlayer) return true;
            try { return LocalClient.CanControlCreature(c.CreatureId); }
            catch { return false; }
        }

        private static PlayMode.State State
        {
            get
            {
                try { return SingletonStateMBehaviour<PlayMode, PlayMode.State>.TryGetInstance(out var pm) ? pm.CurrentState : null; }
                catch { return null; }
            }
        }

        public static bool TurnBased
        {
            get { var s = State; return s != null && s.Is<PlayMode.TurnBased>(); }
        }

        /// <summary>The Game Master is showing a cutscene (TaleSpire takes players' cameras over).</summary>
        public static bool Cutscene
        {
            get
            {
                var s = State;
                try { return s != null && s.Is<PlayMode.Cutscene>() && CutsceneManager.CutsceneFocus.IsValid; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Turn-based mode lets this creature act now: always for a Game Master, outside turn-based mode, or before the
        /// first turn; otherwise only on this creature's own turn.
        /// </summary>
        public static bool TurnAllows(CreatureBoardAsset c)
        {
            if (c == null) return false;
            if (IsGm || !TurnBased) return true;
            try
            {
                var cur = InitiativeManager.CurrentTurnElement;
                if (cur == default(QueueElement)) return true;
                return cur.Type == QueueElement.ElementType.Creature && cur.CreatureGuid == c.CreatureId;
            }
            catch { return false; }
        }

        /// <summary>This creature may move and attack now: you control it, it is its turn, and no cutscene holds you.</summary>
        public static bool CanAct(CreatureBoardAsset c) => CanControl(c) && TurnAllows(c) && (IsGm || !Cutscene);

        /// <summary>
        /// Shown to you: not hidden by the Game Master (even for the Game Master, who sees hidden creatures faintly) and
        /// visible by TaleSpire's line of sight / fog rules for players.
        /// </summary>
        public static bool CanSee(CreatureBoardAsset c)
        {
            if (c == null) return false;
            try { return !c.IsExplicitlyHidden && c.IsVisible; }
            catch { return false; }
        }
    }
}
