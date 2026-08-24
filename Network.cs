using BepInEx;
using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ZexQoLMenu
{
    public partial class Plugin
    {
        private void PatchPhotonRoomCreation()
        {
            try
            {
                Type photonNet = typeof(PhotonNetwork);
                int patched = 0;

                // CreateRoom(string, RoomOptions, TypedLobby, string[])
                MethodInfo create = AccessTools.Method(
                    photonNet,
                    "CreateRoom",
                    new Type[]
                    {
                        typeof(string),
                        typeof(RoomOptions),
                        typeof(TypedLobby),
                        typeof(string[])
                    });
                if (create != null)
                {
                    spectateHarmony.Patch(
                        create,
                        new HarmonyMethod(typeof(Plugin), nameof(PhotonCreateRoomPrefix)));
                    patched++;
                }

                // JoinOrCreateRoom(string, RoomOptions, TypedLobby, string[])
                MethodInfo joinOrCreate = AccessTools.Method(
                    photonNet,
                    "JoinOrCreateRoom",
                    new Type[]
                    {
                        typeof(string),
                        typeof(RoomOptions),
                        typeof(TypedLobby),
                        typeof(string[])
                    });
                if (joinOrCreate != null)
                {
                    spectateHarmony.Patch(
                        joinOrCreate,
                        new HarmonyMethod(typeof(Plugin), nameof(PhotonJoinOrCreateRoomPrefix)));
                    patched++;
                }

                // Fallback: any CreateRoom / JoinOrCreateRoom overload with a RoomOptions parameter
                if (patched == 0)
                {
                    MethodInfo[] methods = photonNet.GetMethods(BindingFlags.Public | BindingFlags.Static);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        MethodInfo m = methods[i];
                        if (m == null) continue;
                        if (m.Name != "CreateRoom" && m.Name != "JoinOrCreateRoom") continue;
                        ParameterInfo[] ps = m.GetParameters();
                        bool hasOpts = false;
                        for (int p = 0; p < ps.Length; p++)
                        {
                            if (ps[p].ParameterType == typeof(RoomOptions))
                            {
                                hasOpts = true;
                                break;
                            }
                        }
                        if (!hasOpts) continue;
                        spectateHarmony.Patch(
                            m,
                            new HarmonyMethod(typeof(Plugin), nameof(PhotonRoomOptionsPrefixGeneric)));
                        patched++;
                    }
                }

                Logger.LogInfo("Photon room-create lobby props patch: " + patched + " method(s).");
            }
            catch (Exception ex)
            {
                Logger.LogError("Photon room-create patch failed: " + ex);
            }
        }


        private void PatchPhotonRoomListSpawner()
        {
            try
            {
                Type spawner = AccessTools.TypeByName("PhotonRoomListSpawner");
                if (spawner == null)
                    spawner = SafeGameType("PhotonRoomListSpawner");
                if (spawner == null)
                {
                    Logger.LogInfo("PhotonRoomListSpawner type not found — in-room list pull limited.");
                    return;
                }

                // Patch OnRoomListUpdate(List<RoomInfo>) to always feed our cache
                MethodInfo onList = AccessTools.Method(spawner, "OnRoomListUpdate", new Type[] { typeof(List<RoomInfo>) });
                if (onList == null)
                {
                    // try without strict signature
                    foreach (var m in spawner.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (m.Name == "OnRoomListUpdate")
                        {
                            onList = m;
                            break;
                        }
                    }
                }
                if (onList != null && spectateHarmony != null)
                {
                    spectateHarmony.Patch(onList, postfix: new HarmonyMethod(typeof(Plugin), nameof(RoomListSpawnerOnRoomListUpdatePostfix)));
                    Logger.LogInfo("Patched PhotonRoomListSpawner.OnRoomListUpdate");
                }

                // Cache type for field scraping
                roomListSpawnerType = spawner;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("PatchPhotonRoomListSpawner: " + ex.Message);
            }
        }

        private static Type roomListSpawnerType;

        /// <summary>Keep Zex cache in sync whenever the game receives a lobby list (even if UI closed).</summary>
        private static void RoomListSpawnerOnRoomListUpdatePostfix(List<RoomInfo> roomList)
        {
            try
            {
                if (Instance == null || roomList == null) return;
                Instance.MergeRoomListUpdate(roomList, fromGameSpawner: true);
            }
            catch { }
        }

        private void MergeRoomListUpdate(List<RoomInfo> roomList, bool fromGameSpawner = false)
        {
            if (roomList == null) return;
            foreach (RoomInfo info in roomList)
            {
                if (info == null) continue;
                if (info.RemovedFromList || !info.IsVisible)
                    cachedRooms.Remove(info.Name);
                else
                    cachedRooms[info.Name] = info;
            }
            lastRoomListUpdateTime = Time.unscaledTime;
            if (isBrowsingServers || cachedRooms.Count > 0)
            {
                string src = fromGameSpawner ? "game" : "lobby";
                if (pendingRejoinPrevious)
                    serverListStatus = "GOT LIST • " + cachedRooms.Count + " rooms • rejoining…";
                else if (PhotonNetwork.InRoom)
                    serverListStatus = "CACHED • " + cachedRooms.Count + " rooms (" + src + ", still in room)";
                else
                    serverListStatus = "LIVE • " + cachedRooms.Count + " rooms";
            }
        }

        /// <summary>
        /// Try to read whatever room list the game already holds (no leave required).
        /// Works when PhotonRoomListSpawner (or similar) still has a list from last lobby visit.
        /// </summary>
        private bool TryPullRoomListFromGameSpawner()
        {
            try
            {
                Type spawner = roomListSpawnerType;
                if (spawner == null)
                {
                    spawner = AccessTools.TypeByName("PhotonRoomListSpawner") ?? SafeGameType("PhotonRoomListSpawner");
                    roomListSpawnerType = spawner;
                }
                if (spawner == null) return false;

                int before = cachedRooms.Count;
                // Instance via FindObjectOfType / singleton field
                object inst = null;
                try
                {
                    var find = typeof(UnityEngine.Object).GetMethod("FindObjectOfType", new Type[] { typeof(Type) });
                    if (find != null)
                        inst = find.Invoke(null, new object[] { spawner });
                }
                catch { }

                if (inst == null)
                {
                    foreach (var f in spawner.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (f.FieldType == spawner || f.FieldType.IsAssignableFrom(spawner))
                        {
                            inst = f.GetValue(null);
                            if (inst != null) break;
                        }
                    }
                }

                // Scrape List<RoomInfo> / RoomInfo[] / dict from type + instance
                var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var f in spawner.GetFields(flags))
                {
                    object owner = f.IsStatic ? null : inst;
                    if (!f.IsStatic && inst == null) continue;
                    object val = null;
                    try { val = f.GetValue(owner); } catch { continue; }
                    if (val == null) continue;
                    if (val is List<RoomInfo> list)
                        MergeRoomListUpdate(list, fromGameSpawner: true);
                    else if (val is RoomInfo[] arr)
                        MergeRoomListUpdate(new List<RoomInfo>(arr), fromGameSpawner: true);
                    else if (val is IDictionary dict)
                    {
                        var tmp = new List<RoomInfo>();
                        foreach (var o in dict.Values)
                            if (o is RoomInfo ri) tmp.Add(ri);
                        if (tmp.Count > 0) MergeRoomListUpdate(tmp, fromGameSpawner: true);
                    }
                }

                // Also try PhotonNetwork.GetRoomList if available (older PUN)
                try
                {
                    var getList = AccessTools.Method(typeof(PhotonNetwork), "GetRoomList");
                    if (getList != null)
                    {
                        object r = getList.Invoke(null, null);
                        if (r is RoomInfo[] a2) MergeRoomListUpdate(new List<RoomInfo>(a2), true);
                        else if (r is List<RoomInfo> l2) MergeRoomListUpdate(l2, true);
                    }
                }
                catch { }

                return cachedRooms.Count > before || cachedRooms.Count > 0;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("TryPullRoomListFromGameSpawner: " + ex.Message);
                return false;
            }
        }

                // Harmony prefixes — inject lobby-visible player-list property into RoomOptions
        private static void PhotonCreateRoomPrefix(ref RoomOptions roomOptions)
        {
            InjectPlayerListLobbyProps(ref roomOptions);
        }

        private static void PhotonJoinOrCreateRoomPrefix(ref RoomOptions roomOptions)
        {
            InjectPlayerListLobbyProps(ref roomOptions);
        }

        /// <summary>Generic prefix: finds RoomOptions arg by scanning __args (Harmony injected).</summary>
        private static void PhotonRoomOptionsPrefixGeneric(object[] __args)
        {
            if (__args == null) return;
            for (int i = 0; i < __args.Length; i++)
            {
                if (__args[i] is RoomOptions)
                {
                    RoomOptions opts = (RoomOptions)__args[i];
                    InjectPlayerListLobbyProps(ref opts);
                    __args[i] = opts;
                    return;
                }
                if (__args[i] == null)
                {
                    // Can't know if this slot is RoomOptions without signature — skip
                }
            }
        }

        private static void InjectPlayerListLobbyProps(ref RoomOptions roomOptions)
        {
            try
            {
                if (roomOptions == null)
                    roomOptions = new RoomOptions();

                // Custom properties bag
                ExitGames.Client.Photon.Hashtable props = roomOptions.CustomRoomProperties;
                if (props == null)
                    props = new ExitGames.Client.Photon.Hashtable();

                if (!props.ContainsKey(RoomPlayersPropertyKey))
                    props[RoomPlayersPropertyKey] = "";

                roomOptions.CustomRoomProperties = props;

                // Lobby-visible keys
                string[] lobbyKeys = roomOptions.CustomRoomPropertiesForLobby;
                bool hasKey = false;
                if (lobbyKeys != null)
                {
                    for (int i = 0; i < lobbyKeys.Length; i++)
                    {
                        if (lobbyKeys[i] == RoomPlayersPropertyKey)
                        {
                            hasKey = true;
                            break;
                        }
                    }
                }

                if (!hasKey)
                {
                    if (lobbyKeys == null || lobbyKeys.Length == 0)
                    {
                        roomOptions.CustomRoomPropertiesForLobby = new string[] { RoomPlayersPropertyKey };
                    }
                    else
                    {
                        string[] expanded = new string[lobbyKeys.Length + 1];
                        for (int i = 0; i < lobbyKeys.Length; i++)
                            expanded[i] = lobbyKeys[i];
                        expanded[lobbyKeys.Length] = RoomPlayersPropertyKey;
                        roomOptions.CustomRoomPropertiesForLobby = expanded;
                    }
                }
            }
            catch
            {
                // Never block room create
            }
        }

        private IEnumerator DelayedPatchScanModSkip()
        {
            // Script Engine / hot-reload: wait until Assembly-CSharp types exist
            for (int i = 0; i < 50; i++)
            {
                if (SafeGameType("ModManager") != null ||
                    SafeGameType("NetworkManager") != null ||
                    SafeGameType("SteamWorkshopModLoader") != null)
                    break;
                yield return new WaitForSecondsRealtime(0.1f);
            }
            yield return null;
            PatchScanModSkip();
        }

        /// <summary>
        /// While room-scanning, skip the game's mod handshake / spawn work so we can
        /// read PhotonNetwork.PlayerList and leave without downloading/reloading mods.
        /// KoboldKare: host raises mod-list event → client compares → may Leave+download+rejoin.
        /// </summary>
        private void PatchScanModSkip()
        {
            int patched = 0;
            try
            {
                // 1) Block PhotonNetwork.Instantiate while scanning (no local kobold spawn)
                MethodInfo[] instMethods = typeof(PhotonNetwork).GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < instMethods.Length; i++)
                {
                    MethodInfo m = instMethods[i];
                    if (m == null || m.Name != "Instantiate") continue;
                    if (m.ReturnType != typeof(GameObject)) continue;
                    try
                    {
                        spectateHarmony.Patch(
                            m,
                            new HarmonyMethod(typeof(Plugin), nameof(ScanSkipInstantiatePrefix)));
                        patched++;
                    }
                    catch { }
                }

                // 2) Emulate non-Workshop join: skip mod handshake / Steam download while scanning.
                //    Known KK types: ModManager, SteamWorkshopModLoader, NetworkManager (mod sync).
                List<string> patchedNames = new List<string>();
                List<Type> targetTypes = new List<Type>();

                string[] knownTypeNames =
                {
                    "ModManager",
                    "SteamWorkshopModLoader",
                    "SteamWorkshopItem",
                    "SteamWorkshop",
                    "WorkshopManager",
                    "ModLoader",
                    "ModDatabase",
                    "NetworkManager"
                };
                for (int k = 0; k < knownTypeNames.Length; k++)
                {
                    Type kt = SafeGameType(knownTypeNames[k]);
                    if (kt != null && !targetTypes.Contains(kt))
                        targetTypes.Add(kt);
                    Logger.LogInfo("Scan mod-skip type " + knownTypeNames[k] + ": " +
                        (kt != null ? kt.FullName : "NOT FOUND"));
                }

                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int a = 0; a < assemblies.Length; a++)
                {
                    Assembly asm = assemblies[a];
                    if (asm == null) continue;
                    string an = asm.GetName().Name ?? "";
                    if (an != "Assembly-CSharp" && an != "Assembly-CSharp-firstpass")
                        continue;

                    Type[] types = null;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtl) { types = rtl.Types; }
                    catch { continue; }
                    if (types == null) continue;

                    for (int t = 0; t < types.Length; t++)
                    {
                        Type type = types[t];
                        if (type == null) continue;
                        string tn = type.Name ?? "";
                        string fn = type.FullName ?? "";

                        bool modRelated =
                            fn.IndexOf(".Modding.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            tn.IndexOf("SteamWorkshop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            tn.Equals("ModManager", StringComparison.OrdinalIgnoreCase) ||
                            tn.Equals("ModLoader", StringComparison.OrdinalIgnoreCase) ||
                            (tn.StartsWith("Mod", StringComparison.OrdinalIgnoreCase) &&
                             tn.IndexOf("Module", StringComparison.OrdinalIgnoreCase) < 0 &&
                             tn.IndexOf("Modifier", StringComparison.OrdinalIgnoreCase) < 0 &&
                             tn.IndexOf("Model", StringComparison.OrdinalIgnoreCase) < 0 &&
                             tn.IndexOf("Mode", StringComparison.OrdinalIgnoreCase) < 0);

                        if (modRelated && !targetTypes.Contains(type))
                            targetTypes.Add(type);
                    }
                }

                for (int t = 0; t < targetTypes.Count; t++)
                {
                    Type type = targetTypes[t];
                    if (type == null) continue;
                    string tn = type.Name ?? "";

                    bool isNetworkManager = tn.Equals("NetworkManager", StringComparison.OrdinalIgnoreCase);
                    bool isWorkshop = tn.IndexOf("SteamWorkshop", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isModManager = tn.Equals("ModManager", StringComparison.OrdinalIgnoreCase) ||
                                        tn.Equals("ModLoader", StringComparison.OrdinalIgnoreCase);

                    MethodInfo[] methods;
                    try
                    {
                        // DeclaredOnly — never patch Unity MonoBehaviour (SendMessage, Awake, etc.)
                        methods = type.GetMethods(
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.DeclaredOnly);
                    }
                    catch { continue; }

                    for (int mi = 0; mi < methods.Length; mi++)
                    {
                        MethodInfo method = methods[mi];
                        if (method == null || method.IsAbstract || method.IsGenericMethodDefinition)
                            continue;
                        // Only methods actually declared on this type
                        if (method.DeclaringType != type)
                            continue;

                        string mn = method.Name ?? "";
                        if (mn.StartsWith("get_") || mn.StartsWith("set_") ||
                            mn.StartsWith("add_") || mn.StartsWith("remove_"))
                            continue;

                        bool interesting;
                        if (isWorkshop)
                        {
                            // All declared workshop methods (download/query/subscribe callbacks)
                            interesting = true;
                        }
                        else if (isNetworkManager)
                        {
                            interesting =
                                mn.IndexOf("Mod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Workshop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Handshake", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Bundle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Catalog", StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        else if (isModManager)
                        {
                            interesting =
                                mn.IndexOf("Sync", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Compare", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Handshake", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Validate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Load", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Reload", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Download", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Mount", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Unmount", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Apply", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Receive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Process", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Finished", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Require", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Missing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Join", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Room", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Bundle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Activate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Enable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("Listener", StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        else
                        {
                            interesting =
                                mn.IndexOf("Download", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("LoadMod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                mn.IndexOf("ModList", StringComparison.OrdinalIgnoreCase) >= 0;
                        }

                        if (!interesting) continue;

                        if (method.ReturnType != typeof(void))
                            continue;

                        try
                        {
                            spectateHarmony.Patch(
                                method,
                                new HarmonyMethod(typeof(Plugin), nameof(ScanSkipModMethodPrefix)));
                            patched++;
                            if (patchedNames.Count < 40)
                                patchedNames.Add(tn + "." + mn);
                        }
                        catch { }
                    }
                }

                Logger.LogInfo("Scan mod-skip Harmony patches: " + patched + " method(s).");
                if (patchedNames.Count > 0)
                    Logger.LogInfo("Scan mod-skip targets: " + string.Join(", ", patchedNames.ToArray()));
            }
            catch (Exception ex)
            {
                Logger.LogWarning("PatchScanModSkip failed: " + ex.Message);
            }
        }

        /// <summary>Harmony prefix: skip PhotonNetwork.Instantiate while room-scanning.</summary>
        private static bool ScanSkipInstantiatePrefix(ref GameObject __result)
        {
            if (Instance == null || !Instance.scanRunning)
                return true;
            __result = null;
            return false;
        }

        /// <summary>Harmony prefix: skip mod sync/load/download methods while room-scanning.</summary>
        private static bool ScanSkipModMethodPrefix()
        {
            if (Instance == null || !Instance.scanRunning)
                return true;
            return false; // skip original
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent == null)
                return;

            // Zex client private messages (always handle)
            if (photonEvent.Code == ZexPrivateMsgEventCode)
            {
                HandleZexClientEvent(photonEvent);
                return;
            }

            if (!scanRunning)
                return;

            try
            {
                byte code = photonEvent.Code;
                // PUN reserved codes are 200+; custom are 0–199. Mod sync is custom.
                // Also block letter codes sometimes used as (byte)'M' == 77.
                if (code == (byte)'M' || code == (byte)'m' || code == 77)
                {
                    // Eat event by not processing — we can't cancel Photon delivery, but if the
                    // game's handler is also Harmony-patched we're fine. Log once per scan room.
                    return;
                }

                object data = photonEvent.CustomData;
                if (data is string)
                {
                    string s = (string)data;
                    if (s.IndexOf("mod", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        (s.IndexOf('{') >= 0 || s.IndexOf('[') >= 0))
                    {
                        // Looks like mod-list JSON — game handler may still see it unless patched
                        return;
                    }
                }
            }
            catch { }
        }

        private void LeaveCurrentRoom()
        {
            if (!PhotonNetwork.InRoom)
                return;
            LeaveRoomSafe();
        }

        /// <summary>
        /// Leave the current Photon room. Optionally destroys local body first (QoL toggle).
        /// </summary>
        private void LeaveRoomSafe()
        {
            if (!PhotonNetwork.InRoom)
                return;

            if (destroyBodyOnLeave)
            {
                ShowToast("Leaving… body cleanup ON", "system");
                // Destroy body first, wait a couple frames so Photon can send the destroy, then leave
                StartCoroutine(LeaveRoomAfterDestroyBodyRoutine());
                return;
            }

            try
            {
                PhotonNetwork.LeaveRoom();
                ShowToast("Left room");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("LeaveRoomSafe: " + ex.Message);
                ShowToast("Leave failed: " + ex.Message);
            }
        }

        private System.Collections.IEnumerator LeaveRoomAfterDestroyBodyRoutine()
        {
            try { DestroyLocalPlayerBodyForBrowse(); }
            catch (Exception ex) { Logger.LogWarning("LeaveRoom destroy body: " + ex.Message); }

            // Let Photon flush destroy messages while still InRoom
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.15f);

            // Second pass in case game respawned / delayed views
            try { DestroyLocalPlayerBodyForBrowse(); }
            catch { }

            yield return null;

            if (!PhotonNetwork.InRoom)
            {
                ShowToast("Left room (body destroyed)");
                yield break;
            }

            try
            {
                PhotonNetwork.LeaveRoom();
                ShowToast("Left room (body destroyed)");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("LeaveRoom after destroy: " + ex.Message);
                ShowToast("Leave failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Community workaround: splashing water on a desynced kobold often resyncs
        /// visuals/physics after join. Spawns water near the target + soft body nudge.
        /// </summary>
        /// <summary>
        /// Auto water splash:
        ///  - You join a room  → splash everyone once, one toast "Splashed all".
        ///  - Someone else joins → splash everyone except the new player, one toast.
        /// Uses CharCon-style FluidProjectile + ScriptableReagent "Water" via reflection.
        /// </summary>
        private Coroutine autoSplashCoroutine;

        private void ScheduleSplashEveryoneOnJoin()
        {
            return; // Splash feature removed (lag in large lobbies)
            if (!autoSplashOnJoin || !PhotonNetwork.InRoom)
                return;
            if (Time.unscaledTime < nextAutoSplashAllowed)
                return;
            nextAutoSplashAllowed = Time.unscaledTime + 1.5f;
            StartAutoSplashRoom(excludeActorId: -1, waitSeconds: 0.75f);
        }

        private void ScheduleSplashNewPlayer(Player newPlayer)
        {
            return; // Splash feature removed
            if (!autoSplashOnJoin || newPlayer == null || newPlayer.IsLocal)
                return;
            if (!PhotonNetwork.InRoom)
                return;
            // Splash the whole room except the person who just joined
            StartAutoSplashRoom(excludeActorId: newPlayer.ActorNumber, waitSeconds: 0.6f);
        }

        private void StartAutoSplashRoom(int excludeActorId, float waitSeconds)
        {
            if (autoSplashCoroutine != null)
            {
                try { StopCoroutine(autoSplashCoroutine); } catch { }
                autoSplashCoroutine = null;
            }
            autoSplashCoroutine = StartCoroutine(AutoSplashRoomRoutine(excludeActorId, waitSeconds));
        }

        private System.Collections.IEnumerator AutoSplashRoomRoutine(int excludeActorId, float waitSeconds)
        {
            // Auto paths gate on autoSplashOnJoin upstream; manual Splash all always runs.
            if (waitSeconds > 0f)
                yield return new WaitForSecondsRealtime(waitSeconds);
            if (!PhotonNetwork.InRoom)
            {
                autoSplashCoroutine = null;
                yield break;
            }

            // Wait-for-bodies: poll until at least one splashable body exists, or 5s timeout
            const float bodyTimeout = 5f;
            float bodyDeadline = Time.unscaledTime + bodyTimeout;
            while (Time.unscaledTime < bodyDeadline)
            {
                if (!PhotonNetwork.InRoom)
                {
                    autoSplashCoroutine = null;
                    yield break;
                }
                int ready = 0;
                Player[] waitList = PhotonNetwork.PlayerList;
                if (waitList != null)
                {
                    for (int i = 0; i < waitList.Length; i++)
                    {
                        Player p = waitList[i];
                        if (p == null) continue;
                        if (excludeActorId > 0 && p.ActorNumber == excludeActorId)
                            continue;
                        if (p.IsLocal || FindPlayerObject(p) != null)
                            ready++;
                    }
                }
                if (ready > 0)
                    break;
                yield return new WaitForSecondsRealtime(0.2f);
            }

            int totalShots = 0;
            int targetsHit = 0;
            Player[] players = PhotonNetwork.PlayerList;
            if (players != null)
            {
                for (int i = 0; i < players.Length; i++)
                {
                    Player p = players[i];
                    if (p == null) continue;
                    if (excludeActorId > 0 && p.ActorNumber == excludeActorId)
                        continue;

                    int shots = 0;
                    for (int attempt = 0; attempt < 5 && shots == 0; attempt++)
                    {
                        if (FindPlayerObject(p) == null && !p.IsLocal)
                        {
                            yield return new WaitForSecondsRealtime(0.15f);
                            continue;
                        }
                        shots = SplashPlayerWithWater(p);
                        if (shots == 0)
                            yield return new WaitForSecondsRealtime(0.1f);
                    }

                    if (shots > 0)
                    {
                        totalShots += shots;
                        targetsHit++;
                    }
                    yield return null;
                }
            }

            if (targetsHit > 0)
            {
                autoSplashStatus = "Splashed all";
                ShowToast("Splashed all");
            }
            else
            {
                autoSplashStatus = "Splash: no targets";
            }
            autoSplashStatusUntil = Time.unscaledTime + 4f;
            Logger.LogInfo("Auto splash: targets=" + targetsHit + " shots=" + totalShots +
                           " exclude=" + excludeActorId);
            autoSplashCoroutine = null;
        }

        /// <summary>
        /// Fire a few Water FluidProjectiles at a player's body (CharCon fluid pistol path).
        /// </summary>
        private int SplashPlayerWithWater(Player target)
        {
            if (target == null || !PhotonNetwork.InRoom)
                return 0;

            try
            {
                GameObject bodyGo = target.IsLocal
                    ? (ResolveLocalPlayerBody() ?? GetLocalPlayer())
                    : FindPlayerObject(target);
                if (bodyGo == null)
                    return 0;

                Component kob = GetKoboldOn(bodyGo);
                if (kob == null)
                {
                    Type kt = SafeGameType("Kobold");
                    if (kt != null)
                        kob = bodyGo.GetComponentInChildren(kt, true);
                }

                return SpawnWaterFluidAt(bodyGo, kob, bodyGo.transform.position);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("SplashPlayerWithWater: " + ex.Message);
                return 0;
            }
        }

        private int SpawnWaterFluidAt(GameObject bodyGo, Component kob, Vector3 targetPos)
        {
            int spawned = 0;
            try
            {
                Type reagentContentsType = SafeGameType("ReagentContents");
                Type scriptableReagentType = SafeGameType("ScriptableReagent");
                Type bitBufferType = SafeGameType("NetStack.Serialization.BitBuffer")
                    ?? SafeGameType("BitBuffer");
                Type halfPrecisionType = SafeGameType("NetStack.Quantization.HalfPrecision")
                    ?? SafeGameType("HalfPrecision");
                Type projectileType = SafeGameType("Projectile");

                if (reagentContentsType == null)
                {
                    Logger.LogWarning("Auto splash: ReagentContents type missing");
                    return 0;
                }
                if (bitBufferType == null)
                {
                    Logger.LogWarning("Auto splash: BitBuffer type missing");
                    return 0;
                }

                object waterReagent = ResolveWaterReagent(scriptableReagentType);
                if (waterReagent == null)
                {
                    Logger.LogWarning("Auto splash: Water reagent not found");
                    return TryInjectWaterFallback(kob, bodyGo);
                }

                MethodInfo setMaxVolume = AccessTools.Method(reagentContentsType, "SetMaxVolume", new Type[] { typeof(float) });
                MethodInfo addReagentContents = FindBitBufferWriteMethod(bitBufferType, "AddReagentContents");
                MethodInfo addUShort = AccessTools.Method(bitBufferType, "AddUShort", new Type[] { typeof(ushort) });
                MethodInfo quantize = halfPrecisionType != null
                    ? AccessTools.Method(halfPrecisionType, "Quantize", new Type[] { typeof(float) })
                    : null;

                // Build ReagentContents with Water using whatever AddMix overload exists
                // Tiny volume so splash triggers systems without filling belly
                System.Func<object> buildContents = () => BuildWaterReagentContents(reagentContentsType, waterReagent, 0.05f, setMaxVolume);
                object testContents = buildContents();
                if (testContents == null)
                {
                    Logger.LogWarning("Auto splash: could not build Water ReagentContents (GetReagent/AddMix)");
                    return TryInjectWaterFallback(kob, bodyGo);
                }
                if (addReagentContents == null)
                {
                    Logger.LogWarning("Auto splash: AddReagentContents extension not found — inject fallback");
                    return TryInjectWaterFallback(kob, bodyGo);
                }

                Vector3 spawnPos = targetPos + Vector3.up * 1.7f;
                Rigidbody kobBody = null;
                if (kob != null)
                {
                    try
                    {
                        FieldInfo bodyField = AccessTools.Field(kob.GetType(), "body");
                        if (bodyField != null)
                            kobBody = bodyField.GetValue(kob) as Rigidbody;
                    }
                    catch { }
                }
                if (kobBody == null && bodyGo != null)
                    kobBody = bodyGo.GetComponentInChildren<Rigidbody>();

                const float volume = 0.05f; // near-zero: contact only, no belly fill
                const float force = 8f;
                const int count = 3;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        object contents = BuildWaterReagentContents(reagentContentsType, waterReagent, volume, setMaxVolume);
                        if (contents == null)
                        {
                            Logger.LogWarning("Water fluid: build contents failed");
                            continue;
                        }

                        Vector3 aim = (targetPos + Vector3.up * 0.5f - spawnPos).normalized;
                        if (aim.sqrMagnitude < 0.01f)
                            aim = Vector3.down;
                        Vector3 velocity = aim * force + UnityEngine.Random.insideUnitSphere * 1.25f;

                        object buffer;
                        try { buffer = Activator.CreateInstance(bitBufferType, new object[] { 16 }); }
                        catch { buffer = Activator.CreateInstance(bitBufferType); }
                        if (buffer == null) continue;

                        // Extension methods are static (buffer, contents)
                        if (addReagentContents.IsStatic)
                            addReagentContents.Invoke(null, new object[] { buffer, contents });
                        else
                            addReagentContents.Invoke(buffer, new object[] { contents });

                        if (addUShort != null && quantize != null)
                        {
                            object qx = quantize.Invoke(null, new object[] { velocity.x });
                            object qy = quantize.Invoke(null, new object[] { velocity.y });
                            object qz = quantize.Invoke(null, new object[] { velocity.z });
                            ushort ux = qx is ushort ? (ushort)qx : Convert.ToUInt16(qx);
                            ushort uy = qy is ushort ? (ushort)qy : Convert.ToUInt16(qy);
                            ushort uz = qz is ushort ? (ushort)qz : Convert.ToUInt16(qz);
                            addUShort.Invoke(buffer, new object[] { ux });
                            addUShort.Invoke(buffer, new object[] { uy });
                            addUShort.Invoke(buffer, new object[] { uz });
                        }

                        Quaternion rot = Quaternion.LookRotation(velocity.sqrMagnitude > 0.01f ? velocity : Vector3.down);
                        Vector3 pos = spawnPos + UnityEngine.Random.insideUnitSphere * 0.1f;
                        GameObject obj = null;
                        string[] prefabNames =
                        {
                            "FluidProjectile",
                            "bucketSplashProjectile",
                            "BucketSplashProjectile",
                            "projectileBlob"
                        };
                        string usedName = null;
                        for (int pn = 0; pn < prefabNames.Length && obj == null; pn++)
                        {
                            try
                            {
                                obj = PhotonNetwork.Instantiate(
                                    prefabNames[pn],
                                    pos,
                                    rot,
                                    0,
                                    new object[] { buffer });
                                if (obj != null)
                                    usedName = prefabNames[pn];
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning("Instantiate " + prefabNames[pn] + ": " + ex.Message);
                            }
                        }
                        if (obj == null)
                        {
                            Logger.LogWarning("All fluid prefab Instantiates returned null");
                            continue;
                        }
                        Logger.LogInfo("Spawned fluid prefab " + usedName);
                        spawned++;
                        spawnedObjects.Add(obj);

                        if (projectileType != null && kobBody != null)
                        {
                            try
                            {
                                Component proj = obj.GetComponent(projectileType)
                                    ?? obj.GetComponentInChildren(projectileType, true);
                                if (proj != null)
                                {
                                    MethodInfo launch = AccessTools.Method(projectileType, "LaunchFrom",
                                        new Type[] { typeof(Rigidbody) });
                                    if (launch != null)
                                        launch.Invoke(proj, new object[] { kobBody });
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning("LaunchFrom: " + ex.Message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Water fluid #" + i + ": " + ex.Message);
                    }
                }

                if (spawned == 0)
                    spawned = TryInjectWaterFallback(kob, bodyGo);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("SpawnWaterFluidAt: " + ex);
            }
            return spawned;
        }

        private static MethodInfo FindInstanceMethod(Type type, string name, int paramCount)
        {
            if (type == null) return null;
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m == null || m.Name != name) continue;
                if (m.GetParameters().Length == paramCount)
                    return m;
            }
            return null;
        }

        /// <summary>
        /// AddReagentContents is often an extension method (same pattern as KoboldGenesBitBufferExtension).
        /// </summary>
        private static MethodInfo FindBitBufferWriteMethod(Type bitBufferType, string methodName)
        {
            if (bitBufferType == null || string.IsNullOrEmpty(methodName))
                return null;

            // KK: AddReagentContents is on ReagentContentsBitBufferExtension (static extension)
            Type preferred = SafeGameType("ReagentContentsBitBufferExtension");
            if (preferred != null)
            {
                MethodInfo pref = AccessTools.Method(preferred, methodName);
                if (pref != null)
                    return pref;
                // try all static methods with that name
                MethodInfo[] all = preferred.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].Name == methodName)
                        return all[i];
                }
            }

            MethodInfo direct = AccessTools.Method(bitBufferType, methodName);
            if (direct != null)
                return direct;

            string[] guessNames =
            {
                "BitBufferReagentContentsExtension",
                "ReagentBitBufferExtension",
                "KoboldReagentBitBufferExtension",
                "BitBufferExtensions"
            };
            for (int i = 0; i < guessNames.Length; i++)
            {
                Type ext = SafeGameType(guessNames[i]);
                if (ext == null) continue;
                MethodInfo m = AccessTools.Method(ext, methodName);
                if (m != null) return m;
            }

            // Scan game assemblies for static method named methodName with BitBuffer as first arg
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int a = 0; a < asms.Length; a++)
                {
                    Assembly asm = asms[a];
                    if (asm == null) continue;
                    string an = asm.GetName().Name ?? "";
                    if (an != "Assembly-CSharp" && an != "Assembly-CSharp-firstpass" &&
                        an.IndexOf("Kobold", StringComparison.OrdinalIgnoreCase) < 0 &&
                        an.IndexOf("NetStack", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    Type[] types = null;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtl) { types = rtl.Types; }
                    catch { continue; }
                    if (types == null) continue;

                    for (int t = 0; t < types.Length; t++)
                    {
                        Type ty = types[t];
                        if (ty == null) continue;
                        MethodInfo[] methods = null;
                        try
                        {
                            methods = ty.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        }
                        catch { continue; }
                        if (methods == null) continue;
                        for (int m = 0; m < methods.Length; m++)
                        {
                            MethodInfo mi = methods[m];
                            if (mi == null || mi.Name != methodName) continue;
                            ParameterInfo[] ps = mi.GetParameters();
                            if (ps == null || ps.Length < 1) continue;
                            if (ps[0].ParameterType == bitBufferType ||
                                bitBufferType.IsAssignableFrom(ps[0].ParameterType) ||
                                ps[0].ParameterType.Name == "BitBuffer")
                                return mi;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Fallback: dump Water into kobold belly/container so fluid systems still run.
        /// </summary>
        private int TryInjectWaterFallback(Component kob, GameObject bodyGo)
        {
            try
            {
                if (kob == null && bodyGo != null)
                {
                    Type kt = SafeGameType("Kobold");
                    if (kt != null)
                        kob = bodyGo.GetComponentInChildren(kt, true);
                }
                if (kob == null) return 0;

                Type scriptableReagentType = SafeGameType("ScriptableReagent");
                Type reagentContentsType = SafeGameType("ReagentContents");
                object water = ResolveWaterReagent(scriptableReagentType);
                if (water == null || reagentContentsType == null) return 0;

                MethodInfo getReagentAmount = AccessTools.Method(water.GetType(), "GetReagent", new Type[] { typeof(float) });
                MethodInfo setMaxVolume = AccessTools.Method(reagentContentsType, "SetMaxVolume", new Type[] { typeof(float) });
                MethodInfo addMixOne = FindInstanceMethod(reagentContentsType, "AddMix", 1);
                if (getReagentAmount == null || addMixOne == null) return 0;

                object contents = Activator.CreateInstance(reagentContentsType, new object[] { 0.05f });
                if (setMaxVolume != null)
                    setMaxVolume.Invoke(contents, new object[] { 0.05f });
                object val = getReagentAmount.Invoke(water, new object[] { 0.05f });
                addMixOne.Invoke(contents, new object[] { val });

                // bellyContainer field on Kobold
                FieldInfo bellyField = AccessTools.Field(kob.GetType(), "bellyContainer");
                object belly = bellyField != null ? bellyField.GetValue(kob) : null;
                if (belly == null)
                {
                    // try property
                    PropertyInfo bellyProp = AccessTools.Property(kob.GetType(), "bellyContainer");
                    if (bellyProp != null)
                        belly = bellyProp.GetValue(kob, null);
                }
                if (belly == null) return 0;

                // AddMix(ReagentContents, InjectType) or ForceMixRPC
                Type injectType = SafeGameType("GenericReagentContainer+InjectType")
                    ?? SafeGameType("GenericReagentContainer.InjectType");
                // nested enum might be GenericReagentContainer.InjectType
                if (injectType == null)
                {
                    Type grc = SafeGameType("GenericReagentContainer");
                    if (grc != null)
                        injectType = grc.GetNestedType("InjectType", BindingFlags.Public | BindingFlags.NonPublic);
                }

                MethodInfo bellyAddMix = null;
                MethodInfo[] bellyMethods = belly.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < bellyMethods.Length; i++)
                {
                    MethodInfo m = bellyMethods[i];
                    if (m == null || m.Name != "AddMix") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps != null && ps.Length == 2 && ps[0].ParameterType == reagentContentsType)
                    {
                        bellyAddMix = m;
                        break;
                    }
                }

                if (bellyAddMix != null)
                {
                    object injectVal = null;
                    if (injectType != null)
                    {
                        try { injectVal = Enum.Parse(injectType, "Inject"); }
                        catch
                        {
                            try { injectVal = Enum.ToObject(injectType, 0); }
                            catch { }
                        }
                    }
                    if (injectVal != null)
                        bellyAddMix.Invoke(belly, new object[] { contents, injectVal });
                    else
                        bellyAddMix.Invoke(belly, new object[] { contents, 0 });

                    Logger.LogInfo("Water inject fallback via bellyContainer.AddMix");
                    return 1;
                }

                // ForceMixRPC on container photon view
                PhotonView cv = null;
                try
                {
                    Component c = belly as Component;
                    if (c != null)
                        cv = c.GetComponent<PhotonView>() ?? c.GetComponentInParent<PhotonView>();
                }
                catch { }
                if (cv != null)
                {
                    Type bitBufferType = SafeGameType("NetStack.Serialization.BitBuffer") ?? SafeGameType("BitBuffer");
                    MethodInfo addReagentContents = FindBitBufferWriteMethod(bitBufferType, "AddReagentContents");
                    if (bitBufferType != null && addReagentContents != null)
                    {
                        object buffer;
                        try { buffer = Activator.CreateInstance(bitBufferType, new object[] { 16 }); }
                        catch { buffer = Activator.CreateInstance(bitBufferType); }
                        if (addReagentContents.IsStatic)
                            addReagentContents.Invoke(null, new object[] { buffer, contents });
                        else
                            addReagentContents.Invoke(buffer, new object[] { contents });

                        PhotonView sourcePv = kob.GetComponent<PhotonView>() ?? kob.GetComponentInParent<PhotonView>();
                        int sourceId = sourcePv != null ? sourcePv.ViewID : 0;
                        cv.RPC("ForceMixRPC", RpcTarget.All, buffer, sourceId, (byte)0);
                        Logger.LogInfo("Water inject fallback via ForceMixRPC");
                        return 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("TryInjectWaterFallback: " + ex.Message);
            }
            return 0;
        }

        /// <summary>
        /// Official KK API (github ReagentContents.cs):
        ///   new ReagentContents(float maxVolume)
        ///   AddMix(byte id, float volume)
        ///   ScriptableReagent.GetReagent(float) → Reagent { id, volume }
        ///   ReagentDatabase.GetID(ScriptableReagent)
        /// IL has no zero-arg ctor (optional param is compiler sugar).
        /// </summary>
        private object BuildWaterReagentContents(Type reagentContentsType, object waterReagent, float volume, MethodInfo setMaxVolume)
        {
            if (reagentContentsType == null || waterReagent == null)
                return null;

            try
            {
                // MUST pass float — Activator.CreateInstance() with 0 args fails
                object contents = null;
                try
                {
                    contents = Activator.CreateInstance(reagentContentsType, new object[] { volume > 0f ? volume : 20f });
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("new ReagentContents(float) failed: " + ex.Message);
                    try
                    {
                        ConstructorInfo ctor = AccessTools.Constructor(reagentContentsType, new Type[] { typeof(float) });
                        if (ctor != null)
                            contents = ctor.Invoke(new object[] { volume > 0f ? volume : 20f });
                    }
                    catch (Exception ex2)
                    {
                        Logger.LogWarning("ReagentContents ctor invoke failed: " + ex2.Message);
                    }
                }
                if (contents == null)
                    return null;

                if (setMaxVolume != null)
                {
                    try { setMaxVolume.Invoke(contents, new object[] { volume }); }
                    catch { }
                }

                // Resolve byte id for Water
                byte waterId = 0;
                bool gotId = false;

                Type reagentDb = SafeGameType("ReagentDatabase");
                if (reagentDb != null)
                {
                    MethodInfo getId = AccessTools.Method(reagentDb, "GetID", new Type[] { waterReagent.GetType() });
                    if (getId == null)
                    {
                        // try base Database.GetID
                        MethodInfo[] ms = reagentDb.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                        for (int i = 0; i < ms.Length; i++)
                        {
                            if (ms[i] != null && ms[i].Name == "GetID" && ms[i].GetParameters().Length == 1)
                            {
                                getId = ms[i];
                                break;
                            }
                        }
                    }
                    if (getId != null)
                    {
                        try
                        {
                            object idObj = getId.Invoke(null, new object[] { waterReagent });
                            waterId = Convert.ToByte(idObj);
                            gotId = true;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning("ReagentDatabase.GetID: " + ex.Message);
                        }
                    }
                }

                // Path A: GetReagent(float) → AddMix(Reagent)
                MethodInfo getReagent = AccessTools.Method(waterReagent.GetType(), "GetReagent", new Type[] { typeof(float) });
                if (getReagent != null)
                {
                    try
                    {
                        object reagentVal = getReagent.Invoke(waterReagent, new object[] { volume });
                        if (reagentVal != null)
                        {
                            // AddMix(Reagent, GenericReagentContainer = null)
                            MethodInfo[] methods = contents.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            for (int i = 0; i < methods.Length; i++)
                            {
                                MethodInfo m = methods[i];
                                if (m == null || m.Name != "AddMix") continue;
                                ParameterInfo[] ps = m.GetParameters();
                                if (ps == null || ps.Length < 1) continue;
                                if (ps[0].ParameterType.IsInstanceOfType(reagentVal) ||
                                    ps[0].ParameterType.IsAssignableFrom(reagentVal.GetType()))
                                {
                                    if (ps.Length == 1)
                                        m.Invoke(contents, new object[] { reagentVal });
                                    else
                                        m.Invoke(contents, new object[] { reagentVal, null });
                                    Logger.LogInfo("Water contents via GetReagent+AddMix(Reagent)");
                                    return contents;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("GetReagent/AddMix(Reagent): " + ex.Message);
                    }
                }

                // Path B: AddMix(byte id, float volume) — official primary API
                if (gotId || true)
                {
                    if (!gotId)
                    {
                        // last chance: Unity instance id low byte (weak)
                        UnityEngine.Object uo = waterReagent as UnityEngine.Object;
                        if (uo != null)
                            waterId = (byte)(uo.GetInstanceID() & 0xFF);
                    }

                    MethodInfo[] methods = contents.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        MethodInfo m = methods[i];
                        if (m == null || m.Name != "AddMix") continue;
                        ParameterInfo[] ps = m.GetParameters();
                        if (ps == null || ps.Length < 2) continue;
                        if (ps[0].ParameterType != typeof(byte)) continue;
                        if (ps[1].ParameterType != typeof(float) && ps[1].ParameterType != typeof(Single))
                            continue;
                        try
                        {
                            if (ps.Length == 2)
                                m.Invoke(contents, new object[] { waterId, volume });
                            else
                                m.Invoke(contents, new object[] { waterId, volume, null });
                            Logger.LogInfo("Water contents via AddMix(byte id=" + waterId + ", vol=" + volume + ")");
                            return contents;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning("AddMix(byte,float): " + ex.Message);
                        }
                    }

                    // OverrideReagent(byte, float)
                    MethodInfo ov = AccessTools.Method(contents.GetType(), "OverrideReagent", new Type[] { typeof(byte), typeof(float) });
                    if (ov != null)
                    {
                        try
                        {
                            ov.Invoke(contents, new object[] { waterId, volume });
                            Logger.LogInfo("Water contents via OverrideReagent id=" + waterId);
                            return contents;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning("OverrideReagent: " + ex.Message);
                        }
                    }
                }

                Logger.LogWarning("BuildWater: failed after ctor ok, gotId=" + gotId + " id=" + waterId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("BuildWaterReagentContents: " + ex.Message);
            }
            return null;
        }

        private static MethodInfo FindAddMixAccepting(Type contentsType, Type argType)
        {
            if (contentsType == null || argType == null) return null;
            MethodInfo[] methods = contentsType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m == null || m.Name != "AddMix") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps == null || ps.Length != 1) continue;
                if (ps[0].ParameterType == argType || ps[0].ParameterType.IsAssignableFrom(argType))
                    return m;
            }
            return null;
        }

        private object ResolveWaterReagent(Type scriptableReagentType)
        {
            try
            {
                // Database<ScriptableReagent>.TryGetAsset("Water", out var)
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int a = 0; a < asms.Length; a++)
                {
                    Assembly asm = asms[a];
                    if (asm == null) continue;
                    string an = asm.GetName().Name ?? "";
                    if (an != "Assembly-CSharp" && an != "Assembly-CSharp-firstpass" &&
                        an.IndexOf("Kobold", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    Type[] types = null;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtl) { types = rtl.Types; }
                    catch { continue; }
                    if (types == null) continue;
                    for (int t = 0; t < types.Length; t++)
                    {
                        Type ty = types[t];
                        if (ty == null || !ty.IsGenericTypeDefinition) continue;
                        if (ty.Name != "Database`1") continue;
                        try
                        {
                            Type closed = ty.MakeGenericType(scriptableReagentType);
                            MethodInfo tryGet = AccessTools.Method(closed, "TryGetAsset",
                                new Type[] { typeof(string), scriptableReagentType.MakeByRefType() });
                            if (tryGet == null) continue;
                            object[] args = new object[] { "Water", null };
                            object ok = tryGet.Invoke(null, args);
                            if (ok is bool && (bool)ok && args[1] != null)
                                return args[1];
                        }
                        catch { }
                    }
                }

                Type reagentDb = SafeGameType("ReagentDatabase");
                if (reagentDb != null)
                {
                    MethodInfo getReagent = AccessTools.Method(reagentDb, "GetReagent", new Type[] { typeof(string) });
                    if (getReagent != null)
                    {
                        try
                        {
                            object r = getReagent.Invoke(null, new object[] { "Water" });
                            if (r != null) return r;
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("ResolveWaterReagent: " + ex.Message);
            }
            return null;
        }

        private void UpdateRoomPlayersPublish()
        {
            // Always publish while host if toggle is on (lobby list needs host + room created with mod)
            if (!publishRoomPlayers)
                return;
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
                return;
            if (Time.unscaledTime < nextRoomPlayersPublishTime)
                return;

            nextRoomPlayersPublishTime = Time.unscaledTime + 2.0f;
            PublishRoomPlayerList(false);
        }

        private void PublishRoomPlayerList(bool force)
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
                return;
            if (!publishRoomPlayers && !force)
                return;

            try
            {
                Player[] players = PhotonNetwork.PlayerList;
                List<string> names = new List<string>();
                if (players != null)
                {
                    for (int i = 0; i < players.Length; i++)
                    {
                        Player p = players[i];
                        if (p == null) continue;
                        string n = GetPlayerName(p);
                        if (string.IsNullOrEmpty(n))
                            n = "Player" + p.ActorNumber;
                        // Comma is delimiter — strip it from names
                        n = n.Replace(',', ' ').Replace(';', ' ').Trim();
                        if (n.Length > 48) n = n.Substring(0, 48);
                        names.Add(n);
                    }
                }

                string payload = string.Join(",", names.ToArray());
                if (!force && payload == lastPublishedRoomPlayers)
                    return;

                lastPublishedRoomPlayers = payload;
                Room room = PhotonNetwork.CurrentRoom;
                if (room == null) return;

                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
                {
                    { RoomPlayersPropertyKey, payload }
                };
                room.SetCustomProperties(props);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("PublishRoomPlayerList: " + ex.Message);
            }
        }

        private string GetRoomPlayersFromInfo(RoomInfo info)
        {
            if (info == null || info.CustomProperties == null)
                return "";
            object v;
            if (info.CustomProperties.TryGetValue(RoomPlayersPropertyKey, out v) && v != null)
                return v.ToString();
            return "";
        }

        // ============================================================
        // PHOTON CALLBACKS (required by the interfaces)
        // ============================================================
        public void OnConnected() { }

        public void OnConnectedToMaster()
        {
            // User picked a different room while in a room — finish the switch after a short delay
            if (!string.IsNullOrEmpty(pendingJoinRoomName) && !PhotonNetwork.InRoom)
            {
                StartCoroutine(JoinPendingRoomAfterDelay());
                return;
            }

            // Scanner coroutine owns connection flow
            if (scanRunning)
                return;

            if (peekInProgress && peekAwaitingTargetJoin && !string.IsNullOrEmpty(peekTargetRoom) && !PhotonNetwork.InRoom)
            {
                BeginPeekJoin();
                return;
            }

            if (!PhotonNetwork.InRoom && (peekNeedLobbyAfterLeave || (!peekInProgress && peekRejoinAfter)))
            {
                TryReturnToLobbyAfterPeek();
                return;
            }

            // After LeaveRoom during browse, Photon often lands here. Join lobby for the list.
            if (isBrowsingServers && !PhotonNetwork.InRoom && !PhotonNetwork.InLobby)
            {
                serverListStatus = "ON MASTER → JOINING LOBBY...";
                TryJoinLobbyForBrowse();
            }
        }

        private IEnumerator JoinPendingRoomAfterDelay()
        {
            if (joinPendingInProgress)
                yield break;

            string target = pendingJoinRoomName;
            if (string.IsNullOrEmpty(target))
                yield break;

            joinPendingInProgress = true;
            serverListStatus = "WAITING 0.03s→ JOINING " + target + "...";
            yield return new WaitForSecondsRealtime(0.03f);

            // Still the same pending target and not already in a room?
            if (pendingJoinRoomName != target || PhotonNetwork.InRoom)
            {
                joinPendingInProgress = false;
                yield break;
            }

            pendingJoinRoomName = "";
            serverListStatus = "JOINING " + target + "...";

            try
            {
                PhotonNetwork.JoinRoom(target);
            }
            catch (Exception ex)
            {
                serverListStatus = "JOIN ERROR: " + ex.Message;
                Logger.LogWarning("Join selected room failed: " + ex);
            }

            joinPendingInProgress = false;
        }

        public void OnDisconnected(DisconnectCause cause)
        {
            if (isBrowsingServers)
            {
                isBrowsingServers = false;
                pendingRejoinPrevious = false;
                if (rejoinCoroutine != null)
                {
                    StopCoroutine(rejoinCoroutine);
                    rejoinCoroutine = null;
                }
                serverListStatus = "DISCONNECTED: " + cause;
            }
        }
        public void OnRegionListReceived(RegionHandler regionHandler) { }
        public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
        public void OnCustomAuthenticationFailed(string debugMessage) { }

        public void OnJoinedLobby()
        {
            if (!isBrowsingServers)
                return;

            serverListStatus = "IN LOBBY - RECEIVING ROOM LIST...";

            // Schedule auto-rejoin (waits for list first)
            if (pendingRejoinPrevious && !string.IsNullOrEmpty(previousRoomName))
            {
                if (rejoinCoroutine != null)
                    StopCoroutine(rejoinCoroutine);
                rejoinCoroutine = StartCoroutine(RejoinPreviousRoomAfterDelay());
            }
        }

        public void OnLeftLobby()
        {
            if (isBrowsingServers && !pendingRejoinPrevious)
                serverListStatus = "LEFT LOBBY";
        }

        public void OnRoomListUpdate(List<RoomInfo> roomList)
        {
            MergeRoomListUpdate(roomList, fromGameSpawner: false);
        }

        public void OnLobbyStatisticsUpdate(List<TypedLobbyInfo> lobbyStatistics) { }

        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
        public void OnCreatedRoom()
        {
            // New room: seed player list into lobby-visible props immediately
            if (publishRoomPlayers && PhotonNetwork.IsMasterClient)
            {
                lastPublishedRoomPlayers = "";
                nextRoomPlayersPublishTime = 0f;
                PublishRoomPlayerList(true);
            }
        }
        public void OnCreateRoomFailed(short returnCode, string message) { }

        private IEnumerator PeekAfterJoinRoutine()
        {
            // Wait until PlayerList is populated (or timeout). Game + Photon can lag a bit.
            float deadline = Time.unscaledTime + 2.5f;
            int bestCount = 0;

            while (Time.unscaledTime < deadline && peekInProgress && PhotonNetwork.InRoom)
            {
                Player[] players = PhotonNetwork.PlayerList;
                int count = players != null ? players.Length : 0;
                if (count > bestCount)
                    bestCount = count;

                int expected = 0;
                if (!string.IsNullOrEmpty(peekTargetRoom) &&
                    cachedRooms.TryGetValue(peekTargetRoom, out RoomInfo info) &&
                    info != null)
                {
                    expected = info.PlayerCount;
                }

                if (expected > 0 && count >= expected)
                    break;
                if (count > 0 && Time.unscaledTime > deadline - 1.5f)
                    break;

                yield return new WaitForSecondsRealtime(0.1f);
            }

            yield return null;

            if (peekInProgress)
                FinishPeekAndLeave();

            peekCoroutine = null;
        }

        // ---- IInRoomCallbacks ----
        public void OnPlayerEnteredRoom(Player newPlayer)
        {
            if (publishRoomPlayers && PhotonNetwork.IsMasterClient)
            {
                nextRoomPlayersPublishTime = 0f;
                PublishRoomPlayerList(true);
            }

            // New player joined → splash them with water (desync helper)
            if (autoSplashOnJoin && newPlayer != null && !newPlayer.IsLocal)
                ScheduleSplashNewPlayer(newPlayer);
        }

        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            if (publishRoomPlayers && PhotonNetwork.IsMasterClient)
            {
                nextRoomPlayersPublishTime = 0f;
                PublishRoomPlayerList(true);
            }

            // Clean up leftover kobold bodies when someone leaves (host can destroy)
            if (destroyBodyOnLeave && otherPlayer != null)
                CleanupBodiesForActor(otherPlayer.ActorNumber);
        }

        /// <summary>
        /// Destroy orphaned kobold PhotonViews that belonged to a player who left.
        /// Master client can destroy any view; others only destroy if somehow still IsMine.
        /// </summary>
        private void CleanupBodiesForActor(int actorNumber)
        {
            if (actorNumber <= 0)
                return;

            try
            {
                PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
                if (views == null) return;

                int destroyed = 0;
                for (int i = 0; i < views.Length; i++)
                {
                    PhotonView view = views[i];
                    if (view == null || view.gameObject == null) continue;

                    bool match = false;
                    try
                    {
                        if (view.OwnerActorNr == actorNumber)
                            match = true;
                        else if (view.CreatorActorNr == actorNumber)
                            match = true;
                        else if (view.Owner != null && view.Owner.ActorNumber == actorNumber)
                            match = true;
                    }
                    catch { }

                    if (!match) continue;
                    if (GetKoboldOn(view.gameObject) == null && !IsValidPlayerKoboldObject(view.gameObject))
                        continue;

                    try
                    {
                        if (PhotonNetwork.IsMasterClient || view.IsMine)
                        {
                            Logger.LogInfo("CleanupBodiesForActor: destroy " + view.gameObject.name +
                                           " actor=" + actorNumber + " view=" + view.ViewID);
                            PhotonNetwork.Destroy(view.gameObject);
                            destroyed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("CleanupBodiesForActor destroy: " + ex.Message);
                    }
                }

                if (playerObjectCache.ContainsKey(actorNumber))
                    playerObjectCache.Remove(actorNumber);

                if (destroyed > 0)
                    Logger.LogInfo("CleanupBodiesForActor #" + actorNumber + " destroyed=" + destroyed);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("CleanupBodiesForActor: " + ex.Message);
            }
        }

        public void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged) { }

        public void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps) { }

        public void OnMasterClientSwitched(Player newMasterClient)
        {
            // Non-mod host left → we became master: start publishing names for the lobby
            if (newMasterClient != null && newMasterClient.IsLocal)
            {
                publishRoomPlayers = true;
                if (configPublishRoomPlayers != null)
                    configPublishRoomPlayers.Value = true;
                lastPublishedRoomPlayers = "";
                nextRoomPlayersPublishTime = 0f;
                PublishRoomPlayerList(true);
                Logger.LogInfo("Became master — publishing room player list");
            }
        }


        private void TryRejoinLastRoom()
        {
            string room = lastJoinedRoomName;
            if (string.IsNullOrEmpty(room))
                room = previousRoomName;
            if (string.IsNullOrEmpty(room))
            {
                ShowToast("Rejoin failed: no last room saved", "system");
                return;
            }
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null &&
                string.Equals(PhotonNetwork.CurrentRoom.Name, room, StringComparison.Ordinal))
            {
                ShowToast("Already in " + room, "system");
                return;
            }
            if (!PhotonNetwork.IsConnected)
            {
                ShowToast("Rejoin failed: not connected to Photon", "system");
                return;
            }
            if (PhotonNetwork.NetworkClientState == ClientState.Disconnecting ||
                PhotonNetwork.NetworkClientState == ClientState.ConnectingToNameServer ||
                PhotonNetwork.NetworkClientState == ClientState.ConnectingToMasterServer)
            {
                ShowToast("Rejoin failed: connection busy (" + PhotonNetwork.NetworkClientState + ")", "system");
                return;
            }
            try
            {
                previousRoomName = room;
                pendingRejoinPrevious = true;
                if (PhotonNetwork.InRoom)
                {
                    serverListStatus = "Leaving → rejoin " + room;
                    PhotonNetwork.LeaveRoom();
                    ShowToast("Leaving… then rejoin " + room, "system");
                }
                else if (PhotonNetwork.IsConnectedAndReady)
                {
                    pendingJoinRoomName = room;
                    StartCoroutine(JoinPendingRoomAfterDelay());
                    ShowToast("Joining " + room + "…", "system");
                }
                else
                {
                    ShowToast("Rejoin failed: not ready (" + PhotonNetwork.NetworkClientState + ")", "system");
                }
            }
            catch (Exception ex)
            {
                ShowToast("Rejoin failed: " + ex.Message, "system");
                Logger.LogWarning("TryRejoinLastRoom: " + ex.Message);
            }
        }

        public void OnJoinedRoom()
        {
            try
            {
                if (PhotonNetwork.CurrentRoom != null && !string.IsNullOrEmpty(PhotonNetwork.CurrentRoom.Name))
                {
                    lastJoinedRoomName = PhotonNetwork.CurrentRoom.Name;
                    previousRoomName = lastJoinedRoomName;
                    if (configLastJoinedRoom != null)
                        configLastJoinedRoom.Value = lastJoinedRoomName;
                }
            }
            catch { }
            pendingJoinRoomName = "";
            joinPendingInProgress = false;

            // You joined → splash everyone (skip during room scan)
            if (welcomeMessageOnJoin && !scanRunning && !peekInProgress)
            {
                string room = (PhotonNetwork.CurrentRoom != null) ? PhotonNetwork.CurrentRoom.Name : "room";
                if (string.IsNullOrEmpty(room)) room = "room";
                ShowToast("You've Joined (" + room + ").");
            }
            if (autoSplashOnJoin && !scanRunning && !peekInProgress)
                ScheduleSplashEveryoneOnJoin();

            // Room scanner owns the join when scanRunning — do not start old peek routine
            if (scanRunning)
            {
                SnapshotRoomPlayers(PhotonNetwork.CurrentRoom != null
                    ? PhotonNetwork.CurrentRoom.Name
                    : scanCurrentRoom);
                return;
            }

            if (peekInProgress && !scanRunning)
            {
                if (peekCoroutine != null)
                    StopCoroutine(peekCoroutine);
                peekCoroutine = StartCoroutine(PeekAfterJoinRoutine());
                return;
            }

            // Auto-rejoin of previous room → keep the cached list + restore last position
            string joinedName = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : "";
            bool isAutoRejoin = !string.IsNullOrEmpty(previousRoomName) &&
                               !string.IsNullOrEmpty(joinedName) &&
                               string.Equals(joinedName, previousRoomName, StringComparison.Ordinal);

            Logger.LogInfo("OnJoinedRoom name=" + joinedName + " previous=" + previousRoomName +
                           " autoRejoin=" + isAutoRejoin + " hasSavedPos=" + browseHasSavedTransform);

            // Host: seed player list into room props for browser hover
            if (publishRoomPlayers && PhotonNetwork.IsMasterClient)
            {
                lastPublishedRoomPlayers = "";
                nextRoomPlayersPublishTime = 0f;
                PublishRoomPlayerList(true);
            }

            // Advertise Zex client so other mod users can see/tag us
            AnnounceZexClient();

            if (isAutoRejoin)
            {
                isBrowsingServers = false;
                pendingRejoinPrevious = false;
                serverListStatus = "CACHED • " + cachedRooms.Count + " rooms (back in your room)";

                if (browseHasSavedTransform && browsePositionRestoreEnabled)
                {
                    // Continuous restore for ~6s so spawn/network snaps lose
                    browseRestoreActive = true;
                    browseRestoreUntil = Time.unscaledTime + 6f;
                    browseRestoreHits = 0;
                    serverListStatus = "CACHED • restoring pos " +
                        browseSavedPosition.x.ToString("0.0") + "," +
                        browseSavedPosition.z.ToString("0.0") + " …";
                    Logger.LogInfo("Server browse: starting continuous restore to " + browseSavedPosition);

                    if (restoreBrowsePositionCoroutine != null)
                        StopCoroutine(restoreBrowsePositionCoroutine);
                    // Also run coroutine as a delayed first shove once body exists
                    restoreBrowsePositionCoroutine = StartCoroutine(RestoreBrowseTransformAfterSpawn());
                }
                else if (browseHasSavedTransform && !browsePositionRestoreEnabled)
                {
                    serverListStatus = "CACHED • " + cachedRooms.Count + " rooms (pos restore OFF)";
                    Logger.LogInfo("Server browse: position restore disabled — skipping restore");
                }
            }
            else
            {
                isBrowsingServers = false;
                pendingRejoinPrevious = false;
                browseHasSavedTransform = false;
                browseRestoreActive = false;
                serverListStatus = "JOINED " + (string.IsNullOrEmpty(joinedName) ? "ROOM" : joinedName);
            }
        }

        private GameObject ResolveLocalPlayerBody()
        {
            Component kob = FindLocalKobold();
            if (kob != null)
                return kob.gameObject;

            GameObject local = GetLocalPlayer();
            if (local != null)
                return local;

            return null;
        }

        private void CaptureBrowseTransform()
        {
            browseHasSavedTransform = false;
            browseSavedPrefabName = "";
            browseSavedSpeciesIndex = -1;
            browseSavedGenes = null;
            browseDidRespawnRestore = false;
            browseTriedRespawnRestore = false;
            try
            {
                GameObject local = ResolveLocalPlayerBody();
                if (local == null)
                {
                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        Vector3 p = cam.transform.position;
                        p.y = Mathf.Max(0.5f, p.y - 1.5f);
                        browseSavedPosition = p;
                        browseSavedRotation = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
                        browseHasSavedTransform = true;
                        Logger.LogInfo("Server browse: saved camera-approx position " + browseSavedPosition);
                        return;
                    }
                    Logger.LogWarning("Server browse: could not find local player to save position");
                    return;
                }

                browseSavedPosition = local.transform.position;
                browseSavedRotation = local.transform.rotation;
                browseHasSavedTransform = true;

                // Prefab resource name (for respawn-at-pos after rejoin)
                string goName = local.name ?? "";
                if (goName.EndsWith("(Clone)"))
                    goName = goName.Substring(0, goName.Length - "(Clone)".Length).Trim();
                browseSavedPrefabName = goName;

                // Genes + species so official spawn path can rebuild body at saved coords
                try
                {
                    ResolveGeneTypes();
                    Component kob = GetKoboldOn(local) ?? FindLocalKobold();
                    if (kob != null && getGenesMethod != null)
                    {
                        object genes = getGenesMethod.Invoke(kob, null);
                        if (genes != null)
                        {
                            browseSavedGenes = CloneGenes(genes);
                            FieldInfo sp = AccessTools.Field(genes.GetType(), "species");
                            if (sp != null)
                            {
                                object v = sp.GetValue(genes);
                                if (v != null)
                                    browseSavedSpeciesIndex = Convert.ToInt32(v);
                            }
                        }
                    }

                    // Prefer official prefab key from Player DB when species is known
                    if (browseSavedSpeciesIndex >= 0)
                    {
                        object playerDb = GetGamePlayerDatabase();
                        List<object> infos = GetValidPrefabInfos(playerDb);
                        if (infos != null && browseSavedSpeciesIndex < infos.Count)
                        {
                            string key = GetPrefabInfoKey(infos[browseSavedSpeciesIndex]);
                            if (!string.IsNullOrEmpty(key))
                                browseSavedPrefabName = key;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Server browse: genes/prefab capture: " + ex.Message);
                }

                Logger.LogInfo("Server browse: saved position " + browseSavedPosition +
                               " prefab=" + browseSavedPrefabName + " species=" + browseSavedSpeciesIndex);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("CaptureBrowseTransform failed: " + ex.Message);
                browseHasSavedTransform = false;
            }
        }

        private void DestroyLocalPlayerBodyForBrowse()
        {
            try
            {
                int destroyed = 0;

                // 1) Official PUN cleanup for everything owned by local player
                if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
                {
                    try
                    {
                        PhotonNetwork.RemoveRPCs(PhotonNetwork.LocalPlayer);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("RemoveRPCs: " + ex.Message);
                    }

                    try
                    {
                        PhotonNetwork.DestroyPlayerObjects(PhotonNetwork.LocalPlayer);
                        destroyed++;
                        Logger.LogInfo("DestroyPlayerObjects(LocalPlayer) issued");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("DestroyPlayerObjects: " + ex.Message);
                    }
                }

                // 2) Explicit IsMine kobold PhotonViews (belt and suspenders)
                PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
                if (views != null)
                {
                    for (int i = 0; i < views.Length; i++)
                    {
                        PhotonView view = views[i];
                        if (view == null || view.gameObject == null) continue;
                        if (!view.IsMine) continue;

                        bool isKobold = GetKoboldOn(view.gameObject) != null;
                        // Also catch player-tagged objects
                        bool isTagged = false;
                        try
                        {
                            if (PhotonNetwork.LocalPlayer != null &&
                                PhotonNetwork.LocalPlayer.TagObject != null)
                            {
                                object tag = PhotonNetwork.LocalPlayer.TagObject;
                                Component tagComp = tag as Component;
                                if (tagComp != null &&
                                    (tagComp.gameObject == view.gameObject ||
                                     tagComp.transform.IsChildOf(view.transform) ||
                                     view.transform.IsChildOf(tagComp.transform)))
                                    isTagged = true;
                            }
                        }
                        catch { }

                        if (!isKobold && !isTagged)
                            continue;

                        try
                        {
                            Logger.LogInfo("Destroying IsMine body view: " + view.gameObject.name + " id=" + view.ViewID);
                            PhotonNetwork.Destroy(view.gameObject);
                            destroyed++;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning("PhotonNetwork.Destroy view failed: " + ex.Message);
                            try
                            {
                                UnityEngine.Object.Destroy(view.gameObject);
                                destroyed++;
                            }
                            catch { }
                        }
                    }
                }

                // 3) Local-only fallback if Photon destroy couldn't run
                Component kob = FindLocalKobold();
                if (kob != null && kob.gameObject != null)
                {
                    PhotonView pv = kob.GetComponent<PhotonView>() ?? kob.GetComponentInParent<PhotonView>();
                    if (pv == null || !PhotonNetwork.InRoom)
                    {
                        try
                        {
                            UnityEngine.Object.Destroy(kob.gameObject);
                            destroyed++;
                            Logger.LogInfo("Local Destroy fallback on kobold");
                        }
                        catch { }
                    }
                }

                cachedLocalPlayer = null;
                if (PhotonNetwork.LocalPlayer != null)
                    PhotonNetwork.LocalPlayer.TagObject = null;

                Logger.LogInfo("DestroyLocalPlayerBodyForBrowse done, ops=" + destroyed);
                if (destroyed > 0)
                    ShowToast("Destroyed local body (" + destroyed + ")");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("DestroyLocalPlayerBodyForBrowse: " + ex.Message);
            }
        }

        /// <summary>
        /// After rejoin the game spawns you at default. Destroy that body and respawn at saved coords
        /// (same approach as character swap) so position actually sticks.
        /// </summary>
        private bool TryRespawnAtBrowsePosition()
        {
            if (!browseHasSavedTransform)
                return false;

            try
            {
                Component kob = FindLocalKobold();
                if (kob == null)
                    return false;

                PhotonView pv = kob.GetComponent<PhotonView>() ?? kob.GetComponentInParent<PhotonView>();
                if (pv == null || !pv.IsMine)
                    return false;

                string photonName = browseSavedPrefabName;
                int speciesIndex = browseSavedSpeciesIndex;
                object genesObj = browseSavedGenes;

                // Fallback: read from the just-spawned body
                if (genesObj == null && getGenesMethod != null)
                {
                    object g = getGenesMethod.Invoke(kob, null);
                    if (g != null) genesObj = CloneGenes(g);
                }
                if (speciesIndex < 0 && genesObj != null)
                {
                    FieldInfo sp = AccessTools.Field(genesObj.GetType(), "species");
                    if (sp != null && sp.GetValue(genesObj) != null)
                        speciesIndex = Convert.ToInt32(sp.GetValue(genesObj));
                }
                if (string.IsNullOrEmpty(photonName) && speciesIndex >= 0)
                {
                    object playerDb = GetGamePlayerDatabase();
                    List<object> infos = GetValidPrefabInfos(playerDb);
                    if (infos != null && speciesIndex < infos.Count)
                        photonName = GetPrefabInfoKey(infos[speciesIndex]);
                }
                if (string.IsNullOrEmpty(photonName))
                {
                    string n = pv.gameObject.name ?? "";
                    if (n.EndsWith("(Clone)"))
                        n = n.Substring(0, n.Length - "(Clone)".Length).Trim();
                    photonName = n;
                }
                if (string.IsNullOrEmpty(photonName))
                {
                    Logger.LogWarning("TryRespawnAtBrowsePosition: no prefab name");
                    return false;
                }

                Vector3 pos = browseSavedPosition;
                Quaternion rot = browseSavedRotation;

                if (speciesIndex >= 0)
                    TrySetSelectedPlayerPrefab(speciesIndex, photonName);

                PhotonNetwork.Destroy(pv.gameObject);
                cachedLocalPlayer = null;
                if (PhotonNetwork.LocalPlayer != null)
                    PhotonNetwork.LocalPlayer.TagObject = null;

                if (speciesIndex >= 0 && TryOfficialSpawnPlayer(pos, rot, photonName, genesObj, speciesIndex))
                {
                    Logger.LogInfo("Server browse: official respawn at " + pos);
                    return true;
                }

                GameObject spawned = PhotonNetwork.Instantiate(photonName, pos, rot, 0);
                if (spawned == null)
                    return false;

                cachedLocalPlayer = spawned;
                Component newKob = GetKoboldOn(spawned);
                if (newKob != null)
                {
                    if (PhotonNetwork.LocalPlayer != null)
                        PhotonNetwork.LocalPlayer.TagObject = newKob;
                    if (genesObj != null && setGenesMethod != null)
                        setGenesMethod.Invoke(newKob, new object[] { CloneGenes(genesObj) });
                }

                Logger.LogInfo("Server browse: bare respawn at " + pos + " as " + photonName);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("TryRespawnAtBrowsePosition: " + ex.Message);
                return false;
            }
        }

        private void ApplyTeleportToBody(GameObject body, Vector3 destination, Quaternion rotation)
        {
            if (body == null)
                return;

            CharacterController[] ccs = body.GetComponentsInChildren<CharacterController>(true);
            if (ccs != null)
            {
                for (int i = 0; i < ccs.Length; i++)
                    if (ccs[i] != null) ccs[i].enabled = false;
            }

            Rigidbody[] rbs = body.GetComponentsInChildren<Rigidbody>(true);
            if (rbs != null)
            {
                for (int i = 0; i < rbs.Length; i++)
                {
                    Rigidbody rb = rbs[i];
                    if (rb == null) continue;
                    try
                    {
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    catch { }
                    rb.position = destination;
                    rb.rotation = rotation;
                }
            }

            body.transform.position = destination;
            body.transform.rotation = rotation;

            PhotonView pv = body.GetComponent<PhotonView>() ?? body.GetComponentInParent<PhotonView>();
            if (pv != null)
            {
                Transform root = pv.transform;
                root.position = destination;
                root.rotation = rotation;
            }

            if (ccs != null)
            {
                for (int i = 0; i < ccs.Length; i++)
                    if (ccs[i] != null) ccs[i].enabled = true;
            }
        }

        /// <summary>
        /// Strong teleport used after rejoin — hits every local IsMine kobold body.
        /// </summary>
        private void ForceTeleportLocalPlayer(Vector3 destination, Quaternion rotation)
        {
            GameObject primary = ResolveLocalPlayerBody();
            if (primary != null)
            {
                ApplyTeleportToBody(primary, destination, rotation);
                cachedLocalPlayer = primary;
            }

            // Belt-and-suspenders: any other IsMine PhotonView that hosts a Kobold
            PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
            if (views == null)
                return;

            for (int i = 0; i < views.Length; i++)
            {
                PhotonView view = views[i];
                if (view == null || !view.IsMine)
                    continue;
                Component k = GetKoboldOn(view.gameObject);
                if (k == null)
                    continue;
                if (primary != null && (k.gameObject == primary || view.gameObject == primary))
                    continue;
                ApplyTeleportToBody(k.gameObject, destination, rotation);
            }
        }

        private IEnumerator RestoreBrowseTransformAfterSpawn()
        {
            // Wait until a local body exists, then leave continuous Update loop to keep shoving
            float findDeadline = Time.unscaledTime + 8f;
            while (Time.unscaledTime < findDeadline)
            {
                if (!PhotonNetwork.InRoom)
                    break;
                if (ResolveLocalPlayerBody() != null)
                {
                    Logger.LogInfo("Server browse: local body found, continuous restore is active");
                    break;
                }
                yield return null;
            }

            if (ResolveLocalPlayerBody() == null)
            {
                Logger.LogWarning("Server browse: never found local body during restore wait");
                serverListStatus = "CACHED • " + cachedRooms.Count + " rooms (no body for pos restore)";
            }

            restoreBrowsePositionCoroutine = null;
        }

        public void OnJoinRoomFailed(short returnCode, string message)
        {
            pendingJoinRoomName = "";
            joinPendingInProgress = false;

            if (scanRunning)
            {
                // Include return code — GameClosed=32764, GameFull=32765, etc.
                scanLastError = message + " (" + returnCode + ")";
                peekStatus = "Join failed: " + scanLastError;
                peekStatusUntil = Time.unscaledTime + 3f;
                serverListStatus = "SCAN FAIL: " + scanLastError;
                // Do not JoinLobby here — scanner coroutine owns the next attempt
                return;
            }

            if (peekInProgress)
            {
                peekInProgress = false;
                peekTargetRoom = "";
                peekAwaitingTargetJoin = false;
                peekNeedLobbyAfterLeave = true;
                peekStatus = "Scan failed: " + message;
                peekStatusUntil = Time.unscaledTime + 4f;
                serverListStatus = "PEEK FAILED: " + message;
                TryReturnToLobbyAfterPeek();
            }
            else if (pendingRejoinPrevious)
            {
                pendingRejoinPrevious = false;
                serverListStatus = "REJOIN FAILED (" + message + ") • list still available (" + cachedRooms.Count + ")";
            }
            else
            {
                serverListStatus = "JOIN FAILED: " + message;
            }

            if (!PhotonNetwork.InLobby && PhotonNetwork.IsConnectedAndReady)
                TryJoinLobbyForBrowse();
        }

        public void OnJoinRandomFailed(short returnCode, string message) { }

        public void OnLeftRoom()
        {
            // Switching to a selected room: leave finished → wait for master / delay, then join
            if (!string.IsNullOrEmpty(pendingJoinRoomName))
            {
                serverListStatus = "LEFT ROOM → WAITING 1s → " + pendingJoinRoomName;
                if (PhotonNetwork.IsConnectedAndReady)
                    StartCoroutine(JoinPendingRoomAfterDelay());
                return;
            }

            // Scanner coroutine owns leave/join — do not interfere
            if (scanRunning)
                return;

            // Legacy peek paths
            if (peekInProgress && peekAwaitingTargetJoin && !string.IsNullOrEmpty(peekTargetRoom))
            {
                serverListStatus = "PEEK · left room → joining " + peekTargetRoom;
                if (PhotonNetwork.IsConnectedAndReady)
                    BeginPeekJoin();
                return;
            }

            if (peekInProgress)
            {
                peekInProgress = false;
                peekTargetRoom = "";
                peekAwaitingTargetJoin = false;
                TryReturnToLobbyAfterPeek();
                return;
            }

            if (peekNeedLobbyAfterLeave)
            {
                TryReturnToLobbyAfterPeek();
                return;
            }

            // Browse flow: left room → OnConnectedToMaster usually follows, then we JoinLobby there.
            if (isBrowsingServers)
            {
                serverListStatus = "LEFT ROOM → WAITING FOR MASTER...";
                if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InLobby)
                    TryJoinLobbyForBrowse();
            }
        }
    }
}