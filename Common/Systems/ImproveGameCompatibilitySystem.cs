using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace ComprehensiveStatistics.Common.Systems
{
    /// <summary>
    /// 在内容加载完成后独立探测可选模组的已审计入口；具体工具是否启用由各能力适配器单独决定。
    /// 所有上游类型通过其程序集查找，缺少 ImproveGame 时不会解析或加载任何强类型依赖。
    /// </summary>
    internal sealed class ImproveGameCompatibilitySystem : ModSystem
    {
        private const BindingFlags DeclaredMethods = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        public override void PostSetupContent()
        {
            if (!ModLoader.TryGetMod("ImproveGame", out Mod improveGame))
                return;
            try
            {
                Assembly code = improveGame.Code;
                bool construction = CheckMethod(code, "ImproveGame.Content.Functions.Construction.GenerateCore",
                    "GenerateFromTag", true, typeof(void), typeof(TagCompound), typeof(Point));
                construction &= CheckIterator(code, "ImproveGame.Content.Functions.Construction.GenerateCore",
                    "Generate", typeof(TagCompound), typeof(Point));
                Type structure = code.GetType("ImproveGame.Content.Functions.Construction.QoLStructure");
                foreach (string name in new[] { "KillTiles", "GenerateWalls", "GenerateSingleTiles",
                    "GenerateMultiTiles", "GenerateOutSet" })
                    construction &= structure != null && CheckIterator(code,
                        "ImproveGame.Content.Functions.Construction.GenerateCore", name, structure, typeof(Point));

                bool building = CheckMethod(code, "ImproveGame.Content.Items.CreateWand", "TryPlace",
                    true, typeof(bool), typeof(Item), typeof(Player), typeof(int), typeof(int));
                building &= CheckMethod(code, "ImproveGame.Content.Items.CreateWand", "TryPlaceWall",
                    true, typeof(bool), typeof(Item), typeof(Player), typeof(int), typeof(int));
                building &= CheckMethod(code, "ImproveGame.Helpers.MyUtils", "TryPlaceMultiTileDirect",
                    true, typeof(bool), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
                    typeof(TileObject).MakeByRefType());
                bool selector = CheckMethod(code, "ImproveGame.Content.Items.SelectorItem", "ModifyTiles",
                    false, typeof(IEnumerator), typeof(Player), typeof(Rectangle));
                bool robot = CheckMethod(code, "ImproveGame.Content.Projectiles.WallRobot", "AI",
                    false, typeof(void));

                Type packet = code.GetType("ImproveGame.Content.Packets.SpaceWandOperation");
                // 网络基类在独立 NetSimplified 程序集中；只读取已加载类型，不自行加载 DLL。
                PropertyInfo sender = packet?.GetProperty("Sender", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                bool senderKnown = sender?.PropertyType == typeof(int) && sender.GetMethod != null;
                Mod.Logger.Info($"ImproveGame {improveGame.Version} entry audit: "
                    + $"construction={construction}, building={building}, selector={selector}, "
                    + $"robot={robot}, sender={senderKnown}. See individual adapter logs for enabled capabilities.");
            }
            catch (Exception exception) when (exception is TypeLoadException
                || exception is ReflectionTypeLoadException || exception is AmbiguousMatchException
                || exception is System.IO.FileNotFoundException || exception is System.IO.FileLoadException
                || exception is BadImageFormatException)
            {
                // 签名或依赖变化只关闭可选探测，不妨碍原版统计加载。
                Mod.Logger.Warn($"ImproveGame entry audit unavailable: {exception.Message}");
            }
        }

        /// <summary>
        /// 精确比较声明类、静态性、返回值和参数，不能仅凭同名方法判断可安装 Hook。
        /// </summary>
        private static bool CheckMethod(Assembly code, string typeName, string methodName,
            bool isStatic, Type returnType, params Type[] parameters)
        {
            Type type = code.GetType(typeName);
            MethodInfo method = type?.GetMethod(methodName, DeclaredMethods, null, parameters, null);
            return method != null && method.IsStatic == isStatic && method.ReturnType == returnType
                && !method.ContainsGenericParameters && method.GetMethodBody() != null;
        }

        /// <summary>
        /// 同时检查迭代器工厂和实际 MoveNext 方法，避免把返回 IEnumerator 当作已覆盖分帧执行。
        /// </summary>
        private static bool CheckIterator(Assembly code, string typeName, string methodName, params Type[] parameters)
        {
            if (!CheckMethod(code, typeName, methodName, true, typeof(IEnumerator), parameters))
                return false;
            MethodInfo factory = code.GetType(typeName).GetMethod(methodName, DeclaredMethods, null, parameters, null);
            Type stateMachine = factory.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            return stateMachine != null && typeof(IEnumerator).IsAssignableFrom(stateMachine)
                && stateMachine.GetMethod("MoveNext", DeclaredMethods, null, Type.EmptyTypes, null)?.ReturnType == typeof(bool);
        }
    }
}
