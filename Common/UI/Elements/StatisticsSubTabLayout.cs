using System;
using System.Collections.Generic;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    /// <summary>子页按钮的逻辑像素布局，与窗口缩放和内容滚动解耦。</summary>
    internal readonly record struct StatisticsSubTabSlot(float X, float Y, float Width);

    /// <summary>共用的换行计算，宽度不足时先换行，单按钮极窄时限制到可用宽度。</summary>
    internal static class StatisticsSubTabLayout
    {
        internal const float ButtonHeight = 28f, Gap = 6f;

        /// <summary>按本地化文本的测量宽度计算各按钮位置，不裁剪或覆盖相邻控件。</summary>
        internal static StatisticsSubTabSlot[] Calculate(float availableWidth, IReadOnlyList<float> textWidths)
        {
            float width = Math.Max(1f, availableWidth), x = 0f, y = 0f;
            var slots = new StatisticsSubTabSlot[textWidths.Count];
            for (int i = 0; i < slots.Length; i++)
            {
                float buttonWidth = Math.Min(width, Math.Max(64f, textWidths[i] + 26f));
                if (x > 0f && x + buttonWidth > width) { x = 0f; y += ButtonHeight + Gap; }
                slots[i] = new StatisticsSubTabSlot(x, y, buttonWidth);
                x += buttonWidth + Gap;
            }
            return slots;
        }
    }
}
