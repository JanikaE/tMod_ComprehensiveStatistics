using Terraria;
using Terraria.ModLoader;

namespace ComprehensiveStatistics
{
    public class ComprehensiveStatistics : Mod
    {
        internal static ModKeybind ToggleStatisticsKeybind { get; private set; }

        public override void Load()
        {
            if (!Main.dedServ)
            {
                ToggleStatisticsKeybind = KeybindLoader.RegisterKeybind(this, "ToggleStatistics", "Back");
            }
        }

        public override void Unload()
        {
            ToggleStatisticsKeybind = null;
        }
    }
}
