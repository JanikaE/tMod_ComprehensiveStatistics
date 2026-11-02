using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.ModLoader;

namespace MagicStorage.Components
{
    /// <summary>隔离存储身份，不读取或修改真实世界。</summary>
    public sealed class TEStorageHeart : ModTileEntity
    {
        public override bool IsTileValidForEntity(int x, int y) => true;
    }
}

namespace MagicStorage
{
    /// <summary>保留真实上游网络入口签名，具体收发由事务夹具控制。</summary>
    public static class NetHelper
    {
        /// <summary>不发送真实网络包。</summary>
        public static void SendCraftRequest(Terraria.DataStructures.Point16 heart, List<Item> withdrawn, List<Item> results) { }
        /// <summary>不读取真实网络流。</summary>
        public static void ReceiveCraftRequest(System.IO.BinaryReader reader, int sender) { }
    }
}

namespace MagicStorage
{
    /// <summary>只模拟已审计调用关系、提取回退和产物存入，不运行真实配方或库存。</summary>
    public static class CraftingGUI
    {
        /// <summary>与上游必需字段一致的普通合成上下文。</summary>
        public sealed class CraftingContext
        {
            public List<Item> results = new(), toWithdraw = new();
            public bool simulation;
        }

        public static bool SimulatingCrafts { get; set; }
        public static bool AcceptMaterials = true, KeepLeftovers, UseRepeated;
        public static Action<CraftingContext> Produce;
        public static Action<Item> ModifyCallback;
        public static CraftingContext Current;
        public static bool UseRecursion, StopRecursion;
        public static Common.Systems.RecurrentRecipes.CraftingSimulation Simulation;

        /// <summary>模拟闭包执行普通步骤后再执行材料结算。</summary>
        public static void Craft(int amount)
        {
            CraftingContext context;
            if (UseRecursion) context = Craft_WithRecursion(amount);
            else
            {
                context = new CraftingContext();
                Action action = () => Craft_DoStandardCraft(context);
                action();
            }
            Current = context;
            HandleCraftWithdrawAndDeposit(new Components.TEStorageHeart(), context.toWithdraw, context.results);
        }

        /// <summary>通过与上游同名闭包调用递归步骤。</summary>
        public static CraftingContext Craft_WithRecursion(int amount)
        {
            var context = new CraftingContext();
            Action action = () => Craft_DoRecursionCraft(context);
            action();
            return context;
        }

        /// <summary>取得原模拟对象，通过实际回调生成最终余量，不制造中间物品。</summary>
        public static void Craft_DoRecursionCraft(CraftingContext context)
        {
            var simulation = GetCraftingSimulationForCurrentRecipe();
            if (StopRecursion || simulation.AmountCrafted <= 0) return;
            foreach (var item in simulation.ExcessResults) Output(context, item.type, item.Stack);
        }

        /// <summary>返回本次实际使用的缓存模拟对象。</summary>
        public static Common.Systems.RecurrentRecipes.CraftingSimulation GetCraftingSimulationForCurrentRecipe() => Simulation;

        /// <summary>普通步骤经过批量产物叶子。</summary>
        public static void Craft_DoStandardCraft(CraftingContext context)
        {
            if (!AttemptLazyBatchCraft(context)) AttemptCraft(_ => true, context);
        }

        /// <summary>执行各测试指定的真实产物通知。</summary>
        public static bool AttemptLazyBatchCraft(CraftingContext context) { if (UseRepeated) return false; Produce(context); return true; }

        /// <summary>保留重复单件合成签名和回调路径供安装器处理。</summary>
        public static void AttemptCraft(Func<CraftingContext, bool> step, CraftingContext context) => Produce(context);

        /// <summary>模拟上游把新产物加入上下文后发送成功回调。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Item Output(CraftingContext context, int type, int units)
        {
            var item = new Item { type = type, stack = units };
            context.results.Add(item);
            RecipeLoader.OnCraft(item, null, new List<Item>(), new Item());
            return item;
        }

        /// <summary>成功提取后才清理材料列表并枚举产物；失败提前返回，模拟上游相同的 IL 边界。</summary>
        public static List<Item> HandleCraftWithdrawAndDeposit(Components.TEStorageHeart heart, List<Item> toWithdraw, List<Item> results)
        {
            var leftovers = new List<Item>();
            foreach (Item item in toWithdraw) leftovers.Add(item);
            if (!AcceptMaterials) return leftovers;
            leftovers.Clear();
            foreach (Item item in results)
            {
                if (KeepLeftovers) leftovers.Add(item);
                else item.stack = 0;
            }
            return leftovers;
        }
    }
}
