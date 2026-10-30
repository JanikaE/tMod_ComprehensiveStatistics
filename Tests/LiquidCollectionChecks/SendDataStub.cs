namespace Terraria.Localization
{
    /// <summary>只保留原版发送签名的网络文本占位类型，不参与液体正文。</summary>
    internal sealed class NetworkText { }
}
namespace Terraria
{
    /// <summary>与实际原版 SendData 相同的 HookGen 签名。</summary>
    internal static partial class On_NetMessage
    {
        /// <summary>原版发送签名，保留全部参数和浮点坐标 ABI。</summary>
        internal delegate void orig_SendData(int message,int to,int ignore,Localization.NetworkText text,int number,
            float number2,float number3,float number4,int number5,int number6,int number7);
        /// <summary>生产发送边界的 Hook 签名。</summary>
        internal delegate void hook_SendData(orig_SendData orig,int message,int to,int ignore,Localization.NetworkText text,
            int number,float number2,float number3,float number4,int number5,int number6,int number7);
        internal static hook_SendData SendData;
    }
}
