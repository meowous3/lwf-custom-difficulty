using System;
using System.Reflection;
using GameState;
using GameState.StaticStateHolders;
using HarmonyLib;
using Tax;
using UI;
using Unlocks;
using UnityEngine;

namespace LwfCustomDifficulty.Patches
{
    /// <summary>
    /// The members a Custom run writes into <see cref="WinCondition"/>.
    ///
    /// Resolved once. Every load-bearing value is written to the field, never left to a
    /// patch on the method that computes it: <c>CalcNormaStart</c> (15 IL bytes) and
    /// <c>CalcTargetProgress</c> (12) are both inside Mono's ~20-byte inline window, and a
    /// patch on <c>CalcTargetProgress</c> was previously applied and never reached.
    /// </summary>
    internal static class WinConditionMembers
    {
        internal static readonly FieldInfo TimeLimit =
            AccessTools.Field(typeof(WinCondition), "_timeLimit");

        internal static readonly FieldInfo ElapsedTime =
            AccessTools.Field(typeof(WinCondition), "_elapsedTime");

        internal static readonly FieldInfo TargetCount =
            AccessTools.Field(typeof(WinCondition), "_targetCount");

        internal static readonly MethodInfo TargetProgressSetter =
            AccessTools.PropertySetter(typeof(WinCondition), nameof(WinCondition.TargetProgress));

        /// <summary>Rebuilds <c>_targetRequirement</c>, the cache that <c>GetTargetCount</c>
        /// actually answers from. Invoked, not patched, so its size is irrelevant.</summary>
        internal static readonly MethodInfo RefreshTargetRequirement =
            AccessTools.Method(typeof(WinCondition), "RefreshTargetRequirement");

        internal static bool Resolved =>
            TimeLimit != null && ElapsedTime != null && TargetCount != null
            && TargetProgressSetter != null && RefreshTargetRequirement != null;

        /// <summary>The condition the constructor postfix seeded, or null if the live run is
        /// not Custom. Identity, rather than a reflected read of <c>_difficulty</c>, because
        /// <c>IsTimeOver</c> is on the frame path and <c>FieldInfo.GetValue</c> would box a
        /// <c>Difficulty</c> on every frame of every run.</summary>
        internal static WinCondition CustomRun;

        internal static bool IsCustomRun(WinCondition instance) =>
            instance != null && ReferenceEquals(instance, CustomRun);
    }

