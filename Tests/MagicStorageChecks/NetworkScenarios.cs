using System;
using System.Collections.Generic;
using System.Reflection;
using ComprehensiveStatistics.Common.Networking;
using Terraria;
using GUI = MagicStorage.CraftingGUI;

/// <summary>在生产接收事务及真实结算 IL 上验证服务器成功、回退和指纹证据，不运行真实网络。</summary>
internal static class NetworkScenarios
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static int checks;

    /// <summary>生产程序集内会话用反射构造，真实生产结算 Hook 在已安装的普通/递归采集之上运行。</summary>
    internal static void Run(Type collector, List<IDisposable> hooks)
    {
        collector.GetMethod("InstallNetworkHooks", Flags).Invoke(null, new object[] { typeof(GUI).Assembly, hooks });
        Type sessionType = collector.Assembly.GetType("ComprehensiveStatistics.Common.Networking.MagicStorageConfirmServer");
        Type peerType = collector.GetNestedType("Peer", Flags), incomingType = collector.GetNestedType("Incoming", Flags);
        MethodInfo run = collector.GetMethod("ExecuteIncomingRequest", Flags);
        GUI.AcceptMaterials = true; GUI.KeepLeftovers = false;
        List<Item> Inputs() => new() { new Item { type = 9, stack = 2 } };
        List<Item> Outputs() => new() { new Item { type = 8, stack = 6 } };
        byte[] hash = MagicStorageConfirmation.Fingerprint(0, 0, new[] { new MagicStorageStack(9, 2, 0, "Terraria/Wood", "Terraria/0") }, new[] { new MagicStorageStack(8, 6, 0, "Terraria/Torch", "Terraria/0") });
        object Incoming(byte[] fingerprint)
        {
            object session = Activator.CreateInstance(sessionType, Flags, null, new object[] { Guid.NewGuid(), (byte)3 }, null);
            sessionType.GetField("Ready", Flags).SetValue(session, true);
            sessionType.GetMethod("Begin", Flags).Invoke(session, new object[] { 1UL, (byte)1, fingerprint, 0UL });
            object window = sessionType.GetMethod("Take", Flags).Invoke(session, new object[] { 0UL });
            object peer = Activator.CreateInstance(peerType, Flags, null, new[] { null, session }, null);
            return Activator.CreateInstance(incomingType, Flags, null, new[] { peer, window }, null);
        }
        bool? Confirm(Action operation, byte[] fingerprint = null, bool noWindow = false)
        {
            bool? success = null;
            try { run.Invoke(null, new object[] { operation, noWindow ? null : Incoming(fingerprint ?? hash), (Action<bool>)(value => success = value) }); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
            return success;
        }
        Check(Confirm(() => GUI.HandleCraftWithdrawAndDeposit(new(), Inputs(), Outputs())) == true, "actual matching settlement accepts");
        GUI.AcceptMaterials = false;
        Check(Confirm(() => GUI.HandleCraftWithdrawAndDeposit(new(), Inputs(), Outputs())) == false, "withdrawal rollback sends failure");
        GUI.AcceptMaterials = true;
        Check(Confirm(() => { var output = Outputs(); output[0].stack++; GUI.HandleCraftWithdrawAndDeposit(new(), Inputs(), output); }) == false, "actual payload mismatch cannot confirm");
        Check(Confirm(() => { }) == false, "invalid heart or receiver early return cannot confirm");
        Check(Confirm(() => { GUI.HandleCraftWithdrawAndDeposit(new(), Inputs(), Outputs()); throw new InvalidOperationException("fixture"); }) == false, "receiver exception after settlement cannot send success");
        GUI.KeepLeftovers = true;
        Check(Confirm(() => GUI.HandleCraftWithdrawAndDeposit(new(), Inputs(), Outputs())) == true, "full storage excess delivery remains successful");
        Check(Confirm(() => GUI.HandleCraftWithdrawAndDeposit(new(), Inputs(), Outputs()), noWindow: true) == null, "unmarked or unauthenticated request cannot confirm");
        Check(Confirm(() => { run.Invoke(null, new object[] { (Action)(() => GUI.HandleCraftWithdrawAndDeposit(new(), Inputs(), Outputs())), null, (Action<bool>)(_ => { }) }); }) == false,
            "nested unrelated receiver cannot inherit outer authority");
        Console.WriteLine($"MAGIC_STORAGE_NETWORK_SCENARIOS_OK: {checks} production receiver/settlement checks; simulated lists, not live network validation");
    }

    /// <summary>验证服务器证据不变量。</summary>
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
}
