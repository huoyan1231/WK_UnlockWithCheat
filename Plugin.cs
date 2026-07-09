using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using WK_huoyan1231COMLib;

namespace WK_UnlockWithCheat
{
    // ====================================================================
    //  WK_UnlockWithCheat
    //
    //  Goal of this mod:
    //  1) When cheat mode is active (CommandConsole.hasCheated == true), the
    //     game still lets you UNLOCK NEW CONTENT (achievements / progression /
    //     XP). The game normally suppresses saving your progress while cheating
    //     (StatManager.SaveStats early-returns when hasCheated), so unlocks would
    //     be lost after quitting. This mod keeps that progression persisted.
    //  2) While this mod is loaded we must NOT pollute the real leaderboard or
    //     the player's real Steam achievements. We delegate that to the shared
    //     WK_huoyan1231COMLib which intercepts the single Steam-achievement upload
    //     entry point (CL_AchievementManager.GameAchievement.GetSteamAchievement)
    //     and the leaderboard gate (CL_Leaderboard.WK_Leaderboard_Core.disableLeaderboards).
    //     In-game flagged state / XP / unlock animations are preserved; only the
    //     external upload is blocked.
    //
    //  Content-access gate (verified against the installed Assembly-CSharp.dll):
    //  ENV_WarpFissure.CheckCheated() returns true while cheating, which makes
    //  OpenFissure() early-return and show "Huh? looks like you cheated or
    //  something?" — so you can NEVER open a warp fissure / progress to new
    //  areas while cheats are on. Patch C bypasses that gate so content/levels
    //  stay reachable. (This is the real reason the mod previously felt
    //  "ineffective": the save persisted, but you couldn't open the fissure to
    //  get there.)
    // ====================================================================
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("huoyan1231.whiteknuckle.comlib", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        // Mirrors the config so the Harmony patch can read it without an instance reference.
        internal static bool EnableUnlockWhileCheating = true;

        private ConfigEntry<bool> _allowUnlockWhileCheating;

        private void Awake()
        {
            Logger = base.Logger;

            _allowUnlockWhileCheating = Config.Bind(
                "General",
                "AllowUnlockWhileCheating",
                true,
                "When true, content unlocks (achievements / progression / XP) persist even while cheat mode is active. " +
                "Leaderboard uploads and Steam achievement uploads are always disabled by this mod regardless of this setting."
            );
            EnableUnlockWhileCheating = _allowUnlockWhileCheating.Value;

            // --- Integrate with the shared COM library ---
            // Never upload scores to the leaderboard while this mod is active.
            LeaderboardManager.DisableForThisRun(MyPluginInfo.PLUGIN_GUID);
            // Disable Steam achievement uploads unconditionally (keeps in-game progress).
            AchievementManager.SetSteamAchievementsDisabled(true);
            AchievementManager.DisableForThisRun(MyPluginInfo.PLUGIN_GUID);

            Logger.LogInfo(
                $"Plugin {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} loaded. " +
                "Leaderboard & Steam achievement uploads disabled via WK_huoyan1231COMLib. " +
                $"UnlockWhileCheating={(EnableUnlockWhileCheating ? "ON" : "OFF")}."
            );

            // --- Apply Harmony patches ---
            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            Logger.LogInfo("Harmony patches applied.");
        }
    }

    // ====================================================================
    //  Patch A: StatManager.SaveStats — keep progression while cheating
    //
    //  Vanilla behaviour (StatManager.cs ~L85):
    //      if (CommandConsole.hasCheated && (!allowCheatedScores)) return;
    //  This drops the entire save, so anything unlocked while cheating is lost
    //  after the run/quit. We temporarily clear hasCheated around the call so the
    //  save proceeds, then restore it so the rest of the game (leaderboard gate in
    //  M_Gamemode.Finish, the on-screen cheat tracker, etc.) still sees the truth.
    //  Steam / leaderboard pollution is separately prevented by WK_huoyan1231COMLib.
    // ====================================================================
    [HarmonyPatch(typeof(StatManager), "SaveStats")]
    internal static class Patch_StatManager_SaveStats
    {
        static void Prefix(out bool __state)
        {
            __state = false;
            if (!Plugin.EnableUnlockWhileCheating)
            {
                return;
            }
            if (CommandConsole.hasCheated)
            {
                __state = true;
                CommandConsole.hasCheated = false;
            }
        }

        static void Postfix(bool __state)
        {
            if (__state)
            {
                CommandConsole.hasCheated = true;
            }
        }
    }

    // ====================================================================
    //  Patch B: M_Gamemode.StartFreshGamemode — re-assert per-run disabling
    //
    //  WK_huoyan1231COMLib clears its per-run disable requests at the end of each
    //  run (M_Gamemode.Finish -> ResetAll). Re-register our requests every time a
    //  fresh gamemode starts so leaderboards / Steam uploads stay blocked for the
    //  whole session. The global Steam hard-switch is also set in Awake and never
    //  cleared, but we register the per-run request for consistency / coordination.
    // ====================================================================
    [HarmonyPatch(typeof(M_Gamemode), "StartFreshGamemode")]
    internal static class Patch_M_Gamemode_StartFreshGamemode
    {
        static void Postfix()
        {
            LeaderboardManager.DisableForThisRun(MyPluginInfo.PLUGIN_GUID);
            AchievementManager.DisableForThisRun(MyPluginInfo.PLUGIN_GUID);
        }
    }

    // ====================================================================
    //  Patch C: ENV_WarpFissure.CheckCheated — keep fissures openable while cheating
    //
    //  Vanilla behaviour (ENV_WarpFissure.cs L140-148):
    //      if (CommandConsole.hasCheated && this.blockCheats) {
    //          tipHeader.ShowText("Huh? looks like you cheated or something?");
    //          return true;
    //      }
    //      return false;
    //  OpenFissure() calls CheckCheated() and early-returns when it is true, so
    //  while cheats are on you can never open a warp fissure and therefore can
    //  never reach new areas / ladder levels. We force the result to false (and
    //  skip the original) when AllowUnlockWhileCheating is on, so the fissure
    //  opens normally. hasCheated itself stays true, so the leaderboard gate,
    //  cheat tracker HUD, and Steam handling are still driven by COMLib.
    // ====================================================================
    [HarmonyPatch(typeof(ENV_WarpFissure), "CheckCheated")]
    internal static class Patch_ENV_WarpFissure_CheckCheated
    {
        static bool Prefix(ref bool __result)
        {
            if (!Plugin.EnableUnlockWhileCheating)
            {
                return true; // run original (vanilla behaviour)
            }
            __result = false; // pretend we didn't cheat -> fissure opens
            return false;     // skip original method
        }
    }
}
