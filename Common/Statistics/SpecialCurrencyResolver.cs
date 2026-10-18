using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI;
using Terraria.ID;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 将只在当前加载会话有效的特殊货币数字 ID 转换为可持久化的代表物品键。
    /// </summary>
    internal static class SpecialCurrencyResolver
    {
        private static readonly FieldInfo CurrencyValuesField =
            typeof(CustomCurrencySystem).GetField(
                "_valuePerUnit",
                BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// 选择货币系统中单位价值最低的物品作为名称和图标；相同价值时按稳定键排序。
        /// </summary>
        internal static bool TryResolve(
            int currencyId,
            out StableContentKey currencyContent,
            out int currencyItemType)
        {
            currencyContent = default;
            currencyItemType = 0;
            if (!CustomCurrencyManager.TryGetCurrencySystem(
                currencyId,
                out CustomCurrencySystem currencySystem))
            {
                return false;
            }

            if (CurrencyValuesField?.GetValue(currencySystem)
                is Dictionary<int, int> valuesByItem
                && TrySelectRepresentative(valuesByItem, out currencyContent, out currencyItemType))
            {
                return true;
            }

            // 兼容完全重写 Accepts、未使用基类 _valuePerUnit 的模组货币实现。
            foreach (KeyValuePair<int, Item> sample in ContentSamples.ItemsByType)
            {
                if (sample.Key <= 0
                    || sample.Value == null
                    || !currencySystem.Accepts(sample.Value)
                    || !StableContentKey.TryCreateForItem(sample.Key, out StableContentKey content))
                {
                    continue;
                }

                if (currencyItemType == 0
                    || StringComparer.Ordinal.Compare(
                        content.ToString(),
                        currencyContent.ToString()) < 0)
                {
                    currencyContent = content;
                    currencyItemType = sample.Key;
                }
            }

            return currencyItemType > 0;
        }

        private static bool TrySelectRepresentative(
            Dictionary<int, int> valuesByItem,
            out StableContentKey currencyContent,
            out int currencyItemType)
        {
            currencyContent = default;
            currencyItemType = 0;
            int selectedUnitValue = int.MaxValue;
            foreach (KeyValuePair<int, int> pair in valuesByItem)
            {
                if (pair.Key <= 0
                    || pair.Value <= 0
                    || !StableContentKey.TryCreateForItem(
                        pair.Key,
                        out StableContentKey content))
                {
                    continue;
                }

                if (currencyItemType == 0
                    || pair.Value < selectedUnitValue
                    || (pair.Value == selectedUnitValue
                        && StringComparer.Ordinal.Compare(
                            content.ToString(),
                            currencyContent.ToString()) < 0))
                {
                    currencyContent = content;
                    currencyItemType = pair.Key;
                    selectedUnitValue = pair.Value;
                }
            }

            return currencyItemType > 0;
        }
    }
}
