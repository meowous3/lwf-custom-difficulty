using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using GameState;
using HarmonyLib;
using LwfCustomDifficulty.Patches;
using LwfCustomDifficulty.Ui;
using Scene.TitleScene;
using TMPro;
using UnityEngine.UI;

namespace LwfCustomDifficulty
{
    [BepInPlugin(PluginGuid, "LWF Custom Difficulty", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "dev.meow.lwfcustomdifficulty";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            PluginConfig.Bind(Config);
            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll();

            // Computed rather than declared, so it cannot go in an attribute: every save
            // writer on the accessor is guarded for the length of a Custom run.
            CustomRunScope.Install(harmony);

            // BuildOrder filters through IsDifficultyIncluded, so it runs after PatchAll.
            var order = CustomDifficulty.BuildOrder();

            // Touch each type first: overwriting a static field before its static
            // constructor has run would be undone when the class initialises.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                typeof(Unlocks.DifficultyUnlockManager).TypeHandle);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                typeof(Utility.SaveData.GameData).TypeHandle);

            AccessTools.Field(typeof(Unlocks.DifficultyUnlockManager), "DIFFICULTY_ORDER")
                .SetValue(null, order);
            AccessTools.Field(typeof(Utility.SaveData.GameData), "DifficultyOrder")
                .SetValue(null, order);

            Log.LogInfo($"Custom difficulty registered at index {Array.IndexOf(order, CustomDifficulty.Id)} "
                        + $"of {order.Length}; first vanilla index {CustomDifficulty.FirstVanillaIndex}.");
        }
    }

    /// <summary>
    /// Builds the options panel and follows the carousel. SetDifficulty is the one place
    /// every selection change passes through — Initialize ends by calling it, so the
    /// first call both builds the panel and sets its initial visibility.
    ///
    /// Gated on the game mode because the scene holds two DifficultySetters and this is a
    /// single static panel. The SpecialMission card's setter calls SetDifficulty on its own
    /// schedule — Initialize, and again from SetSpecialMission every time the mission
    /// changes — with no regard for which card the player is looking at. Left ungated, the
    /// panel gets reparented under whichever card fired last and takes that card's
    /// visibility and that setter's difficulty with it. Only the normal-game card can hold
    /// Custom anyway: SpecialMission's minimum selectable index is IndexOf(Hell1), so
    /// ClampSelectableDifficulty lifts Custom straight past it.
    /// </summary>
    [HarmonyPatch(typeof(DifficultySetter), "SetDifficulty")]
    internal static class SetDifficultyPatch
    {
        private static readonly FieldInfo DifficultyTextField =
            AccessTools.Field(typeof(DifficultySetter), "_difficultyText");

        private static readonly FieldInfo GameModeField =
            AccessTools.Field(typeof(DifficultySetter), "_gameMode");

        private static readonly FieldInfo LeftButtonField =
            AccessTools.Field(typeof(DifficultySetter), "_leftButton");

        private static void Postfix(DifficultySetter __instance)
        {
            if (!(GameModeField?.GetValue(__instance) is GameMode gameMode))
            {
                Plugin.Log.LogError("Options panel: _gameMode not resolved; panel not shown.");
                return;
            }

            if (gameMode != GameMode.NormalGame) return;

            HideLeftArrowOnCustom(__instance);

            // The card's own title text is the only font asset on hand at runtime.
            var label = DifficultyTextField?.GetValue(__instance) as TextMeshProUGUI;

            if (label == null)
            {
                Plugin.Log.LogError("Options panel: _difficultyText not resolved; no font available.");
                return;
            }

            CustomOptionsPanel.Attach(__instance, label.font);
            CustomOptionsPanel.SetVisible(CustomDifficulty.IsCustom(__instance.ToSetDifficulty));
        }

        /// <summary>
        /// Custom is the leftmost card, so the game already disables the left arrow there —
        /// HandleButtonInteractable sets interactable = index > 0. Disabled still draws it,
        /// greyed, pointing at nothing.
        ///
        /// Hidden rather than destroyed, and restored on every other difficulty, because the
        /// card is one object the carousel reuses: destroying the arrow would leave the whole
        /// ladder unable to scroll left. Runs after the vanilla body, whose own
        /// HandleButtonInteractable call would otherwise fight it.
        /// </summary>
        private static void HideLeftArrowOnCustom(DifficultySetter setter)
        {
            if (!(LeftButtonField?.GetValue(setter) is Button left) || left == null) return;

            var show = !CustomDifficulty.IsCustom(setter.ToSetDifficulty);
            if (left.gameObject.activeSelf != show) left.gameObject.SetActive(show);
        }
    }
}
