using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Persistent settings for the NVIDIA Tool mod.
    /// Saved to the RimWorld config folder automatically.
    /// </summary>
    public class DevToolsSettings : ModSettings
    {
        /// <summary>
        /// When true, show VRAM status dialog on every game load (informational).
        /// When false, only show if VRAM is critically low (warning).
        /// Default: false (warn only).
        /// </summary>
        public bool alwaysNotifyVram = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref alwaysNotifyVram, "alwaysNotifyVram", false);
            base.ExposeData();
        }
    }
}
