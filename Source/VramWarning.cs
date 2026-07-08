using UnityEngine;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Checks VRAM headroom on game load and warns the player if
    /// available VRAM is dangerously low before the colony even starts.
    ///
    /// NOT a GameComponent — uses static state only. Zero save-file footprint.
    /// The mod can be safely added or removed from any save game.
    ///
    /// Called from the Harmony GUI patch when a colony is first loaded.
    /// </summary>
    internal static class VramWarning
    {
        /// <summary>Minimum GB of free VRAM recommended for stable play.</summary>
        private const float MinFreeGb = 2.0f;

        /// <summary>Only warn/notify once per game load.</summary>
        private static bool _hasChecked;

        /// <summary>Track which Game instance we last checked for.</summary>
        private static int _lastGameId;

        /// <summary>
        /// Called from the GUI patch each frame. Checks once per game load.
        /// </summary>
        internal static void CheckOnce()
        {
            if (!NvidiaSmiReader.IsAvailable) return;
            if (NvidiaSmiReader.TotalVramMb <= 0f) return;

            // Detect new game load by checking if the Game instance changed
            var game = Verse.Current.Game;
            if (game == null) return;
            int gameId = game.GetHashCode();

            if (_hasChecked && gameId == _lastGameId) return;

            _hasChecked = true;
            _lastGameId = gameId;

            CheckVramHeadroom();
        }

        private static void CheckVramHeadroom()
        {
            // Check user preference
            bool alwaysNotify = DevToolsMod.Instance?.Settings?.alwaysNotifyVram ?? false;
            bool isRemote = RimSynapseMod.Instance?.Settings?.IsRemoteUrl ?? false;

            if (isRemote)
            {
                if (alwaysNotify)
                {
                    ShowRemoteInfoDialog();
                }
                return; // Never trigger a low-VRAM warning for a remote LM Studio setup
            }

            float totalMb = NvidiaSmiReader.TotalVramMb;
            float usedMb = NvidiaSmiReader.UsedVramMb;
            float freeMb = totalMb - usedMb;
            float freeGb = freeMb / 1024f;

            if (alwaysNotify)
            {
                // Always show — informational status, not alarming
                ShowInfoDialog(freeGb, totalMb, usedMb);
            }
            else if (freeGb < MinFreeGb)
            {
                // Only warn when VRAM is critically low
                ShowWarningDialog(freeGb, totalMb, usedMb);
            }
        }

        private static void ShowRemoteInfoDialog()
        {
            string msg = "RimSynapse NVIDIA Tool is active, but RimSynapse Core is configured to use a Remote LM Studio host.\n\n" +
                         "Local VRAM will not be monitored for LM Studio memory overhead.";

            var dialog = new Dialog_MessageBox(
                text: msg,
                buttonAText: "OK",
                title: "RimSynapse GPU: Remote Host Detected"
            );
            Find.WindowStack.Add(dialog);
        }

        /// <summary>
        /// Informational dialog — shown every load when "Always Notify" is checked.
        /// Non-alarming, just tells them their VRAM status.
        /// </summary>
        private static void ShowInfoDialog(float freeGb, float totalMb, float usedMb)
        {
            VramBreakdown.Refresh();
            float totalGb = totalMb / 1024f;
            float usedGb = usedMb / 1024f;
            float systemGb = VramBreakdown.SystemMb / 1024f;
            float lmsGb = VramBreakdown.LmStudioMb / 1024f;
            float rwGb = VramBreakdown.RimWorldMb / 1024f;

            string status = freeGb >= MinFreeGb
                ? $"✓  You have {freeGb:F1} GB free — you're in good shape."
                : $"⚠  You have {freeGb:F1} GB free — this is tight for late-game.";

            string msg =
                "RimSynapse GPU — VRAM Status\n\n" +
                $"GPU: {NvidiaSmiReader.GpuName}\n" +
                $"VRAM: {usedGb:F1} / {totalGb:F1} GB used\n\n" +
                $"  • System / Desktop:  {systemGb:F1} GB\n" +
                $"  • LM Studio model:   {lmsGb:F1} GB\n" +
                $"  • RimWorld:          {rwGb:F1} GB\n" +
                $"  • Free:              {freeGb:F1} GB\n\n" +
                status + "\n\n" +
                "Disable 'Always show VRAM status' in mod settings to only see warnings.";

            Find.WindowStack.Add(new Dialog_MessageBox(
                msg,
                "OK",
                null,
                null,
                null,
                null,
                false,
                null,
                null));
        }

        /// <summary>
        /// Warning dialog — shown only when VRAM headroom is critically low.
        /// Includes actionable suggestions.
        /// </summary>
        private static void ShowWarningDialog(float freeGb, float totalMb, float usedMb)
        {
            VramBreakdown.Refresh();
            float totalGb = totalMb / 1024f;
            float usedGb = usedMb / 1024f;
            float systemGb = VramBreakdown.SystemMb / 1024f;
            float lmsGb = VramBreakdown.LmStudioMb / 1024f;
            float rwGb = VramBreakdown.RimWorldMb / 1024f;

            string msg =
                "RimSynapse GPU — VRAM Warning\n\n" +
                $"Your GPU has {freeGb:F1} GB free out of {totalGb:F1} GB.\n" +
                $"Before RimWorld even started, your system was already using {usedGb:F1} GB:\n\n" +
                $"  • System / Desktop:  ~{systemGb:F1} GB\n" +
                $"  • LM Studio model:   ~{lmsGb:F1} GB\n" +
                $"  • RimWorld:          ~{rwGb:F1} GB\n\n" +
                $"With less than {MinFreeGb:F0} GB free, you may experience:\n" +
                "  • Late-game slowdowns as colony grows\n" +
                "  • Frame drops during large raids\n" +
                "  • GPU memory thrashing (stuttering)\n\n" +
                "Suggestions:\n" +
                "  • Load a smaller LLM in LM Studio (e.g., 7B instead of 12B)\n" +
                "  • Reduce the model's context window in LM Studio\n" +
                "  • Close GPU-heavy background apps (Chrome, Discord)\n" +
                "  • Lower RimWorld graphics settings\n\n" +
                "Enable 'Always show VRAM status' in mod settings for info every load.";

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
    }
}
