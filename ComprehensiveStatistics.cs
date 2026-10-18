using System.IO;
using ComprehensiveStatistics.Common.Networking;
using Terraria;
using Terraria.ModLoader;

namespace ComprehensiveStatistics
{
    /// <summary>
    /// 模组入口，仅负责注册跨系统共享的基础资源。
    /// </summary>
    public class ComprehensiveStatistics : Mod
    {
        // 快捷键只在客户端注册，专用服务器不会创建任何界面资源。
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

        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            StatisticsNetcode.HandlePacket(reader, whoAmI);
        }
    }
}
