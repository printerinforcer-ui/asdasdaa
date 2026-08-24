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
        private static Type SafeGameType(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int a = 0; a < assemblies.Length; a++)
                {
                    Assembly asm = assemblies[a];
                    if (asm == null) continue;
                    string an = asm.GetName().Name ?? "";
                    if (an.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ||
                        an.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                        an.StartsWith("Mono.", StringComparison.OrdinalIgnoreCase) ||
                        an.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                        an.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
                        an.StartsWith("Photon", StringComparison.OrdinalIgnoreCase) ||
                        an.StartsWith("Harmony", StringComparison.OrdinalIgnoreCase) ||
                        an.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (an.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) &&
                        !an.EndsWith("UnityInput", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool prefer =
                        an == "Assembly-CSharp" ||
                        an == "Assembly-CSharp-firstpass" ||
                        an.IndexOf("Kobold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        an.IndexOf("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        an.EndsWith(".UnityInput", StringComparison.OrdinalIgnoreCase);
                    if (!prefer)
                        continue;

                    Type t = null;
                    try { t = asm.GetType(name, false); } catch { }
                    if (t != null) return t;
                    try { t = asm.GetType("KoboldKare." + name, false); } catch { }
                    if (t != null) return t;

                    Type[] types = null;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtl) { types = rtl.Types; }
                    catch { continue; }
                    if (types == null) continue;

                    for (int i = 0; i < types.Length; i++)
                    {
                        Type cand = types[i];
                        if (cand == null) continue;
                        if (cand.Name == name || cand.FullName == name)
                            return cand;
                        if (cand.FullName != null &&
                            (cand.FullName.EndsWith("." + name, StringComparison.Ordinal) ||
                             cand.FullName.EndsWith("+" + name, StringComparison.Ordinal)))
                            return cand;
                    }
                }
            }
            catch { }

            try { return AccessTools.TypeByName(name); }
            catch { return null; }
        }

        private GameObject GetTagObject(Player player)
        {
            if (player == null || player.TagObject == null)
                return null;

            GameObject tagged = player.TagObject as GameObject;
            if (tagged != null)
                return tagged;

            Component component = player.TagObject as Component;
            return component != null ? component.gameObject : null;
        }

        private GameObject GetPlayerRoot(PhotonView view)
        {
            if (view == null)
                return null;

            return view.gameObject;
        }

        private void RefreshPlayerObjectCache()
        {
            if (Time.unscaledTime < nextPlayerCacheRefresh)
                return;

            nextPlayerCacheRefresh = Time.unscaledTime + PlayerCacheRefreshInterval;

            if (!PhotonNetwork.InRoom)
            {
                cachedLocalPlayer = null;
                playerObjectCache.Clear();
                return;
            }

            Player[] players = PhotonNetwork.PlayerList;

            // Fast path: use Photon TagObject whenever available.
            for (int i = 0; i < players.Length; i++)
            {
                Player player = players[i];
                if (player == null)
                    continue;

                GameObject tagged = GetTagObject(player);
                if (tagged == null)
                    continue;

                playerObjectCache[player.ActorNumber] = tagged;

                if (player.IsLocal)
                    cachedLocalPlayer = tagged;
            }

            // Fallback: only map PhotonViews that are actual Kobolds (never bananas/doors/props).
            PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
            if (views != null)
            {
                for (int i = 0; i < views.Length; i++)
                {
                    PhotonView view = views[i];
                    if (view == null)
                        continue;

                    GameObject root = GetPlayerRoot(view);
                    if (root == null || !IsValidPlayerKoboldObject(root))
                        continue;

                    Component kob = GetKoboldOn(root);
                    GameObject kobGo = kob != null ? kob.gameObject : root;

                    if (view.IsMine && (cachedLocalPlayer == null || !IsValidPlayerKoboldObject(cachedLocalPlayer)))
                        cachedLocalPlayer = kobGo;

                    if (view.Owner != null)
                    {
                        int actorId = view.Owner.ActorNumber;
                        if (!playerObjectCache.ContainsKey(actorId) || !IsValidPlayerKoboldObject(playerObjectCache[actorId]))
                            playerObjectCache[actorId] = kobGo;
                    }

                    if (view.Controller != null)
                    {
                        int actorId = view.Controller.ActorNumber;
                        if (!playerObjectCache.ContainsKey(actorId) || !IsValidPlayerKoboldObject(playerObjectCache[actorId]))
                            playerObjectCache[actorId] = kobGo;
                    }
                }
            }

            // Validate TagObject entries too
            for (int i = 0; i < players.Length; i++)
            {
                Player player = players[i];
                if (player == null) continue;
                GameObject tagged = GetTagObject(player);
                if (tagged != null && IsValidPlayerKoboldObject(tagged))
                {
                    Component kob = GetKoboldOn(tagged);
                    playerObjectCache[player.ActorNumber] = kob != null ? kob.gameObject : tagged;
                    if (player.IsLocal)
                        cachedLocalPlayer = playerObjectCache[player.ActorNumber];
                }
            }

            HashSet<int> validActors = new HashSet<int>();
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null)
                    validActors.Add(players[i].ActorNumber);
            }

            List<int> cachedActors = new List<int>(playerObjectCache.Keys);
            for (int i = 0; i < cachedActors.Count; i++)
            {
                int id = cachedActors[i];
                if (!validActors.Contains(id))
                {
                    playerObjectCache.Remove(id);
                    continue;
                }
                GameObject go;
                if (playerObjectCache.TryGetValue(id, out go) && !IsValidPlayerKoboldObject(go))
                    playerObjectCache.Remove(id);
            }

            if (cachedLocalPlayer != null && !IsValidPlayerKoboldObject(cachedLocalPlayer))
                cachedLocalPlayer = null;
        }

        private GameObject FindPlayerObject(Player player)
        {
            if (player == null)
                return null;

            GameObject tagged = GetTagObject(player);
            if (tagged != null && IsValidPlayerKoboldObject(tagged))
            {
                Component kob = GetKoboldOn(tagged);
                GameObject go = kob != null ? kob.gameObject : tagged;
                playerObjectCache[player.ActorNumber] = go;
                return go;
            }

            GameObject cached;
            if (playerObjectCache.TryGetValue(player.ActorNumber, out cached))
            {
                if (cached != null && IsValidPlayerKoboldObject(cached))
                    return cached;

                playerObjectCache.Remove(player.ActorNumber);
            }

            // Scan PhotonViews owned/created by this actor (TagObject is often null on join)
            try
            {
                PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
                if (views != null)
                {
                    int actor = player.ActorNumber;
                    for (int i = 0; i < views.Length; i++)
                    {
                        PhotonView view = views[i];
                        if (view == null || view.gameObject == null) continue;

                        bool owned = false;
                        try
                        {
                            if (view.Owner != null && view.Owner.ActorNumber == actor)
                                owned = true;
                            else if (view.OwnerActorNr == actor)
                                owned = true;
                            else if (view.CreatorActorNr == actor)
                                owned = true;
                        }
                        catch { }

                        if (!owned) continue;
                        if (!IsValidPlayerKoboldObject(view.gameObject) && GetKoboldOn(view.gameObject) == null)
                            continue;

                        Component kob = GetKoboldOn(view.gameObject);
                        GameObject go = kob != null ? kob.gameObject : view.gameObject;
                        playerObjectCache[actor] = go;
                        return go;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("FindPlayerObject scan: " + ex.Message);
            }

            return null;
        }
    }
}
