using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 多人固定世界种子跨端广播——全程 Harmony 运行时 patch，不改动 KrokMP 源码、不增消息号、不加程序集引用。
    ///
    /// KrokMP 主机每次世界生成都用种子公告包（Server_AnnounceSeed）把 LastBeforeGenerationState 广播给客户端，
    /// 客户端据此在本地重生成世界。本类：
    ///   · 主机（self 引擎）接管 Server_AnnounceSeed，在原结构体之后向同一个包追加 1 个 int（CurrentSeed）后发出；
    ///   · 客户端后置种子公告接收器，读出尾部那个 int 并激活逐阶段重置。
    /// 非 self 引擎主机不接管（原版逻辑、无尾部 int），客户端按 AvailableBytes 判定，自然回落原版行为。
    /// KrokMP/LiteNetLib 全用反射访问；不在场或签名不符则整体跳过。
    /// </summary>
    internal static class MpSeedBroadcast
    {
        private static bool _patched;

        private static FieldInfo _fFirstParams;   // WorldGeneration_GenerateWorld_MultiplayerPatch.firstworldgenparams（静态结构体）
        private static MethodInfo _mCreateWriter;  // Net.CreateWriter(ushort)
        private static MethodInfo _mPutStruct;     // MyLiteNetLibExtensions.Put<LastBeforeGenerationState>(writer, value)
        private static MethodInfo _mWriterPutInt;  // NetDataWriter.Put(int)
        private static MethodInfo _mSendToClients; // Net.Server_SendToClients(in DeliveryMethod, in NetDataWriter, in 客户端 id 集合)
        private static object _deliveryReliableOrdered; // DeliveryMethod.ReliableOrdered（装箱枚举值）
        private static MethodInfo _mReaderGetInt;  // NetDataReader.GetInt()
        private static PropertyInfo _pAvailableBytes; // NetDataReader.AvailableBytes
        private static ushort _seedMsgId;          // GenerateWorld_SeedAnnounce 的消息号（按 NetmsgId 枚举解析，跨版本自适应）

        internal static void TryPatch()
        {
            if (_patched) return;
            try
            {
                Type tNet = AccessTools.TypeByName("KrokoshaCasualtiesMP.Net");
                Type tExt = AccessTools.TypeByName("KrokoshaCasualtiesMP.MyLiteNetLibExtensions");
                Type tServer = AccessTools.TypeByName("KrokoshaCasualtiesMP.ServerMain");
                Type tClient = AccessTools.TypeByName("KrokoshaCasualtiesMP.ClientMain");
                Type tGenPatch = AccessTools.TypeByName("KrokoshaCasualtiesMP.WorldGeneration_GenerateWorld_MultiplayerPatch");
                Type tState = AccessTools.TypeByName("KrokoshaCasualtiesMP.LastBeforeGenerationState");
                Type tWriter = AccessTools.TypeByName("LiteNetLib.Utils.NetDataWriter");
                Type tReader = AccessTools.TypeByName("LiteNetLib.Utils.NetDataReader");
                Type tDelivery = AccessTools.TypeByName("LiteNetLib.DeliveryMethod");
                Type tMsgId = AccessTools.TypeByName("KrokoshaCasualtiesMP.NetmsgId");
                // KrokMP/LiteNetLib 尚未加载：不置 _patched，留待世界生成时重试。
                if (tNet == null || tExt == null || tServer == null || tClient == null
                    || tGenPatch == null || tState == null || tWriter == null || tReader == null || tDelivery == null || tMsgId == null)
                    return;

                _fFirstParams = AccessTools.Field(tGenPatch, "firstworldgenparams");
                _mCreateWriter = AccessTools.Method(tNet, "CreateWriter", new[] { typeof(ushort) });
                _mWriterPutInt = AccessTools.Method(tWriter, "Put", new[] { typeof(int) });
                _mReaderGetInt = AccessTools.Method(tReader, "GetInt");
                _pAvailableBytes = AccessTools.Property(tReader, "AvailableBytes");

                MethodInfo putDef = tExt.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Put" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                _mPutStruct = putDef != null ? putDef.MakeGenericMethod(tState) : null;

                // 收件人集合的元素类型以 Server_AnnounceSeed 的形参为准（不同版本可能是 uint 或网络 id 结构体），
                // 据此匹配 Server_SendToClients 中“元素类型一致的泛型集合”重载，排除单值与玩家列表重载。
                MethodInfo announceForElem = AccessTools.Method(tServer, "Server_AnnounceSeed");
                Type idListType = announceForElem?.GetParameters().FirstOrDefault()?.ParameterType;
                Type idType = (idListType != null && idListType.IsGenericType)
                    ? idListType.GetGenericArguments().FirstOrDefault() : null;
                _mSendToClients = tNet.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "Server_SendToClients") return false;
                        var ps = m.GetParameters();
                        if (ps.Length != 3) return false;
                        var p2 = ps[2].ParameterType;
                        var elem = p2.IsByRef ? p2.GetElementType() : p2;
                        if (elem == null || !elem.IsGenericType) return false;
                        var ga = elem.GetGenericArguments();
                        return idType != null && ga.Length == 1 && ga[0] == idType;
                    });

                try { _deliveryReliableOrdered = Enum.Parse(tDelivery, "ReliableOrdered"); } catch { _deliveryReliableOrdered = null; }
                try { _seedMsgId = Convert.ToUInt16(Enum.Parse(tMsgId, "GenerateWorld_SeedAnnounce")); } catch { _seedMsgId = 0; }

                MethodInfo announce = AccessTools.Method(tServer, "Server_AnnounceSeed");
                MethodInfo receiver = AccessTools.Method(tClient, "ClientReceiver__GenerateWorld_SeedAnnounce")
                    ?? AccessTools.Method(tClient, "ClientReciever__GenerateWorld_SeedAnnounce");

                if (_fFirstParams == null || _mCreateWriter == null || _mPutStruct == null || _mWriterPutInt == null
                    || _mSendToClients == null || _deliveryReliableOrdered == null || _seedMsgId == 0
                    || _mReaderGetInt == null || _pAvailableBytes == null || announce == null || receiver == null)
                {
                    ModLog.Warning("MpSeedBroadcast：KrokMP/LiteNetLib 句柄不全，多人固定世界种子广播跳过");
                    _patched = true;
                    return;
                }

                var harmony = new Harmony("com.casualtiesUnknown.saveManager.mpSeedBroadcast");
                var t = typeof(MpSeedBroadcast);
                harmony.Patch(announce, prefix: new HarmonyMethod(
                    t.GetMethod(nameof(AnnounceSeed_Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
                harmony.Patch(receiver, postfix: new HarmonyMethod(
                    t.GetMethod(nameof(SeedAnnounce_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
                _patched = true;
                ModLog.Info("多人种子广播已挂载（接管 Server_AnnounceSeed 追加种子 / 客户端读取激活）");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"MpSeedBroadcast.TryPatch 失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 主机 self 引擎：接管广播，原结构体之后追加 CurrentSeed 后整包发出（return false 取代原方法）。
        /// 非 self 引擎或非主机则放行原版（无尾部 int，客户端按 AvailableBytes 自然回落）。
        /// </summary>
        private static bool AnnounceSeed_Prefix(object[] __args)
        {
            object to_who = (__args != null && __args.Length > 0) ? __args[0] : null;
            if (!(SeededWorldEngine.IsActive && MultiplayerBridge.IsServer()))
            {
                SeededWorldEngine.MpReseedEnabled = false;
                return true; // 原版逻辑
            }
            try
            {
                int seed = SeededWorldEngine.CurrentSeed;
                object writer = _mCreateWriter.Invoke(null, new object[] { _seedMsgId });
                object state = _fFirstParams.GetValue(null);
                _mPutStruct.Invoke(null, new object[] { writer, state });
                _mWriterPutInt.Invoke(writer, new object[] { seed });
                _mSendToClients.Invoke(null, new object[] { _deliveryReliableOrdered, writer, to_who });
                SeededWorldEngine.MpReseedEnabled = true;
                ModLog.Info($"多人种子广播：随种子公告包追加 saveManagerSeed={seed}");
                return false; // 已接管发送
            }
            catch (Exception ex)
            {
                // 接管失败则放行原版（发出不含尾部 int 的干净包），客户端据 AvailableBytes 回落，不会读到脏数据。
                ModLog.Warning($"MpSeedBroadcast.AnnounceSeed_Prefix 异常，回落原版广播：{ex.Message}");
                return true;
            }
        }

        /// <summary>客户端：种子公告包收完结构体后，若包尾还有 int 则读出；非 0 即激活逐阶段重置使世界与主机/同种子单机一致。</summary>
        private static void SeedAnnounce_Postfix(object[] __args)
        {
            try
            {
                if (MultiplayerBridge.IsServer()) return; // 主机回环忽略（接收器本身 ignorehost）
                if (__args == null || __args.Length < 2) return;
                object reader = __args[1];
                if (reader == null) return;
                int avail = Convert.ToInt32(_pAvailableBytes.GetValue(reader));
                if (avail < sizeof(int))
                {
                    SeededWorldEngine.MpReseedEnabled = false; // 原版包（无尾部种子）：回落 KrokMP 原生世界
                    return;
                }
                int seed = (int)_mReaderGetInt.Invoke(reader, null);
                if (seed != 0)
                {
                    SeededWorldEngine.Activate(seed, "");
                    SeededWorldEngine.MpReseedEnabled = true;
                    ModLog.Info($"多人种子接收：激活逐阶段重置 seed={seed}");
                }
                else
                {
                    SeededWorldEngine.MpReseedEnabled = false;
                }
            }
            catch (Exception ex) { ModLog.Warning($"MpSeedBroadcast.SeedAnnounce_Postfix 异常：{ex.Message}"); }
        }
    }
}
