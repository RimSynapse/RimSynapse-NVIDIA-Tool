using UnityEngine;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Checks VRAM headroom on game load and warns the player if
    /// available VRAM is dangerously low before the colony even starts.
    ///
    /// This runs once when the game component initializes (colony load).
    /// It uses NVML data (already polling) and the VramBreakdown estimates.
    /// </summary>
    public class VramWarning : GameComponent
    {
        /// <summary>Minimum GB of free VRAM recommended for stable play.</summary>
        private const float MinFreeGb = 2.0f;

        /// <summary>Only warn once per game session.</summary>
        private static bool _hasWarned;

        public VramWarning(Game game) : base() { }

        /// <summary>
        /// Called once when the game component is first initialized.
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();

            // Delay the check slightly to let NVML poll at least once
            if (_hasWarned) return;
            if (!NvidiaSmiReader.IsAvailable) return;

            CheckVramHeadroom();
        }

        private void CheckVramHeadroom()
        {
            float totalMb = NvidiaSmiReader.TotalVramMb;
            float usedMb = NvidiaSmiReader.UsedVramMb;

            // If NVML hasn't polled yet, try again on next GameComponentOnGUI
            if (totalMb <= 0f) return;

            float freeMb = totalMb - usedMb;
            float freeGb = freeMb / 1024f;

            if (freeGb < MinFreeGb)
            {
                _hasWarned = true;

                // Build a helpful warning message
                VramBreakdown.Refresh();
                float systemGb = VramBreakdown.SystemMb / 1024f;
                float lmsGb = VramBreakdown.LmStudioMb / 1024f;
                float totalGb = totalMb / 1024f;
                float usedGb = usedMb / 1024f;

                string msg =
                    "RimSynapse NVIDIA Tool — VRAM Warning\n\n" +
                    $"Your GPU has {freeGb:F1} GB free out of {totalGb:F1} GB.\n" +
                    $"Before RimWorld even started, your system was already using {usedGb:F1} GB:\n\n" +
                    $"  • System / Desktop: ~{systemGb:F1} GB\n" +
                    $"  • LM Studio model:  ~{lmsGb:F1} GB\n" +
                    $"  • RimWorld:          ~{VramBreakdown.RimWorldMb / 1024f:F1} GB\n\n" +
                    $"With less than {MinFreeGb:F0} GB free, you may experience:\n" +
                    "  • Late-game slowdowns as colony grows\n" +
                    "  • Frame drops during large raids\n" +
                    "  • GPU memory thrashing (stuttering)\n\n" +
                    "Suggestions:\n" +
                    "  • Load a smaller LLM in LM Studio (e.g., 7B instead of 12B)\n" +
                    "  • Reduce the model's context window in LM Studio\n" +
                    "  • Close GPU-heavy background apps (Chrome, Discord)\n" +
                    "  • Lower RimWorld graphics settings\n\n" +
                    "This warning won't appear again this session.";

                // Show as a RimWorld dialog
                Find.WindowStack.Add(new Dialog_MessageBox(
                    msg,
                    "Got it",
                    null,
                    "Open GPU Overlay",
                    delegate
                    {
                        OverlayHud.SetMode(OverlayMode.Advanced);
                    },
                    null,
                    false,
                    null,
                    null));

                Verse.Log.Warning(
                    $"[RimSynapse NV] Low VRAM: {freeGb:F1} GB free of {totalGb:F1} GB. " +
                    $"System: {systemGb:F1} GB, LM Studio: {lmsGb:F1} GB.");
            }
            else
            {
                _hasWarned = true; // Don't check again even if OK
            }
        }

        /// <summary>
        /// Fallback: if NVML hadn't polled during FinalizeInit,
        /// check on first GUI frame instead.
        /// </summary>
        public override void GameComponentOnGUI()
        {
            if (_hasWarned) return;
            if (!NvidiaSmiReader.IsAvailable) return;
            if (NvidiaSmiReader.TotalVramMb <= 0f) return;

            CheckVramHeadroom();
        }
    }
}
