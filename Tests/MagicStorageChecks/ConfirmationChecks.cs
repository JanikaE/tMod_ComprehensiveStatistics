using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComprehensiveStatistics.Common.Networking;

/// <summary>直接链接生产会话模型，检查指纹、双条件提交、去重、预算及角色隔离。</summary>
internal static class ConfirmationChecks
{
    private static int checks;

    /// <summary>所有输入均为内存标量，不连接网络、不访问真实角色。</summary>
    internal static void Run()
    {
        MagicStorageStack[] withdrawn = { new(9, 2, 0), new(23, 2, 0) };
        MagicStorageStack[] results = { new(8, 6, 0) };
        byte[] Hash() => MagicStorageConfirmation.Fingerprint(3, 4, withdrawn, results);
        var server = new MagicStorageConfirmServer(Guid.NewGuid(), 3);
        Check(!server.Begin(1, 1, Hash(), 0), "ready handshake required"); server.Ready = true;
        Check(server.Begin(1, 1, Hash(), 0), "first sequential request accepted");
        var window = server.Take(0);
        Check(window.Matches(Hash()) && server.Take(0) == null, "one actual request consumes window once");
        Check(!server.Begin(1, 1, Hash(), 0) && !server.Begin(3, 1, Hash(), 0), "replay and skipped sequence rejected");
        Check(server.Begin(2, 2, Hash(), 0), "recursive capability accepted");
        var recursive = server.Take(0);
        Check(!recursive.Matches(MagicStorageConfirmation.Fingerprint(4, 4, withdrawn, results)), "heart identity participates in digest");
        Check(!recursive.Matches(MagicStorageConfirmation.Fingerprint(3, 4, new[] { withdrawn[1], withdrawn[0] }, results)), "input order participates in digest");
        Check(!recursive.Matches(MagicStorageConfirmation.Fingerprint(3, 4, withdrawn, new[] { new MagicStorageStack(8, 7, 0) })), "quantity mismatch rejected");
        Check(!recursive.Matches(MagicStorageConfirmation.Fingerprint(3, 4, withdrawn, new[] { new MagicStorageStack(8, 6, 1) })), "prefix mismatch rejected");
        byte[] stable = MagicStorageConfirmation.Fingerprint(0, 0, Array.Empty<MagicStorageStack>(), new[] { new MagicStorageStack(8, 6, 90, "Provider/Torch", "PrefixProvider/Shiny") });
        Check(stable.SequenceEqual(MagicStorageConfirmation.Fingerprint(0, 0, Array.Empty<MagicStorageStack>(), new[] { new MagicStorageStack(888, 6, 900, "Provider/Torch", "PrefixProvider/Shiny") })), "stable item and prefix names tolerate different runtime IDs");
        Check(MagicStorageConfirmation.Fingerprint(0, 0, Array.Empty<MagicStorageStack>(), results) != null, "free or saved ingredients supported");
        Check(MagicStorageConfirmation.Fingerprint(0, 0, withdrawn, Array.Empty<MagicStorageStack>()) == null, "empty output rejected");
        Check(MagicStorageConfirmation.Fingerprint(0, 0, withdrawn, new[] { new MagicStorageStack(8, -1, 0) }) == null, "invalid scalar rejected");
        Check(MagicStorageConfirmation.Fingerprint(0, 0, new MagicStorageStack[MagicStorageConfirmation.MaxStacks + 1], results) == null, "fingerprint allocation budget bounded");
        var normalOnly = new MagicStorageConfirmServer(Guid.NewGuid(), 1) { Ready = true };
        Check(!normalOnly.Begin(1, 2, Hash(), 0) && normalOnly.Begin(1, 1, Hash(), 0), "partial capability negotiation preserved");
        normalOnly.Cancel(1); Check(normalOnly.Take(0) == null && normalOnly.Begin(2, 1, Hash(), 0), "abort clears only its pending marker and preserves sequence");
        Check(normalOnly.Take(1801) == null && normalOnly.Begin(3, 1, Hash(), 1802), "expired marker cannot confirm and later requests recover");
        Check(!normalOnly.Begin(4, 1, Hash(), 1802) && normalOnly.Take(1802) == null && normalOnly.Begin(5, 1, Hash(), 1802), "overlapping markers discard ambiguity and recover at next sequence");
        var client = new MagicStorageConfirmClient(); Guid challenge = Guid.NewGuid(), identity = Guid.NewGuid();
        client.Reset(challenge);
        Check(!client.Welcome(Guid.NewGuid(), identity, 3) && !client.Welcome(challenge, Guid.Empty, 3), "foreign or empty session rejected");
        Check(client.Welcome(challenge, identity, 3), "matching welcome activates shared capabilities");
        var totals = new Dictionary<int, long> { [8] = 6, [9] = 8 };
        ulong id = client.Begin(2, totals, 0); totals[8] = 999;
        Check(client.Complete(id, 0) == null && client.Count == 1, "local completion alone cannot credit");
        Check(client.Acknowledge(challenge, Guid.NewGuid(), id, true, 0) == null, "foreign server cannot credit");
        var credited = client.Acknowledge(challenge, identity, id, true, 0);
        Check(credited[8] == 6 && credited[9] == 8 && client.Count == 0, "server confirmation releases immutable full-chain candidate once");
        Check(client.Acknowledge(challenge, identity, id, true, 0) == null, "duplicate acknowledgement cannot credit twice");
        id = client.Begin(1, new() { [8] = 3 }, 0);
        Check(client.Acknowledge(challenge, identity, id, true, 0) == null && client.Complete(id, 0)?[8] == 3, "early acknowledgement waits for local completion");
        id = client.Begin(1, new() { [8] = 3 }, 0);
        Check(client.Acknowledge(challenge, identity, id, false, 0) == null && client.Complete(id, 0) == null && client.Count == 0, "server rollback discards candidate");
        id = client.Begin(1, new() { [8] = 3 }, 0); client.Cancel(id);
        Check(client.Acknowledge(challenge, identity, id, true, 0) == null, "local exception cancels future successful ack");
        id = client.Begin(1, new() { [8] = 3 }, 0); client.Complete(id, 0); client.Expire(1801);
        Check(client.Acknowledge(challenge, identity, id, true, 1801) == null, "expired acknowledgement never backfills");
        id = client.Begin(1, new() { [8] = 3 }, 1802); client.Reset(Guid.NewGuid());
        Check(client.Acknowledge(challenge, identity, id, true, 1802) == null, "old world or character challenge rejected");
        client.Welcome(client.Client, Guid.NewGuid(), 1);
        Check(client.Begin(2, new() { [8] = 3 }, 0) == 0, "client cannot submit unnegotiated recursive candidate");
        for (int i = 0; i < MagicStorageConfirmation.MaxPending; i++) Check(client.Begin(1, new() { [8] = 3 }, 0) != 0, "bounded pending slot available");
        Check(client.Begin(1, new() { [8] = 3 }, 0) == 0, "pending request limit enforced");
        client.Expire(1801); Check(client.Count == 0, "queue expiration releases all pending slots");
        client.Reset(Guid.NewGuid()); client.Welcome(client.Client, Guid.NewGuid(), 3);
        var large = new Dictionary<int, long>(); for (int type = 1; type <= MagicStorageConfirmation.MaxEntries; type++) large.Add(type, long.MaxValue);
        for (int i = 0; i < 4; i++) Check(client.Begin(2, large, 0) != 0, "scalar budget allows bounded full-chain snapshots");
        Check(client.Begin(1, new() { [8] = 3 }, 0) == 0, "aggregate scalar budget enforced");
        foreach (int length in new[] { 0, 129, 65535 })
        {
            using var frame = new MemoryStream(); using var writer = new BinaryWriter(frame, System.Text.Encoding.UTF8, true);
            writer.Write((ushort)length); writer.Flush(); frame.Position = 0; using var reader = new BinaryReader(frame);
            bool rejected = false; try { MagicStorageConfirmation.ReadFrame(reader); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid frame length rejected before allocation");
        }
        using (var frame = new MemoryStream(new byte[] { 5, 0, 1, 2 }))
        using (var reader = new BinaryReader(frame))
        { bool rejected = false; try { MagicStorageConfirmation.ReadFrame(reader); } catch (EndOfStreamException) { rejected = true; } Check(rejected, "truncated frame rejected"); }
        var playerA = new MagicStorageConfirmClient(); var playerB = new MagicStorageConfirmClient();
        playerA.Reset(Guid.NewGuid()); playerB.Reset(Guid.NewGuid());
        var sessionA = new MagicStorageConfirmServer(playerA.Client, 3) { Ready = true };
        var sessionB = new MagicStorageConfirmServer(playerB.Client, 3) { Ready = true };
        playerA.Welcome(playerA.Client, sessionA.Server, 3); playerB.Welcome(playerB.Client, sessionB.Server, 3);
        ulong requestA = playerA.Begin(2, new() { [8] = 6, [9] = 8 }, 0), requestB = playerB.Begin(2, new() { [8] = 6, [9] = 8 }, 0);
        playerA.Complete(requestA, 0); playerB.Complete(requestB, 0);
        sessionA.Begin(requestA, 2, Hash(), 0); sessionB.Begin(requestB, 2, Hash(), 0);
        Check(sessionA.Take(0).Matches(Hash()) && sessionB.Take(0).Matches(Hash()), "same storage request remains isolated per connection");
        Check(playerB.Acknowledge(playerA.Client, sessionA.Server, requestA, true, 0) == null, "another player cannot inherit identical successful craft");
        Check(playerA.Acknowledge(playerA.Client, sessionA.Server, requestA, true, 0)?[9] == 8
            && playerB.Acknowledge(playerB.Client, sessionB.Server, requestB, false, 0) == null, "material competition credits successful full chain only");
        Console.WriteLine($"MAGIC_STORAGE_CONFIRMATION_CHECKS_OK: {checks} handshake, digest, replay, early ack, rollback, timeout, disconnect and budget checks");
    }

    /// <summary>检查一个确认协议不变量。</summary>
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
}
