using System;
using System.Reflection;
using BaseSystem;
using GameRule.SpecialMission;
using GameState;
using HarmonyLib;
using Scene.TitleScene;
using TMPro;
using Utility.Localization;
using Utility.SaveData;

namespace LwfCustomDifficulty.Patches
{
    /// <summary>Rejects anything Enum.IsDefined does not know, which includes our id.</summary>
    [HarmonyPatch(typeof(BuildContentPolicy), nameof(BuildContentPolicy.IsDifficultyIncluded))]
    internal static class IsDifficultyIncludedPatch
    {
        private static void Postfix(Difficulty difficulty, ref bool __result)
        {
            if (!__result && CustomDifficulty.IsCustom(difficulty)) __result = true;
        }
    }

    /// <summary>Collapses undefined values to NewCustomer, which would erase a selection.</summary>
    [HarmonyPatch(typeof(BuildContentPolicy), nameof(BuildContentPolicy.ClampDifficulty))]
    internal static class ClampDifficultyPatch
    {
        private static bool Prefix(Difficulty difficulty, ref Difficulty __result)
        {
            if (!CustomDifficulty.IsCustom(difficulty)) return true;
            __result = difficulty;
            return false;
        }
    }

    /// <summary>
    /// The carousel's own copy of the order, plus the selection that copy decides.
    ///
    /// Initialize rebuilds _difficultyValues from Enum.GetValues at the top of its body and
    /// then, at the bottom, reads the saved difficulty, runs it through
    /// ClampSelectableDifficulty and hands it to SetDifficulty — all against the stock,
    /// Custom-less array. Substituting the array in a postfix alone is too late twice over:
    ///
    ///   * HandleButtonInteractable has already compared Array.IndexOf(stock, selection)
    ///     against the selectable bounds, so on a saved New Customer the left arrow starts
    ///     greyed out even though Custom now sits to its left, and nothing recomputes it.
    ///   * A saved Custom has already been destroyed. IndexOf returns -1, the clamp falls
    ///     back to GetMinSelectableIndex() = 0 = stock New Customer, and SetDifficulty
    ///     writes that back through LwfSaveDataAccessor.
    ///
    /// The second of those is why the saved value is captured in a prefix rather than read
    /// back from the instance afterwards: by the time the postfix runs it is gone.
    ///
    /// The postfix installs the array and then re-decides the selection against it. Which
    /// of the two repairs is needed is settled by re-running the clamp, because only one of
    /// them may touch the save: LwfSaveDataAccessor.SetDifficultyNormalGame goes through
    /// InvokeSave, so SetDifficulty writes the ES3 file every time it is called.
    ///
    ///   * Clamp agrees with what Initialize settled on — the ordinary case. Everything
    ///     SetDifficulty would redo is a function of the difficulty value, which has not
    ///     changed, except HandleButtonInteractable, which is a function of its index.
    ///     That one method is re-run on its own and nothing is written.
    ///   * Clamp disagrees. The stored selection is wrong, so SetDifficulty runs in full
    ///     and persists the corrected value — the same write Initialize itself just made,
    ///     with the right value in it.
    /// </summary>
    [HarmonyPatch(typeof(DifficultySetter), nameof(DifficultySetter.Initialize))]
    internal static class DifficultySetterInitializePatch
    {
        private static readonly FieldInfo DifficultyValuesField =
            AccessTools.Field(typeof(DifficultySetter), "_difficultyValues");

        // Invoked, never patched, so Mono's inline limit does not apply — but for the
        // record: 112, 52 and 59 IL bytes respectively.
        private static readonly MethodInfo SetDifficultyMethod =
            AccessTools.Method(typeof(DifficultySetter), "SetDifficulty");

        private static readonly MethodInfo ClampSelectableDifficultyMethod =
            AccessTools.Method(typeof(DifficultySetter), "ClampSelectableDifficulty");

        private static readonly MethodInfo HandleButtonInteractableMethod =
            AccessTools.Method(typeof(DifficultySetter), "HandleButtonInteractable");

        private static void Prefix(GameMode gameMode, MissionType specialMissionType,
                                   out Difficulty? __state)
        {
            try
            {
                __state = SavedDifficulty(gameMode, specialMissionType);
            }
            catch (Exception error)
            {
                // A throwing prefix takes Initialize down with it, which would cost the whole
                // difficulty screen. Null instead: the postfix then repairs the arrow states
                // against whatever Initialize settled on and leaves the selection alone.
                Plugin.Log?.LogError($"Reading the saved difficulty failed: {error}");
                __state = null;
            }
        }

