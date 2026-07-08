using HarmonyLib;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Harmony patch that drives the overlay HUD and VRAM warning
    /// without using GameComponent (zero save-file footprint).
    ///
    /// Patches GameComponentUtility.GameComponentOnGUI() which is called
    /// every GUI frame when a game is active — same timing as a
    /// GameComponent.GameComponentOnGUI() override, but no save entry.
    /// </summary>
    [HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.GameComponentOnGUI))]
    internal static class GuiHookPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            // Drive the overlay rendering
            OverlayHud.OnGUI();

            // Check VRAM headroom (only triggers once per game load)
            VramWarning.CheckOnce();
        }
    }
}
