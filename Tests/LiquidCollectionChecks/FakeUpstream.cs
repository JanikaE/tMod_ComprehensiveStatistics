using System;
using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Terraria;

namespace ImproveGame.Content.Items
{
    /// <summary>生成真实 C# 迭代器，签名与本机上游一致；不把生产 Hook 替换成模拟 Hook。</summary>
    internal class SelectorItem
    {
        /// <summary>上游逐格扩展入口。</summary>
        public virtual bool ModifySelectedTiles(Player player,int x,int y)=>true;
        /// <summary>模拟真实选区工厂，每个格位后让出执行权便于验证跨帧身份。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal IEnumerator ModifyTiles(Player player,Rectangle area)
        {
            for(int y=area.Y;y<area.Y+area.Height;y++)
            for(int x=area.X;x<area.X+area.Width;x++)
            {
                if(!ModifySelectedTiles(player,x,y))yield break;
                yield return null;
            }
            Post?.Invoke();
        }
        internal Action Post;
        internal IEnumerator Pending;
        internal Rectangle Selection = new(4,4,1,1);

        /// <summary>真实游戏的框选入口体积较大不会内联；小工厂可在 tML 预编译时内联进该入口。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DoSelector(Player player) { Pending = ModifyTiles(player, Selection); }

        /// <summary>从已预编译的用户操作路径启动，区别于测试直接调用工厂。</summary>
        internal void Launch(Player player) { DoSelector(player); }
    }
    /// <summary>本机真实上游规则的最小状态夹具，主动调用也可注入嵌套与异常。</summary>
    internal class LiquidWand:SelectorItem
    {
        internal bool IsAdvancedWand,AbsorptionMode,Infinite,ReturnFalse;
        internal int LiquidMode,Storage=1000,Cap=1000,ThrowX=-1;
        internal Action<Player,int,int> Custom;

        /// <summary>小型叶子刻意允许内联，测试安装顺序不能依赖 NoInlining。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool ModifySelectedTiles(Player player,int x,int y)=>Apply(player,x,y);

        /// <summary>按实际液体量处理储量不足、满储量清除、无限填充及微光能力。</summary>
        private bool Apply(Player player,int x,int y)
        {
            if(Custom!=null){Custom(player,x,y);return !ReturnFalse;}
            Tile tile=Main.Tiles[x,y];
            if(AbsorptionMode)
            {
                if(tile.LiquidType!=3||IsAdvancedWand)
                {Storage=Math.Min(Cap,Storage+tile.LiquidAmount);tile.LiquidAmount=0;}
            }
            else if((tile.LiquidAmount==0||tile.LiquidType==LiquidMode)&&(LiquidMode!=3||IsAdvancedWand))
            {
                tile.LiquidType=LiquidMode;
                int amount=Infinite||IsAdvancedWand?255-tile.LiquidAmount:Math.Min(Storage,255-tile.LiquidAmount);
                tile.LiquidAmount+=amount;
                if(!Infinite&&!IsAdvancedWand)Storage-=amount;
            }
            if(x==ThrowX)throw new InvalidOperationException("upstream failure");
            return !ReturnFalse;
        }
    }
    /// <summary>高级形态继承同一个叶子方法，测试重复挂接保护。</summary>
    internal sealed class LiquidWandAdvanced:LiquidWand
    {
        /// <summary>对应实际高级形态标记。</summary>
        internal LiquidWandAdvanced(){IsAdvancedWand=true;}
    }
}