        private static void Postfix(DifficultySetter __instance, GameMode gameMode, Difficulty? __state)
        {
            var order = CustomDifficulty.BuildOrder();
            DifficultyValuesField.SetValue(__instance, order);

            if (SetDifficultyMethod == null || ClampSelectableDifficultyMethod == null
                || HandleButtonInteractableMethod == null)
            {
                Plugin.Log?.LogError("DifficultySetter internals not resolved; the carousel keeps "
                                     + "the selection Initialize computed against the stock order.");
                return;
            }

            var saved = __state ?? __instance.ToSetDifficulty;

            try
            {
                var target = (Difficulty)ClampSelectableDifficultyMethod
                    .Invoke(__instance, new object[] { saved });

                if (target == __instance.ToSetDifficulty)
                {
                    HandleButtonInteractableMethod.Invoke(__instance, null);
                }
                else
                {
                    SetDifficultyMethod.Invoke(__instance, new object[] { saved });
                }

                Plugin.Log?.LogInfo($"DifficultySetter[{gameMode}]: saved={saved}, "
                                    + $"applied={__instance.ToSetDifficulty}, "
                                    + $"index={Array.IndexOf(order, __instance.ToSetDifficulty)} "
                                    + $"of {order.Length}.");
            }
            catch (Exception error)
            {
                Plugin.Log?.LogError($"Re-applying the saved difficulty failed: {error}");
            }
        }

        /// <summary>
        /// What DifficultySetter.GetSavedDifficulty would return. Mirrored rather than called:
        /// it is private and reads _gameMode, which the prefix runs ahead of. The arguments
        /// Initialize was handed carry the same information.
        /// </summary>
        private static Difficulty SavedDifficulty(GameMode gameMode, MissionType specialMissionType)
        {
            switch (gameMode)
            {
                case GameMode.NormalGame:
                    return LwfSaveDataAccessor.GetDifficultyNormalGame();
                case GameMode.Reckoning:
                    return LwfSaveDataAccessor.GetDifficultyReckoning();
                case GameMode.SpecialMission:
                    return LwfSaveDataAccessor.GetDifficultySpecialMission(specialMissionType);
                default:
                    return Difficulty.NewCustomer;
            }
        }
    }

    /// <summary>
    /// Custom sits at index 0, below every bound the game applies, so the carousel needs
    /// no widening to reach it. What it does need is the floor that moved: this method
    /// returns Min(Max(GetMinSelectableIndex(), unlocked), IndexOf(Hell1)), and the
    /// unlocked figure comes from GameData.CalculateMaxUnlockedDifficultyIndex, whose
    /// "cleared nothing" branch is a literal 0 — which now names Custom rather than New
    /// Customer. Lifting the result to the first vanilla entry restores exactly the stock
    /// guarantee that New Customer is always selectable, and grants nothing past it.
    /// </summary>
    [HarmonyPatch(typeof(DifficultySetter), "GetMaxSelectableIndex")]
    internal static class GetMaxSelectableIndexFloorPatch
    {
        private static void Postfix(ref int __result)
        {
            __result = Math.Max(__result, CustomDifficulty.FirstVanillaIndex);
        }
    }

    /// <summary>Without this the card would read the string table's miss sentinel.</summary>
    [HarmonyPatch(typeof(LocalizedTextGetter), nameof(LocalizedTextGetter.GetDifficultyName))]
    internal static class GetDifficultyNamePatch
    {
        private static bool Prefix(Difficulty toSetDifficulty, ref string __result)
        {
            if (!CustomDifficulty.IsCustom(toSetDifficulty)) return true;
            __result = CustomDifficulty.Name;
            return false;
        }
    }