    /// <summary>
    /// Seeds a Custom run: time limit, repayment count and the first repayment's demand.
    ///
    /// The constructor's own body ends in <c>SetWinCondition</c>, which calls
    /// <c>RefreshTargetRequirement()</c> — so by the time this postfix runs the game has
    /// already cached a <c>TagCount("Cash", vanillaTargetCount)</c> in
    /// <c>_targetRequirement</c>. <c>GetTargetCount()</c> returns
    /// <c>_targetRequirement?.Count ?? _targetCount</c> and the provider never returns null,
    /// so writing <c>_targetCount</c> alone would leave the game demanding the vanilla first
    /// norma. The refresh is re-run here, after every write, to rebuild that cache.
    ///
    /// The ctor is reached at all because <c>IsDifficultyIncluded</c> is already patched to
    /// accept the Custom id; its two guards — <c>difficulty &gt; Hell1</c> and
    /// <c>ThrowIfDifficultyUnavailable</c> — therefore both pass for a negative id.
    /// </summary>
    [HarmonyPatch(typeof(WinCondition), MethodType.Constructor, new[] { typeof(Difficulty) })]
    internal static class WinConditionCtorPatch
    {
        private static void Postfix(WinCondition __instance, Difficulty difficulty)
        {
            // Cleared for a vanilla run so a Custom run followed by a vanilla one in the same
            // session cannot leave the later run matching on a stale instance.
            WinConditionMembers.CustomRun = null;
            IsTimeOverPatch.ResetProbe();

            if (!CustomDifficulty.IsCustom(difficulty)) return;

            if (!WinConditionMembers.Resolved)
            {
                // Not a benign fallback. There are no vanilla rules for the Custom id, and the
                // run is over before it starts: TargetProgress falls back to
                // CalcTargetProgress(-100) = -95, so IsWon() — `CurrentProgress >=
                // TargetProgress` — is `0 >= -95`, true at the first CheckWinCondition.
                //
                // This is *not* the "drains every repayment in one frame" shape of the growth
                // defect, where TargetProgress was written correctly and only _targetCount
                // went negative. Here AddProgress cannot advance at all: its guard is
                // `TargetProgress > CurrentProgress`, i.e. `-95 > 0`, false. The win lands
                // with zero repayments completed.
                Plugin.Log?.LogError(
                    "Custom: WinCondition members unresolved — the build has drifted. Rules NOT "
                    + "applied. TargetProgress falls back to CalcTargetProgress(-100) = -95, so "
                    + "IsWon() is already 0 >= -95: this run is won before a single repayment "
                    + "completes, and AddProgress can never advance it. Abandon the run and "
                    + "re-verify the field names.");
                return;
            }

            WinConditionMembers.CustomRun = __instance;

            var minutes = PluginConfig.TimeLimitMinutes;

            WinConditionMembers.TimeLimit.SetValue(__instance, minutes * 60f);

            // "No time limit" is a direct field write rather than a patch on IsTimeOver,
            // which is 18 IL bytes — inside the inline window and unsafe to depend on.
            // IsTimeOver is `_elapsedTime >= _timeLimit`, and with _timeLimit left at 0 a
            // fresh _elapsedTime of 0 already satisfies it, so the run would be lost on its
            // first frame. Negative infinity is absorbing under the only write the field
            // ever receives (UpdateElapsedTime's `+=`), so the comparison stays false for
            // the whole run whether or not the patch below is reached. The clock is
            // unaffected: Timer counts its own ElapsedGameTime and reads only GetTimeLimit,
            // which stays 0 and formats as 00:00. WinCondition.GetElapsedTime has no callers
            // anywhere in the assembly, and SetRemainingTime is reached only from
            // GameStateManager.DebugSetRemainingTime.
            if (minutes == 0)
            {
                WinConditionMembers.ElapsedTime.SetValue(__instance, float.NegativeInfinity);
            }

            WinConditionMembers.TargetProgressSetter.Invoke(
                __instance, new object[] { PluginConfig.Repayments });
            WinConditionMembers.TargetCount.SetValue(__instance, PluginConfig.Rules.FirstRepayment);
            WinConditionMembers.RefreshTargetRequirement.Invoke(__instance, null);

            // Read back off the instance, through the same accessors the game uses, so an
            // inlining failure or a stale requirement cache shows up here as a vanilla number.
            Plugin.Log?.LogInfo(
                $"Custom: timeLimit={__instance.GetTimeLimit() / 60f}m "
                + $"repayments={__instance.TargetProgress} "
                + $"firstRepayment={__instance.GetTargetCount()} "
                + $"elapsed={WinConditionMembers.ElapsedTime.GetValue(__instance)}");
        }
    }

    /// <summary>
    /// Applies the growth curve after each repayment.
    ///
    /// <c>RefreshTargetCount</c> is 73 IL bytes, comfortably outside Mono's ~20-byte inline
    /// window, so the patch is reached. Its sole caller is <c>AddProgress</c>.
    ///
    /// The prefix captures <c>_targetCount</c> *before* the vanilla body runs. Reading it in
    /// the postfix instead would not discard the vanilla increment but compound it: the body
    /// adds <c>(int)(progressDifficulty + 1) * 10 + 10</c>, which for the Custom id of -100
    /// is -980, and the curve would then be applied to that already-corrupted value.
    ///
    /// <c>AddProgress</c> calls <c>RefreshTargetRequirement()</c> immediately after this
    /// method returns, so unlike the constructor no explicit refresh is needed here.
    /// </summary>
    [HarmonyPatch(typeof(WinCondition), "RefreshTargetCount")]
    internal static class RefreshTargetCountPatch
    {
        /// <summary>Unconditional: guarding it as well would risk the two guards disagreeing
        /// and leaving the postfix with a __state of 0.</summary>
        private static void Prefix(WinCondition __instance, out int __state)
        {
            __state = WinConditionMembers.TargetCount != null
                ? (int)WinConditionMembers.TargetCount.GetValue(__instance)
                : 0;
        }

        /// <param name="__runOriginal">False when another mod's prefix skipped the original —
        /// and with it this class's own prefix, leaving <c>__state</c> at its default 0. Acting
        /// on that would collapse the target to the bare growth amount.</param>
        private static void Postfix(WinCondition __instance, int __state, bool __runOriginal)
        {
            if (!__runOriginal) return;
            if (!WinConditionMembers.Resolved) return;
            if (!WinConditionMembers.IsCustomRun(__instance)) return;

            // 1-based, as NextTargetCount requires: AddProgress increments CurrentProgress
            // before calling this method, so it reads 1 after the first repayment.
            var repaymentIndex = __instance.CurrentProgress;

            WinConditionMembers.TargetCount.SetValue(
                __instance, PluginConfig.Rules.NextTargetCount(__state, repaymentIndex));

            Plugin.Log?.LogInfo(
                $"Custom: repayment={repaymentIndex} "
                + $"targetCount={WinConditionMembers.TargetCount.GetValue(__instance)}");
        }
    }

