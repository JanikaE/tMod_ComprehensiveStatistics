using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;

namespace ComprehensiveStatistics.Common.UI.Elements
{
    /// <summary>物块页标题旁的紧凑说明入口；只构造视觉元素，运行时提示由窗口管理。</summary>
    internal sealed class CompatibilityInfoButton : UIPanel
    {
        /// <summary>使用通用问号避免中英文标题挤占说明入口，完整名称在悬停提示中显示。</summary>
        internal CompatibilityInfoButton()
        {
            SetPadding(3f);
            OverflowHidden = true;
            var text = new UIText("?", 0.8f) { HAlign = 0.5f, VAlign = 0.5f, IgnoresMouseInteraction = true };
            Append(text);
        }

        public override void Update(GameTime gameTime)
        {
            BackgroundColor = IsMouseHovering ? new Color(63, 82, 151, 245) : new Color(45, 59, 110, 235);
            BorderColor = new Color(75, 97, 174, 255);
            base.Update(gameTime);
        }
    }
}
