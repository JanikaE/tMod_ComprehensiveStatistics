using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ComprehensiveStatistics.Common.Systems;
using ComprehensiveStatistics.Common.Players;
using Terraria;

/// <summary>用隔离玩家和回调验证真实生产采集器，不运行游戏。</summary>
internal static class Program
{
    /// <summary>覆盖真实作用域、付款筛选、预览、异常清理及两种调用入口。</summary>
    private static void Main()
    {
        MainSetup();
        var system = new StatisticsReforgeSystem();
        system.Load();
        var hooks = new List<IDisposable>();
        var ui = new GadgetBox.GadgetUI.ReforgeMachineUI();
        foreach (string name in new[] { "ReforgeItem", "OnReforgeButtonClick", "Update" })
            RuntimeHelpers.PrepareMethod(ui.GetType().GetMethod(name).MethodHandle);
        AutoRerollCompatibilitySystem.InstallHooks(Assembly.GetExecutingAssembly(), hooks);
        var stats = Terraria.Main.LocalPlayer.Statistics;
        try
        {
            ui.OnReforgeButtonClick(null, null);
            Check(stats.Count == 1 && stats.Amount == 123 && stats.LastType == 7, "Manual custom slot");
            ui.Update(null);
            Check(stats.Count == 2 && stats.Amount == 246, "Automatic once per operation");
            // 上游预览会给克隆物品调用 PostReforge，但此时不得持有付款。
            stats.RecordSuccessfulReforge(new Item { type = 7 });
            Check(stats.Count == 2, "Unpaid preview");
            Terraria.Main.LocalPlayer.Paid = false;
            ui.Update(null);
            Check(stats.Count == 2, "Rejected payment");
            Terraria.Main.LocalPlayer.Paid = true;
            ui.Currency = 2;
            ui.Update(null);
            Check(stats.Count == 2, "Special currency excluded");
            ui.Currency = -1;
            ui.Complete = false;
            ui.Update(null);
            stats.RecordSuccessfulReforge(new Item { type = 7 });
            Check(stats.Count == 2, "Missing completion does not leak into preview");
            ui.Throw = true;
            try { ui.Update(null); } catch (InvalidOperationException) { }
            Check(AutoRerollCompatibilitySystem.ActiveItem == null && stats.Pending == null, "Exception clears scope/payment");
            ui.Throw = false;
            ui.Complete = true;
            ui.Price = 0;
            ui.Update(null);
            Check(stats.Count == 3 && stats.Amount == 246, "Free successful reforge");
            ui.Price = 51;
            Terraria.Main.InReforgeMenu = true;
            Terraria.Main.reforgeItem = new Item { type = 99 };
            ui.Update(null);
            Check(stats.Count == 4 && stats.Amount == 297 && stats.LastType == 7, "Custom slot wins and no duplicate vanilla capture");
            Terraria.Main.InReforgeMenu = false;
            Terraria.Main.LocalPlayer.BuyItem(10, -1);
            stats.RecordSuccessfulReforge(new Item { type = 99 });
            Check(stats.Count == 4, "Unrelated purchase excluded");
            Terraria.Main.InReforgeMenu = true;
            Terraria.Main.LocalPlayer.BuyItem(80, -1);
            stats.RecordSuccessfulReforge(Terraria.Main.reforgeItem);
            Check(stats.Count == 5 && stats.Amount == 377 && stats.LastType == 99, "Vanilla path retained");
            Console.WriteLine("AUTO_REROLL_SCENARIOS_OK: 11 payment, completion, cleanup and vanilla regression checks");
        }
        finally
        {
            for (int i = hooks.Count - 1; i >= 0; i--) hooks[i].Dispose();
            system.Unload();
        }
    }

    /// <summary>创建隔离玩家，原版槽位故意留空以复现自定义界面漏记。</summary>
    private static void MainSetup() => Terraria.Main.LocalPlayer = new Player { whoAmI = 0 };

    /// <summary>断言采集增量及边界。</summary>
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}

namespace GadgetBox.GadgetUI
{
    /// <summary>模拟上游独立物品槽。</summary>
    public sealed class UIItemSlot { public Item item = new Item { type = 7 }; }

    /// <summary>模拟经真实 Hook 的手动、自动及克隆物品后置回调。</summary>
    public sealed class ReforgeMachineUI
    {
        public UIItemSlot reforgeSlot = new();
        public int Price = 123, Currency = -1;
        public bool Complete = true, Throw;