    /// <summary>
    /// Second line of defence for an unlimited run; the constructor's <c>_elapsedTime</c>
    /// write is what actually carries it.
    ///
    /// <c>IsTimeOver</c> is 18 IL bytes against Mono's ~20-byte threshold, so whether this
    /// postfix is reached at all is a coin-flip. The one-shot log line settles it from a real
    /// run instead of by argument, and costs one bool on every other frame.
    /// </summary>
    [HarmonyPatch(typeof(WinCondition), nameof(WinCondition.IsTimeOver))]
    internal static class IsTimeOverPatch
    {
        private static bool _logged;

        /// <summary>Re-arms the probe for each new run, so a second attempt in the same
        /// session still reports.</summary>
        internal static void ResetProbe() => _logged = false;

        private static void Postfix(WinCondition __instance, ref bool __result)
        {
            // Identity first: this runs on the frame path, and TimeLimitMinutes is a config
            // read plus a Clamp. The cheap reference comparison rejects every vanilla run.
            if (!WinConditionMembers.IsCustomRun(__instance)) return;
            if (PluginConfig.TimeLimitMinutes != 0) return;

            __result = false;

            if (_logged) return;
            _logged = true;
            Plugin.Log?.LogInfo("Custom: IsTimeOver postfix reached, result=False");
        }
    }

    /// <summary>
    /// The Taxes toggle.
    ///
    /// This two-argument overload is the only one carrying logic — 24 IL bytes, outside the
    /// inline window. Vanilla answers <c>IsUnlocked &amp;&amp; IsTaxGameMode(mode) &amp;&amp;
    /// IsInferno(difficulty)</c>, and the Custom id is far below Inferno1, so without this
    /// the toggle could never turn taxes on.
    /// </summary>
    [HarmonyPatch(typeof(TaxUnlockPolicy), nameof(TaxUnlockPolicy.CanShowWindow),
                  new[] { typeof(GameMode), typeof(Difficulty) })]
    internal static class CanShowWindowPatch
    {
        private static void Postfix(GameMode gameMode, Difficulty difficulty, ref bool __result)
        {
            if (!CustomDifficulty.IsCustom(difficulty)) return;
            if (!TaxPolicy.IsTaxGameMode(gameMode)) return;
            __result = PluginConfig.TaxesEnabled;
        }
    }

    /// <summary>
    /// The runtime half of the Taxes toggle, without which switching taxes off would hide the
    /// window and leave the effects running.
    ///
    /// The real signature takes <c>GameMode</c> alone — it does not mirror
    /// <c>CanShowWindow</c>'s two arguments — so the difficulty is read from
    /// <c>CurrentDifficulty</c> here, exactly as the target's own body does.
    ///
    /// The method is 12 IL bytes and may well be inlined into its three callers in
    /// <c>TaxRuntimeController</c>, but the toggle holds either way: its entire body is
    /// <c>return CanShowWindow(gameMode, CurrentDifficulty.Get);</c>, and an inlined copy
    /// still calls the 24-byte overload patched above. This patch is the belt to that brace,
    /// and agrees with it by construction.
    /// </summary>
    [HarmonyPatch(typeof(TaxUnlockPolicy), nameof(TaxUnlockPolicy.CanApplyRuntimeEffects),
                  new[] { typeof(GameMode) })]
    internal static class CanApplyRuntimeEffectsPatch
    {
        private static void Postfix(GameMode gameMode, ref bool __result)
        {
            if (!CustomDifficulty.IsCustom(CurrentDifficulty.Get)) return;
            if (!TaxPolicy.IsTaxGameMode(gameMode)) return;
            __result = PluginConfig.TaxesEnabled;
        }
    }

    /// <summary>
    /// Mirrors <c>TaxUnlockPolicy.IsTaxGameMode</c>, which is private.
    ///
    /// Both tax postfixes force <c>__result</c> for the Custom id, and vanilla gates every
    /// tax answer on this test as well. Without it the toggle would make taxes reachable in
    /// modes the game excludes outright — <c>TutorialGame</c> among them — which is a
    /// behaviour the difficulty was never meant to unlock.
    /// </summary>
    internal static class TaxPolicy
    {
        internal static bool IsTaxGameMode(GameMode gameMode) =>
            gameMode == GameMode.NormalGame || gameMode == GameMode.Reckoning;
    }

