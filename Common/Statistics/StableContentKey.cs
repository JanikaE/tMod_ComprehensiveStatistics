using System;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 使用模组名与内容内部名标识内容，避免存档依赖会随加载顺序变化的运行时数字 ID。
    /// </summary>
    internal readonly struct StableContentKey : IEquatable<StableContentKey>
    {
        internal StableContentKey(string modName, string contentName)
        {
            if (string.IsNullOrWhiteSpace(modName))
            {
                throw new ArgumentException("Mod name cannot be empty.", nameof(modName));
            }

            if (string.IsNullOrWhiteSpace(contentName))
            {
                throw new ArgumentException("Content name cannot be empty.", nameof(contentName));
            }

            ModName = modName;
            ContentName = contentName;
        }

        internal string ModName { get; }

        internal string ContentName { get; }

        public bool Equals(StableContentKey other)
        {
            return string.Equals(ModName, other.ModName, StringComparison.Ordinal)
                && string.Equals(ContentName, other.ContentName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is StableContentKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                ModName == null ? 0 : StringComparer.Ordinal.GetHashCode(ModName),
                ContentName == null ? 0 : StringComparer.Ordinal.GetHashCode(ContentName));
        }

        public override string ToString()
        {
            return $"{ModName}/{ContentName}";
        }

        public static bool operator ==(StableContentKey left, StableContentKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StableContentKey left, StableContentKey right)
        {
            return !left.Equals(right);
        }
    }
}