        /// <summary>保留与已审计上游一致的单次扣款后重铸布局。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ReforgeItem(bool silent)
        {
            Terraria.Main.LocalPlayer.BuyItem(Price, Currency);
            if (Throw) throw new InvalidOperationException();
            if (Complete) Terraria.Main.LocalPlayer.Statistics.RecordSuccessfulReforge(new Item { type = reforgeSlot.item.type });
        }

        /// <summary>手动调用单次付费入口。</summary>
        public void OnReforgeButtonClick(Terraria.UI.UIMouseEvent evt, Terraria.UI.UIElement element) => ReforgeItem(false);

        /// <summary>自动调用同一入口。</summary>
        public void Update(Microsoft.Xna.Framework.GameTime time) => ReforgeItem(true);
    }
}

namespace Terraria
{
    /// <summary>隔离物品身份。</summary>
    public sealed class Item { public int type; public bool IsAir => type == 0; }
    /// <summary>隔离本地玩家及原版菜单状态。</summary>
    public static class Main { public static bool dedServ, InReforgeMenu; public static int myPlayer; public static Item reforgeItem; public static Player LocalPlayer; }
    /// <summary>隔离付款结果，不操作库存。</summary>
    public sealed class Player
    {
        public int whoAmI;
        public bool Paid = true;
        public StatisticsPlayer Statistics = new();
        /// <summary>返回隔离统计对象。</summary>
        public T GetModPlayer<T>() where T : class => Statistics as T;
        /// <summary>调用生产采集器注册的付款 Hook。</summary>
        public bool BuyItem(long price, int currency) => On_Player.Handler?.Invoke((p, value, kind) => p.Paid, this, price, currency) ?? Paid;
    }
    /// <summary>隔离原版付款事件。</summary>
    public static class On_Player
    {
        /// <summary>原始扣款签名。</summary>
        public delegate bool orig_BuyItem(Player player, long price, int currency);
        /// <summary>扣款拦截签名。</summary>
        public delegate bool hook_BuyItem(orig_BuyItem orig, Player player, long price, int currency);
        public static event hook_BuyItem BuyItem { add => Handler += value; remove => Handler -= value; }
        public static hook_BuyItem Handler;
    }
}
namespace ComprehensiveStatistics.Common.Players
{
    /// <summary>模拟既有快照消费者，用于观察生产 Hook 是否传递正确付款和生命周期。</summary>
    public sealed class StatisticsPlayer
    {
        public Item Pending;
        public long Price, Amount;
        public int Count, LastType;
        /// <summary>保存待消费付款。</summary>
        public void ObserveReforgePayment(Item item, long price) { Pending = item; Price = price; }
        /// <summary>模拟既有一次性类型核对，并累计观察值。</summary>
        public void RecordSuccessfulReforge(Item item)
        {
            if (Pending != null && Pending.type == item.type) { Count++; Amount += Price; LastType = item.type; }
            ClearPendingReforge();
        }
        /// <summary>清除付款候选。</summary>
        public void ClearPendingReforge() => Pending = null;
    }
}
namespace Terraria.ModLoader
{
    /// <summary>隔离模组生命周期。</summary>
    public abstract class ModSystem
    {
        public Mod Mod = new();
        public virtual void Load() { }
        public virtual void Unload() { }
        public virtual void PostSetupContent() { }
    }
    /// <summary>隔离可选模组元数据。</summary>
    public sealed class Mod { public Assembly Code; public Version Version; public Logger Logger = new(); }
    /// <summary>隔离日志。</summary>
    public sealed class Logger
    {
        /// <summary>忽略隔离测试信息日志。</summary>
        public void Info(string text) { }
        /// <summary>忽略隔离测试警告日志。</summary>
        public void Warn(string text) { }
    }
    /// <summary>隔离模组发现。</summary>
    public static class ModLoader
    {
        /// <summary>隔离夹具不自动发现模组。</summary>
        public static bool TryGetMod(string name, out Mod mod) { mod = null; return false; }
    }
}
namespace Terraria.UI
{
    /// <summary>隔离鼠标事件签名。</summary>
    public sealed class UIMouseEvent { }
    /// <summary>隔离界面元素签名。</summary>
    public sealed class UIElement { }
}
namespace Microsoft.Xna.Framework
{
    /// <summary>隔离游戏时间签名。</summary>
    public sealed class GameTime { }
}
