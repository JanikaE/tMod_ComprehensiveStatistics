using System.Collections.Generic;

namespace ComprehensiveStatistics.Common.Statistics
{
    /// <summary>保存直接施工目标的两侧值；候选不包含可直接上传的统计数量。</summary>
    internal readonly record struct TileChangeCandidate(int X, int Y, TileChangeLayer Layer, TileChangeIntent Intent,
        TileContentSnapshot Before, TileContentSnapshot After);

    /// <summary>使客户端施工只生成精确候选，并为实际清理提供有序同步边界。</summary>
    internal interface ITileChangeCandidateObserver
    {
        /// <summary>在首个直接目标修改前开启已绑定的施工窗口。</summary>
        void Observe();
        /// <summary>在旧内容确实移除后同步当前状态，不用最终净差猜测中间拆除。</summary>
        void Removed(int x, int y, TileChangeLayer layer, TileChangeIntent intent, TileContentSnapshot before);
        /// <summary>同步步骤结束后提交直接目标，不在客户端直接增加统计。</summary>
        void Complete(IReadOnlyList<TileChangeCandidate> targets);
    }
}
