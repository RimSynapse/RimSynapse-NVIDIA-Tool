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
        /// When true (default), show VRAM status dialog on every game load.
        /// When false, only show if VRAM is critically low.
        /// </summary>
        public bool alwaysNotifyVram = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref alwaysNotifyVram, "alwaysNotifyVram", false);
            base.ExposeData();
        }
    }
}
