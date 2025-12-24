// BossGate.cs
// BepInEx plugin for Valheim 0.221.4
// - Blocks boss spawns server-side using OfferingBowl RPC interception
// - Blocks the Mistlands Queen door prefab (dungeon_queen_door) until Queen is enabled
// - Simple admin UI (checkboxes) toggled with F7, no server restart needed
//

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BossGate
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class BossGatePlugin : BaseUnityPlugin
    {
        // NOTE: Config file name is derived from the plugin GUID by default.
        // We override it to produce a simpler file name: bossgate.cfg
        public const string ModGUID = "Tyranidex.BossGate";
        public const string ModName = "BossGate";
        public const string ModVersion = "1.0.1";

        internal static BossGatePlugin Instance;
        internal static Harmony HarmonyInstance;
        internal static BepInEx.Logging.ManualLogSource Log;

        private static ConfigFile _cfg;

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

        // One-time in-game confirmation
        private bool _sentLoadedToast;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Use a custom config file name (not tied to GUID).
            try
            {
                string path = Path.Combine(Paths.ConfigPath, "bossgate.cfg");
                _cfg = new ConfigFile(path, true);
            }
            catch
            {
                // Fallback to default config file if something goes wrong.
                _cfg = Config;
            }

            _toggleKey = _cfg.Bind("UI", "ToggleKey", "F7", "Key to toggle the admin UI window.");
            _blockedMessage = _cfg.Bind("Messages", "BlockedMessage", "The boss is still deeply asleep.", "Message shown when a boss spawn is blocked. You can use {boss}.");
            _alsoShowCenterMessage = _cfg.Bind("Messages", "AlsoShowCenterMessage", true, "Also show the message as a center-screen message (in addition to chat).");
            _blockUnknownBosses = _cfg.Bind("Safety", "BlockUnknownBosses", true, "If the boss id cannot be resolved, block anyway (safer).");

            // The Queen entrance is NOT a normal altar. It's the big door structure.
            // The most reliable fix is to block the door prefab itself until Queen is enabled.
            _blockQueenDoorWhenQueenDisabled = _cfg.Bind("Queen", "BlockQueenDoorWhenQueenDisabled", true, "Block the Mistlands queen door prefab 'dungeon_queen_door' until Queen is enabled.");

            ParseHotkey(_toggleKey.Value);

            HarmonyInstance = new Harmony(ModGUID);
            HarmonyInstance.PatchAll();

            Logger.LogInfo(string.Format("{0} {1} loaded", ModName, ModVersion));
        }

        private void Update()
        {
            if (Input.GetKeyDown(_hotKey))
            {
                // Avoid opening in menus; Player is null there.
                if (Player.m_localPlayer == null)
                    return;

                _uiVisible = !_uiVisible;
            }

            // Small in-game confirmation (host/admin only).
            if (!_sentLoadedToast && Player.m_localPlayer != null)
            {
                _sentLoadedToast = true;

                // Dedicated servers have no local player, so this won't fire there.
                // Only show to the local player to avoid spam.
                BossGateMessaging.SendToPeer(BossGateAuth.GetMyPeerIdBestEffort_NoLogs(), "BossGate loaded (F7).", false);
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
                : "Admin not detected (best-effort). Server will still verify adminlist.txt.");

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
                    // Request server to change (server checks admin)
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
                KeyCode key;
                if (Enum.TryParse(value, true, out key))
                {
                    _hotKey = key;
                    return;
                }
            }
            catch { }

            _hotKey = KeyCode.F7;
            Logger.LogWarning(string.Format("Invalid ToggleKey '{0}', falling back to F7.", value));
        }

        internal static string BlockedMessage
        {
            get { return _blockedMessage != null ? _blockedMessage.Value : "The boss is still deeply asleep."; }
        }

        internal static bool AlsoShowCenterMessage
        {
            get { return _alsoShowCenterMessage != null && _alsoShowCenterMessage.Value; }
        }

        internal static bool BlockUnknownBosses
        {
            get { return _blockUnknownBosses != null && _blockUnknownBosses.Value; }
        }

        internal static bool BlockQueenDoorWhenQueenDisabled
        {
            get { return _blockQueenDoorWhenQueenDisabled != null && _blockQueenDoorWhenQueenDisabled.Value; }
        }
    }

    internal static class BossGateWorld
    {
        internal sealed class BossDef
        {
            public string Id;
            public string DisplayName;

            public BossDef(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }
        }

        // These are BossGate internal IDs (used by global keys + UI).
        // Default state = disabled (global key absent).
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

        // Quick lookup for validation + display name.
        private static readonly Dictionary<string, string> _idToDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "eikthyr", "Eikthyr" },
            { "elder", "The Elder" },
            { "bonemass", "Bonemass" },
            { "moder", "Moder" },
            { "yagluth", "Yagluth" },
            { "queen", "The Queen" },
            { "fader", "Fader" },
        };

        internal static bool IsKnownBossId(string bossId)
        {
            if (string.IsNullOrEmpty(bossId))
                return false;

            return _idToDisplay.ContainsKey(bossId);
        }

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

            // Removal method name changed across versions sometimes; try reflection fallbacks.
            MethodInfo remove = typeof(ZoneSystem).GetMethod("RemoveGlobalKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(string) }, null);
            if (remove != null)
            {
                remove.Invoke(ZoneSystem.instance, new object[] { key });
                return;
            }

            // Fallback: remove directly from m_globalKeys and try to broadcast if possible.
            FieldInfo globalKeysField = typeof(ZoneSystem).GetField("m_globalKeys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object keysObj = globalKeysField != null ? globalKeysField.GetValue(ZoneSystem.instance) : null;

            ICollection<string> coll = keysObj as ICollection<string>;
            if (coll != null && coll.Contains(key))
            {
                coll.Remove(key);

                MethodInfo send = typeof(ZoneSystem).GetMethod("SendGlobalKeys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (send != null)
                    send.Invoke(ZoneSystem.instance, new object[0]);
            }
        }

        internal static string GetDisplayName(string bossId)
        {
            string name;
            if (_idToDisplay.TryGetValue(bossId, out name))
                return name;

            return bossId;
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

                // Register on both server and client; server will actually apply changes.
                ZRoutedRpc.instance.Register(RPC_SetBossEnabled, new Action<long, string, bool>(RPC_SetBossEnabled_Handler));
                BossGatePlugin.Log.LogInfo("BossGate RPC registered");
            }
        }

        internal static void RequestSetBossEnabled(string bossId, bool enabled)
        {
            if (string.IsNullOrEmpty(bossId))
                return;

            if (ZRoutedRpc.instance == null || ZNet.instance == null)
                return;

            // If we are the server (local host / dedicated), apply immediately.
            if (ZNet.instance.IsServer())
            {
                long my = BossGateAuth.GetMyPeerIdBestEffort_NoLogs();
                // If there is no routed RPC yet, or we're calling from host, run directly.
                RPC_SetBossEnabled_Handler(my, bossId, enabled);
                return;
            }

            long serverPeerId = GetServerPeerIdBestEffort();
            if (serverPeerId == 0L)
                return;

            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeerId, RPC_SetBossEnabled, bossId, enabled);
        }

        private static long GetServerPeerIdBestEffort()
        {
            try
            {
                // Try common method name (varies across Valheim versions/modpacks).
                MethodInfo m = typeof(ZRoutedRpc).GetMethod("GetServerPeerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null)
                    m = typeof(ZRoutedRpc).GetMethod("GetServerPeerId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (m != null && ZRoutedRpc.instance != null)
                {
                    object res = m.Invoke(ZRoutedRpc.instance, new object[0]);
                    if (res is long)
                        return (long)res;
                }

                // Try field name fallback.
                FieldInfo f = typeof(ZRoutedRpc).GetField("m_serverPeerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && ZRoutedRpc.instance != null)
                {
                    object v = f.GetValue(ZRoutedRpc.instance);
                    if (v is long)
                        return (long)v;
                }
            }
            catch { }

            // Common convention: server is 0 in some builds.
            return 0L;
        }

        private static void RPC_SetBossEnabled_Handler(long sender, string bossId, bool enabled)
        {
            // Only the server is allowed to mutate world state.
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return;

            // Validate boss id
            if (!BossGateWorld.IsKnownBossId(bossId))
            {
                BossGateMessaging.SendToPeer(sender, "Unknown boss id '" + bossId + "'.", false);
                return;
            }

            // Admin check
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
        // Cache reflection results (avoid repeated lookups + warnings).
        private static bool _dedicatedChecked;
        private static bool _isDedicatedCached;

        internal static bool IsLocalAdminBestEffort()
        {
            try
            {
                if (ZNet.instance == null)
                    return false;

                // If hosting (not dedicated), treat local player as admin (UI hint + local testing convenience).
                if (ZNet.instance.IsServer() && !IsDedicatedServerBestEffort())
                    return true;
            }
            catch { }

            // We avoid calling ZNet.IsAdmin here because it changes across versions and may not exist.
            return false;
        }

        internal static bool IsSenderAdmin(long senderPeerId)
        {
            try
            {
                if (ZNet.instance == null)
                    return false;

                // If hosting locally (not dedicated), allow the host to toggle without editing adminlist.
                if (ZNet.instance.IsServer() && !IsDedicatedServerBestEffort())
                {
                    long my = GetMyPeerIdBestEffort_NoLogs();
                    if (my != 0L && senderPeerId == my)
                        return true;
                }

                // Dedicated / remote: check adminlist.
                return IsInAdminList(senderPeerId);
            }
            catch { }

            return false;
        }

        private static bool IsInAdminList(long senderPeerId)
        {
            try
            {
                if (ZNet.instance == null)
                    return false;

                ZNetPeer peer = GetPeer(senderPeerId);
                if (peer == null)
                    return false;

                string id = GetPeerIdStringBestEffort(peer, senderPeerId);
                if (string.IsNullOrEmpty(id))
                    return false;

                FieldInfo adminListField = typeof(ZNet).GetField("m_adminList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object adminListObj = adminListField != null ? adminListField.GetValue(ZNet.instance) : null;
                if (adminListObj == null)
                    return false;

                // Try ZNet.ListContainsId(list, id) if present (handles backend specifics in some versions).
                MethodInfo listContainsId = typeof(ZNet).GetMethod("ListContainsId", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (listContainsId != null)
                {
                    // Some builds: instance method; some builds: static method. Handle both.
                    object target = listContainsId.IsStatic ? null : ZNet.instance;
                    object res = listContainsId.Invoke(target, new object[] { adminListObj, id });
                    if (res is bool && (bool)res)
                        return true;

                    // Also try a pure numeric form (SteamID) if needed.
                    object res2 = listContainsId.Invoke(target, new object[] { adminListObj, senderPeerId.ToString() });
                    if (res2 is bool && (bool)res2)
                        return true;
                }

                // Fallback: if list has Contains(string)
                MethodInfo contains = adminListObj.GetType().GetMethod("Contains", new Type[] { typeof(string) });
                if (contains != null)
                {
                    object res = contains.Invoke(adminListObj, new object[] { id });
                    if (res is bool && (bool)res)
                        return true;

                    object res2 = contains.Invoke(adminListObj, new object[] { senderPeerId.ToString() });
                    if (res2 is bool && (bool)res2)
                        return true;
                }

                // Fallback: ICollection<string>
                ICollection<string> coll = adminListObj as ICollection<string>;
                if (coll != null)
                {
                    if (coll.Contains(id) || coll.Contains(senderPeerId.ToString()))
                        return true;
                }
            }
            catch { }

            return false;
        }

        internal static long GetMyPeerIdBestEffort_NoLogs()
        {
            try
            {
                if (ZRoutedRpc.instance == null)
                    return 0L;

                // Avoid HarmonyX AccessTools warnings by using plain reflection.
                MethodInfo m = typeof(ZRoutedRpc).GetMethod("GetMyPeerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null)
                    m = typeof(ZRoutedRpc).GetMethod("GetMyPeerId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (m != null)
                {
                    object res = m.Invoke(ZRoutedRpc.instance, new object[0]);
                    if (res is long)
                        return (long)res;
                }

                // Field fallback commonly used in multiple versions.
                FieldInfo f = typeof(ZRoutedRpc).GetField("m_id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    object v = f.GetValue(ZRoutedRpc.instance);
                    if (v is long)
                        return (long)v;
                }
            }
            catch { }

            return 0L;
        }

        private static bool IsDedicatedServerBestEffort()
        {
            if (_dedicatedChecked)
                return _isDedicatedCached;

            _dedicatedChecked = true;
            _isDedicatedCached = false;

            try
            {
                if (ZNet.instance == null)
                    return false;

                MethodInfo m = typeof(ZNet).GetMethod("IsDedicated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[0], null);
                if (m != null)
                {
                    object res = m.Invoke(ZNet.instance, new object[0]);
                    if (res is bool)
                        _isDedicatedCached = (bool)res;
                }
            }
            catch { }

            return _isDedicatedCached;
        }

        private static ZNetPeer GetPeer(long senderPeerId)
        {
            try
            {
                // Prefer direct method if present.
                MethodInfo getPeer = typeof(ZNet).GetMethod("GetPeer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(long) }, null);
                if (getPeer != null && ZNet.instance != null)
                    return getPeer.Invoke(ZNet.instance, new object[] { senderPeerId }) as ZNetPeer;
            }
            catch { }

            // Fallback: search in ZNet.instance.m_peers
            try
            {
                if (ZNet.instance == null)
                    return null;

                FieldInfo peersField = typeof(ZNet).GetField("m_peers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object peersObj = peersField != null ? peersField.GetValue(ZNet.instance) : null;

                List<ZNetPeer> peers = peersObj as List<ZNetPeer>;
                if (peers == null)
                    return null;

                FieldInfo uidField = typeof(ZNetPeer).GetField("m_uid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (uidField == null)
                    return null;

                for (int i = 0; i < peers.Count; i++)
                {
                    object uidObj = uidField.GetValue(peers[i]);
                    if (uidObj is long && (long)uidObj == senderPeerId)
                        return peers[i];
                }
            }
            catch { }

            return null;
        }

        private static string GetPeerIdStringBestEffort(ZNetPeer peer, long senderPeerId)
        {
            // Most common: peer.m_socket.GetHostName() returns a platform id-like string.
            string host = GetPeerHostName(peer);
            if (!string.IsNullOrEmpty(host))
                return host;

            // Some backends may use a numeric uid.
            return senderPeerId.ToString();
        }

        private static string GetPeerHostName(ZNetPeer peer)
        {
            try
            {
                // Common: peer.m_socket.GetHostName()
                FieldInfo socketField = typeof(ZNetPeer).GetField("m_socket", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object socketObj = socketField != null ? socketField.GetValue(peer) : null;
                if (socketObj != null)
                {
                    MethodInfo getHostName = socketObj.GetType().GetMethod("GetHostName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[0], null);
                    if (getHostName != null)
                        return getHostName.Invoke(socketObj, new object[0]) as string;
                }

                // Alternative: peer.m_rpc.GetSocket().GetHostName()
                FieldInfo rpcField = typeof(ZNetPeer).GetField("m_rpc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object rpcObj = rpcField != null ? rpcField.GetValue(peer) : null;
                if (rpcObj != null)
                {
                    MethodInfo getSocket = rpcObj.GetType().GetMethod("GetSocket", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[0], null);
                    object sock = getSocket != null ? getSocket.Invoke(rpcObj, new object[0]) : null;
                    if (sock != null)
                    {
                        MethodInfo getHostName2 = sock.GetType().GetMethod("GetHostName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[0], null);
                        if (getHostName2 != null)
                            return getHostName2.Invoke(sock, new object[0]) as string;
                    }
                }
            }
            catch { }

            return null;
        }
    }

    internal static class BossGateMessaging
    {
        // Cache chat signature resolution to avoid repeating reflection on every message.
        private static bool _chatResolved;
        private static int _chatVariant; // 0 = unknown, 1..4 = known variants

        internal static void SendBlocked(long peerId, string bossId)
        {
            string bossName = BossGateWorld.GetDisplayName(bossId);
            string msg = BossGatePlugin.BlockedMessage.Replace("{boss}", bossName);
            SendToPeer(peerId, msg, BossGatePlugin.AlsoShowCenterMessage);
        }

        internal static void SendToPeer(long peerId, string text, bool forceCenter)
        {
            if (peerId == 0L)
                return;

            // Try vanilla chat first (works for unmodded clients).
            bool chatOk = TrySendChatMessage(peerId, text);

            // Optional center message as a fallback / extra visibility.
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
            catch
            {
                return false;
            }
        }

        private static bool TrySendChatMessage(long peerId, string text)
        {
            try
            {
                if (ZRoutedRpc.instance == null)
                    return false;

                if (!_chatResolved)
                    ResolveChatSignature();

                if (_chatVariant == 0)
                    return false;

                Vector3 pos = Vector3.zero;
                int type = (int)Talker.Type.Shout;
                string serverName = "Server";
                string networkUserId = ""; // keep empty; avoid missing-type warnings.

                object[] args;

                if (_chatVariant == 1)
                    args = new object[] { pos, type, serverName, text };
                else if (_chatVariant == 2)
                    args = new object[] { pos, type, MakeServerUserInfo(serverName), text };
                else if (_chatVariant == 3)
                    args = new object[] { pos, type, MakeServerUserInfo(serverName), text, networkUserId };
                else // _chatVariant == 4
                    args = new object[] { pos, type, serverName, text, networkUserId };

                // Routed RPC name used by vanilla is "ChatMessage".
                ZRoutedRpc.instance.InvokeRoutedRPC(peerId, "ChatMessage", args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ResolveChatSignature()
        {
            _chatResolved = true;
            _chatVariant = 0;

            try
            {
                MethodInfo rpc = typeof(Chat).GetMethod("RPC_ChatMessage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (rpc == null)
                    return;

                ParameterInfo[] ps = rpc.GetParameters();
                int start = 0;
                if (ps.Length > 0 && ps[0].ParameterType == typeof(long))
                    start = 1;

                int count = ps.Length - start;

                if (count == 4 &&
                    ps[start + 0].ParameterType == typeof(Vector3) &&
                    ps[start + 1].ParameterType == typeof(int) &&
                    ps[start + 2].ParameterType == typeof(string) &&
                    ps[start + 3].ParameterType == typeof(string))
                {
                    _chatVariant = 1;
                    return;
                }

                if (count == 4 &&
                    ps[start + 0].ParameterType == typeof(Vector3) &&
                    ps[start + 1].ParameterType == typeof(int) &&
                    ps[start + 2].ParameterType == typeof(UserInfo) &&
                    ps[start + 3].ParameterType == typeof(string))
                {
                    _chatVariant = 2;
                    return;
                }

                if (count == 5 &&
                    ps[start + 0].ParameterType == typeof(Vector3) &&
                    ps[start + 1].ParameterType == typeof(int) &&
                    ps[start + 2].ParameterType == typeof(UserInfo) &&
                    ps[start + 3].ParameterType == typeof(string) &&
                    ps[start + 4].ParameterType == typeof(string))
                {
                    _chatVariant = 3;
                    return;
                }

                if (count == 5 &&
                    ps[start + 0].ParameterType == typeof(Vector3) &&
                    ps[start + 1].ParameterType == typeof(int) &&
                    ps[start + 2].ParameterType == typeof(string) &&
                    ps[start + 3].ParameterType == typeof(string) &&
                    ps[start + 4].ParameterType == typeof(string))
                {
                    _chatVariant = 4;
                    return;
                }
            }
            catch { }
        }

        private static UserInfo MakeServerUserInfo(string name)
        {
            try
            {
                MethodInfo m = typeof(UserInfo).GetMethod("GetLocalUser", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null)
                {
                    object res = m.Invoke(null, new object[0]);
                    if (res is UserInfo)
                    {
                        UserInfo u = (UserInfo)res;
                        u.Name = name;
                        return u;
                    }
                }
            }
            catch { }

            // Safe fallback for struct/class (best-effort).
            try
            {
                object obj = Activator.CreateInstance(typeof(UserInfo));
                if (obj is UserInfo)
                {
                    UserInfo u2 = (UserInfo)obj;
                    u2.Name = name;
                    return u2;
                }
            }
            catch { }

            UserInfo u3 = default(UserInfo);
            try { u3.Name = name; } catch { }
            return u3;
        }
    }

    internal static class BossGatePrefabUtil
    {
        // Returns a best-effort prefab name.
        // If ZNetView exposes GetPrefabName, prefer that.
        internal static string GetPrefabName(GameObject go)
        {
            if (go == null)
                return "";

            try
            {
                ZNetView znv = go.GetComponent<ZNetView>();
                if (znv != null)
                {
                    MethodInfo m = znv.GetType().GetMethod("GetPrefabName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null)
                    {
                        object res = m.Invoke(znv, new object[0]);
                        string s = res as string;
                        if (!string.IsNullOrEmpty(s))
                            return s;
                    }
                }
            }
            catch { }

            // Fallback: strip "(Clone)"
            string n = go.name;
            if (n.EndsWith("(Clone)"))
                n = n.Substring(0, n.Length - "(Clone)".Length);

            return n;
        }

        internal static bool HasPrefabInParents(GameObject go, string wantedPrefabLower)
        {
            if (go == null || string.IsNullOrEmpty(wantedPrefabLower))
                return false;

            Transform t = go.transform;
            while (t != null)
            {
                string p = GetPrefabName(t.gameObject);
                if (!string.IsNullOrEmpty(p) && p.ToLowerInvariant() == wantedPrefabLower)
                    return true;

                t = t.parent;
            }
            return false;
        }
    }

    internal static class BossGateBossResolver
    {
        // Exact prefab mappings (based on your 0.221.4 data):
        private static readonly Dictionary<string, string> BossPrefabToBossIdLower = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Eikthyr", "eikthyr" },
            { "gd_king", "elder" },
            { "Bonemass", "bonemass" },
            { "Dragon", "moder" },
            { "GoblinKing", "yagluth" },
            { "SeekerQueen", "queen" },
            { "Fader", "fader" },
        };

        // Altar / related structure mappings
        private static readonly Dictionary<string, string> OwnerPrefabToBossIdLower = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "BossStone_Eikthyr", "eikthyr" },
            { "BossStone_TheElder", "elder" },
            { "BossStone_Bonemass", "bonemass" },
            { "BossStone_DragonQueen", "moder" },
            { "BossStone_Yagluth", "yagluth" },
            { "BossStone_TheQueen", "queen" },
            { "BossStone_Fader", "fader" },

            // Extra holders / cups (defensive mapping)
            { "dragoneggcup", "moder" },
            { "goblinking_totemholder", "yagluth" },
            { "fader_bellholder", "fader" },
        };

        internal static bool TryResolveBossIdFromOfferingBowl(OfferingBowl bowl, out string bossId)
        {
            bossId = "";

            if (bowl == null)
                return false;

            // 1) Prefer the boss prefab field if present.
            GameObject bossPrefab = TryGetBossPrefab(bowl);
            if (bossPrefab != null)
            {
                string prefabName = BossGatePrefabUtil.GetPrefabName(bossPrefab);
                if (TryMapBossPrefabName(prefabName, out bossId))
                    return true;
            }

            // 2) Fallback: infer from altar/owner prefab names by scanning parents.
            Transform t = bowl.transform;
            while (t != null)
            {
                string ownerPrefab = BossGatePrefabUtil.GetPrefabName(t.gameObject);
                if (TryMapOwnerPrefabName(ownerPrefab, out bossId))
                    return true;

                t = t.parent;
            }

            return false;
        }

        private static GameObject TryGetBossPrefab(OfferingBowl bowl)
        {
            try
            {
                FieldInfo direct = typeof(OfferingBowl).GetField("m_bossPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (direct != null)
                    return direct.GetValue(bowl) as GameObject;

                // Heuristic scan: look for GameObject fields containing "boss" in name.
                FieldInfo[] fields = typeof(OfferingBowl).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo f = fields[i];
                    if (f.FieldType != typeof(GameObject))
                        continue;

                    string n = f.Name.ToLowerInvariant();
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

        private static bool TryMapOwnerPrefabName(string ownerPrefabName, out string bossId)
        {
            bossId = "";

            if (string.IsNullOrEmpty(ownerPrefabName))
                return false;

            string mapped;
            if (OwnerPrefabToBossIdLower.TryGetValue(ownerPrefabName, out mapped))
            {
                bossId = mapped;
                return true;
            }

            string lower = ownerPrefabName.ToLowerInvariant();
            foreach (KeyValuePair<string, string> kv in OwnerPrefabToBossIdLower)
            {
                if (kv.Key.ToLowerInvariant() == lower)
                {
                    bossId = kv.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryMapBossPrefabName(string bossPrefabName, out string bossId)
        {
            bossId = "";

            if (string.IsNullOrEmpty(bossPrefabName))
                return false;

            string mapped;
            if (BossPrefabToBossIdLower.TryGetValue(bossPrefabName, out mapped))
            {
                bossId = mapped;
                return true;
            }

            string lower = bossPrefabName.ToLowerInvariant();
            foreach (KeyValuePair<string, string> kv in BossPrefabToBossIdLower)
            {
                if (kv.Key.ToLowerInvariant() == lower)
                {
                    bossId = kv.Value;
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch]
    internal static class BossGatePatches
    {
        // --- OfferingBowl RPC interception (server authoritative) ---
        [HarmonyPatch(typeof(OfferingBowl), "RPC_BossSpawnInitiated")]
        [HarmonyPrefix]
        private static bool OfferingBowl_RPC_BossSpawnInitiated_Prefix(OfferingBowl __instance, long sender)
        {
            return BossGateGateLogic.ServerAllowOrBlock(__instance, sender);
        }

        [HarmonyPatch(typeof(OfferingBowl), "RPC_RemoveBossSpawnInventoryItems")]
        [HarmonyPrefix]
        private static bool OfferingBowl_RPC_RemoveBossSpawnInventoryItems_Prefix(OfferingBowl __instance, long sender)
        {
            return BossGateGateLogic.ServerAllowOrBlock(__instance, sender);
        }

        [HarmonyPatch(typeof(OfferingBowl), "RPC_SpawnBoss")]
        [HarmonyPrefix]
        private static bool OfferingBowl_RPC_SpawnBoss_Prefix(OfferingBowl __instance, long sender)
        {
            return BossGateGateLogic.ServerAllowOrBlock(__instance, sender);
        }

        // --- Mistlands Queen door block ---
        [HarmonyPatch(typeof(Door), "RPC_UseDoor")]
        [HarmonyPrefix]
        private static bool Door_RPC_UseDoor_Prefix(Door __instance, long sender)
        {
            if (!BossGatePlugin.BlockQueenDoorWhenQueenDisabled)
                return true;

            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return true;

            if (BossGateWorld.IsBossEnabled("queen"))
                return true;

            // Only block the queen door prefab (root or any parent).
            if (BossGatePrefabUtil.HasPrefabInParents(__instance.gameObject, "dungeon_queen_door"))
            {
                BossGateMessaging.SendBlocked(sender, "queen");
                return false;
            }

            return true;
        }
    }

    internal static class BossGateGateLogic
    {
        internal static bool ServerAllowOrBlock(OfferingBowl bowl, long senderPeerId)
        {
            // Only enforce on server/host. Clients should follow server decisions.
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

                // If not blocking unknown, allow.
                return true;
            }

            if (BossGateWorld.IsBossEnabled(bossId))
                return true;

            BossGateMessaging.SendBlocked(senderPeerId, bossId);
            return false;
        }
    }
}
