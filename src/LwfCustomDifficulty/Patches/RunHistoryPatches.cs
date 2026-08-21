using System.Reflection;
using GameState.StaticStateHolders;
using HarmonyLib;

namespace LwfCustomDifficulty.Patches
{
    /// <summary>
    /// Keeps a Custom run out of the full release's run history.
    ///
    /// <c>RunHistory</c> does not exist in the demo. It records every run to disk, and
    /// <c>RunRecordingService.ValidateStartContext</c> opens by round-tripping the difficulty
    /// through its enum name — <c>TryParseDefinedEnumId&lt;Difficulty&gt;</c> — which the
    /// Custom id has none of. The result is an unhandled <c>ArgumentException</c> inside
    /// <c>InGameSceneInitializer.DoAfterWipeAsync</c>: the scene never finishes loading and
    /// starting a Custom run fails outright.
    ///
    /// Excluded rather than bypassed. Suppressing the validation would let the run record,
    /// writing an id no reader can resolve into the player's run history — and a Custom run
    /// is a sandbox that already writes nothing. The game has its own seam for exactly this:
    /// <c>RunRecordingRuntime.BeginPreparedGameAsync</c> guards the whole recording block
    /// with <c>if (isRecordingEnabledForCurrentSession &amp;&amp; !host.IsTutorialGame)</c>,
    /// so a tutorial run prepares a session and never begins one. Custom now takes that same
    /// path, which the surrounding code already handles: <c>HasActiveRun</c> stays false and
    /// the finalize paths are all guarded on it.
    ///
    /// <c>IsTutorialGame</c> has exactly one consumer in the assembly, that guard, so
    /// widening it changes nothing else.
    ///
    /// The getter is **9 IL bytes**, well inside Mono's inlining window. What protects the
    /// patch is the dispatch, not the size: <c>host</c> is typed as
    /// <c>IRunRecordingRuntimeHost</c>, so the call is a <c>callvirt</c> through an interface
    /// and cannot be inlined at the call site. It logs on the first hit, so a run either
    /// shows the line or reproduces the original crash — this cannot fail quietly.
    /// </summary>
    [HarmonyPatch]
    internal static class RunHistoryExclusionPatch
    {
        private const string HostTypeName =
            "RunHistory.RunRecordingRuntime+UnityRunRecordingRuntimeHost";

        private static bool _logged;

        /// <summary>Resolved by name: the host is a private nested type of an internal static
        /// class, so it cannot be named in C# without InternalsVisibleTo.</summary>
        private static MethodBase TargetMethod()
        {
            var host = AccessTools.TypeByName(HostTypeName);
            return AccessTools.PropertyGetter(host, "IsTutorialGame");
        }

        private static void Postfix(ref bool __result)
        {
            if (__result || !CustomDifficulty.IsCustom(CurrentDifficulty.Get)) return;

            __result = true;

            if (_logged) return;
            _logged = true;
            Plugin.Log.LogInfo("Run history: Custom run excluded from recording, as a tutorial "
                               + "run is. No run record is written.");
        }
    }
}
