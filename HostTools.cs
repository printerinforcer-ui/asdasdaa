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
        private void TrackRecentPlayers()
        {
            if (!PhotonNetwork.InRoom)
            {
                if (knownRoomPlayers.Count > 0)
                    knownRoomPlayers.Clear();
                return;
            }

            Player[] players = PhotonNetwork.PlayerList;
            bool rosterChanged = false;

            HashSet<int> currentActors = new HashSet<int>();
            for (int i = 0; i < players.Length; i++)
            {
                Player p = players[i];
                if (p == null)
                    continue;

                currentActors.Add(p.ActorNumber);

                if (!knownRoomPlayers.ContainsKey(p.ActorNumber))
                {
                    knownRoomPlayers[p.ActorNumber] = GetPlayerName(p);
                    rosterChanged = true;

                    if (!p.IsLocal)
                    {
                        // Enforce the ban list: reject a banned account that tries to rejoin.
                        if (PhotonNetwork.IsMasterClient && IsPlayerBanned(p))
                        {
                            PhotonNetwork.CloseConnection(p);
                            AddRecentPlayerEvent("REJECTED BANNED PLAYER: " + GetPlayerName(p) + "  #" + p.ActorNumber);
                        }
                        else
                        {
                            AddRecentPlayerEvent("JOINED: " + GetPlayerName(p) + "  #" + p.ActorNumber);
                        }
                    }
                }
            }

            List<int> knownActors = new List<int>(knownRoomPlayers.Keys);
            for (int i = 0; i < knownActors.Count; i++)
            {
                int actorId = knownActors[i];
                if (!currentActors.Contains(actorId))
                {
                    string name = knownRoomPlayers[actorId];
                    knownRoomPlayers.Remove(actorId);
                    rosterChanged = true;
                    AddRecentPlayerEvent("LEFT: " + name + "  #" + actorId);
                }
            }

            if (rosterChanged && publishRoomPlayers && PhotonNetwork.IsMasterClient)
            {
                nextRoomPlayersPublishTime = 0f;
                PublishRoomPlayerList(true);
            }
        }

        private bool IsPlayerBanned(Player player)
        {
            if (player == null)
                return false;

            string key = !string.IsNullOrEmpty(player.UserId) ? player.UserId : GetPlayerName(player);
            return bannedUserIds.Contains(key);
        }

        // ============================================================
        // TESTING TAB — collapsible sections: QoL / Server / Cross-client / Misc
        // ============================================================
        private void DrawTestingPanel(float x, float y, float width, float maxHeight)
        {
            float startY = y;
            float bottom = startY + maxHeight;
            Event e = Event.current;

            GUI.Label(new Rect(x, y, width, 22f), new GUIContent("TESTING / PREVIEW"), headerStyle);
            y += 24f;
            GUI.Label(new Rect(x, y, width, 32f),
                new GUIContent("New experiments land here first. Expand a section — vote favorites later."),
                smallStyle);
            y += 34f;
            if (DrawCollapsibleHeader("test_changelog", "CHANGELOG", x, y, width))
            {
                y += 28f;
                GUI.Label(new Rect(x, y, width, 90f),
                    new GUIContent(
                        "• Quick bar, friends-only radar, spectate smooth\n" +
                        "• WP share to ˚ʚ♡ɞ˚ clients · PM reply (R)\n" +
                        "• Spawn at waypoint · orphan kobold preview\n" +
                        "• Server modded/vanilla filter · list sort\n" +
                        "• sidebar collapse"),
                    smallStyle);
                y += 94f;
            }
            else y += 28f;
            y += 8f;

            // Scrollable body
            float bodyH = Mathf.Max(120f, bottom - y - 4f);
            Rect view = new Rect(x, y, width, bodyH);
            GUI.Box(view, "");

            // Measured last frame (grows with expanded sections / lighting controls)
            float contentH = Mathf.Max(testingPanelContentH, bodyH + 40f);
            float maxScroll = Mathf.Max(0f, contentH - bodyH + 24f);
            if (e != null && e.type == EventType.ScrollWheel && view.Contains(e.mousePosition))
            {
                testingPanelScroll.y = Mathf.Clamp(testingPanelScroll.y + e.delta.y * 28f, 0f, maxScroll);
                e.Use();
            }
            testingPanelScroll.y = Mathf.Clamp(testingPanelScroll.y, 0f, maxScroll);

            GUI.BeginGroup(new Rect(view.x + 4f, view.y + 4f, width - 8f, bodyH - 8f), GUIContent.none, GUIStyle.none);
            float iy = -testingPanelScroll.y;
            float iw = width - 16f;
            float contentTop = iy;

            // ---- QOL ----
            if (DrawCollapsibleHeader("test_qol", "VOTE WEEK 1 ( QoL )", 0f, iy, iw))
            {
                iy += 30f;
                iy = DrawTestingQoLSection(0f, iy, iw);
            }
            else iy += 30f;
            iy += 8f;

            // ---- SERVER ----
            if (DrawCollapsibleHeader("test_server", "VOTE WEEK 2 ( SERVER )", 0f, iy, iw))
            {
                iy += 30f;
                iy = DrawTestingServerSection(0f, iy, iw);
            }
            else iy += 30f;
            iy += 8f;

            // ---- CROSS-CLIENT ----
            if (DrawCollapsibleHeader("test_cross", "VOTE WEEK 3 ( CROSS-CLIENT )", 0f, iy, iw))
            {
                iy += 30f;
                iy = DrawTestingCrossClientSection(0f, iy, iw);
            }
            else iy += 30f;
            iy += 8f;

            // ---- MISC ----
            if (DrawCollapsibleHeader("test_misc", "To Be Determined.", 0f, iy, iw))
            {
                iy += 30f;
                iy = DrawTestingMiscSection(0f, iy, iw);
            }
            else iy += 30f;

            // +40px bottom padding so last slider isn't flush against the edge
            float measured = (iy - contentTop) + testingPanelScroll.y + 40f;
            if (measured > 80f)
                testingPanelContentH = measured;

            GUI.EndGroup();
        }

        private Vector2 testingPanelScroll;
        private float testingPanelContentH = 1400f;

        private float DrawTestingQoLSection(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, w, 18f), new GUIContent("Voting Ends On V1.3"), smallStyle);
            y += 22f;

            if (GUI.Button(new Rect(x, y, w * 0.48f, 26f),
                new GUIContent(showHudWaypoints ? "HUD Waypoints: ON" : "HUD Waypoints: OFF"), buttonStyle))
            {
                showHudWaypoints = !showHudWaypoints;
                if (configShowHudWaypoints != null) configShowHudWaypoints.Value = showHudWaypoints;
            }
            if (GUI.Button(new Rect(x + w * 0.52f, y, w * 0.48f, 26f),
                new GUIContent(destroyBodyOnLeave ? "Clean leave: ON" : "Clean leave: OFF"), buttonStyle))
            {
                destroyBodyOnLeave = !destroyBodyOnLeave;
                if (configDestroyBodyOnLeave != null) configDestroyBodyOnLeave.Value = destroyBodyOnLeave;
            }
            y += 30f;

            GUI.Label(new Rect(x, y, w, 18f),
                new GUIContent("UI SCALE: " + uiScale.ToString("0.00") + "x  (drag menu to corners when small)"), labelStyle);
            y += 20f;
            float ns = GUI.HorizontalSlider(new Rect(x, y, w, 18f), uiScale, UiScaleMin, UiScaleMax,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            // Snap to 0.05 steps for cleaner control
            ns = Mathf.Round(ns * 20f) / 20f;
            if (!Mathf.Approximately(ns, uiScale))
            {
                uiScale = Mathf.Clamp(ns, UiScaleMin, UiScaleMax);
                if (configUiScale != null) configUiScale.Value = uiScale;
            }
            y += 26f;

            if (GUI.Button(new Rect(x, y, w, 28f),
                new GUIContent("CLEAN SCREENSHOT (" +
                    (cleanScreenshotKey != null ? cleanScreenshotKey.Value.ToString() : "F9") + ")"),
                buttonStyle))
                ToggleCleanUiAndScreenshot();
            y += 34f;

            if (GUI.Button(new Rect(x, y, w * 0.48f, 26f),
                new GUIContent(showQuickActionBar ? "Quick bar: ON" : "Quick bar: OFF"), buttonStyle))
                showQuickActionBar = !showQuickActionBar;
            if (GUI.Button(new Rect(x + w * 0.52f, y, w * 0.48f, 26f),
                new GUIContent(friendsOnlyMode ? "Friends only: ON" : "Friends only: OFF"), buttonStyle))
                friendsOnlyMode = !friendsOnlyMode;
            y += 30f;

            GUI.Label(new Rect(x, y, w, 18f),
                new GUIContent("RADAR RANGE: " + radarRange.ToString("0") + "m"), labelStyle);
            y += 18f;
            float rr = GUI.HorizontalSlider(new Rect(x, y, w, 18f), radarRange, 10f, 200f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(rr, radarRange)) radarRange = rr;
            y += 22f;

            GUI.Label(new Rect(x, y, w, 18f),
                new GUIContent("RADAR OPACITY: " + Mathf.RoundToInt(radarOpacity * 100f) + "%"), labelStyle);
            y += 18f;
            float ro = GUI.HorizontalSlider(new Rect(x, y, w, 18f), radarOpacity, 0.15f, 1f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(ro, radarOpacity)) radarOpacity = ro;
            y += 22f;

            GUI.Label(new Rect(x, y, w, 18f),
                new GUIContent("SPECTATE SMOOTH: " + spectateSmooth.ToString("0.0")), labelStyle);
            y += 18f;
            float ss = GUI.HorizontalSlider(new Rect(x, y, w, 18f), spectateSmooth, 0f, 30f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(ss, spectateSmooth)) spectateSmooth = ss;
            y += 22f;

            if (GUI.Button(new Rect(x, y, w * 0.48f, 26f), new GUIContent("Copy coords"), buttonStyle))
                CopyLocalCoordsToClipboard();
            if (GUI.Button(new Rect(x + w * 0.52f, y, w * 0.48f, 26f), new GUIContent("Paste → WP"), buttonStyle))
                PasteCoordsAsWaypoint();
            y += 30f;

            GUI.Label(new Rect(x, y, w, 40f),
                new GUIContent(
                    "Session: " + sessionRoomTimeLabel +
                    "  ·  TP " + sessionTeleportCount +
                    "  ·  Spec " + sessionSpectateCount +
                    "  ·  Kicks " + sessionKickCount +
                    "\nStop spectate: " + (stopSpectateKey != null ? stopSpectateKey.Value.ToString() : "?")),
                smallStyle);
            y += 44f;
            return y;
        }

        private float DrawTestingServerSection(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, w, 18f), new GUIContent("Voting Ends On V1.4"), smallStyle);
            y += 22f;

            string lastRoom = !string.IsNullOrEmpty(lastJoinedRoomName) ? lastJoinedRoomName : "(none)";
            if (lastRoom.Length > 28) lastRoom = lastRoom.Substring(0, 27) + "…";
            if (GUI.Button(new Rect(x, y, w, 26f), new GUIContent("Rejoin last: " + lastRoom), buttonStyle))
                TryRejoinLastRoom();
            y += 30f;

            if (GUI.Button(new Rect(x, y, w * 0.48f, 26f),
                new GUIContent(publishRoomPlayers ? "Publish names: ON" : "Publish names: OFF"), buttonStyle))
            {
                publishRoomPlayers = !publishRoomPlayers;
                if (configPublishRoomPlayers != null) configPublishRoomPlayers.Value = publishRoomPlayers;
                if (publishRoomPlayers && PhotonNetwork.IsMasterClient)
                    PublishRoomPlayerList(true);
            }
            if (GUI.Button(new Rect(x + w * 0.52f, y, w * 0.48f, 26f),
                new GUIContent(welcomeMessageOnJoin ? "Welcome toast: ON" : "Welcome toast: OFF"), buttonStyle))
            {
                welcomeMessageOnJoin = !welcomeMessageOnJoin;
                if (configWelcomeMessageOnJoin != null) configWelcomeMessageOnJoin.Value = welcomeMessageOnJoin;
            }
            y += 34f;
            return y;
        }

        private float DrawTestingCrossClientSection(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, w, 36f),
                new GUIContent("Features that talk to other ˚ʚ♡ɞ˚ clients only (Photon event 175)."),
                smallStyle);
            y += 40f;

            if (GUI.Button(new Rect(x, y, w, 32f),
                new GUIContent("PARTY PING (share marker)"), buttonStyle))
                SendPartyPing();
            y += 34f;
            string wp = string.IsNullOrEmpty(selectedShareWaypointName) ? "(pick WP in Teleport)" : selectedShareWaypointName;
            if (GUI.Button(new Rect(x, y, w, 28f),
                new GUIContent("SHARE WAYPOINT: " + wp), buttonStyle))
            {
                if (!string.IsNullOrEmpty(selectedShareWaypointName))
                    ShareWaypointToZexClients(selectedShareWaypointName);
                else
                    ShowToast("Select a waypoint in Teleport popup first", "system");
            }
            y += 32f;
            if (GUI.Button(new Rect(x, y, w, 26f),
                new GUIContent("REPLY LAST PM (R)"), buttonStyle))
                ReplyToLastPm();
            y += 30f;

            GUI.Label(new Rect(x, y, w, 48f),
                new GUIContent(
                    "Places a purple world marker for other Zex users.\n" +
                    "Right-click a player → Message ˚ʚ♡ɞ˚ for PM.\n" +
                    "Both need this mod + ZQL presence prop."),
                smallStyle);
            y += 54f;

            int zex = 0;
            if (PhotonNetwork.InRoom && PhotonNetwork.PlayerList != null)
            {
                for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
                    if (PhotonNetwork.PlayerList[i] != null &&
                        !PhotonNetwork.PlayerList[i].IsLocal &&
                        PlayerHasZexClient(PhotonNetwork.PlayerList[i]))
                        zex++;
            }
            GUI.Label(new Rect(x, y, w, 18f),
                new GUIContent("Other ˚ʚ♡ɞ˚ clients in room: " + zex), labelStyle);
            y += 24f;

            if (GUI.Button(new Rect(x, y, w, 28f),
                new GUIContent("OPEN CHAT TAB"), buttonStyle))
            {
                tab = 13;
                menuVisible = true;
            }
            y += 34f;
            return y;
        }

        private float DrawTestingMiscSection(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, w, 18f), new GUIContent("MORE WILL BE HERE IN V1.4 ( NEXT VOTE )"), smallStyle);
            y += 22f;

            if (GUI.Button(new Rect(x, y, w, 26f), new GUIContent("Create Offline Lobby"), buttonStyle))
                ForceOfflineSoloHost();
            y += 30f;

            if (!string.IsNullOrEmpty(ownershipStatus) && Time.unscaledTime < ownershipStatusUntil)
            {
                GUI.Label(new Rect(x, y, w, 36f), new GUIContent(ownershipStatus), smallStyle);
                y += 40f;
            }
            return y;
        }

        private void DrawPlayerList(float x, float y, float width, float height)
        {
            GUI.Box(new Rect(x, y, width, height), "");
            if (!PhotonNetwork.InRoom)
            {
                GUI.Label(new Rect(x + 12f, y + 15f, width - 24f, 25f), new GUIContent("Not in a multiplayer room."), labelStyle);
                return;
            }

            Player[] players = PhotonNetwork.PlayerList;
            if (players != null && players.Length > 1)
            {
                var list = new System.Collections.Generic.List<Player>(players);
                GameObject localObj = GetLocalPlayer();
                list.Sort((a, b) =>
                {
                    if (a == null && b == null) return 0;
                    if (a == null) return 1;
                    if (b == null) return -1;
                    switch (playerListSortMode)
                    {
                        case 1: // distance
                        {
                            float da = 99999f, db = 99999f;
                            if (localObj != null)
                            {
                                var oa = FindPlayerObject(a); var ob = FindPlayerObject(b);
                                if (oa != null) da = Vector3.Distance(localObj.transform.position, oa.transform.position);
                                if (ob != null) db = Vector3.Distance(localObj.transform.position, ob.transform.position);
                            }
                            int c = da.CompareTo(db);
                            return c != 0 ? c : string.Compare(a.NickName, b.NickName, StringComparison.OrdinalIgnoreCase);
                        }
                        case 2: // actor
                            return a.ActorNumber.CompareTo(b.ActorNumber);
                        case 3: // host first
                        {
                            int ha = a.IsMasterClient ? 0 : 1;
                            int hb = b.IsMasterClient ? 0 : 1;
                            int c = ha.CompareTo(hb);
                            return c != 0 ? c : string.Compare(a.NickName, b.NickName, StringComparison.OrdinalIgnoreCase);
                        }
                        default:
                            return string.Compare(a.NickName ?? "", b.NickName ?? "", StringComparison.OrdinalIgnoreCase);
                    }
                });
                players = list.ToArray();
            }
            const float rowHeight = 38f;
            float viewportH = height - 10f;
            float contentH = players.Length * rowHeight;
            float maxScroll = Mathf.Max(0f, contentH - viewportH);
            Rect viewport = new Rect(x + 5f, y + 5f, width - 10f, viewportH);

            Event e = Event.current;
            if (e != null && e.type == EventType.ScrollWheel && viewport.Contains(e.mousePosition))
            {
                playerScroll.y = Mathf.Clamp(playerScroll.y + e.delta.y * 25f, 0f, maxScroll);
                e.Use();
            }

            playerScroll.y = Mathf.Clamp(playerScroll.y, 0f, maxScroll);
            GUI.BeginGroup(viewport, new GUIContent(""), GUIStyle.none);
            float rowY = -playerScroll.y;
            GameObject local = GetLocalPlayer();
            for (int i = 0; i < players.Length; i++)
            {
                Player p = players[i];
                if (p == null) continue;
                string name = string.IsNullOrEmpty(p.NickName) ? "Player " + p.ActorNumber : p.NickName;
                GameObject obj = FindPlayerObject(p);
                float dist = local != null && obj != null ? Vector3.Distance(local.transform.position, obj.transform.position) : -1f;
                string text = name + "  #" + p.ActorNumber;
                if (dist >= 0f) text += "  " + dist.ToString("0.0") + "m";
                if (p.IsLocal) text += "  [YOU]";
                string note = GetPlayerNote(p);
                if (!string.IsNullOrEmpty(note)) text += "  📝";
                GUIStyle style = selectedActorId == p.ActorNumber ? selectedButtonStyle : buttonStyle;
                if (GUI.Button(new Rect(0f, rowY, viewport.width - 8f, 32f), new GUIContent(text), style))
                {
                    SelectPlayer(p);
                }
                rowY += rowHeight;
            }
            GUI.EndGroup();
        }

        private void DrawHostToolsPanel(float x, float y, float width, float maxHeight)
        {
            // Content ends near LEAVE ROOM; keep room for purge + room options.
            float panelH = Mathf.Min(Mathf.Max(280f, maxHeight), 560f);
            GUI.Box(new Rect(x, y, width, panelH), "");

            bool inRoom = PhotonNetwork.InRoom;
            bool isHost = inRoom && PhotonNetwork.IsMasterClient;

            if (!isHost)
            {
                kickConfirmationVisible = false;
                kickAllConfirmationVisible = false;
                pendingKickPlayer = null;
                banConfirmationVisible = false;
                pendingBanPlayer = null;
            }

            GUI.Label(
                new Rect(x + 12f, y + 10f, width - 24f, 24f),
                new GUIContent(
                    !inRoom
                        ? "HOST STATUS: NOT IN ROOM"
                        : isHost
                            ? "HOST STATUS: MASTER CLIENT"
                            : "HOST STATUS: NOT HOST"),
                headerStyle);

            if (!inRoom)
            {
                GUI.Label(
                    new Rect(x + 12f, y + 48f, width - 24f, 40f),
                    new GUIContent("Join a room first"),
                    labelStyle);
                return;
            }

            float gap = 12f;
            float leftWidth = width * 0.46f;
            float rightX = x + leftWidth + gap;
            float rightWidth = width - leftWidth - gap;

            GUI.Label(
                new Rect(x + 12f, y + 40f, 80f, 22f),
                new GUIContent("Players"),
                headerStyle);

            string[] sortNames = { "NAME", "DIST", "ACTOR", "HOST" };
            float sortW = 100f;
            if (GUI.Button(new Rect(x + leftWidth - sortW - 4f, y + 38f, sortW, 24f),
                new GUIContent(sortNames[Mathf.Clamp(playerListSortMode, 0, 3)]), buttonStyle))
                playerListSortMode = (playerListSortMode + 1) % 4;

            GUI.Label(
                new Rect(rightX, y + 40f, rightWidth - 12f, 22f),
                new GUIContent("Actions"),
                headerStyle);

            float playerListH = Mathf.Max(160f, panelH - 90f);
            DrawPlayerList(
                x + 12f,
                y + 68f,
                leftWidth - 12f,
                playerListH);

            Player selected = selectedPlayer;
            bool anyConfirmationVisible = kickConfirmationVisible || kickAllConfirmationVisible || banConfirmationVisible;
            bool canUseSelected =
                isHost &&
                selected != null &&
                !selected.IsLocal &&
                !anyConfirmationVisible;

            int otherPlayerCount =
                PhotonNetwork.PlayerList != null
                    ? Mathf.Max(0, PhotonNetwork.PlayerList.Length - 1)
                    : 0;

            bool kickAllOnCooldown = Time.unscaledTime < kickAllCooldownUntil;

            bool canKickAll =
                isHost &&
                otherPlayerCount > 0 &&
                !kickAllOnCooldown &&
                !anyConfirmationVisible;

            string selectedName =
                selected == null ? "None" : GetPlayerName(selected);

            GUI.Label(
                new Rect(rightX, y + 70f, rightWidth - 12f, 24f),
                new GUIContent("Selected: " + selectedName),
                labelStyle);

            if (GUI.Button(
                new Rect(rightX, y + 106f, rightWidth - 12f, 34f),
                new GUIContent("KICK SELECTED PLAYER"),
                canUseSelected ? buttonStyle : GUI.skin.button) &&
                canUseSelected)
            {
                RequestKickSelectedPlayer();
            }

            string kickAllLabel = kickAllOnCooldown
                ? "KICK ALL OTHERS (" + Mathf.CeilToInt(kickAllCooldownUntil - Time.unscaledTime) + "s)"
                : "KICK ALL OTHERS";

            if (GUI.Button(
                new Rect(rightX, y + 146f, rightWidth - 12f, 34f),
                new GUIContent(kickAllLabel),
                canKickAll ? buttonStyle : GUI.skin.button) &&
                canKickAll)
            {
                RequestKickAllOtherPlayers();
            }

            if (GUI.Button(
                new Rect(rightX, y + 186f, rightWidth - 12f, 34f),
                new GUIContent("TRANSFER MASTER CLIENT"),
                canUseSelected ? buttonStyle : GUI.skin.button) &&
                canUseSelected)
            {
                TransferMasterClient();
            }

            if (GUI.Button(
                new Rect(rightX, y + 226f, rightWidth - 12f, 34f),
                new GUIContent("BAN SELECTED PLAYER"),
                canUseSelected ? buttonStyle : GUI.skin.button) &&
                canUseSelected)
            {
                RequestBanSelectedPlayer();
            }

            int orphanPreview = isHost ? CountOrphanKobolds() : 0;
            string purgeLabel = isHost
                ? ("DELETE EXTRA KOBOLDS (" + orphanPreview + ")")
                : "DELETE EXTRA KOBOLDS";
            if (GUI.Button(
                new Rect(rightX, y + 266f, rightWidth - 12f, 34f),
                new GUIContent(purgeLabel),
                isHost ? buttonStyle : GUI.skin.button) &&
                isHost)
            {
                if (orphanPreview <= 0)
                    ShowToast("No extra kobolds found", "host");
                else
                {
                    int n = PurgeOrphanKobolds();
                    sessionKickCount += 0; // keep stats field warm
                    ShowToast(n > 0 ? ("DELETED " + n + " EXTRA KOBOLD(s)") : "No extra kobolds found", "host");
                }
            }

            Room room = PhotonNetwork.CurrentRoom;
            if (room == null)
                return;

            GUI.Label(
                new Rect(rightX, y + 312f, rightWidth - 12f, 22f),
                new GUIContent("ROOM OPTIONS"),
                headerStyle);

            float roomOptGap = 10f;
            float roomOptButtonWidth = (rightWidth - 12f - roomOptGap) / 2f;

            string roomOpenLabel = room.IsOpen ? "CLOSE ROOM" : "OPEN ROOM";
            string roomVisibleLabel = room.IsVisible ? "HIDE ROOM" : "SHOW ROOM";

            if (GUI.Button(
                new Rect(rightX, y + 338f, roomOptButtonWidth, 32f),
                new GUIContent(roomOpenLabel),
                isHost ? buttonStyle : GUI.skin.button) &&
                isHost)
            {
                ToggleRoomOpen();
            }

            if (GUI.Button(
                new Rect(rightX + roomOptButtonWidth + roomOptGap, y + 338f, roomOptButtonWidth, 32f),
                new GUIContent(roomVisibleLabel),
                isHost ? buttonStyle : GUI.skin.button) &&
                isHost)
            {
                ToggleRoomVisibility();
            }

            GUI.Label(new Rect(rightX, y + 382f, rightWidth - 12f, 22f), new GUIContent("ROOM SIZE: " + room.MaxPlayers), labelStyle);

            int maxPlayers = room.MaxPlayers > 0
                ? room.MaxPlayers
                : 32;

            int newMaxPlayers = Mathf.RoundToInt(GUI.HorizontalSlider(
                new Rect(rightX, y + 410f, rightWidth - 12f, 18f),
                maxPlayers,
                1f,
                32f,
                GUI.skin.horizontalSlider,
                GUI.skin.horizontalSliderThumb));

            if (isHost && newMaxPlayers != room.MaxPlayers)
                room.MaxPlayers = (byte)newMaxPlayers;

            string currentLabel = GetRoomLabel(room);

            GUI.Label(
                new Rect(rightX, y + 446f, rightWidth - 12f, 20f),
                new GUIContent("ROOM: " + room.Name + (string.IsNullOrEmpty(currentLabel) ? "" : "  (" + currentLabel + ")")),
                smallStyle);

            if (!roomLabelFocused)
                roomLabelInput = currentLabel;

            Rect labelFieldRect = new Rect(rightX, y + 468f, rightWidth - 12f - 62f, 26f);
            GUI.Box(labelFieldRect, new GUIContent(""), GUI.skin.box);
            string labelDisplay = string.IsNullOrEmpty(roomLabelInput) ? "CLICK TO SET ROOM LABEL..." : roomLabelInput;
            GUI.Label(new Rect(labelFieldRect.x + 6f, labelFieldRect.y + 3f, labelFieldRect.width - 12f, 20f), new GUIContent(labelDisplay), labelStyle);

            Event labelEvent = Event.current;
            if (labelEvent != null && labelEvent.type == EventType.MouseDown && labelFieldRect.Contains(labelEvent.mousePosition))
            {
                roomLabelFocused = true;
                labelEvent.Use();
            }
            else if (labelEvent != null && labelEvent.type == EventType.MouseDown && !labelFieldRect.Contains(labelEvent.mousePosition))
            {
                roomLabelFocused = false;
            }

            if (roomLabelFocused && labelEvent != null && labelEvent.type == EventType.KeyDown)
            {
                if (labelEvent.keyCode == KeyCode.Backspace)
                {
                    if (roomLabelInput.Length > 0) roomLabelInput = roomLabelInput.Substring(0, roomLabelInput.Length - 1);
                    labelEvent.Use();
                }
                else if (labelEvent.keyCode == KeyCode.Return || labelEvent.keyCode == KeyCode.KeypadEnter)
                {
                    if (isHost) SetRoomLabel(room, roomLabelInput);
                    roomLabelFocused = false;
                    labelEvent.Use();
                }
                else if (labelEvent.keyCode == KeyCode.Escape)
                {
                    roomLabelInput = currentLabel;
                    roomLabelFocused = false;
                    labelEvent.Use();
                }
                else if (labelEvent.character != '\0' && !char.IsControl(labelEvent.character))
                {
                    if (roomLabelInput.Length < 40) roomLabelInput += labelEvent.character;
                    labelEvent.Use();
                }
            }

            if (GUI.Button(
                new Rect(rightX + rightWidth - 12f - 56f, y + 468f, 56f, 26f),
                new GUIContent("Set"),
                isHost ? buttonStyle : GUI.skin.button) &&
                isHost)
            {
                SetRoomLabel(room, roomLabelInput);
                roomLabelFocused = false;
            }

            if (GUI.Button(
                new Rect(rightX, y + 500f, rightWidth - 12f, 30f),
                new GUIContent("LEAVE ROOM"),
                buttonStyle))
            {
                LeaveCurrentRoom();
            }

            // Recent events + ban list live on the Host Logs tab now.

            if (kickConfirmationVisible && pendingKickPlayer != null)
            {
                float dialogWidth = rightWidth - 20f;
                float dialogX = rightX + 5f;
                float dialogY = y + 95f;

                GUI.Box(
                    new Rect(dialogX, dialogY, dialogWidth, 112f),
                    "");

                GUI.Label(
                    new Rect(dialogX + 8f, dialogY + 8f, dialogWidth - 16f, 24f),
                    new GUIContent(
                        "Kick " + GetPlayerName(pendingKickPlayer) + "?"),
                    labelStyle);

                float buttonWidth = (dialogWidth - 24f) / 2f;

                if (GUI.Button(
                    new Rect(dialogX + 8f, dialogY + 48f, buttonWidth, 32f),
                    new GUIContent("YES"),
                    buttonStyle))
                {
                    ConfirmKickPlayer();
                }

                if (GUI.Button(
                    new Rect(
                        dialogX + 16f + buttonWidth,
                        dialogY + 48f,
                        buttonWidth,
                        32f),
                    new GUIContent("NO"),
                    buttonStyle))
                {
                    CancelKickPlayer();
                }
            }
            else if (kickAllConfirmationVisible)
            {
                float dialogWidth = rightWidth - 20f;
                float dialogX = rightX + 5f;
                float dialogY = y + 95f;

                GUI.Box(
                    new Rect(dialogX, dialogY, dialogWidth, 112f),
                    "");

                GUI.Label(
                    new Rect(dialogX + 8f, dialogY + 8f, dialogWidth - 16f, 24f),
                    new GUIContent("Kick ALL " + otherPlayerCount + " other player" + (otherPlayerCount == 1 ? "" : "s") + "?"),
                    labelStyle);

                float buttonWidth = (dialogWidth - 24f) / 2f;

                if (GUI.Button(
                    new Rect(dialogX + 8f, dialogY + 48f, buttonWidth, 32f),
                    new GUIContent("YES"),
                    buttonStyle))
                {
                    ConfirmKickAllOtherPlayers();
                }

                if (GUI.Button(
                    new Rect(
                        dialogX + 16f + buttonWidth,
                        dialogY + 48f,
                        buttonWidth,
                        32f),
                    new GUIContent("NO"),
                    buttonStyle))
                {
                    CancelKickAllOtherPlayers();
                }
            }
            else if (banConfirmationVisible && pendingBanPlayer != null)
            {
                float dialogWidth = rightWidth - 20f;
                float dialogX = rightX + 5f;
                float dialogY = y + 95f;

                GUI.Box(
                    new Rect(dialogX, dialogY, dialogWidth, 112f),
                    "");

                GUI.Label(
                    new Rect(dialogX + 8f, dialogY + 8f, dialogWidth - 16f, 24f),
                    new GUIContent(
                        "Ban " + GetPlayerName(pendingBanPlayer) + "?"),
                    labelStyle);

                float buttonWidth = (dialogWidth - 24f) / 2f;

                if (GUI.Button(
                    new Rect(dialogX + 8f, dialogY + 48f, buttonWidth, 32f),
                    new GUIContent("YES"),
                    buttonStyle))
                {
                    ConfirmBanPlayer();
                }

                if (GUI.Button(
                    new Rect(
                        dialogX + 16f + buttonWidth,
                        dialogY + 48f,
                        buttonWidth,
                        32f),
                    new GUIContent("NO"),
                    buttonStyle))
                {
                    CancelBanPlayer();
                }
            }
        }

        private void RequestKickAllOtherPlayers()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
                return;

            kickAllConfirmationVisible = true;
        }

        private string GetRoomLabel(Room room)
        {
            if (room == null || room.CustomProperties == null)
                return "";

            object value;
            if (room.CustomProperties.TryGetValue(RoomLabelPropertyKey, out value) && value is string)
                return (string)value;

            return "";
        }

        private void SetRoomLabel(Room room, string label)
        {
            if (room == null || !PhotonNetwork.IsMasterClient)
                return;

            ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
            {
                { RoomLabelPropertyKey, label ?? "" }
            };

            room.SetCustomProperties(props);
        }

        private void RequestKickSelectedPlayer()
        {
            if (!PhotonNetwork.InRoom ||
                !PhotonNetwork.IsMasterClient ||
                selectedPlayer == null ||
                selectedPlayer.IsLocal)
            {
                return;
            }

            pendingKickPlayer = selectedPlayer;
            ConfirmKickPlayer();
        }

        private void TransferMasterClient()
        {
            if (!PhotonNetwork.InRoom ||
                !PhotonNetwork.IsMasterClient ||
                selectedPlayer == null ||
                selectedPlayer.IsLocal)
            {
                return;
            }

            PhotonNetwork.SetMasterClient(selectedPlayer);
        }

        private void ToggleRoomOpen()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
                return;

            Room room = PhotonNetwork.CurrentRoom;
            if (room != null)
                room.IsOpen = !room.IsOpen;
        }

        private void ToggleRoomVisibility()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
                return;

            Room room = PhotonNetwork.CurrentRoom;
            if (room != null)
                room.IsVisible = !room.IsVisible;
        }

        private void ConfirmKickPlayer()
        {
            if (pendingKickPlayer == null ||
                !PhotonNetwork.InRoom ||
                !PhotonNetwork.IsMasterClient ||
                pendingKickPlayer.IsLocal)
            {
                CancelKickPlayer();
                return;
            }

            string playerName = GetPlayerName(pendingKickPlayer);
            int actorId = pendingKickPlayer.ActorNumber;

            PhotonNetwork.CloseConnection(pendingKickPlayer);

            AddRecentPlayerEvent(
                "KICKED: " + playerName + "  #" + actorId
            );

            if (selectedActorId == actorId)
            {
                selectedActorId = -1;
                selectedPlayer = null;
            }

            CancelKickPlayer();
        }

        private void CancelKickPlayer()
        {
            pendingKickPlayer = null;
            kickConfirmationVisible = false;
        }

        private void CancelKickAllPlayers()
        {
            kickAllConfirmationVisible = false;
        }

        private void KickAllOtherPlayers()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
                return;

            Player[] players = PhotonNetwork.PlayerList;
            if (players == null)
                return;

            int kicked = 0;
            for (int i = 0; i < players.Length; i++)
            {
                Player p = players[i];
                if (p == null || p.IsLocal)
                    continue;

                PhotonNetwork.CloseConnection(p);
                kicked++;
            }

            AddRecentPlayerEvent("KICKED ALL: " + kicked + " player" + (kicked == 1 ? "" : "s"));

            if (selectedPlayer != null && !selectedPlayer.IsLocal)
            {
                selectedActorId = -1;
                selectedPlayer = null;
            }
        }

        private void ConfirmKickAllOtherPlayers()
        {
            KickAllOtherPlayers();
            kickAllConfirmationVisible = false;
            kickAllCooldownUntil = Time.unscaledTime + KickAllCooldownSeconds;
        }

        private void CancelKickAllOtherPlayers()
        {
            kickAllConfirmationVisible = false;
        }

        private void RequestBanSelectedPlayer()
        {
            if (!PhotonNetwork.InRoom ||
                !PhotonNetwork.IsMasterClient ||
                selectedPlayer == null ||
                selectedPlayer.IsLocal)
            {
                return;
            }

            pendingBanPlayer = selectedPlayer;
            banConfirmationVisible = true;
        }

        private void ConfirmBanPlayer()
        {
            if (pendingBanPlayer == null ||
                !PhotonNetwork.InRoom ||
                !PhotonNetwork.IsMasterClient ||
                pendingBanPlayer.IsLocal)
            {
                CancelBanPlayer();
                return;
            }

            string playerName = GetPlayerName(pendingBanPlayer);
            int actorId = pendingBanPlayer.ActorNumber;

            RecordBannedPlayer(pendingBanPlayer);
            PhotonNetwork.CloseConnection(pendingBanPlayer);

            AddRecentPlayerEvent(
                "BANNED: " + playerName + "  #" + actorId
            );

            if (selectedActorId == actorId)
            {
                selectedActorId = -1;
                selectedPlayer = null;
            }

            CancelBanPlayer();
        }

        private void RecordBannedPlayer(Player player)
        {
            if (player == null)
                return;

            string key = !string.IsNullOrEmpty(player.UserId) ? player.UserId : GetPlayerName(player);
            if (string.IsNullOrEmpty(key))
                return;

            bannedUserIds.Add(key);
            SaveBannedUserIds();
        }

        private void AddRecentPlayerEvent(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            recentPlayerEvents.Insert(0, line);
            if (recentPlayerEvents.Count > 40)
                recentPlayerEvents.RemoveAt(recentPlayerEvents.Count - 1);
        }

        private void CancelBanPlayer()
        {
            pendingBanPlayer = null;
            banConfirmationVisible = false;
        }

        private void DrawHostLogsPanel(float x, float y, float width, float maxHeight)
        {
            float startY = y;

            GUI.Label(
                new Rect(x, y, width - 100f, 22f),
                new GUIContent("RECENT PLAYER EVENTS"),
                headerStyle);

            if (GUI.Button(
                new Rect(x + width - 90f, y - 2f, 90f, 26f),
                new GUIContent("CLEAR"),
                recentPlayerEvents.Count > 0 ? buttonStyle : GUI.skin.button) &&
                recentPlayerEvents.Count > 0)
            {
                recentPlayerEvents.Clear();
            }

            y += 30f;

            float eventsH = Mathf.Max(120f, (maxHeight - 60f) * 0.45f);
            Rect eventsViewRect = new Rect(x, y, width, eventsH);

            const float eventRowHeight = 20f;
            float eventsContentHeight = recentPlayerEvents.Count * eventRowHeight;
            float eventsMaxScroll = Mathf.Max(0f, eventsContentHeight - eventsH);

            Event eventsScrollEvent = Event.current;
            if (eventsScrollEvent != null &&
                eventsScrollEvent.type == EventType.ScrollWheel &&
                eventsViewRect.Contains(eventsScrollEvent.mousePosition))
            {
                recentEventsScroll.y = Mathf.Clamp(recentEventsScroll.y + eventsScrollEvent.delta.y * 25f, 0f, eventsMaxScroll);
                eventsScrollEvent.Use();
            }

            recentEventsScroll.y = Mathf.Clamp(recentEventsScroll.y, 0f, eventsMaxScroll);

            GUI.Box(eventsViewRect, new GUIContent(""), GUI.skin.box);
            GUI.BeginGroup(eventsViewRect, new GUIContent(""), GUIStyle.none);

            float eventY = -recentEventsScroll.y;
            if (recentPlayerEvents.Count == 0)
            {
                GUI.Label(new Rect(12f, 12f, width - 24f, 22f),
                    new GUIContent("No join/leave events yet."),
                    labelStyle);
            }
            else
            {
                for (int i = 0; i < recentPlayerEvents.Count; i++)
                {
                    GUI.Label(
                        new Rect(8f, eventY, eventsViewRect.width - 16f, eventRowHeight),
                        new GUIContent(recentPlayerEvents[i]),
                        smallStyle);
                    eventY += eventRowHeight;
                }
            }

            GUI.EndGroup();

            y += eventsH + 18f;

            GUI.Label(
                new Rect(x, y, width - 100f, 22f),
                new GUIContent("BANNED PLAYERS (" + bannedUserIds.Count + ")"),
                headerStyle);

            if (GUI.Button(
                new Rect(x + width - 90f, y - 2f, 90f, 26f),
                new GUIContent("CLEAR ALL"),
                bannedUserIds.Count > 0 ? buttonStyle : GUI.skin.button) &&
                bannedUserIds.Count > 0)
            {
                bannedUserIds.Clear();
                if (configBannedUserIds != null)
                    configBannedUserIds.Value = "";
            }

            y += 30f;

            float banListHeight = Mathf.Max(80f, maxHeight - (y - startY) - 8f);
            Rect banListRect = new Rect(x, y, width, banListHeight);

            if (bannedUserIds.Count == 0)
            {
                GUI.Box(banListRect, "");
                GUI.Label(
                    new Rect(x + 12f, y + 12f, width - 24f, 22f),
                    new GUIContent("No players banned this session."),
                    labelStyle);
            }
            else
            {
                List<string> bannedList = new List<string>(bannedUserIds);

                const float rowHeight = 34f;
                float contentHeight = bannedList.Count * rowHeight;
                float maxScroll = Mathf.Max(0f, contentHeight - banListHeight);

                Event banEvent = Event.current;
                if (banEvent != null &&
                    banEvent.type == EventType.ScrollWheel &&
                    banListRect.Contains(banEvent.mousePosition))
                {
                    bannedListScroll.y = Mathf.Clamp(bannedListScroll.y + banEvent.delta.y * 25f, 0f, maxScroll);
                    banEvent.Use();
                }

                bannedListScroll.y = Mathf.Clamp(bannedListScroll.y, 0f, maxScroll);

                GUI.Box(banListRect, "");
                GUI.BeginGroup(banListRect, new GUIContent(""), GUIStyle.none);

                float rowY = -bannedListScroll.y;
                for (int i = 0; i < bannedList.Count; i++)
                {
                    string entry = bannedList[i];

                    GUI.Label(
                        new Rect(8f, rowY + 6f, width - 100f, 24f),
                        new GUIContent(entry),
                        labelStyle);

                    if (GUI.Button(
                        new Rect(width - 84f, rowY + 4f, 76f, 26f),
                        new GUIContent("UNBAN"),
                        buttonStyle))
                    {
                        bannedUserIds.Remove(entry);
                    }

                    rowY += rowHeight;
                }

                GUI.EndGroup();
            }
        }

        private void SetRewardStatus(string message)
        {
            rewardStatus = message;
            rewardStatusUntil = Time.unscaledTime + 4f;
        }

        private void GiveMyMaxMoney()
        {
            GameObject local = GetLocalPlayer();
            if (local == null)
            {
                SetRewardStatus("Money: local player not found.");
                return;
            }

            MoneyHolder holder = local.GetComponentInChildren<MoneyHolder>(true);
            if (holder == null)
            {
                SetRewardStatus("Money: MoneyHolder not found.");
                return;
            }

            holder.SetMoney(MaxMoneyValue);
            SetRewardStatus("Money set to " + MaxMoneyValue.ToString("0") + ".");
        }

        private void GiveMyMaxStars()
        {
            if (!PhotonNetwork.InRoom)
            {
                SetRewardStatus("Stars: not in a room.");
                return;
            }

            int current = ObjectiveManager.GetStars();
            int amount = MaxStarsValue - current;
            if (amount <= 0)
            {
                SetRewardStatus("Stars are already at max.");
                return;
            }

            ObjectiveManager.GiveStars(amount);
            SetRewardStatus("Stars set to " + MaxStarsValue + ".");
        }

        private void GiveAllMaxMoney()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
                return;

            Player[] players = PhotonNetwork.PlayerList;
            int changed = 0;

            for (int i = 0; i < players.Length; i++)
            {
                Player player = players[i];
                GameObject obj = FindPlayerObject(player);
                if (obj == null)
                    continue;

                MoneyHolder holder = obj.GetComponentInChildren<MoneyHolder>(true);
                if (holder == null || holder.photonView == null)
                    continue;

                if (holder.photonView.IsMine)
                {
                    holder.SetMoney(MaxMoneyValue);
                    changed++;
                    continue;
                }

                float current = holder.GetMoney();
                float add = MaxMoneyValue - current;
                if (add > 0f)
                {
                    holder.photonView.RPC("AddMoney", holder.photonView.Owner, add);
                    changed++;
                }
            }

            SetRewardStatus("Max money requested for " + changed + " player(s).");
        }

        private void StartGiveAllMaxStars()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
                return;

            StartCoroutine(GiveAllMaxStarsRoutine());
        }

        private IEnumerator GiveAllMaxStarsRoutine()
        {
            Player[] players = PhotonNetwork.PlayerList;
            int requested = 0;
            int skipped = 0;

            // ObjectiveManager.GiveStars() only works for the PhotonView owner.
            // Temporarily request ownership where possible, apply the reward,
            // then return ownership to the original player.

            for (int i = 0; i < players.Length; i++)
            {
                Player player = players[i];

                GameObject obj =
                    FindPlayerObject(player);

                if (obj == null)
                {
                    skipped++;
                    continue;
                }

                ObjectiveManager objective =
                    obj.GetComponentInChildren<ObjectiveManager>(true);

                if (objective == null ||
                    objective.photonView == null)
                {
                    skipped++;
                    continue;
                }

                PhotonView view =
                    objective.photonView;

                Player originalOwner =
                    view.Owner;

                // Already ours.
                if (view.IsMine)
                {
                    int current =
                        ObjectiveManager.GetStars();

                    int amount =
                        MaxStarsValue - current;

                    if (amount > 0)
                        ObjectiveManager.GiveStars(amount);

                    requested++;
                    continue;
                }

                bool transferRequested = false;

                try
                {
                    view.TransferOwnership(
                        PhotonNetwork.LocalPlayer
                    );

                    transferRequested = true;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(
                        "Give All Max Stars: ownership transfer failed: " +
                        ex.Message
                    );

                    skipped++;
                    continue;
                }

                // Wait for Photon to process the ownership change.
                if (transferRequested)
                    yield return null;

                // Check ownership AFTER the yield.
                if (!view.IsMine)
                {
                    skipped++;
                    continue;
                }

                int ownedCurrent =
                    GetStarsFromObjective(objective);

                int ownedAmount =
                    MaxStarsValue - ownedCurrent;

                if (ownedAmount > 0)
                    ObjectiveManager.GiveStars(
                        ownedAmount
                    );

                requested++;

                // Return ownership to the original owner.
                if (originalOwner != null &&
                    originalOwner != PhotonNetwork.LocalPlayer)
                {
                    try
                    {
                        view.TransferOwnership(
                            originalOwner
                        );
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(
                            "Give All Max Stars: failed to restore ownership: " +
                            ex.Message
                        );
                    }

                    // Yield OUTSIDE the try/catch.
                    yield return null;
                }
            }

            SetRewardStatus(
                "Max stars requested for " +
                requested +
                " player(s); skipped " +
                skipped +
                "."
            );
        }

        private int GetStarsFromObjective(ObjectiveManager objective)
        {
            if (objective == null)
                return 0;

            MethodInfo getStars = typeof(ObjectiveManager).GetMethod(
                "GetStars",
                BindingFlags.Public | BindingFlags.Static);

            if (getStars != null)
            {
                try
                {
                    object value = getStars.Invoke(null, null);
                    if (value is int)
                        return (int)value;
                }
                catch { }
            }

            FieldInfo starsField = typeof(ObjectiveManager).GetField(
                "stars",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (starsField != null && starsField.FieldType == typeof(int))
            {
                try
                {
                    return (int)starsField.GetValue(objective);
                }
                catch { }
            }

            return 0;
        }

        // ============================================================
        // TESTING HELPERS — offline host / ownership + world ping
        // ============================================================
        /// <summary>
        /// Solo offline room so you are always Master Client. Good for testing host tools.
        /// </summary>
        private void ForceOfflineSoloHost()
        {
            try
            {
                if (PhotonNetwork.InRoom)
                {
                    try { LeaveRoomSafe(); } catch { }
                }

                PhotonNetwork.OfflineMode = true;

                // OfflineMode auto-connects a fake local server; create/join a room.
                if (!PhotonNetwork.InRoom)
                {
                    RoomOptions opts = new RoomOptions
                    {
                        MaxPlayers = 8,
                        IsVisible = false,
                        IsOpen = true
                    };
                    PhotonNetwork.CreateRoom("ZexQoL_Offline_" + UnityEngine.Random.Range(1000, 9999), opts, TypedLobby.Default);
                }

                ownershipStatus = "OfflineMode ON · InRoom=" + PhotonNetwork.InRoom +
                                  " · Master=" + PhotonNetwork.IsMasterClient +
                                  " · (host tools should unlock)";
                ownershipStatusUntil = Time.unscaledTime + 8f;
                Logger.LogInfo("ForceOfflineSoloHost: " + ownershipStatus);
            }
            catch (Exception ex)
            {
                ownershipStatus = "Offline host failed: " + ex.Message;
                ownershipStatusUntil = Time.unscaledTime + 6f;
                Logger.LogWarning("ForceOfflineSoloHost: " + ex);
            }
        }

        /// <summary>
        /// Try to become Master Client. Works reliably offline / when already alone;
        /// online only the current master can transfer (Photon rule).
        /// </summary>
        private void TryClaimMasterClient()
        {
            try
            {
                if (!PhotonNetwork.InRoom)
                {
                    ownershipStatus = "Not in a room — use FORCE OFFLINE + ROOM first.";
                    ownershipStatusUntil = Time.unscaledTime + 5f;
                    return;
                }

                if (PhotonNetwork.IsMasterClient)
                {
                    ownershipStatus = "Already Master Client.";
                    ownershipStatusUntil = Time.unscaledTime + 4f;
                    return;
                }

                // Offline / single-player style rooms: this usually works.
                // Online multiplayer: only current master can SetMasterClient.
                bool ok = PhotonNetwork.SetMasterClient(PhotonNetwork.LocalPlayer);
                ownershipStatus = ok
                    ? ("SetMasterClient sent · now Master=" + PhotonNetwork.IsMasterClient)
                    : ("SetMasterClient returned false · still Master=" + PhotonNetwork.IsMasterClient +
                       " (online: only current host can transfer)");
                ownershipStatusUntil = Time.unscaledTime + 6f;
                Logger.LogInfo("TryClaimMasterClient: ok=" + ok + " isMaster=" + PhotonNetwork.IsMasterClient);
            }
            catch (Exception ex)
            {
                ownershipStatus = "Claim master error: " + ex.Message;
                ownershipStatusUntil = Time.unscaledTime + 5f;
                Logger.LogWarning("TryClaimMasterClient: " + ex);
            }
        }

        private void RequestLocalKoboldOwnership()
        {
            try
            {
                Component kob = FindLocalKobold();
                if (kob == null)
                {
                    ownershipStatus = "No local Kobold found.";
                    ownershipStatusUntil = Time.unscaledTime + 4f;
                    return;
                }

                PhotonView pv = kob.GetComponent<PhotonView>() ?? kob.GetComponentInParent<PhotonView>();
                if (pv == null)
                {
                    ownershipStatus = "Kobold has no PhotonView.";
                    ownershipStatusUntil = Time.unscaledTime + 4f;
                    return;
                }

                int before = pv.OwnerActorNr;
                bool wasMine = pv.IsMine;
                pv.RequestOwnership();

                ownershipStatus = "RequestOwnership · view=" + pv.ViewID +
                                  " wasMine=" + wasMine +
                                  " owner=" + before +
                                  " local=" + (PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1);
                ownershipStatusUntil = Time.unscaledTime + 6f;
                Logger.LogInfo("RequestLocalKoboldOwnership: ViewID=" + pv.ViewID +
                               " wasMine=" + wasMine + " owner=" + before);
            }
            catch (Exception ex)
            {
                ownershipStatus = "Ownership error: " + ex.Message;
                ownershipStatusUntil = Time.unscaledTime + 5f;
                Logger.LogWarning("RequestLocalKoboldOwnership: " + ex);
            }
        }

        /// <summary>
        /// Request ownership on every PhotonView that currently reports IsMine or is on local kobold tree.
        /// </summary>
        private void RequestOwnershipAllLocalViews()
        {
            try
            {
                int n = 0;
                PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
                if (views != null)
                {
                    for (int i = 0; i < views.Length; i++)
                    {
                        PhotonView pv = views[i];
                        if (pv == null) continue;
                        // Local-ish: already mine, or no owner, or on our kobold
                        bool localish = pv.IsMine;
                        if (!localish && PhotonNetwork.LocalPlayer != null &&
                            pv.OwnerActorNr == PhotonNetwork.LocalPlayer.ActorNumber)
                            localish = true;
                        if (!localish)
                        {
                            Component kob = GetKoboldOn(pv.gameObject);
                            if (kob != null)
                            {
                                Component mine = FindLocalKobold();
                                if (mine != null && (kob == mine || kob.transform.IsChildOf(mine.transform) ||
                                    mine.transform.IsChildOf(kob.transform)))
                                    localish = true;
                            }
                        }
                        if (!localish) continue;
                        pv.RequestOwnership();
                        n++;
                    }
                }

                ownershipStatus = "Requested ownership on " + n + " local-ish views · Master=" +
                                  PhotonNetwork.IsMasterClient;
                ownershipStatusUntil = Time.unscaledTime + 6f;
                Logger.LogInfo("RequestOwnershipAllLocalViews: " + n);
            }
            catch (Exception ex)
            {
                ownershipStatus = "Own-all error: " + ex.Message;
                ownershipStatusUntil = Time.unscaledTime + 5f;
            }
        }

        private void PlacePingMark()
        {
            Vector3 pos;
            GameObject local = GetLocalPlayer();
            if (local != null)
                pos = local.transform.position;
            else if (Camera.main != null)
                pos = Camera.main.transform.position;
            else
            {
                ownershipStatus = "Ping failed: no position.";
                ownershipStatusUntil = Time.unscaledTime + 3f;
                return;
            }

            pingMarkWorld = pos;
            pingMarkUntil = Time.unscaledTime + PingMarkDuration;
            pingMarkActive = true;
            Logger.LogInfo("Ping mark @ " + pos);
        }

        private void DrawPingMark()
        {
            if (!pingMarkActive)
                return;

            if (Time.unscaledTime > pingMarkUntil)
            {
                pingMarkActive = false;
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
                return;

            // Vertical beam in world → screen (ground to head height)
            Vector3 baseW = pingMarkWorld;
            Vector3 topW = pingMarkWorld + Vector3.up * 2.2f;
            Vector3 baseS = cam.WorldToScreenPoint(baseW);
            Vector3 topS = cam.WorldToScreenPoint(topW);

            if (baseS.z <= 0f && topS.z <= 0f)
                return;

            Vector2 baseGui = new Vector2(baseS.x, Screen.height - baseS.y);
            Vector2 topGui = new Vector2(topS.x, Screen.height - topS.y);

            float t = 1f - Mathf.Clamp01((pingMarkUntil - Time.unscaledTime) / PingMarkDuration);
            // pulse alpha
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
            Color col = new Color(1f, 0.85f, 0.15f, pulse);

            if (baseS.z > 0f && topS.z > 0f)
                DrawTracerLine(baseGui, topGui, col);

            if (topS.z > 0f)
            {
                // Cross at tip
                float arm = 10f;
                DrawTracerLine(topGui + new Vector2(-arm, 0f), topGui + new Vector2(arm, 0f), col);
                DrawTracerLine(topGui + new Vector2(0f, -arm), topGui + new Vector2(0f, arm), col);

                float left = Mathf.Max(0f, pingMarkUntil - Time.unscaledTime);
                string label = "PING  " + left.ToString("0.0") + "s";
                GUIStyle st = smallStyle != null ? smallStyle : GUI.skin.label;
                Vector2 sz = st.CalcSize(new GUIContent(label));
                GUI.color = col;
                GUI.Label(new Rect(topGui.x - sz.x * 0.5f, topGui.y - sz.y - 4f, sz.x, sz.y), label, st);
                GUI.color = Color.white;
            }
        }

        // ============================================================

        // ============================================================
        // HOST: BRING / FREEZE
        // ============================================================
        private void BringPlayerToMe(Player target)
        {
            if (target == null || target.IsLocal) return;
            if (!PhotonNetwork.IsMasterClient) return;

            GameObject me = GetLocalPlayer();
            GameObject them = FindPlayerObject(target);
            if (me == null || them == null) return;

            Vector3 dest = me.transform.position + me.transform.forward * 1.5f;
            ApplyTeleportToBody(them, dest, them.transform.rotation);
            AddRecentPlayerEvent("BROUGHT: " + GetPlayerName(target) + " → me");
        }

        private void BringAllPlayersToMe()
        {
            if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
            Player[] players = PhotonNetwork.PlayerList;
            if (players == null) return;
            for (int i = 0; i < players.Length; i++)
            {
                Player p = players[i];
                if (p == null || p.IsLocal) continue;
                BringPlayerToMe(p);
            }
            AddRecentPlayerEvent("BROUGHT ALL to host");
        }

        private void ToggleFreezePlayer(Player target)
        {
            if (target == null || target.IsLocal) return;
            int id = target.ActorNumber;
            if (frozenActorIds.Contains(id))
            {
                frozenActorIds.Remove(id);
                frozenPlayerPositions.Remove(id);
                AddRecentPlayerEvent("UNFROZE: " + GetPlayerName(target));
            }
            else
            {
                GameObject obj = FindPlayerObject(target);
                if (obj == null) return;
                frozenActorIds.Add(id);
                frozenPlayerPositions[id] = obj.transform.position;
                AddRecentPlayerEvent("FROZE: " + GetPlayerName(target));
            }
        }

        private void UpdateFrozenPlayers()
        {
            if (frozenActorIds.Count == 0) return;
            if (!PhotonNetwork.InRoom)
            {
                frozenActorIds.Clear();
                frozenPlayerPositions.Clear();
                return;
            }

            // Host (or anyone who can see the object) keeps shoving them back
            List<int> ids = new List<int>(frozenActorIds);
            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                Player p = GetPlayerByActorId(id);
                if (p == null)
                {
                    frozenActorIds.Remove(id);
                    frozenPlayerPositions.Remove(id);
                    continue;
                }

                GameObject obj = FindPlayerObject(p);
                if (obj == null) continue;

                Vector3 locked;
                if (!frozenPlayerPositions.TryGetValue(id, out locked))
                {
                    locked = obj.transform.position;
                    frozenPlayerPositions[id] = locked;
                }

                if ((obj.transform.position - locked).sqrMagnitude > 0.01f)
                    ApplyTeleportToBody(obj, locked, obj.transform.rotation);
            }
        }

        private bool IsPlayerFrozen(Player p)
        {
            return p != null && frozenActorIds.Contains(p.ActorNumber);
        }
    }
}
