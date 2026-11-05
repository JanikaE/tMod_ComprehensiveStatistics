using System;
using System.Collections.Generic;
using System.Reflection;
using ComprehensiveStatistics.Common.Players;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ModLoader;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>为自动重铸的实际付费入口绑定槽位，排除前缀预览产生的后置回调。</summary>
    internal sealed class AutoRerollCompatibilitySystem : ModSystem
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private readonly List<IDisposable> hooks = new();
        private static FieldInfo slotField;
        private static FieldInfo itemField;
        [ThreadStatic] private static Item activeItem;

        /// <summary>提供当前真实重铸的物品，优先于上游用于预览的 Main.reforgeItem。</summary>
        internal static Item ActiveItem => activeItem;

        public override void PostSetupContent()
        {
            if (Main.dedServ || !ModLoader.TryGetMod("AutoReroll", out Mod upstream)) return;
            try
            {
                InstallHooks(upstream.Code, hooks);
                Mod.Logger.Info($"AutoReroll {upstream.Version} paid reforge statistics enabled.");
            }
            catch (Exception error)
            {
                ReleaseHooks();
                Mod.Logger.Warn($"AutoReroll reforge statistics disabled: {error.Message}");
            }
        }

        public override void Unload() => ReleaseHooks();

        /// <summary>验证实际签名，先安装单次重铸叶子，再重建手动和自动入口以移除旧内联副本。</summary>
        internal static void InstallHooks(Assembly code, List<IDisposable> installed)
        {
            Type ui = code.GetType("GadgetBox.GadgetUI.ReforgeMachineUI", true);
            Type slot = code.GetType("GadgetBox.GadgetUI.UIItemSlot", true);
            FieldInfo slotBinding = ui.GetField("reforgeSlot", Members);
            FieldInfo itemBinding = slot.GetField("item", Members);
            if (slotBinding?.FieldType != slot || itemBinding?.FieldType != typeof(Item))
                throw new MissingFieldException("AutoReroll reforge slot signature changed.");
            MethodInfo root = RequireMethod(ui, "ReforgeItem", typeof(bool));
            MethodInfo manual = RequireMethod(ui, "OnReforgeButtonClick", typeof(Terraria.UI.UIMouseEvent), typeof(Terraria.UI.UIElement));
            MethodInfo automatic = RequireMethod(ui, "Update", typeof(Microsoft.Xna.Framework.GameTime));
            slotField = slotBinding;
            itemField = itemBinding;
            installed.Add(new Hook(root, (Action<Action<object, bool>, object, bool>)HandleReforge));
            installed.Add(new ILHook(manual, RecompileCaller));
            installed.Add(new ILHook(automatic, RecompileCaller));
        }

        /// <summary>只接受声明在目标类型上的实例 void 方法，签名变化时停止该兼容。</summary>
        private static MethodInfo RequireMethod(Type type, string name, params Type[] arguments)
        {
            MethodInfo method = type.GetMethod(name, Members, null, arguments, null);
            if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        /// <summary>固定单次付费的槽位，完成或异常后立即丢弃未消费快照，预览不能补记旧付款。</summary>
        private static void HandleReforge(Action<object, bool> orig, object ui, bool silent)
        {
            Item item = itemField.GetValue(slotField.GetValue(ui)) as Item;
            Player player = Main.LocalPlayer;
            StatisticsPlayer statistics = player?.GetModPlayer<StatisticsPlayer>();
            Item previous = activeItem;
            statistics?.ClearPendingReforge();
            activeItem = item;
            try
            {
                orig(ui, silent);
            }
            finally
            {
                activeItem = previous;
                statistics?.ClearPendingReforge();
            }
        }

        /// <summary>让已经预编译的调用方重新生成，保证它们经过叶子 Hook。</summary>
        private static void RecompileCaller(ILContext il) => new ILCursor(il).Emit(Mono.Cecil.Cil.OpCodes.Nop);

        /// <summary>逆序释放已安装入口，失败时也不留下半安装状态或槽位引用。</summary>
        private void ReleaseHooks()
        {
            for (int index = hooks.Count - 1; index >= 0; index--) hooks[index].Dispose();
            hooks.Clear();
            activeItem = null;
            slotField = null;
            itemField = null;
        }
    }
}
