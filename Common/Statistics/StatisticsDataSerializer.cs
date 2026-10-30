using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.ModLoader.IO;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>
    /// 将角色与世界统计转换为版本化数据；只写入非零条目，并隔离损坏条目。
    /// </summary>
    internal static class StatisticsDataSerializer
    {
        internal const string PlayerTagKey = "CharacterStatistics";
        internal const string WorldTagKey = "WorldStatistics";

        private const int MaximumNetworkEntryCount = 1_000_000;
        private const int MaximumStoredEntryCount = 1_000_000;
        private const int MaximumWorldSliceCount = 100_000;
        private const int MaximumExpansionStateCount = 100_000;
        private const int MaximumExpansionTabKeyLength = 128;
        private const int MaximumExpansionGroupPathLength = 8_192;
        private const int MaximumStatisticIdLength = 256;
        private const int MaximumContentKeyPartLength = 512;
        private const int MaximumStyleLength = 1_024;
        private const string InternalStatisticIdPrefix = "internal/";

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
        private const string ExpansionStatesKey = "ExpansionStates";
        private const string ExpansionTabKey = "Tab";
        private const string ExpansionGroupPathKey = "GroupPath";
        private const string ExpansionExpandedKey = "Expanded";

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

            TagCompound result = new TagCompound
            {
                [FormatVersionKey] = StatisticsDataFormat.CurrentVersion,
                [CareerKey] = SerializeScope(data.Career),
                [WorldSlicesKey] = worldTags,
                [ExpansionStatesKey] = SerializeExpansionStates(data.ExpansionStates),
                ["ListViewStates"] = SerializeListViewStates(data.ListViewStates)
            };
            if (data.SelectedSubTabs.Count > 0)
                result["SelectedSubTabs"] = data.SelectedSubTabs.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => new TagCompound { ["Tab"] = p.Key, ["SubTab"] = p.Value }).ToList();
            return result;
        }

        /// <summary>
        /// 将当前世界公共统计序列化为世界存档标签。
        /// </summary>
        internal static TagCompound SerializeWorld(StatisticsData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return SerializeScope(data);
        }

        /// <summary>
        /// 尝试从世界标签恢复受支持版本的公共统计。
        /// </summary>
        internal static bool TryDeserializeWorld(
            TagCompound worldTag,
            out StatisticsData data,
            out string error)
        {
            data = null;
            error = null;
            if (worldTag == null || !worldTag.ContainsKey(WorldTagKey))
            {
                return false;
            }

            try
            {
                if (TryDeserializeScope(worldTag.GetCompound(WorldTagKey), out data))
                {
                    return true;
                }

                error = "The world statistics scope is invalid or uses an unsupported format.";
                return false;
            }
            catch (Exception exception) when (IsRecoverableDataError(exception))
            {
                error = $"Failed to read world statistics data: {exception.Message}";
                return false;
            }
        }

        /// <summary>
        /// 将一个完整统计范围写入服务器世界数据同步流。
        /// </summary>
        internal static void WriteScope(BinaryWriter writer, StatisticsData data)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            List<KeyValuePair<StatisticKey, StatisticMetrics>> entries = data.Entries
                .Where(pair => IsPersistentEntry(pair.Key, pair.Value))
                .OrderBy(pair => pair.Key.StatisticId, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Content?.ModName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Content?.ContentName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Style, StringComparer.Ordinal)
                .ToList();
            writer.Write(data.FormatVersion);
            writer.Write(data.StartedAtUtc.ToUnixTimeMilliseconds());
            writer.Write(entries.Count);
            foreach (KeyValuePair<StatisticKey, StatisticMetrics> pair in entries)
            {
                writer.Write(pair.Key.StatisticId);
                writer.Write(pair.Key.Content.HasValue);
                if (pair.Key.Content.HasValue)
                {
                    writer.Write(pair.Key.Content.Value.ModName);
                    writer.Write(pair.Key.Content.Value.ContentName);
                }

                writer.Write(pair.Key.Style);
                writer.Write(pair.Value.Events);
                writer.Write(pair.Value.Units);
                writer.Write(pair.Value.Amount);
            }
        }

        /// <summary>
        /// 尝试从服务器世界数据同步流恢复一个完整统计范围。
        /// </summary>
        internal static bool TryReadScope(
            BinaryReader reader,
            out StatisticsData data,
            out string error)
        {
            data = null;
            error = null;
            if (reader == null)
            {
                error = "The statistics network reader is unavailable.";
                return false;
            }

            try
            {
                int formatVersion = reader.ReadInt32();
                if (!StatisticsDataFormat.CanLoad(formatVersion))
                {
                    error = $"Unsupported statistics network format version: {formatVersion}.";
                    return false;
                }

                DateTimeOffset startedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(
                    reader.ReadInt64());
                int entryCount = reader.ReadInt32();
                if (entryCount < 0 || entryCount > MaximumNetworkEntryCount)
                {
                    error = $"Invalid statistics network entry count: {entryCount}.";
                    return false;
                }

                // 版本 1–4 共用条目结构；仅将版本标记升级到当前值，原始指标不换算、不补算。
                StatisticsData loadedData = new StatisticsData(StatisticsDataFormat.CurrentVersion, startedAtUtc);
                for (int index = 0; index < entryCount; index++)
                {
                    string statisticId = reader.ReadString();
                    string contentMod = null;
                    string contentName = null;
                    if (reader.ReadBoolean())
                    {
                        contentMod = reader.ReadString();
                        contentName = reader.ReadString();
                    }

                    string style = reader.ReadString();
                    long events = reader.ReadInt64();
                    long units = reader.ReadInt64();
                    long amount = reader.ReadInt64();
                    if (TryCreateEntry(
                        statisticId,
                        contentMod,
                        contentName,
                        style,
                        events,
                        units,
                        amount,
                        out StatisticKey key,
                        out StatisticMetrics metrics))
                    {
                        loadedData.Add(key, metrics);
                    }
                }

                data = loadedData;
                return true;
            }
            catch (Exception exception) when (IsRecoverableDataError(exception)
                || exception is EndOfStreamException
                || exception is OverflowException)
            {
                error = $"Failed to read synchronized statistics data: {exception.Message}";
                return false;
            }
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
                if (!StatisticsDataFormat.CanLoad(formatVersion))
                {
                    error = $"Unsupported statistics data format version: {formatVersion}.";
                    return false;
                }

                if (!TryDeserializeScope(root.GetCompound(CareerKey), out StatisticsData career))
                {
                    error = "The character career statistics scope is invalid.";
                    return false;
                }

                IList<TagCompound> worldTags = Array.Empty<TagCompound>();
                if (root.ContainsKey(WorldSlicesKey))
                {
                    try
                    {
                        worldTags = root.GetList<TagCompound>(WorldSlicesKey);
                    }
                    catch (Exception exception) when (IsRecoverableDataError(exception))
                    {
                        // 世界切片列表损坏时仍保留已经成功读取的角色生涯数据。
                    }
                }

                Dictionary<Guid, StatisticsData> worldSlices =
                    new Dictionary<Guid, StatisticsData>();
                int worldCount = Math.Min(worldTags.Count, MaximumWorldSliceCount);
                for (int index = 0; index < worldCount; index++)
                {
                    TagCompound worldTag = worldTags[index];
                    try
                    {
                        // 单个世界切片损坏不应阻止角色生涯及其他有效世界的数据恢复。
                        if (worldTag == null
                            || !Guid.TryParse(worldTag.GetString(WorldIdKey), out Guid worldId)
                            || worldSlices.ContainsKey(worldId)
                            || !TryDeserializeScope(
                                worldTag.GetCompound(DataKey),
                                out StatisticsData worldData))
                        {
                            continue;
                        }

                        worldSlices.Add(worldId, worldData);
                    }
                    catch (Exception exception) when (IsRecoverableDataError(exception))
                    {
                        // 保留其他可读取的世界切片。
                    }
                }

                StatisticsExpansionStateStore expansionStates =
                    DeserializeExpansionStates(root);
                data = new CharacterStatisticsData(career, worldSlices, expansionStates);
                DeserializeListViewStates(root, data.ListViewStates);
                DeserializeSubTabs(root, data);
                StatisticsSubTabs.Migrate(data);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Failed to read statistics data: {exception.Message}";
                return false;
            }
        }

        /// <summary>
        /// 将人物生涯及各世界的 Tab 分组状态按稳定顺序写入人物存档。
        /// </summary>
        private static List<TagCompound> SerializeExpansionStates(
            StatisticsExpansionStateStore store)
        {
            List<TagCompound> stateTags = new List<TagCompound>();
            IEnumerable<KeyValuePair<StatisticsExpansionStateKey, bool>> orderedStates =
                store.Entries
                    .OrderBy(pair => pair.Key.WorldId.HasValue ? 1 : 0)
                    .ThenBy(pair => pair.Key.WorldId ?? Guid.Empty)
                    .ThenBy(pair => pair.Key.TabKey, StringComparer.Ordinal)
                    .ThenBy(pair => pair.Key.GroupPath, StringComparer.Ordinal);

            foreach (KeyValuePair<StatisticsExpansionStateKey, bool> pair in orderedStates)
            {
                TagCompound stateTag = new TagCompound
                {
                    [ExpansionTabKey] = pair.Key.TabKey,
                    [ExpansionGroupPathKey] = pair.Key.GroupPath,
                    [ExpansionExpandedKey] = pair.Value
                };
                if (pair.Key.WorldId.HasValue)
                {
                    stateTag[WorldIdKey] = pair.Key.WorldId.Value.ToString("N");
                }

                stateTags.Add(stateTag);
            }

            return stateTags;
        }

        /// <summary>
        /// 从人物存档恢复分组状态；旧存档缺少该字段时返回空状态，损坏条目独立跳过。
        /// </summary>
        private static StatisticsExpansionStateStore DeserializeExpansionStates(TagCompound root)
        {
            StatisticsExpansionStateStore store = new StatisticsExpansionStateStore();
            if (!root.ContainsKey(ExpansionStatesKey))
            {
                return store;
            }

            IList<TagCompound> stateTags;
            try
            {
                stateTags = root.GetList<TagCompound>(ExpansionStatesKey);
            }
            catch (Exception exception) when (IsRecoverableDataError(exception))
            {
                return store;
            }

            int stateCount = Math.Min(stateTags.Count, MaximumExpansionStateCount);
            for (int index = 0; index < stateCount; index++)
            {
                TagCompound stateTag = stateTags[index];
                if (stateTag == null || !stateTag.ContainsKey(ExpansionExpandedKey))
                {
                    continue;
                }

                try
                {
                    string tabKey = stateTag.GetString(ExpansionTabKey);
                    string groupPath = stateTag.GetString(ExpansionGroupPathKey);
                    if (tabKey.Length == 0
                        || tabKey.Length > MaximumExpansionTabKeyLength
                        || groupPath.Length == 0
                        || groupPath.Length > MaximumExpansionGroupPathLength)
                    {
                        continue;
                    }

                    Guid? worldId = null;
                    if (stateTag.ContainsKey(WorldIdKey))
                    {
                        if (!Guid.TryParse(stateTag.GetString(WorldIdKey), out Guid parsedWorldId))
                        {
                            continue;
                        }

                        worldId = parsedWorldId;
                    }

                    store.AddLoadedState(
                        new StatisticsExpansionStateKey(tabKey, groupPath, worldId),
                        stateTag.GetBool(ExpansionExpandedKey));
                }
                catch (Exception exception) when (IsRecoverableDataError(exception))
                {
                    // 单个 UI 状态损坏不应阻止人物统计和其他有效折叠状态加载。
                }
            }

            return store;
        }

        /// <summary>读取可选的角色子页选择；单项损坏或未知身份不影响统计和其他页面。</summary>
        private static void DeserializeSubTabs(TagCompound root, CharacterStatisticsData data)
        {
            if (!root.ContainsKey("SelectedSubTabs")) return;
            try
            {
                foreach (TagCompound tag in root.GetList<TagCompound>("SelectedSubTabs").Take(128))
                {
                    try
                    {
                        if (tag == null) continue;
                        string main = tag.GetString("Tab");
                        string id = StatisticsSubTabs.Resolve(main, tag.GetString("SubTab"));
                        if (id != null) data.SelectedSubTabs[main] = id;
                    }
                    catch (Exception error) when (IsRecoverableDataError(error)) { }
                }
            }
            catch (Exception error) when (IsRecoverableDataError(error)) { }
        }

        /// <summary>以稳定字符串保存排序枚举，避免未来枚举插入导致旧存档含义变化。</summary>
        private static List<TagCompound> SerializeListViewStates(StatisticsListViewStateStore store)
        {
            return store.Entries.OrderBy(pair => pair.Key.WorldId.HasValue ? 1 : 0)
                .ThenBy(pair => pair.Key.WorldId ?? Guid.Empty)
                .ThenBy(pair => pair.Key.Tab, StringComparer.Ordinal)
                .Select(pair =>
                {
                    TagCompound tag = new TagCompound
                    {
                        ["Tab"] = pair.Key.Tab,
                        ["SortColumn"] = pair.Value.SortColumn ?? "Name",
                        ["SortDirection"] = pair.Value.SortDirection ?? "None",
                        ["ScrollPosition"] = pair.Value.ScrollPosition
                    };
                    if (pair.Key.WorldId.HasValue)
                        tag[WorldIdKey] = pair.Key.WorldId.Value.ToString("N");
                    return tag;
                }).ToList();
        }

        /// <summary>独立跳过损坏的 UI 条目；缺少可选字段不影响旧统计与折叠状态。</summary>
        private static void DeserializeListViewStates(TagCompound root, StatisticsListViewStateStore store)
        {
            if (!root.ContainsKey("ListViewStates")) return;
            IList<TagCompound> tags;
            try { tags = root.GetList<TagCompound>("ListViewStates"); }
            catch (Exception exception) when (IsRecoverableDataError(exception)) { return; }
            foreach (TagCompound tag in tags.Take(MaximumExpansionStateCount))
            {
                try
                {
                    if (tag == null) continue;
                    string tab = tag.GetString("Tab");
                    if (tab.Length == 0 || tab.Length > MaximumExpansionTabKeyLength) continue;
                    Guid? worldId = null;
                    if (tag.ContainsKey(WorldIdKey))
                    {
                        if (!Guid.TryParse(tag.GetString(WorldIdKey), out Guid id)) continue;
                        worldId = id;
                    }
                    store.SetTabState(tab, worldId, new StatisticsListViewState(
                        tag.GetString("SortColumn"), tag.GetString("SortDirection"), tag.GetFloat("ScrollPosition")));
                }
                catch (Exception exception) when (IsRecoverableDataError(exception))
                {
                    // 视图损坏不能丢弃人物统计，也不能污染其他范围。
                }
            }
        }

        /// <summary>
        /// 将单个查看范围序列化，并按稳定键排序以保证输出可复现。
        /// </summary>
        private static TagCompound SerializeScope(StatisticsData data)
        {
            List<TagCompound> entryTags = new List<TagCompound>();
            IEnumerable<KeyValuePair<StatisticKey, StatisticMetrics>> orderedEntries = data.Entries
                .Where(pair => IsPersistentEntry(pair.Key, pair.Value))
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
                if (!StatisticsDataFormat.CanLoad(formatVersion)
                    || !tag.ContainsKey(StartedAtKey))
                {
                    return false;
                }

                DateTimeOffset startedAtUtc =
                    DateTimeOffset.FromUnixTimeMilliseconds(tag.GetLong(StartedAtKey));
                // 液体只新增稳定键并使用既有 Units；旧范围的开始时间、累计与内容身份原样保留。
                StatisticsData loadedData = new StatisticsData(StatisticsDataFormat.CurrentVersion, startedAtUtc);
                IList<TagCompound> entryTags = Array.Empty<TagCompound>();
                if (tag.ContainsKey(EntriesKey))
                {
                    try
                    {
                        entryTags = tag.GetList<TagCompound>(EntriesKey);
                    }
                    catch (Exception exception) when (IsRecoverableDataError(exception))
                    {
                        // 条目列表损坏时保留有效的范围元数据，并按空统计继续加载。
                    }
                }

                int entryCount = Math.Min(entryTags.Count, MaximumStoredEntryCount);
                for (int index = 0; index < entryCount; index++)
                {
                    if (TryDeserializeEntry(
                        entryTags[index],
                        out StatisticKey key,
                        out StatisticMetrics metrics))
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
            if (tag == null)
            {
                return false;
            }

            try
            {
                string statisticId = tag.GetString(StatisticIdKey);
                bool hasContentMod = tag.ContainsKey(ContentModKey);
                bool hasContentName = tag.ContainsKey(ContentNameKey);
                if (hasContentMod != hasContentName)
                {
                    return false;
                }

                return TryCreateEntry(
                    statisticId,
                    hasContentMod ? tag.GetString(ContentModKey) : null,
                    hasContentName ? tag.GetString(ContentNameKey) : null,
                    tag.GetString(StyleKey),
                    tag.GetLong(EventsKey),
                    tag.GetLong(UnitsKey),
                    tag.GetLong(AmountKey),
                    out key,
                    out metrics);
            }
            catch (Exception exception) when (IsRecoverableDataError(exception))
            {
                return false;
            }
        }

        /// <summary>
        /// 验证持久化键和三个非负指标，并创建一条可安全加载的正式统计记录。
        /// </summary>
        private static bool TryCreateEntry(
            string statisticId,
            string contentMod,
            string contentName,
            string style,
            long events,
            long units,
            long amount,
            out StatisticKey key,
            out StatisticMetrics metrics)
        {
            key = default;
            metrics = default;
            style ??= string.Empty;
            bool hasContentMod = contentMod != null;
            bool hasContentName = contentName != null;
            if (!IsPersistentStatisticId(statisticId)
                || style.Length > MaximumStyleLength
                || hasContentMod != hasContentName
                || (hasContentMod
                    && (string.IsNullOrWhiteSpace(contentMod)
                        || string.IsNullOrWhiteSpace(contentName)
                        || contentMod.Length > MaximumContentKeyPartLength
                        || contentName.Length > MaximumContentKeyPartLength)))
            {
                return false;
            }

            try
            {
                StatisticMetrics loadedMetrics = new StatisticMetrics(
                    events,
                    units,
                    amount);
                if (loadedMetrics.IsZero)
                {
                    return false;
                }

                StableContentKey? content = hasContentMod
                    ? new StableContentKey(contentMod, contentName)
                    : null;
                key = new StatisticKey(statisticId, content, style);
                metrics = loadedMetrics;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// 判断条目是否属于应进入正式存档与网络同步的非零统计。
        /// </summary>
        private static bool IsPersistentEntry(
            StatisticKey key,
            StatisticMetrics metrics)
        {
            return !metrics.IsZero && IsPersistentStatisticId(key.StatisticId);
        }

        /// <summary>
        /// 排除空键、过长键和开发阶段的 internal 测试键；旧存档中的测试项会被迁移丢弃。
        /// </summary>
        private static bool IsPersistentStatisticId(string statisticId)
        {
            return !string.IsNullOrWhiteSpace(statisticId)
                && statisticId.Length <= MaximumStatisticIdLength
                && !statisticId.StartsWith(
                    InternalStatisticIdPrefix,
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// 判断异常是否属于可由损坏或旧式存档触发的预期读取错误。
        /// </summary>
        private static bool IsRecoverableDataError(Exception exception)
        {
            return exception is ArgumentException
                || exception is FormatException
                || exception is IOException
                || exception is InvalidCastException
                || exception is KeyNotFoundException
                || exception is OverflowException;
        }
    }
}