    /// <summary>
    /// Bounds what the repayment indicator builds, so a large <c>Repayments</c> cannot hang
    /// the run.
    ///
    /// <c>GameStateManager.Initialize</c> calls <c>Initialize(_winCondition.TargetProgress)</c>,
    /// and the method <c>Object.Instantiate</c>s one <c>Image</c> per repayment into a
    /// <c>GridLayoutGroup</c>. At the configuration ceiling of 536,870,911 that is an
    /// unbounded allocation loop on run start.
    ///
    /// The cap is applied to the **indicator**, not to <c>Repayments</c> itself: the win
    /// condition stays exactly what the player configured, the completion readout still
    /// counts to the true total, and only the row of dots stops growing. Vanilla's own
    /// maximum is <c>CalcTargetProgress(Hell1)</c> = 15, so 200 is more than an order of
    /// magnitude above anything the game produces unaided while staying instant to build.
    /// </summary>
    [HarmonyPatch(typeof(ProgressIndicatorUIManager), nameof(ProgressIndicatorUIManager.Initialize))]
    internal static class ProgressIndicatorInitializePatch
    {
        internal const int MaxIndicators = 200;

        private static void Prefix(ref int countProgress)
        {
            if (!CustomDifficulty.IsCustom(CurrentDifficulty.Get)) return;
            if (countProgress <= MaxIndicators) return;

            Plugin.Log?.LogInfo(
                $"Custom: indicator capped at {MaxIndicators} of {countProgress} repayments.");
            countProgress = MaxIndicators;
        }
    }

    /// <summary>
    /// The same bound for the sprite array.
    ///
    /// <c>GetRemainingRequirementSprites</c> allocates
    /// <c>new Sprite[TargetProgress - CurrentProgress]</c> and fills it one
    /// <c>CreateRequirement</c> call per element, on every <c>UpdateNormaText</c> — which
    /// runs at run start and after each repayment, lease and win. At the ceiling that array
    /// alone is several gigabytes.
    ///
    /// Below the cap the vanilla path is left completely alone, so ordinary Custom runs keep
    /// their per-repayment sprites. Above it the array is dropped: the consumer,
    /// <c>ProgressIndicatorUIManager.SetSprites</c>, returns immediately on an empty list and
    /// leaves the dots on the default sprite, and it would in any case only read
    /// <c>Mathf.Min(_images.Count, …)</c> entries — at most the cap above.
    /// </summary>
    [HarmonyPatch(typeof(WinCondition), nameof(WinCondition.GetRemainingRequirementSprites))]
    internal static class GetRemainingRequirementSpritesPatch
    {
        private static bool Prefix(WinCondition __instance, ref Sprite[] __result)
        {
            if (!WinConditionMembers.IsCustomRun(__instance)) return true;
            if (__instance.TargetProgress - __instance.CurrentProgress
                <= ProgressIndicatorInitializePatch.MaxIndicators) return true;

            __result = Array.Empty<Sprite>();
            return false;
        }
    }

    /// <summary>
    /// A Custom run pays nothing.
    ///
    /// Left alone, <c>GetDifficultyMultiplier(-100)</c> misses <c>DIFFICULTY_MULTIPLIERS</c>
    /// and <c>GetNearestDifficulty</c> resolves it to New Customer's x1.00 — so a run
    /// configured for one trivial repayment wins in seconds and grants full currency, which
    /// buys perks, which unlock difficulties permanently. The save guards deliberately do not
    /// cover that: it is the same path a legitimate win uses, and the only thing separating
    /// the two is the pay rate.
    ///
    /// Patching here rather than at the award sites covers both, because
    /// <c>CurrencyRewardCalculator.CalculateGameplayRewards</c> and
    /// <c>ResultUIManager.PrepareCurrencyRewardDisplay</c> call this one method — so the
    /// figure shown on the results screen and the figure banked cannot disagree. The card's
    /// multiplier readout reads back through it too, and will show x0.00.
    ///
    /// The method is a guard plus a dictionary lookup plus a fallback, well clear of Mono's
    /// inlining window, and <c>docs/AGENTS.md</c> records it as observed working.
    /// </summary>
    [HarmonyPatch(typeof(CurrencyParams), nameof(CurrencyParams.GetDifficultyMultiplier))]
    internal static class GetDifficultyMultiplierPatch
    {
        private static void Postfix(Difficulty difficulty, ref float __result)
        {
            if (CustomDifficulty.IsCustom(difficulty)) __result = 0f;
        }
    }
}
