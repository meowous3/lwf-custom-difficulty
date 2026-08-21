using BaseSystem;
using GameState;
using GameState.StaticStateHolders;
using HarmonyLib;
using Unlocks;
using Utility.SaveData;

namespace LwfCustomDifficulty.Patches
{
    /// <summary>
    /// Keeps a Custom run out of the save file.
    ///
    /// <see cref="Utility.SaveData.GameData.AddClearedDifficulty"/> opens with the game's
    /// own guard, <c>if (!BuildContentPolicy.IsDifficultyIncluded(difficulty)) return
    /// false;</c>, which would reject an id vanilla does not contain. This plugin patches
    /// <c>IsDifficultyIncluded</c> to accept Custom — that is what makes the difficulty
    /// selectable — and in doing so defeats that guard. These two patches restore it for
    /// Custom alone, leaving the difficulty selectable everywhere else.
    ///
    /// Two layers for that method, because <see cref="Unlocks.DifficultyUnlockManager.OnWin"/>
    /// performs three writes, not one: the cleared difficulty, the pending unlock notice,
    /// and the last-played difficulty for the mode. Guarding only the cleared-difficulty
    /// call would leave the other two reachable.
    ///
    /// A third patch covers <c>GameData.AddClearedAdvPatronDifficulty</c>, which is the
    /// same defect reached through a different method: it opens with the same
    /// <c>IsDifficultyIncluded</c> guard, and <see cref="ClampDifficultyPatch"/> already
    /// carries Custom through its <c>SanitizeDifficulty</c> call unchanged.
    ///
    /// No target is at risk from Mono's inliner. Measured over the shipped
    /// Assembly-CSharp: <c>DifficultyUnlockManager.OnWin</c> 87 IL bytes,
    /// <c>AddClearedDifficulty</c> 81, <c>AddClearedAdvPatronDifficulty</c> 166 — all far
    /// above the ~20-byte threshold.
    /// </summary>
    internal static class SaveGuard
    {
        internal static void LogOnce(ref bool logged, string message)
        {
            if (logged) return;
            logged = true;
            Plugin.Log.LogInfo("Save guard: " + message);
        }
    }

    /// <summary>
    /// Layer one — skip the whole progression handler.
    ///
    /// All three of OnWin's writes live behind this prefix. It also skips the method's
    /// opening <c>BuildContentPolicy.ThrowIfDifficultyUnavailable</c>, which is harmless:
    /// that call passes for Custom anyway while <c>IsDifficultyIncluded</c> is patched.
    /// </summary>
    [HarmonyPatch(typeof(DifficultyUnlockManager), nameof(DifficultyUnlockManager.OnWin))]
    internal static class DifficultyUnlockManagerOnWinGuard
    {
        private static bool _logged;

        private static bool Prefix()
        {
            var difficulty = CurrentDifficulty.Get;
            if (!CustomDifficulty.IsCustom(difficulty)) return true;

            SaveGuard.LogOnce(ref _logged,
                $"skipped DifficultyUnlockManager.OnWin; CurrentDifficulty.Get={(int)difficulty}, "
                + $"GameMode={CurrentGameMode.Get}. No cleared difficulty, unlock notice or "
                + "last-played difficulty was written.");
            return false;
        }
    }

    /// <summary>
    /// Layer two — the choke point.
    ///
    /// Every route to the cleared-difficulty field passes here, including callers neither
    /// traced nor foreseen. <c>false</c> is the value vanilla returns when it rejects a
    /// difficulty, so callers branching on the result behave as they would unmodded.
    /// </summary>
    [HarmonyPatch(typeof(GameData), nameof(GameData.AddClearedDifficulty))]
    internal static class GameDataAddClearedDifficultyGuard
    {
        private static bool _logged;

        private static bool Prefix(Difficulty difficulty, ref bool __result)
        {
            if (!CustomDifficulty.IsCustom(difficulty)) return true;

            __result = false;
            SaveGuard.LogOnce(ref _logged,
                $"rejected GameData.AddClearedDifficulty(difficulty={(int)difficulty}); __result={__result}.");
            return false;
        }
    }

    /// <summary>
    /// The same defect in a second method, and the reason a choke-point patch alone is
    /// the right shape here.
    ///
    /// <see cref="Adventure.AdvGameProgressRecorder.OnWin"/> calls this once per selected
    /// patron on every non-tutorial win, so on a fresh profile a won Custom run appends
    /// three <c>(patron, (Difficulty)(-100))</c> pairs. That entry sanitises down to
    /// NewCustomer on the first mod-free load, so it needs no repair — but until then it
    /// is a patron clear the player never earned, feeding
    /// <c>IsAdvPatronDifficultyCleared</c> and the adventure episode unlock conditions
    /// through it.
    ///
    /// Deliberately NOT mirrored with a prefix on the recorder's own <c>OnWin</c>, unlike
    /// <see cref="DifficultyUnlockManagerOnWinGuard"/> — but only because
    /// <see cref="CustomRunScope"/> now covers that method's other writes wholesale, not
    /// because they are wanted.
    ///
    /// An earlier version of this comment argued that its
    /// <c>AddClearedInitialBiome(initialPointData.biomeType)</c> write carried no difficulty,
    /// recorded something the player genuinely did, and had to be preserved. That reasoning
    /// was wrong, and it was wrong in a way worth keeping written down: nothing done in a
    /// Custom run is earned, because the run's own settings decide what winning takes. One
    /// repayment of one coin is a legal configuration, so a Custom biome clear is a click,
    /// not an achievement. The remaining two writes are unreachable regardless —
    /// <c>AddClearedInfernoWithAllTaxes</c> is gated on <c>DifficultyRules.IsInferno</c>,
    /// which Custom's negative id fails, and <c>AddClearedReckoningDifficulty</c> is keyed
    /// to a game mode a Custom run is never in.
    /// </summary>
    [HarmonyPatch(typeof(GameData), nameof(GameData.AddClearedAdvPatronDifficulty))]
    internal static class GameDataAddClearedAdvPatronDifficultyGuard
    {
        private static bool _logged;

        private static bool Prefix(Patron patron, Difficulty difficulty, ref bool __result)
        {
            if (!CustomDifficulty.IsCustom(difficulty)) return true;

            __result = false;
            SaveGuard.LogOnce(ref _logged,
                $"rejected GameData.AddClearedAdvPatronDifficulty(patron={patron}, "
                + $"difficulty={(int)difficulty}); __result={__result}. Applies to every "
                + "patron of this run; logged once.");
            return false;
        }
    }
}
