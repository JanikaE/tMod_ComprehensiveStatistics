using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.ModLoader.IO;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 将角色统计转换为版本化 TagCompound；只写入非零条目，并在加载时隔离损坏条目。
    /// </summary>
    internal static class StatisticsDataSerializer
    {
        internal const string PlayerTagKey = "CharacterStatistics";

        private const string FormatVersionKey = "FormatVersion";
        private const string StartedAtKey = "StartedAtUnixMilliseconds";
        private const string CareerKey = "Career";
        private const string WorldSlicesKey = "WorldSlices";
        private const string WorldIdKey = "WorldId";
        private const string DataKey = "Data";
        private const string EntriesKey = "Entries";
        private const string StatisticIdKey = "StatisticId";
        private const string ContentModKey = "ContentMod";
        private const string ContentNameKey = "ContentName";
        private const string StyleKey = "Style";
        private const string EventsKey = "Events";
        private const string UnitsKey = "Units";
        private const string AmountKey = "Amount";

        /// <summary>
        /// 将完整角色统计序列化为稳定、有序且稀疏的标签结构。
        /// </summary>
        internal static TagCompound Serialize(CharacterStatisticsData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            List<TagCompound> worldTags = new List<TagCompound>();
            foreach (KeyValuePair<Guid, StatisticsData> pair in
                data.WorldSlices.OrderBy(pair => pair.Key))
            {
                worldTags.Add(new TagCompound
                {
                    [WorldIdKey] = pair.Key.ToString("N"),
                    [DataKey] = SerializeScope(pair.Value)
                });
            }

            return new TagCompound
            {
                [FormatVersionKey] = StatisticsDataFormat.CurrentVersion,
                [CareerKey] = SerializeScope(data.Career),
                [WorldSlicesKey] = worldTags
            };
        }

        /// <summary>
        /// 尝试从玩家标签恢复受支持版本的角色统计。
        /// </summary>
        internal static bool TryDeserialize(
            TagCompound playerTag,
            out CharacterStatisticsData data,
            out string error)
        {
            data = null;
            error = null;

            if (playerTag == null || !playerTag.ContainsKey(PlayerTagKey))
            {
                return false;
            }

            try
            {
                TagCompound root = playerTag.GetCompound(PlayerTagKey);
                int formatVersion = root.GetInt(FormatVersionKey);
                if (formatVersion != StatisticsDataFormat.CurrentVersion)
                {
                    error = $"Unsupported statistics data format version: {formatVersion}.";
                    return false;
                }

                if (!TryDeserializeScope(root.GetCompound(CareerKey), out StatisticsData career))
                {
                    error = "The character career statistics scope is invalid.";
                    return false;
                }

                Dictionary<Guid, StatisticsData> worldSlices =
                    new Dictionary<Guid, StatisticsData>();
                foreach (TagCompound worldTag in root.GetList<TagCompound>(WorldSlicesKey))
                {
                    // 单个世界切片损坏不应阻止角色生涯及其他有效世界的数据恢复。
                    if (!Guid.TryParse(worldTag.GetString(WorldIdKey), out Guid worldId)
                        || worldSlices.ContainsKey(worldId)
                        || !TryDeserializeScope(worldTag.GetCompound(DataKey), out StatisticsData worldData))
                    {
                        continue;
                    }

                    worldSlices.Add(worldId, worldData);
                }

                data = new CharacterStatisticsData(career, worldSlices);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Failed to read statistics data: {exception.Message}";
                return false;
            }
        }

        /// <summary>
        /// 将单个查看范围序列化，并按稳定键排序以保证输出可复现。
        /// </summary>
        private static TagCompound SerializeScope(StatisticsData data)
        {
            List<TagCompound> entryTags = new List<TagCompound>();
            IEnumerable<KeyValuePair<StatisticKey, StatisticMetrics>> orderedEntries = data.Entries
                .Where(pair => !pair.Value.IsZero)
                .OrderBy(pair => pair.Key.StatisticId, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Content?.ModName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Content?.ContentName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Style, StringComparer.Ordinal);

            foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in orderedEntries)
            {
                TagCompound entryTag = new TagCompound
                {
                    [StatisticIdKey] = pair.Key.StatisticId
                };

                if (pair.Key.Content.HasValue)
                {
                    entryTag[ContentModKey] = pair.Key.Content.Value.ModName;
                    entryTag[ContentNameKey] = pair.Key.Content.Value.ContentName;
                }

                if (!string.IsNullOrEmpty(pair.Key.Style))
                {
                    entryTag[StyleKey] = pair.Key.Style;
                }

                if (pair.Value.Events != 0L)
                {
                    entryTag[EventsKey] = pair.Value.Events;
                }

                if (pair.Value.Units != 0L)
                {
                    entryTag[UnitsKey] = pair.Value.Units;
                }

                if (pair.Value.Amount != 0L)
                {
                    entryTag[AmountKey] = pair.Value.Amount;
                }

                entryTags.Add(entryTag);
            }

            return new TagCompound
            {
                [FormatVersionKey] = data.FormatVersion,
                [StartedAtKey] = data.StartedAtUtc.ToUnixTimeMilliseconds(),
                [EntriesKey] = entryTags
            };
        }

        /// <summary>
        /// 尝试恢复单个统计范围，同时跳过其中损坏的独立条目。
        /// </summary>
        private static bool TryDeserializeScope(TagCompound tag, out StatisticsData data)
        {
            data = null;
            if (tag == null)
            {
                return false;
            }

            try
            {
                int formatVersion = tag.GetInt(FormatVersionKey);
                if (formatVersion != StatisticsDataFormat.CurrentVersion
                    || !tag.ContainsKey(StartedAtKey))
                {
                    return false;
                }

                DateTimeOffset startedAtUtc =
                    DateTimeOffset.FromUnixTimeMilliseconds(tag.GetLong(StartedAtKey));
                StatisticsData loadedData = new StatisticsData(formatVersion, startedAtUtc);
                foreach (TagCompound entryTag in tag.GetList<TagCompound>(EntriesKey))
                {
                    if (TryDeserializeEntry(entryTag, out StatisticKey key, out StatisticMetrics metrics))
                    {
                        // 重复键按增量合并，损坏存档不会覆盖先前已经读到的有效值。
                        loadedData.Add(key, metrics);
                    }
                }

                data = loadedData;
                return true;
            }
            catch (Exception exception) when (IsRecoverableDataError(exception))
            {
                return false;
            }
        }

        /// <summary>
        /// 尝试恢复一项非零统计记录，并验证内容键的完整性。
        /// </summary>
        private static bool TryDeserializeEntry(
            TagCompound tag,
            out StatisticKey key,
            out StatisticMetrics metrics)
        {
            key = default;
            metrics = default;

            try
            {
                string statisticId = tag.GetString(StatisticIdKey);
                bool hasContentMod = tag.ContainsKey(ContentModKey);
                bool hasContentName = tag.ContainsKey(ContentNameKey);
                if (hasContentMod != hasContentName)
                {
                    return false;
                }

                StableContentKey? content = hasContentMod
                    ? new StableContentKey(
                        tag.GetString(ContentModKey),
                        tag.GetString(ContentNameKey))
                    : null;
                StatisticMetrics loadedMetrics = new StatisticMetrics(
                    tag.GetLong(EventsKey),
                    tag.GetLong(UnitsKey),
                    tag.GetLong(AmountKey));
                if (loadedMetrics.IsZero)
                {
                    return false;
                }

                key = new StatisticKey(statisticId, content, tag.GetString(StyleKey));
                metrics = loadedMetrics;
                return true;
            }
            catch (Exception exception) when (IsRecoverableDataError(exception))
            {
                return false;
            }
        }

        /// <summary>
        /// 判断异常是否属于可由损坏或旧式存档触发的预期读取错误。
        /// </summary>
        private static bool IsRecoverableDataError(Exception exception)
        {
            return exception is ArgumentException
                || exception is IOException
                || exception is InvalidCastException;
        }
    }
}