    /// <summary>
    /// Everything the Custom card reads: its description and its two numbers.
    ///
    /// UpdateDifficultyText is 277 IL bytes — an order of magnitude past Mono's ~20-byte
    /// inline window — and is the only writer of all four card fields. What it writes them
    /// from cannot be patched: _tmpFirstNorma comes from WinCondition.CalcNormaStart (15 IL
    /// bytes) and _tmpSeparatedNTimes from CalcTargetProgress (12), both inside the window,
    /// and for the Custom id of -100 they evaluate to -990 and -95. The card therefore read
    /// "-990" and "x-95" beside an options panel showing the configured rules — the two
    /// numbers on a screen whose whole job is presenting a difficulty by its numbers. The
    /// fields are written directly here instead, after the vanilla body has had its say.
    ///
    /// The description arrives the same way. DifficultySetter builds its key inline rather
    /// than through a patchable method, and everything below NewCustomer takes the
    /// "DifficultyDescUndefined" branch, which the string table answers with "Unsupported
    /// difficulty level". That key used to be intercepted in a prefix on
    /// LocalizedTextGetter.GetMessageData — an 8 IL byte forwarder, well inside the inline
    /// window and so quite possibly never reached. Writing the field is not subject to that
    /// question, and the key is referenced from exactly one site in the whole assembly
    /// (UpdateDifficultyText's local function), so the prefix covered no other path and has
    /// been removed rather than kept as a second line of defence.
    ///
    /// Every value comes from PluginConfig, through the same NumericText formatting the
    /// panel uses, so the card and the panel cannot disagree about either the numbers or
    /// the culture they are rendered in.
    ///
    /// _tmpRewardMultiplier is deliberately left alone. It is fed by
    /// CurrencyParams.GetDifficultyMultiplier, whose dictionary miss on the Custom id falls
    /// through to GetNearestDifficulty — nearest to -100 is NewCustomer at 1.0 — so the card
    /// reads "x1.00", and 1.0 is genuinely the multiplier a Custom run pays. Nothing in this
    /// plugin alters it. Writing a value there would be inventing one.
    ///
    /// Not gated on the game mode, unlike the options panel: only the normal-game card can
    /// hold Custom (SpecialMission's minimum selectable index is IndexOf(Hell1), which
    /// ClampSelectableDifficulty lifts Custom straight past), and if that ever changed the
    /// numbers should be right on whichever card is showing.
    /// </summary>
    [HarmonyPatch(typeof(DifficultySetter), "UpdateDifficultyText")]
    internal static class UpdateDifficultyTextPatch
    {
        private static readonly FieldInfo DescriptionTextField =
            AccessTools.Field(typeof(DifficultySetter), "_descriptionText");

        private static readonly FieldInfo FirstNormaField =
            AccessTools.Field(typeof(DifficultySetter), "_tmpFirstNorma");

        private static readonly FieldInfo SeparatedNTimesField =
            AccessTools.Field(typeof(DifficultySetter), "_tmpSeparatedNTimes");

        /// <summary>Read back for the log line only; never written.</summary>
        private static readonly FieldInfo RewardMultiplierField =
            AccessTools.Field(typeof(DifficultySetter), "_tmpRewardMultiplier");

        private static bool _reportedUnresolved;
        private static string _lastLogged;

        private static void Postfix(DifficultySetter __instance) => Apply(__instance);

        /// <summary>
        /// Called from the options panel as well as from the postfix. Editing a rule changes
        /// the numbers without changing the selection, and nothing in the game re-runs
        /// UpdateDifficultyText for that — the card would otherwise keep whatever it was
        /// built with until the player cycled the carousel away and back.
        /// </summary>
        internal static void Apply(DifficultySetter setter)
        {
            if (setter == null) return;
            if (!CustomDifficulty.IsCustom(setter.ToSetDifficulty)) return;

            if (DescriptionTextField == null || FirstNormaField == null || SeparatedNTimesField == null)
            {
                // Cosmetic only, and said once: the run itself is seeded from WinCondition's
                // constructor and is unaffected. What the player would see is the vanilla
                // readout for id -100.
                if (_reportedUnresolved) return;
                _reportedUnresolved = true;
                Plugin.Log?.LogError(
                    "Custom card: DifficultySetter's text fields are not resolved — the build has "
                    + "drifted. The card keeps the vanilla readout, which for the Custom id is "
                    + "-990 and x-95. The rules themselves are unaffected.");
                return;
            }

            SetText(FirstNormaField.GetValue(setter),
                    NumericText.Format(PluginConfig.Rules.FirstRepayment));
            SetText(SeparatedNTimesField.GetValue(setter),
                    "x" + NumericText.Format(PluginConfig.Repayments));
            SetText(DescriptionTextField.GetValue(setter), CustomDifficulty.Describe());

            // Read straight back off the four TextMeshProUGUI components, never echoed from
            // what was passed in: that is what would catch the writes silently not landing.
            // The reward multiplier is in the line because it is the one field left to
            // vanilla, and the human should be able to see its value rather than take it on
            // trust. Logged only when the card's state changes, so cycling the carousel back
            // and forth does not fill the log.
            var line = $"Custom card: firstNorma={Read(FirstNormaField, setter)} "
                       + $"separatedNTimes={Read(SeparatedNTimesField, setter)} "
                       + $"rewardMultiplier={Read(RewardMultiplierField, setter)} "
                       + $"description=\"{Read(DescriptionTextField, setter)}\"";

            if (line == _lastLogged) return;
            _lastLogged = line;
            Plugin.Log?.LogInfo(line);
        }

        /// <summary>The null test is Unity's, not the reference's: a text object destroyed
        /// with its scene leaves a live C# reference that throws on use.</summary>
        private static void SetText(object field, string value)
        {
            if (field is TextMeshProUGUI text && text != null) text.text = value;
        }

        private static string Read(FieldInfo field, DifficultySetter setter)
        {
            return field?.GetValue(setter) is TextMeshProUGUI text && text != null
                ? text.text
                : "<unset>";
        }
    }
}
