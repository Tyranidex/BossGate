// BossGate.cs
// BepInEx plugin for Valheim 0.221.4 (C# 7.3 compatible)
// - Blocks boss spawns server-side using robust OfferingBowl RPC interception
// - Optional: blocks Sealbreaker door (Queen access) until enabled
// - Simple admin UI (checkboxes) toggled with F7, no server restart needed
// - Uses a custom config file name: BepInEx/config/bossgate.cfg
//

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using UnityEngine;

namespace BossGate
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class BossGatePlugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.yourname.bossgate";
        public const string ModName = "BossGate";
        public const string ModVersion = "1.0.5"; // digits + dots only (BepInEx requirement)

        internal static BossGatePlugin Instance;
        internal static Harmony Harmony;

        // Custom config file (bossgate.cfg)
        private ConfigFile _customConfig;

        private static ConfigEntry<string> _toggleKey;
        private static ConfigEntry<string> _blockedMessage;
        private static ConfigEntry<bool> _alsoShowCenterMessage;
        private static ConfigEntry<bool> _blockUnknownBosses;
        private static ConfigEntry<bool> _blockSealbreakerDoorWhenQueenDisabled;
        private static ConfigEntry<bool> _announceOnJoin;
        private static ConfigEntry<string> _loadedMessage;

        private KeyCode _hotKey = KeyCode.F7;

        // Simple UI state
        private bool _uiVisible;
        private Rect _windowRect = new Rect(60, 60, 430, 420);
        private Vector2 _scroll;

        private void Awake()
        {
            Instance = this;

            // Create a custom-named config file like "server_devcommands.cfg" style mods.
            _customConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "bossgate.cfg"), true);

            _toggleKey = _customConfig.Bind("UI", "ToggleKey", "F7", "Key to toggle the admin UI window.");
            _blockedMessage = _customConfig.Bind("Messages", "BlockedMessage", "The boss is still deeply asleep.", "Message shown when a boss spawn is blocked. You can use {boss} placeholder.");
            _alsoShowCenterMessage = _customConfig.Bind("Messages", "AlsoShowCenterMessage", true, "Also show the message as a center-screen message (in addition to chat).");
            _loadedMessage = _customConfig.Bind("Messages", "LoadedMessage", "BossGate loaded.", "Shown locally when the plugin is loaded and optionally announced to joiners (server).");
            _announceOnJoin = _customConfig.Bind("Messages", "AnnounceToJoiners", true, "If true, server announces BossGate loaded when players join.");

            _blockUnknownBosses = _customConfig.Bind("Safety", "BlockUnknownBosses", true, "If the boss id cannot be resolved, block anyway (safer).");
            _blockSealbreakerDoorWhenQueenDisabled = _customConfig.Bind("Queen", "BlockSealbreakerDoorWhenQueenDisabled", true, "Block Sealbreaker door usage until Queen is enabled.");

            ParseHotkey(_toggleKey.Value);

            Harmony = new Harmony(ModGUID);
            Harmony.PatchAll();

            LogInfo(ModName + " " + ModVersion + " loaded");
            StartCoroutine(ShowLocalLoadedMessage());
        }

        private IEnumerator ShowLocalLoadedMessage()
        {
            // Dedicated servers typically run in batchmode and have no HUD.
            if (Application.isBatchMode)
                yield break;

            while (MessageHud.instance == null)
                yield return null;

            try
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "[BossGate] " + _loadedMessage.Value);
            }
            catch { }
        }

        private void Update()
        {
            if (Input.GetKeyDown(_hotKey))
            {
                if (ZNet.instance == null)
                    return;

                _uiVisible = !_uiVisible;
            }
        }

        private void OnGUI()
        {
            if (!_uiVisible) return;
            _windowRect = GUILayout.Window(GetHashCode(), _windowRect, DrawWindow, "BossGate (Admin)");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.Label("Server-authoritative boss gating.");
            GUILayout.Label("Default: ALL bosses disabled.");
            GUILayout.Space(6);

            bool isAdminLocal = BossGateAuth.IsLocalAdminBestEffort();
            if (isAdminLocal)
                GUILayout.Label("Admin detected (best-effort).");
            else
                GUILayout.Label("Admin not detected (best-effort). Server will verify adminlist.txt.");

            // Helpful hint for local testing
            if (BossGateAuth.IsLocalHostNonDedicated())
            {
                GUILayout.Space(4);
                GUILayout.Label("Local host detected: admin actions are allowed for testing.");
            }

            GUILayout.Space(6);

            if (ZoneSystem.instance == null)
            {
                GUILayout.Label("ZoneSystem not ready yet.");
                GUILayout.EndVertical();
                GUI.DragWindow(new Rect(0, 0, 10000, 25));
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(280));

            foreach (BossGateWorld.BossDef boss in BossGateWorld.Bosses)
            {
                bool current = BossGateWorld.IsBossEnabled(boss.Id);
                string label = boss.DisplayName + "  [" + (current ? "ENABLED" : "DISABLED") + "]";
                bool next = GUILayout.Toggle(current, label);

                if (next != current)
                {
                    BossGateNetwork.RequestSetBossEnabled(boss.Id, next);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Enable all"))
            {
                foreach (BossGateWorld.BossDef boss in BossGateWorld.Bosses)
                    BossGateNetwork.RequestSetBossEnabled(boss.Id, true);
            }
            if (GUILayout.Button("Disable all"))
            {
                foreach (BossGateWorld.BossDef boss in BossGateWorld.Bosses)
                    BossGateNetwork.RequestSetBossEnabled(boss.Id, false);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (GUILayout.Button("Close"))
                _uiVisible = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 25));
        }

        private void ParseHotkey(string value)
        {
            KeyCode key;
            if (Enum.TryParse<KeyCode>(value, true, out key))
            {
                _hotKey = key;
                return;
            }

            _hotKey = KeyCode.F7;
            LogWarning("Invalid ToggleKey '" + value + "', falling back to F7.");
        }

        internal static string GetBlockedMessage() { return _blockedMessage.Value; }
        internal static bool GetAlsoShowCenterMessage() { return _alsoShowCenterMessage.Value; }
        internal static bool GetBlockUnknownBosses() { return _blockUnknownBosses.Value; }
        internal static bool GetBlockSealbreakerDoorWhenQueenDisabled() { return _blockSealbreakerDoorWhenQueenDisabled.Value; }
        internal static bool GetAnnounceOnJoin() { return _announceOnJoin.Value; }
        internal static string GetLoadedMessage() { return _loadedMessage.Value; }

        // Logging helpers (avoid protected Logger access from static classes)
        internal static void LogInfo(string msg) { try { if (Instance != null) Instance.Logger.LogInfo(msg); } catch { } }
        internal static void LogWarning(string msg) { try { if (Instance != null) Instance.Logger.LogWarning(msg); } catch { } }
        internal static void LogError(string msg) { try { if (Instance != null) Instance.Logger.LogError(msg); } catch { } }
    }

    internal static class BossGateWorld
    {
        internal sealed class BossDef
        {
            public string Id { get; private set; }
            public string DisplayName { get; private set; }

            public BossDef(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }
        }

        internal static readonly List<BossDef> Bosses = new List<BossDef>
        {
            new BossDef("eikthyr", "Eikthyr"),
            new BossDef("elder", "The Elder"),
            new BossDef("bonemass", "Bonemass"),
            new BossDef("moder", "Moder"),
            new BossDef("yagluth", "Yagluth"),
            new BossDef("queen", "The Queen"),
            new BossDef("fader", "Fader"),
        };

        internal static string GetBossKey(string bossId)
        {
            return ("bossgate_allow_" + bossId).ToLowerInvariant();
        }

        internal static bool IsBossEnabled(string bossId)
        {
            if (ZoneSystem.instance == null) return false;
            return ZoneSystem.instance.GetGlobalKey(GetBossKey(bossId));
        }

        internal static void SetBossEnabledServer(string bossId, bool enabled)
        {
            if (ZoneSystem.instance == null) return;

            string key = GetBossKey(bossId);

            if (enabled)
            {
                ZoneSystem.instance.SetGlobalKey(key);
                return;
            }

            MethodInfo remove = AccessTools.Method(typeof(ZoneSystem), "RemoveGlobalKey", new Type[] { typeof(string) });
            if (remove != null)
            {
                remove.Invoke(ZoneSystem.instance, new object[] { key });
                return;
            }

            FieldInfo globalKeysField = AccessTools.Field(typeof(ZoneSystem), "m_globalKeys");
            object keysObj = (globalKeysField != null) ? globalKeysField.GetValue(ZoneSystem.instance) : null;
            ICollection<string> coll = keysObj as ICollection<string>;
            if (coll != null && coll.Contains(key))
            {
                coll.Remove(key);
                MethodInfo send = AccessTools.Method(typeof(ZoneSystem), "SendGlobalKeys");
                if (send != null) send.Invoke(ZoneSystem.instance, new object[0]);
            }
        }

        internal static string GetDisplayName(string bossId)
        {
            BossDef def = Bosses.FirstOrDefault(b => b.Id.Equals(bossId, StringComparison.OrdinalIgnoreCase));
            return def != null ? def.DisplayName : bossId;
        }
    }

    internal static class BossGateNetwork
    {
        private const string RPC_SetBossEnabled = "BossGate_SetBossEnabled";

        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class ZNet_Awake_RegisterRPC
        {
            private static void Postfix()
            {
                if (ZRoutedRpc.instance == null) return;
                ZRoutedRpc.instance.Register(RPC_SetBossEnabled, new Action<long, string, bool>(RPC_SetBossEnabled_Handler));
                BossGatePlugin.LogInfo("BossGate RPC registered");
            }
        }

        internal static void RequestSetBossEnabled(string bossId, bool enabled)
        {
            if (ZNet.instance == null) return;

            // If we're the server (non-dedicated host), apply directly.
            if (ZNet.instance.IsServer())
            {
                RPC_SetBossEnabled_Handler(0L, bossId, enabled);
                return;
            }

            if (ZRoutedRpc.instance == null) return;

            long serverPeerId = GetServerPeerIdSafe();
            if (serverPeerId == 0L) serverPeerId = 1L;

            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(serverPeerId, RPC_SetBossEnabled, bossId, enabled);
            }
            catch (Exception ex)
            {
                BossGatePlugin.LogWarning("Failed to send RPC to server: " + ex.Message);
            }
        }

        private static long GetServerPeerIdSafe()
        {
            if (ZRoutedRpc.instance == null) return 0L;

            MethodInfo mi = AccessTools.Method(typeof(ZRoutedRpc), "GetServerPeerID", new Type[0]);
            if (mi == null) mi = AccessTools.Method(typeof(ZRoutedRpc), "GetServerPeerId", new Type[0]);
            if (mi != null)
            {
                object r = mi.Invoke(ZRoutedRpc.instance, new object[0]);
                if (r is long) return (long)r;
                if (r is int) return (int)r;
            }

            FieldInfo fi = AccessTools.Field(typeof(ZRoutedRpc), "m_serverPeerID");
            if (fi == null) fi = AccessTools.Field(typeof(ZRoutedRpc), "m_serverPeerId");
            if (fi != null)
            {
                object r = fi.GetValue(ZRoutedRpc.instance);
                if (r is long) return (long)r;
                if (r is int) return (int)r;
            }

            if (ZNet.instance != null)
            {
                FieldInfo fi2 = AccessTools.Field(typeof(ZNet), "m_serverPeerID");
                if (fi2 == null) fi2 = AccessTools.Field(typeof(ZNet), "m_serverPeerId");
                if (fi2 != null)
                {
                    object r = fi2.GetValue(ZNet.instance);
                    if (r is long) return (long)r;
                    if (r is int) return (int)r;
                }
            }

            return 0L;
        }

        private static void RPC_SetBossEnabled_Handler(long sender, string bossId, bool enabled)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return;

            if (!BossGateWorld.Bosses.Any(b => b.Id.Equals(bossId, StringComparison.OrdinalIgnoreCase)))
            {
                BossGateMessaging.SendToPeer(sender, "Unknown boss id '" + bossId + "'.", false);
                return;
            }

            if (!BossGateAuth.IsSenderAdmin(sender))
            {
                BossGateMessaging.SendToPeer(sender, "You are not an admin.", false);
                return;
            }

            BossGateWorld.SetBossEnabledServer(bossId, enabled);
            string state = enabled ? "ENABLED" : "DISABLED";
            BossGateMessaging.SendToPeer(sender, BossGateWorld.GetDisplayName(bossId) + " is now " + state + ".", false);
        }
    }

    internal static class BossGateAuth
    {
        // Public so the UI can display a helpful line.
        internal static bool IsLocalHostNonDedicated()
        {
            return IsLocalHost();
        }

        internal static bool IsLocalAdminBestEffort()
        {
            try
            {
                if (ZNet.instance == null) return false;

                // Local host (non-dedicated) is considered admin for testing convenience.
                if (IsLocalHost())
                    return true;

                // Avoid HarmonyX warnings: do not use AccessTools.Method for a method that may not exist.
                // Instead scan for IsAdmin() (no args) safely.
                MethodInfo[] methods = typeof(ZNet).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (MethodInfo m in methods)
                {
                    if (m.Name != "IsAdmin") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 0)
                    {
                        object res = m.Invoke(ZNet.instance, new object[0]);
                        if (res is bool) return (bool)res;
                    }
                }
            }
            catch { }

            return false;
        }

        internal static bool IsSenderAdmin(long senderPeerId)
        {
            try
            {
                if (ZNet.instance == null) return false;

                // Local test / host mode: sender is often 0 and there may be no peer entry.
                // In non-dedicated host mode, allow admin actions from host.
                if (senderPeerId == 0L && IsLocalHost())
                    return true;

                // Try overloads ZNet.IsAdmin(long) / ZNet.IsAdmin(ZNetPeer) if present.
                List<MethodInfo> methods = typeof(ZNet).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "IsAdmin").ToList();

                MethodInfo mLong = methods.FirstOrDefault(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 1 && p[0].ParameterType == typeof(long);
                });
                if (mLong != null)
                {
                    object res = mLong.Invoke(ZNet.instance, new object[] { senderPeerId });
                    if (res is bool) return (bool)res;
                }

                ZNetPeer peer = GetPeer(senderPeerId);
                if (peer != null)
                {
                    MethodInfo mPeer = methods.FirstOrDefault(m =>
                    {
                        ParameterInfo[] p = m.GetParameters();
                        return p.Length == 1 && p[0].ParameterType == typeof(ZNetPeer);
                    });

                    if (mPeer != null)
                    {
                        object res = mPeer.Invoke(ZNet.instance, new object[] { peer });
                        if (res is bool) return (bool)res;
                    }
                }

                // Fallback: attempt m_adminList contains peer's host name/id
                if (peer != null)
                {
                    string id = GetPeerHostName(peer);
                    if (!string.IsNullOrEmpty(id))
                    {
                        FieldInfo adminListField = AccessTools.Field(typeof(ZNet), "m_adminList");
                        object adminListObj = (adminListField != null) ? adminListField.GetValue(ZNet.instance) : null;
                        if (adminListObj != null)
                        {
                            MethodInfo listContainsId = AccessTools.Method(typeof(ZNet), "ListContainsId");
                            if (listContainsId != null)
                            {
                                object res = listContainsId.Invoke(ZNet.instance, new object[] { adminListObj, id });
                                if (res is bool) return (bool)res;
                            }

                            MethodInfo contains = adminListObj.GetType().GetMethod("Contains", new Type[] { typeof(string) });
                            if (contains != null)
                            {
                                object res = contains.Invoke(adminListObj, new object[] { id });
                                if (res is bool) return (bool)res;
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool IsLocalHost()
        {
            try
            {
                if (ZNet.instance == null) return false;
                if (!ZNet.instance.IsServer()) return false;

                // Prefer official method if present: ZNet.IsDedicated()
                MethodInfo isDedicated = typeof(ZNet).GetMethod("IsDedicated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (isDedicated != null)
                {
                    object res = isDedicated.Invoke(ZNet.instance, new object[0]);
                    if (res is bool)
                        return !((bool)res); // host if NOT dedicated
                }

                // If the method doesn't exist, assume host (safe for local testing).
                return true;
            }
            catch { return false; }
        }

        private static ZNetPeer GetPeer(long senderPeerId)
        {
            MethodInfo getPeer = AccessTools.Method(typeof(ZNet), "GetPeer", new Type[] { typeof(long) });
            if (getPeer != null && ZNet.instance != null)
                return getPeer.Invoke(ZNet.instance, new object[] { senderPeerId }) as ZNetPeer;

            try
            {
                if (ZNet.instance == null) return null;

                FieldInfo peersField = AccessTools.Field(typeof(ZNet), "m_peers");
                object obj = (peersField != null) ? peersField.GetValue(ZNet.instance) : null;
                List<ZNetPeer> peers = obj as List<ZNetPeer>;
                if (peers == null) return null;

                FieldInfo uidField = AccessTools.Field(typeof(ZNetPeer), "m_uid");
                if (uidField == null) return null;

                foreach (ZNetPeer p in peers)
                {
                    object uidObj = uidField.GetValue(p);
                    if (uidObj is long && (long)uidObj == senderPeerId)
                        return p;
                }
            }
            catch { }

            return null;
        }

        private static string GetPeerHostName(ZNetPeer peer)
        {
            try
            {
                FieldInfo socketField = AccessTools.Field(typeof(ZNetPeer), "m_socket");
                object socketObj = (socketField != null) ? socketField.GetValue(peer) : null;
                if (socketObj != null)
                {
                    MethodInfo getHostName = socketObj.GetType().GetMethod("GetHostName", new Type[0]);
                    if (getHostName != null)
                        return getHostName.Invoke(socketObj, new object[0]) as string;
                }
            }
            catch { }

            return null;
        }
    }

    internal static class BossGateMessaging
    {
        internal static void SendBlocked(long peerId, string bossId)
        {
            string bossName = BossGateWorld.GetDisplayName(bossId);
            string msg = BossGatePlugin.GetBlockedMessage().Replace("{boss}", bossName);
            SendToPeer(peerId, msg, BossGatePlugin.GetAlsoShowCenterMessage());
        }

        internal static void SendToPeer(long peerId, string text, bool forceCenter)
        {
            bool chatOk = TrySendChatMessage(peerId, text);
            if (forceCenter || !chatOk)
                TrySendCenterMessage(peerId, text);
        }

        internal static void AnnounceLoaded(long peerId)
        {
            SendToPeer(peerId, "[BossGate] " + BossGatePlugin.GetLoadedMessage(), true);
        }

        private static bool TrySendCenterMessage(long peerId, string text)
        {
            try
            {
                if (ZRoutedRpc.instance == null) return false;
                ZRoutedRpc.instance.InvokeRoutedRPC(peerId, "ShowMessage", (int)MessageHud.MessageType.Center, text);
                return true;
            }
            catch { return false; }
        }

        private static bool TrySendChatMessage(long peerId, string text)
        {
            // Simple compatibility signature: (Vector3, int, string, string)
            try
            {
                if (ZRoutedRpc.instance == null) return false;

                Vector3 pos = Vector3.zero;
                int type = (int)Talker.Type.Shout;
                string serverName = "Server";

                ZRoutedRpc.instance.InvokeRoutedRPC(peerId, "ChatMessage", pos, type, serverName, text);
                return true;
            }
            catch { return false; }
        }
    }

    internal static class BossGatePrefabUtil
    {
        // We do NOT rely on Valheim's Utils.GetPrefabName because it may not exist in some builds.
        internal static string GetPrefabName(GameObject go)
        {
            if (go == null) return "";
            string n = go.name ?? "";
            int idx = n.IndexOf("(Clone)", StringComparison.Ordinal);
            if (idx >= 0) n = n.Substring(0, idx);
            return n;
        }

        internal static string GetPrefabName(Component c)
        {
            if (c == null) return "";
            return GetPrefabName(c.gameObject);
        }
    }

    internal static class BossGateBossResolver
    {
        internal static bool TryResolveBossIdFromOfferingBowl(OfferingBowl bowl, out string bossId)
        {
            bossId = "";

            GameObject bossPrefab = TryGetBossPrefab(bowl);
            if (bossPrefab != null)
            {
                string prefabName = BossGatePrefabUtil.GetPrefabName(bossPrefab);
                if (TryMapBossPrefabName(prefabName, out bossId))
                    return true;
            }

            string altarName = BossGatePrefabUtil.GetPrefabName(bowl.gameObject);
            if (TryMapOfferingBowlOwnerName(altarName, out bossId))
                return true;

            return false;
        }

        private static GameObject TryGetBossPrefab(OfferingBowl bowl)
        {
            try
            {
                FieldInfo direct = AccessTools.Field(typeof(OfferingBowl), "m_bossPrefab");
                if (direct != null)
                    return direct.GetValue(bowl) as GameObject;

                FieldInfo[] fields = typeof(OfferingBowl).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (FieldInfo f in fields)
                {
                    if (f.FieldType != typeof(GameObject)) continue;
                    string n = f.Name.ToLowerInvariant();
                    if (!n.Contains("boss")) continue;

                    GameObject go = f.GetValue(bowl) as GameObject;
                    if (go != null) return go;
                }
            }
            catch { }

            return null;
        }

        private static bool TryMapOfferingBowlOwnerName(string altarPrefabName, out string bossId)
        {
            bossId = "";
            string n = (altarPrefabName ?? "").ToLowerInvariant();

            if (n.Contains("eikthyr")) { bossId = "eikthyr"; return true; }
            if (n.Contains("gdking")) { bossId = "elder"; return true; }
            if (n.Contains("bonemass")) { bossId = "bonemass"; return true; }
            if (n.Contains("dragonqueen")) { bossId = "moder"; return true; }
            if (n.Contains("goblinking")) { bossId = "yagluth"; return true; }
            if (n.Contains("fader")) { bossId = "fader"; return true; }

            return false;
        }

        private static bool TryMapBossPrefabName(string bossPrefabName, out string bossId)
        {
            bossId = "";
            string n = (bossPrefabName ?? "").ToLowerInvariant();

            if (n.Contains("eikthyr")) { bossId = "eikthyr"; return true; }
            if (n.Contains("gdking") || n.Contains("elder")) { bossId = "elder"; return true; }
            if (n.Contains("bonemass")) { bossId = "bonemass"; return true; }
            if (n.Contains("dragonqueen") || n.Contains("moder")) { bossId = "moder"; return true; }
            if (n.Contains("goblinking") || n.Contains("yagluth")) { bossId = "yagluth"; return true; }
            if (n.Contains("seekerqueen") || n.Contains("queen")) { bossId = "queen"; return true; }
            if (n.Contains("fader")) { bossId = "fader"; return true; }

            return false;
        }

        internal static bool IsSealbreakerDoor(Door door)
        {
            try
            {
                FieldInfo keyItemField = AccessTools.Field(typeof(Door), "m_keyItem");
                object keyObj = (keyItemField != null) ? keyItemField.GetValue(door) : null;

                ItemDrop itemDrop = keyObj as ItemDrop;
                if (itemDrop != null)
                {
                    string keyPrefab = BossGatePrefabUtil.GetPrefabName(itemDrop.gameObject);
                    return keyPrefab.ToLowerInvariant().Contains("sealbreaker");
                }

                GameObject go = keyObj as GameObject;
                if (go != null)
                {
                    string keyPrefab = BossGatePrefabUtil.GetPrefabName(go);
                    return keyPrefab.ToLowerInvariant().Contains("sealbreaker");
                }

                string s = keyObj as string;
                if (!string.IsNullOrEmpty(s))
                {
                    return s.ToLowerInvariant().Contains("sealbreaker");
                }

                string doorPrefab = BossGatePrefabUtil.GetPrefabName(door.gameObject).ToLowerInvariant();
                if (doorPrefab.Contains("sealbreaker")) return true;
            }
            catch { }

            return false;
        }
    }

    internal static class BossGateGateLogic
    {
        internal static bool ServerAllowOrBlock(OfferingBowl bowl, long senderPeerId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return true;

            string bossId;
            bool resolved = BossGateBossResolver.TryResolveBossIdFromOfferingBowl(bowl, out bossId);

            if (!resolved)
            {
                if (BossGatePlugin.GetBlockUnknownBosses())
                {
                    BossGateMessaging.SendToPeer(senderPeerId, BossGatePlugin.GetBlockedMessage(), BossGatePlugin.GetAlsoShowCenterMessage());
                    return false;
                }
                return true;
            }

            if (BossGateWorld.IsBossEnabled(bossId))
                return true;

            BossGateMessaging.SendBlocked(senderPeerId, bossId);
            return false;
        }
    }

    // ---- PATCH 1: OfferingBowl RPC interception (robust) ----
    [HarmonyPatch]
    internal static class BossGateOfferingBowlPatches
    {
        private static readonly string[] OfferingBowlRpcNames = new string[]
        {
            "RPC_BossSpawnInitiated",
            "RPC_RemoveBossSpawnInventoryItems",
            "RPC_SpawnBoss"
        };

        static IEnumerable<MethodBase> TargetMethods()
        {
            Type t = typeof(OfferingBowl);
            foreach (string name in OfferingBowlRpcNames)
            {
                MethodInfo m = AccessTools.Method(t, name);
                if (m != null) yield return m;
            }
        }

        static bool Prefix(OfferingBowl __instance, object[] __args)
        {
            long sender = 0L;
            if (__args != null && __args.Length > 0 && (__args[0] is long))
                sender = (long)__args[0];

            return BossGateGateLogic.ServerAllowOrBlock(__instance, sender);
        }
    }

    // ---- PATCH 2: Door Sealbreaker block ----
    [HarmonyPatch(typeof(Door), "RPC_UseDoor")]
    internal static class BossGateDoorPatch
    {
        static bool Prefix(Door __instance, object[] __args)
        {
            if (!BossGatePlugin.GetBlockSealbreakerDoorWhenQueenDisabled())
                return true;

            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return true;

            long sender = 0L;
            if (__args != null && __args.Length > 0 && (__args[0] is long))
                sender = (long)__args[0];

            if (!BossGateWorld.IsBossEnabled("queen") && BossGateBossResolver.IsSealbreakerDoor(__instance))
            {
                BossGateMessaging.SendBlocked(sender, "queen");
                return false;
            }

            return true;
        }
    }

    // ---- PATCH 3: Announce to joiners (robust) ----
    [HarmonyPatch]
    internal static class BossGateAnnouncePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            // Patch all overloads of RPC_PeerInfo to be safe.
            return typeof(ZNet).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "RPC_PeerInfo")
                .Cast<MethodBase>();
        }

        static void Postfix(object[] __args)
        {
            try
            {
                if (!BossGatePlugin.GetAnnounceOnJoin())
                    return;

                if (ZNet.instance == null || !ZNet.instance.IsServer())
                    return;

                long sender = 0L;
                if (__args != null && __args.Length > 0 && (__args[0] is long))
                    sender = (long)__args[0];

                if (sender != 0L)
                    BossGateMessaging.AnnounceLoaded(sender);
            }
            catch { }
        }
    }
}
