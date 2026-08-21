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
                .Where(WritesProgress)
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

            Plugin.Log.LogInfo($"Custom run: guarding {patched.Count} progress writers — "
                               + string.Join(", ", patched.Distinct().OrderBy(n => n)));
        }

        /// <summary>
        /// True when the method's own body calls <c>InvokeSave</c>.
        ///
        /// The first version of this matched on name — Add, Set, Increment, and so on — and
        /// swept up 72 methods where there are 54 writers. Settings live on the accessor too
        /// and persist by a different route, so <c>SetVolume</c>, <c>SetScreenResolution</c>,
        /// <c>SetLocale</c> and <c>SetKeyConfig</c> were all being suppressed: change the
        /// volume during a Custom run and it would silently not stick. It also caught
        /// <c>EnsureInitializedForSave</c>, which is not a writer but the initialiser
        /// <c>InvokeSave</c> itself calls.
        ///
        /// Reading the body asks the question that actually matters, and keeps the property
        /// the name rule was reaching for: a writer added upstream is covered because it has
        /// to go through the same funnel, not because someone remembered to list it.
        /// </summary>
        private static bool WritesProgress(MethodInfo method)
        {
            try
            {
                foreach (var instruction in PatchProcessor.ReadMethodBody(method))
                {
                    if (instruction.Value is MethodBase called
                        && called.Name == "InvokeSave"
                        && called.DeclaringType == typeof(LwfSaveDataAccessor))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Unreadable body: guard it rather than assume it is safe.
                Plugin.Log.LogWarning($"Custom run: could not read {method.Name} — {ex.GetType().Name}; guarding it.");
                return true;
            }

            return false;
        }

        private static bool SuppressWhileCustomRun()
        {
            return !_active;
        }
    }

    /// <summary>
    /// Sets the scope to match the run being constructed. The constructor is the first thing
    /// that knows the difficulty; <see cref="RulePatches"/> already writes the rules from here.
    ///
    /// A vanilla run lowers it rather than merely not raising it. The three lowering points
    /// are each expected to fire, but the failure they cannot cover is a scope left standing
    /// into someone's real run — which would suppress that run's progress silently, the worst
    /// outcome available here. Starting any vanilla run now guarantees saving is on,
    /// independently of whether the previous run ended tidily.
    /// </summary>
    [HarmonyPatch(typeof(WinCondition), MethodType.Constructor, new[] { typeof(Difficulty) })]
    internal static class WinConditionScopeBeginPatch
    {
        private static void Postfix(Difficulty difficulty)
        {
            if (CustomDifficulty.IsCustom(difficulty))
            {
                CustomRunScope.Begin();
            }
            else
            {
                CustomRunScope.End();
            }
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
