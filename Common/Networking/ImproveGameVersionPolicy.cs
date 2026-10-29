using System;

namespace ComprehensiveStatistics.Common.Networking
{
    /// <summary>从 1.8.2 起默认向后兼容；具体能力仍须通过运行时签名检查与多人协商。</summary>
    internal static class ImproveGameVersionPolicy
    {
        private static readonly Version Minimum = new(1, 8, 2);

        internal static bool Supports(Version version) => version != null && version >= Minimum;
    }
}
