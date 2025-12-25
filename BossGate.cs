// BossGate.cs
// BepInEx plugin for Valheim 0.221.4
// - Blocks boss spawns server-side using OfferingBowl RPC interception
// - Optional: blocks Mistlands Queen door prefab "dungeon_queen_door" until Queen is enabled
// - Simple admin UI (checkboxes) toggled with F7, no server restart needed
//

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BossGate
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class BossGatePlugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.tyranidex.bossgate";
        public const string ModName = "BossGate";
        public const string ModVersion = "1.0.2";

        internal static BossGatePlugin Instance;
        internal static ManualLogSource Log;
        internal static Harmony Harmony;

        // Use a custom cfg file name (not GUID-based)
        internal static ConfigFile Cfg;

        private static ConfigEntry<string> _toggleKey;
        private static ConfigEntry<string> _blockedMessage;
        private static ConfigEntry<bool> _alsoShowCenterMessage;
        private static ConfigEntry<bool> _blockUnknownBosses;
        private static ConfigEntry<bool> _blockQueenDoorWhenQueenDisabled;

        private KeyCode _hotKey = KeyCode.F7;

        // Simple UI state
        private bool _uiVisible;
        private Rect _windowRect = new Rect(60, 60, 430, 420);
        private Vector2 _scroll;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Create bossgate.cfg
            try
            {
                string cfgPath = Path.Combine(Paths.ConfigPath, "bossgate.cfg");
                Cfg = new ConfigFile(cfgPath, true);
            }
            catch
            {
                // Fallback to default plugin config if something goes wrong
                Cfg = Config;
            }

            _toggleKey = Cfg.Bind("UI", "ToggleKey", "F7", "Key to toggle the admin UI window.");
            _blockedMessage = Cfg.Bind("Messages", "BlockedMessage", "The boss is still deeply asleep.", "Message shown when a boss spawn is blocked. Supports {boss}.");
            _alsoShowCenterMessage = Cfg.Bind("Messages", "AlsoShowCenterMessage", true, "Also show as a center-screen message (in addition to chat).");
            _blockUnknownBosses = Cfg.Bind("Safety", "BlockUnknownBosses", true, "If the boss id cannot be resolved, block anyway (safer).");
            _blockQueenDoorWhenQueenDisabled = Cfg.Bind("Queen", "BlockQueenDoorWhenQueenDisabled", true, "Block Mistlands Queen door (dungeon_queen_door) until Queen is enabled.");

            ParseHotkey(_toggleKey.Value);

            Harmony = new Harmony(ModGUID);
            Harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.LogInfo(ModName + " " + ModVersion + " loaded");

            // Optional: show a small confirmation in-game once player exists (useful for testing)
            StartCoroutine(DelayedLocalLoadedPing());
        }

        private IEnumerator DelayedLocalLoadedPing()
        {
            // Wait until the world is actually loaded (local player exists)
            float timeout = 10f;
            float t = 0f;
            while (Player.m_localPlayer == null && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            // Show only to the local player (if any)
            if (Player.m_localPlayer != null && MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "BossGate loaded");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(_hotKey))
            {
                // Don't open UI in menus / not connected.
                if (ZNet.instance == null || ZRoutedRpc.instance == null)
                    return;

                _uiVisible = !_uiVisible;
            }
        }

        private void OnGUI()
        {
            if (!_uiVisible)
                return;

            _windowRect = GUILayout.Window(GetHashCode(), _windowRect, DrawWindow, "BossGate (Admin)");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Server-authoritative boss gating.");
            GUILayout.Label("Default: ALL bosses disabled.");
            GUILayout.Space(6);

            bool isAdminLocal = BossGateAuth.IsLocalAdminBestEffort();
            GUILayout.Label(isAdminLocal
                ? "Admin detected (best-effort)."
                : "Admin not detected (best-effort). Server will verify adminlist.txt.");

            GUILayout.Space(6);

            if (ZoneSystem.instance == null)
            {
                GUILayout.Label("ZoneSystem not ready yet.");
                GUILayout.EndVertical();
                GUI.DragWindow(new Rect(0, 0, 10000, 25));
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(280));
            for (int i = 0; i < BossGateWorld.Bosses.Count; i++)
            {
                BossGateWorld.BossDef boss = BossGateWorld.Bosses[i];

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
                for (int i = 0; i < BossGateWorld.Bosses.Count; i++)
                    BossGateNetwork.RequestSetBossEnabled(BossGateWorld.Bosses[i].Id, true);
            }
            if (GUILayout.Button("Disable all"))
            {
                for (int i = 0; i < BossGateWorld.Bosses.Count; i++)
                    BossGateNetwork.RequestSetBossEnabled(BossGateWorld.Bosses[i].Id, false);
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
            try
            {
                KeyCode parsed = (KeyCode)Enum.Parse(typeof(KeyCode), value, true);
                _hotKey = parsed;
            }
            catch
            {
                _hotKey = KeyCode.F7;
                Log.LogWarning("Invalid ToggleKey '" + value + "', falling back to F7.");
            }
        }

        internal static string BlockedMessage { get { return _blockedMessage.Value; } }
        internal static bool AlsoShowCenterMessage { get { return _alsoShowCenterMessage.Value; } }
        internal static bool BlockUnknownBosses { get { return _blockUnknownBosses.Value; } }
        internal static bool BlockQueenDoorWhenQueenDisabled { get { return _blockQueenDoorWhenQueenDisabled.Value; } }
    }

    internal static class BossGateWorld
    {
        internal sealed class BossDef
        {
            public string Id;
            public string DisplayName;
            public string BossPrefabName;

            public BossDef(string id, string displayName, string bossPrefabName)
            {
                Id = id;
                DisplayName = displayName;
                BossPrefabName = bossPrefabName;
            }
        }

        // Boss prefab mapping per your confirmed list
        internal static readonly List<BossDef> Bosses = new List<BossDef>
        {
            new BossDef("eikthyr", "Eikthyr", "Eikthyr"),
            new BossDef("elder", "The Elder", "gd_king"),
            new BossDef("bonemass", "Bonemass", "Bonemass"),
            new BossDef("moder", "Moder", "Dragon"),
            new BossDef("yagluth", "Yagluth", "GoblinKing"),
            new BossDef("queen", "The Queen", "SeekerQueen"),
            new BossDef("fader", "Fader", "Fader"),
        };

        internal static string GetBossKey(string bossId)
        {
            return ("bossgate_allow_" + bossId).ToLowerInvariant();
        }

        internal static bool IsBossEnabled(string bossId)
        {
            if (ZoneSystem.instance == null)
                return false;

            return ZoneSystem.instance.GetGlobalKey(GetBossKey(bossId));
        }

        internal static void SetBossEnabledServer(string bossId, bool enabled)
        {
            if (ZoneSystem.instance == null)
                return;

            string key = GetBossKey(bossId);

            if (enabled)
            {
                ZoneSystem.instance.SetGlobalKey(key);
                return;
            }

            // Try RemoveGlobalKey if present
            try
            {
                MethodInfo remove = typeof(ZoneSystem).GetMethod("RemoveGlobalKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(string) }, null);
                if (remove != null)
                {
                    remove.Invoke(ZoneSystem.instance, new object[] { key });
                    return;
                }
            }
            catch { }

            // Fallback: remove directly from m_globalKeys and broadcast if possible
            try
            {
                FieldInfo globalKeysField = typeof(ZoneSystem).GetField("m_globalKeys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object keysObj = globalKeysField != null ? globalKeysField.GetValue(ZoneSystem.instance) : null;
                if (keysObj == null)
                    return;

                // HashSet<string> or List<string> etc.
                MethodInfo contains = keysObj.GetType().GetMethod("Contains", new Type[] { typeof(string) });
                MethodInfo remove2 = keysObj.GetType().GetMethod("Remove", new Type[] { typeof(string) });

                if (contains != null && remove2 != null)
                {
                    object has = contains.Invoke(keysObj, new object[] { key });
                    if (has is bool && (bool)has)
                    {
                        remove2.Invoke(keysObj, new object[] { key });

                        MethodInfo send = typeof(ZoneSystem).GetMethod("SendGlobalKeys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (send != null)
                            send.Invoke(ZoneSystem.instance, new object[0]);
                    }
                }
            }
            catch { }
        }

        internal static string GetDisplayName(string bossId)
        {
            for (int i = 0; i < Bosses.Count; i++)
            {
                if (string.Equals(Bosses[i].Id, bossId, StringComparison.OrdinalIgnoreCase))
                    return Bosses[i].DisplayName;
            }
            return bossId;
        }

        internal static bool IsKnownBossId(string bossId)
        {
            for (int i = 0; i < Bosses.Count; i++)
            {
                if (string.Equals(Bosses[i].Id, bossId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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
                if (ZRoutedRpc.instance == null)
                    return;

                ZRoutedRpc.instance.Register(RPC_SetBossEnabled, new Action<long, string, bool>(RPC_SetBossEnabled_Handler));
                BossGatePlugin.Log.LogInfo("BossGate RPC registered");
            }
        }

        internal static void RequestSetBossEnabled(string bossId, bool enabled)
        {
            if (ZNet.instance == null || ZRoutedRpc.instance == null)
                return;

            // If we're the server (local host), apply immediately (helps local testing, avoids RPC/admin issues)
            if (ZNet.instance.IsServer() && !ZNet.instance.IsDedicated())
            {
                BossGateWorld.SetBossEnabledServer(bossId, enabled);
                BossGatePlugin.Log.LogInfo("Local host set " + bossId + " = " + enabled);
                return;
            }

            if (ZRoutedRpc.instance == null)
                return;

            long serverPeerId = BossGateNetUtil.GetServerPeerIdBestEffort();
            if (serverPeerId <= 0)
            {
                BossGatePlugin.Log.LogWarning("Could not resolve server peer id; cannot send RPC.");
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeerId, RPC_SetBossEnabled, bossId, enabled);
        }

        private static void RPC_SetBossEnabled_Handler(long senderPeerId, string bossId, bool enabled)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return;

            if (!BossGateWorld.IsKnownBossId(bossId))
            {
                BossGateMessaging.SendToPeer(senderPeerId, "Unknown boss id '" + bossId + "'.", false);
                return;
            }

            if (!BossGateAuth.IsSenderAdmin(senderPeerId))
            {
                BossGateMessaging.SendToPeer(senderPeerId, "You are not an admin.", false);
                return;
            }

            BossGateWorld.SetBossEnabledServer(bossId, enabled);

            string state = enabled ? "ENABLED" : "DISABLED";
            BossGateMessaging.SendToPeer(senderPeerId, BossGateWorld.GetDisplayName(bossId) + " is now " + state + ".", false);
        }
    }

    internal static class BossGateAuth
    {
        internal static bool IsLocalAdminBestEffort()
        {
            try
            {
                if (ZNet.instance == null)
                    return false;

                // Local host convenience: treat host as admin
                if (ZNet.instance.IsServer() && !ZNet.instance.IsDedicated())
                    return true;

                // Try methods named IsAdmin (varies by version)
                MethodInfo[] methods = typeof(ZNet).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m == null || m.ReturnType != typeof(bool))
                        continue;
                    if (!string.Equals(m.Name, "IsAdmin", StringComparison.OrdinalIgnoreCase))
                        continue;

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
                if (ZNet.instance == null || !ZNet.instance.IsServer())
                    return false;

                // Try ZNet.IsAdmin(long) if present
                MethodInfo[] methods = typeof(ZNet).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m == null || m.ReturnType != typeof(bool))
                        continue;
                    if (!string.Equals(m.Name, "IsAdmin", StringComparison.OrdinalIgnoreCase))
                        continue;

                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(long))
                    {
                        object res = m.Invoke(ZNet.instance, new object[] { senderPeerId });
                        if (res is bool) return (bool)res;
                    }
                }

                // Fallback: compare senderPeerId string with m_adminList
                FieldInfo adminListField = typeof(ZNet).GetField("m_adminList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object adminListObj = adminListField != null ? adminListField.GetValue(ZNet.instance) : null;
                if (adminListObj != null)
                {
                    string idStr = senderPeerId.ToString();

                    // Try Contains(string)
                    MethodInfo contains = adminListObj.GetType().GetMethod("Contains", new Type[] { typeof(string) });
                    if (contains != null)
                    {
                        object res = contains.Invoke(adminListObj, new object[] { idStr });
                        if (res is bool && (bool)res)
                            return true;
                    }

                    // Also try peer hostname if available
                    string hostName = BossGateNetUtil.GetPeerHostNameBestEffort(senderPeerId);
                    if (!string.IsNullOrEmpty(hostName) && contains != null)
                    {
                        object res2 = contains.Invoke(adminListObj, new object[] { hostName });
                        if (res2 is bool && (bool)res2)
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }
    }

    internal static class BossGateNetUtil
    {
        // Best-effort resolve server peer id without AccessTools warnings.
        internal static long GetServerPeerIdBestEffort()
        {
            try
            {
                if (ZRoutedRpc.instance == null)
                    return 0;

                // Try common method names
                MethodInfo m =
                    typeof(ZRoutedRpc).GetMethod("GetServerPeerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? typeof(ZRoutedRpc).GetMethod("GetServerPeerId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (m != null && m.ReturnType == typeof(long) && m.GetParameters().Length == 0)
                {
                    object res = m.Invoke(ZRoutedRpc.instance, new object[0]);
                    if (res is long) return (long)res;
                }

                // Try fields containing "server" and "peer"
                FieldInfo[] fields = typeof(ZRoutedRpc).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo f = fields[i];
                    if (f.FieldType != typeof(long))
                        continue;

                    string n = (f.Name ?? "").ToLowerInvariant();
                    if (n.Contains("server") && n.Contains("peer"))
                    {
                        object v = f.GetValue(ZRoutedRpc.instance);
                        if (v is long) return (long)v;
                    }
                }
            }
            catch { }

            return 0;
        }

        internal static string GetPeerHostNameBestEffort(long peerId)
        {
            try
            {
                if (ZNet.instance == null)
                    return "";

                FieldInfo peersField = typeof(ZNet).GetField("m_peers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object peersObj = peersField != null ? peersField.GetValue(ZNet.instance) : null;
                List<ZNetPeer> peers = peersObj as List<ZNetPeer>;
                if (peers == null)
                    return "";

                for (int i = 0; i < peers.Count; i++)
                {
                    ZNetPeer p = peers[i];
                    if (p == null) continue;

                    FieldInfo uidField = typeof(ZNetPeer).GetField("m_uid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    object uidObj = uidField != null ? uidField.GetValue(p) : null;
                    if (!(uidObj is long)) continue;

                    long uid = (long)uidObj;
                    if (uid != peerId) continue;

                    FieldInfo socketField = typeof(ZNetPeer).GetField("m_socket", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    object socketObj = socketField != null ? socketField.GetValue(p) : null;
                    if (socketObj == null)
                        return "";

                    MethodInfo getHost = socketObj.GetType().GetMethod("GetHostName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (getHost != null && getHost.ReturnType == typeof(string) && getHost.GetParameters().Length == 0)
                    {
                        object hn = getHost.Invoke(socketObj, new object[0]);
                        return hn as string ?? "";
                    }

                    return "";
                }
            }
            catch { }

            return "";
        }
    }

    internal static class BossGateMessaging
    {
        internal static void SendBlocked(long peerId, string bossId)
        {
            string bossName = BossGateWorld.GetDisplayName(bossId);
            string msg = BossGatePlugin.BlockedMessage.Replace("{boss}", bossName);
            SendToPeer(peerId, msg, BossGatePlugin.AlsoShowCenterMessage);
        }

        internal static void SendToPeer(long peerId, string text, bool forceCenter)
        {
            bool chatOk = TrySendChatMessage(peerId, text);
            if (forceCenter || !chatOk)
                TrySendCenterMessage(peerId, text);
        }

        private static bool TrySendCenterMessage(long peerId, string text)
        {
            try
            {
                if (ZRoutedRpc.instance == null)
                    return false;

                ZRoutedRpc.instance.InvokeRoutedRPC(peerId, "ShowMessage", (int)MessageHud.MessageType.Center, text);
                return true;
            }
            catch { return false; }
        }

        private static MethodInfo _cachedChatRpc;
        private static Type[] _cachedArgTypes;

        private static bool TrySendChatMessage(long peerId, string text)
        {
            try
            {
                if (ZRoutedRpc.instance == null)
                    return false;

                CacheChatSignatureIfNeeded();
                if (_cachedArgTypes == null)
                    return false;

                Vector3 pos = Vector3.zero;
                int type = (int)Talker.Type.Shout;
                string serverName = "Server";

                object[] args = BuildChatArgs(_cachedArgTypes, pos, type, serverName, text);
                if (args == null)
                    return false;

                ZRoutedRpc.instance.InvokeRoutedRPC(peerId, "ChatMessage", args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void CacheChatSignatureIfNeeded()
        {
            if (_cachedArgTypes != null)
                return;

            try
            {
                // Reflect Chat.RPC_ChatMessage to infer expected argument signature
                MethodInfo rpc = typeof(Chat).GetMethod("RPC_ChatMessage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (rpc == null)
                    return;

                ParameterInfo[] ps = rpc.GetParameters();
                int start = 0;
                if (ps.Length > 0 && ps[0].ParameterType == typeof(long))
                    start = 1;

                int len = ps.Length - start;
                if (len <= 0)
                    return;

                Type[] types = new Type[len];
                for (int i = 0; i < len; i++)
                    types[i] = ps[start + i].ParameterType;

                _cachedChatRpc = rpc;
                _cachedArgTypes = types;
            }
            catch { }
        }

        private static object[] BuildChatArgs(Type[] argTypes, Vector3 pos, int type, string name, string text)
        {
            // Known variants:
            // (Vector3, int, string, string)
            // (Vector3, int, string, string, string)
            // (Vector3, int, UserInfo, string)
            // (Vector3, int, UserInfo, string, string)
            try
            {
                if (argTypes.Length == 4 &&
                    argTypes[0] == typeof(Vector3) &&
                    argTypes[1] == typeof(int) &&
                    argTypes[2] == typeof(string) &&
                    argTypes[3] == typeof(string))
                {
                    return new object[] { pos, type, name, text };
                }

                if (argTypes.Length == 5 &&
                    argTypes[0] == typeof(Vector3) &&
                    argTypes[1] == typeof(int) &&
                    argTypes[2] == typeof(string) &&
                    argTypes[3] == typeof(string) &&
                    argTypes[4] == typeof(string))
                {
                    // userId can be empty; works on many builds
                    return new object[] { pos, type, name, text, "" };
                }

                if (argTypes.Length == 4 &&
                    argTypes[0] == typeof(Vector3) &&
                    argTypes[1] == typeof(int) &&
                    argTypes[2].Name == "UserInfo" &&
                    argTypes[3] == typeof(string))
                {
                    object userInfo = CreateServerUserInfo(argTypes[2], name);
                    return new object[] { pos, type, userInfo, text };
                }

                if (argTypes.Length == 5 &&
                    argTypes[0] == typeof(Vector3) &&
                    argTypes[1] == typeof(int) &&
                    argTypes[2].Name == "UserInfo" &&
                    argTypes[3] == typeof(string) &&
                    argTypes[4] == typeof(string))
                {
                    object userInfo = CreateServerUserInfo(argTypes[2], name);
                    return new object[] { pos, type, userInfo, text, "" };
                }
            }
            catch { }

            return null;
        }

        private static object CreateServerUserInfo(Type userInfoType, string name)
        {
            try
            {
                // Try: UserInfo.GetLocalUser() then set Name
                MethodInfo getLocal = userInfoType.GetMethod("GetLocalUser", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (getLocal != null)
                {
                    object u = getLocal.Invoke(null, new object[0]);
                    if (u != null)
                    {
                        PropertyInfo prop = userInfoType.GetProperty("Name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (prop != null && prop.CanWrite)
                            prop.SetValue(u, name, null);
                        else
                        {
                            FieldInfo f = userInfoType.GetField("Name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (f != null) f.SetValue(u, name);
                        }
                        return u;
                    }
                }

                // Fallback: default instance
                object inst = Activator.CreateInstance(userInfoType);
                return inst;
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class BossGatePrefabUtil
    {
        internal static string GetPrefabName(GameObject go)
        {
            if (go == null)
                return "";

            string n = go.name ?? "";
            if (n.EndsWith("(Clone)"))
                n = n.Replace("(Clone)", "");
            return n.Trim();
        }

        internal static bool HasParentNamed(Transform t, string prefabNameLower, int maxDepth)
        {
            if (t == null)
                return false;

            string target = (prefabNameLower ?? "").ToLowerInvariant();

            int depth = 0;
            Transform cur = t;
            while (cur != null && depth <= maxDepth)
            {
                string n = GetPrefabName(cur.gameObject).ToLowerInvariant();
                if (n == target)
                    return true;

                cur = cur.parent;
                depth++;
            }
            return false;
        }
    }

    internal static class BossGateBossResolver
    {
        internal static bool TryResolveBossIdFromOfferingBowl(OfferingBowl bowl, out string bossId)
        {
            bossId = "";

            // Prefer boss prefab field if present
            GameObject bossPrefab = TryGetBossPrefab(bowl);
            if (bossPrefab != null)
            {
                string prefabName = BossGatePrefabUtil.GetPrefabName(bossPrefab);
                if (TryMapBossPrefabName(prefabName, out bossId))
                    return true;
            }

            // Fallback: infer from altar prefab name
            string altarName = BossGatePrefabUtil.GetPrefabName(bowl.gameObject);
            if (TryMapOfferingBowlOwnerName(altarName, out bossId))
                return true;

            return false;
        }

        private static GameObject TryGetBossPrefab(OfferingBowl bowl)
        {
            try
            {
                FieldInfo direct = typeof(OfferingBowl).GetField("m_bossPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (direct != null)
                    return direct.GetValue(bowl) as GameObject;

                FieldInfo[] fields = typeof(OfferingBowl).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo f = fields[i];
                    if (f.FieldType != typeof(GameObject))
                        continue;

                    string n = (f.Name ?? "").ToLowerInvariant();
                    if (!n.Contains("boss"))
                        continue;

                    GameObject go = f.GetValue(bowl) as GameObject;
                    if (go != null)
                        return go;
                }
            }
            catch { }

            return null;
        }

        private static string Normalize(string s)
        {
            if (s == null) return "";
            string n = s.Trim().ToLowerInvariant();
            n = n.Replace("_", "");
            n = n.Replace("-", "");
            return n;
        }

        private static bool TryMapOfferingBowlOwnerName(string altarPrefabName, out string bossId)
        {
            bossId = "";
            string nRaw = altarPrefabName ?? "";
            string n = Normalize(nRaw);

            // Map by known altar prefab names
            if (n.Contains("bossstoneeikthyr")) { bossId = "eikthyr"; return true; }
            if (n.Contains("bossstonetheelder") || n.Contains("bossstoneelder")) { bossId = "elder"; return true; }
            if (n.Contains("bossstonebonemass")) { bossId = "bonemass"; return true; }
            if (n.Contains("bossstonedragonqueen")) { bossId = "moder"; return true; }
            if (n.Contains("bossstoneyagluth")) { bossId = "yagluth"; return true; }
            if (n.Contains("bossstonethequeen") || n.Contains("bossstonequeen")) { bossId = "queen"; return true; }
            if (n.Contains("bossstonefader")) { bossId = "fader"; return true; }

            // Older names / misc fallback
            if (n.Contains("eikthyr")) { bossId = "eikthyr"; return true; }
            if (n.Contains("gdking") || n.Contains("theelder") || n.Contains("elder")) { bossId = "elder"; return true; }
            if (n.Contains("bonemass")) { bossId = "bonemass"; return true; }
            if (n.Contains("dragonqueen")) { bossId = "moder"; return true; }
            if (n.Contains("goblinking")) { bossId = "yagluth"; return true; }
            if (n.Contains("seekerqueen")) { bossId = "queen"; return true; }
            if (n.Contains("fader")) { bossId = "fader"; return true; }

            return false;
        }

        private static bool TryMapBossPrefabName(string bossPrefabName, out string bossId)
        {
            bossId = "";
            string raw = bossPrefabName ?? "";
            string n = Normalize(raw);

            // Use your confirmed boss prefabs
            if (n == Normalize("Eikthyr") || n.Contains("eikthyr")) { bossId = "eikthyr"; return true; }
            if (n == Normalize("gd_king") || n.Contains("gdking")) { bossId = "elder"; return true; }
            if (n == Normalize("Bonemass") || n.Contains("bonemass")) { bossId = "bonemass"; return true; }
            if (n == Normalize("Dragon") || n.Contains("dragon")) { bossId = "moder"; return true; }
            if (n == Normalize("GoblinKing") || n.Contains("goblinking")) { bossId = "yagluth"; return true; }
            if (n == Normalize("SeekerQueen") || n.Contains("seekerqueen")) { bossId = "queen"; return true; }
            if (n == Normalize("Fader") || n.Contains("fader")) { bossId = "fader"; return true; }

            return false;
        }
    }

    internal static class BossGateGateLogic
    {
        internal static bool ServerAllowOrBlockOffering(OfferingBowl bowl, long senderPeerId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return true;

            string bossId;
            bool resolved = BossGateBossResolver.TryResolveBossIdFromOfferingBowl(bowl, out bossId);

            if (!resolved)
            {
                if (BossGatePlugin.BlockUnknownBosses)
                {
                    BossGateMessaging.SendToPeer(senderPeerId, BossGatePlugin.BlockedMessage, BossGatePlugin.AlsoShowCenterMessage);
                    return false;
                }
                return true;
            }

            if (BossGateWorld.IsBossEnabled(bossId))
                return true;

            BossGateMessaging.SendBlocked(senderPeerId, bossId);
            return false;
        }

        internal static bool ShouldBlockQueenDoor(Door door)
        {
            if (!BossGatePlugin.BlockQueenDoorWhenQueenDisabled)
                return false;

            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return false;

            if (BossGateWorld.IsBossEnabled("queen"))
                return false;

            // Block any Door component that belongs to the dungeon_queen_door prefab hierarchy
            Transform t = door != null ? door.transform : null;
            if (BossGatePrefabUtil.HasParentNamed(t, "dungeon_queen_door", 10))
                return true;

            // Extra fallback: sometimes only child names differ, so check root names too
            if (t != null && t.root != null)
            {
                string rootName = BossGatePrefabUtil.GetPrefabName(t.root.gameObject).ToLowerInvariant();
                if (rootName == "dungeon_queen_door")
                    return true;
            }

            return false;
        }
    }

    // ---------------- Harmony patches ----------------
    // IMPORTANT: We use object[] __args to avoid HarmonyX binding by parameter NAME.
    // This fixes: "Parameter 'sender' not found in method ... senderId"

    [HarmonyPatch(typeof(OfferingBowl), "RPC_BossSpawnInitiated")]
    internal static class BossGate_Patch_OfferingBowl_BossSpawnInitiated
    {
        private static bool Prefix(OfferingBowl __instance, object[] __args)
        {
            long senderId = 0L;
            if (__args != null && __args.Length > 0 && __args[0] is long)
                senderId = (long)__args[0];

            return BossGateGateLogic.ServerAllowOrBlockOffering(__instance, senderId);
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), "RPC_RemoveBossSpawnInventoryItems")]
    internal static class BossGate_Patch_OfferingBowl_RemoveItems
    {
        private static bool Prefix(OfferingBowl __instance, object[] __args)
        {
            long senderId = 0L;
            if (__args != null && __args.Length > 0 && __args[0] is long)
                senderId = (long)__args[0];

            return BossGateGateLogic.ServerAllowOrBlockOffering(__instance, senderId);
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), "RPC_SpawnBoss")]
    internal static class BossGate_Patch_OfferingBowl_SpawnBoss
    {
        private static bool Prefix(OfferingBowl __instance, object[] __args)
        {
            long senderId = 0L;
            if (__args != null && __args.Length > 0 && __args[0] is long)
                senderId = (long)__args[0];

            return BossGateGateLogic.ServerAllowOrBlockOffering(__instance, senderId);
        }
    }

    [HarmonyPatch(typeof(Door), "RPC_UseDoor")]
    internal static class BossGate_Patch_Door_UseDoor
    {
        private static bool Prefix(Door __instance, object[] __args)
        {
            // __args[0] is usually senderId, but we only need it to show a message
            long senderId = 0L;
            if (__args != null && __args.Length > 0 && __args[0] is long)
                senderId = (long)__args[0];

            if (BossGateGateLogic.ShouldBlockQueenDoor(__instance))
            {
                BossGateMessaging.SendBlocked(senderId, "queen");
                return false;
            }

            return true;
        }
    }
}
