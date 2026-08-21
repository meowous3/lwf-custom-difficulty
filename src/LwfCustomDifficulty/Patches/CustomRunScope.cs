using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameState;
using Scene.TitleScene;
using HarmonyLib;
using Utility.SaveData;

namespace LwfCustomDifficulty.Patches
{
    /// <summary>
    /// Suppresses every write to the progress save for the length of a Custom run.
    ///
    /// The three named guards beside this one each close a single writer, and each was added
    /// after a run leaked through the previous set: the cleared difficulty, then the patron
    /// clear, then — found by playing, not by reading — <c>SetAdvEpisodeRead</c>, which feeds
    /// the "attend meetings with X" perk conditions. <c>LwfSaveDataAccessor</c> has **54**
    /// writers. Enumerating the ones that happen to be reachable today is not a design; the
    /// next patch or the next mode moves the boundary again.
    ///
    /// So the guarantee is enforced where it is stated: while a Custom run is live, a write to
    /// the progress save does not happen. Every writer funnels through a private
    /// <c>InvokeSave</c>, whose body is `mutate in memory, then persist` — blocking the public
    /// writer rather than the persist step means the in-memory record is not touched either,
    /// so nothing is left behind to be flushed by an unrelated save later.
    ///
    /// A skipping Harmony prefix leaves <c>__result</c> at its default, which reads correctly
    /// for these signatures: the <c>Add*</c>/<c>Set*</c> writers return <c>false</c> for "not
    /// recorded", which is the truth.
    ///
    /// Scope is the run, not the selection. The human chose to let the difficulty screen
    /// remember Custom, and that write happens on the menu with no run in flight. The flag is
    /// raised by the <c>WinCondition</c> constructor and lowered by its <c>Dispose</c>, which
    /// <c>GameStateManager.OnDestroy</c> calls on scene unload — after the results screen, so
    /// end-of-run rewards are inside the scope, and the office is outside it.
    /// </summary>
    internal static class CustomRunScope
    {
        /// <summary>Writers are matched by prefix rather than listed. A name that is added or
        /// renamed upstream is then covered by default instead of silently escaping, which is
        /// the failure this class exists to end.</summary>
        private static readonly string[] WritePrefixes =
        {
            "Add", "Set", "Increment", "Ensure", "Reset", "Clear", "TrySpend",
        };

        private static bool _active;

        internal static bool Active => _active;

        internal static void Begin()
        {
            if (_active) return;
            _active = true;
            Plugin.Log.LogInfo("Custom run: progress save suppressed for the run.");
        }

        internal static void End()
        {
            if (!_active) return;
            _active = false;
            Plugin.Log.LogInfo("Custom run: progress save restored.");
        }

        /// <summary>
        /// Applied after PatchAll, because the set is computed rather than declared.
        ///
        /// Logs the count and the names: this patches by a name rule, so the rule having
        /// matched what was expected is itself a thing to verify from a run.
        /// </summary>
        internal static void Install(Harmony harmony)
        {
            var prefix = new HarmonyMethod(AccessTools.Method(typeof(CustomRunScope), nameof(SuppressWhileCustomRun)));

            var targets = AccessTools.GetDeclaredMethods(typeof(LwfSaveDataAccessor))
                .Where(m => m.IsStatic && !m.IsAbstract && !m.ContainsGenericParameters)
                .Where(m => WritePrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal)))
                .ToList();

            var patched = new List<string>();

            foreach (var target in targets)
            {
                try
                {
                    harmony.Patch(target, prefix: prefix);
                    patched.Add(target.Name);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Custom run: could not guard {target.Name} — {ex.GetType().Name}");
                }
            }

            Plugin.Log.LogInfo($"Custom run: guarding {patched.Count} save writers — "
                               + string.Join(", ", patched.Distinct().OrderBy(n => n)));
        }

        private static bool SuppressWhileCustomRun()
        {
            return !_active;
        }
    }

    /// <summary>Raises the scope. The constructor is the first thing that knows the run's
    /// difficulty; <see cref="RulePatches"/> already writes the rules from here.</summary>
    [HarmonyPatch(typeof(WinCondition), MethodType.Constructor, new[] { typeof(Difficulty) })]
    internal static class WinConditionScopeBeginPatch
    {
        private static void Postfix(Difficulty difficulty)
        {
            if (CustomDifficulty.IsCustom(difficulty)) CustomRunScope.Begin();
        }
    }

    /// <summary>
    /// Lowers it, from three places, because the obvious one is not safe to rely on.
    ///
    /// <c>WinCondition.Dispose</c> is the natural boundary and is **17 IL bytes** — against a
    /// threshold this project has measured at just below 18. If it inlines into
    /// <c>GameStateManager.OnDestroy</c> the postfix never runs, the scope never lowers, and
    /// every save for the rest of the session is suppressed: the office stops recording
    /// anything. That failure is far worse than the leak this class exists to close, so the
    /// scope is lowered wherever the run can be over.
    ///
    /// <c>OnDestroy</c> (334 bytes) is the method that calls Dispose, and
    /// <c>DifficultySetter.Initialize</c> (336) runs on the way back to the menu. Both are far
    /// outside the inlining window, and lowering an already-lowered flag is a no-op, so any
    /// one of the three is sufficient.
    ///
    /// Unconditional: the scope is only ever raised for a Custom run.
    /// </summary>
    [HarmonyPatch]
    internal static class ScopeEndPatches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WinCondition), nameof(WinCondition.Dispose));
            yield return AccessTools.Method(typeof(GameStateManager), "OnDestroy");
            yield return AccessTools.Method(typeof(DifficultySetter), nameof(DifficultySetter.Initialize));
        }

        private static void Postfix()
        {
            CustomRunScope.End();
        }
    }
}
