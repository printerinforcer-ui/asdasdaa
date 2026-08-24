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
        private void PatchOrbitCamera()
        {
            try
            {
                Type orbitCameraType =
                    SafeGameType("OrbitCamera");

                if (orbitCameraType == null)
                {
                    Logger.LogWarning(
                        "Spectate: OrbitCamera type was not found."
                    );
                    return;
                }

                MethodInfo lateUpdate =
                    AccessTools.Method(
                        orbitCameraType,
                        "LateUpdate"
                    );

                if (lateUpdate == null)
                {
                    Logger.LogWarning(
                        "Spectate: OrbitCamera.LateUpdate was not found."
                    );
                    return;
                }

                spectateHarmony.Patch(
                    lateUpdate,
                    new HarmonyMethod(
                        typeof(Plugin),
                        nameof(OrbitCameraLateUpdatePrefix)
                    )
                );

                Logger.LogInfo(
                    "Spectate: OrbitCamera.LateUpdate patched."
                );
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    "Spectate patch failed: " + ex
                );
            }
        }

        /// <summary>
        /// Ensure ZexQoLPlayers is in CustomRoomProperties + CustomRoomPropertiesForLobby
        /// so lobby RoomInfo can carry the host-published name list.
        /// </summary>

        private static bool OrbitCameraLateUpdatePrefix()
        {
            if (Instance == null)
                return true;

            // Only spectate owns the camera. Fly uses orbit look direction (CharCon-style).
            return !Instance.spectating;
        }

        /// <summary>
        /// Swallow Photon custom events that drive the mod handshake while scanning.
        /// DeepWiki: host RaiseEvent mod list → client compares → leave/download/rejoin.
        /// Event code is game-defined; we block common codes and string payloads that look like mod lists.
        /// </summary>

        private void SpectateCycleFromHotkey(int direction)
        {
            if (!PhotonNetwork.InRoom)
            {
                ShowToast("Not in a room");
                return;
            }
            if (!spectating)
            {
                // Pick first remote player
                Player[] list = PhotonNetwork.PlayerList;
                if (list == null) return;
                Player pick = null;
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] != null && !list[i].IsLocal)
                    {
                        pick = list[i];
                        break;
                    }
                }
                if (pick == null)
                {
                    ShowToast("No one to spectate");
                    return;
                }
                selectedPlayer = pick;
                selectedActorId = pick.ActorNumber;
                StartSpectating();
                if (spectating)
                    ShowToast("Spectate: " + (pick.NickName ?? ("#" + pick.ActorNumber)));
                return;
            }
            CycleSpectatePlayer(direction);
            if (selectedPlayer != null)
                ShowToast("Spectate: " + (selectedPlayer.NickName ?? ("#" + selectedPlayer.ActorNumber)));
        }

        // Replace DrawPlayerOverlay() with this right-aligned, spacer-free layout
        private void DrawPlayerOverlay()
        {
            Player[] players = PhotonNetwork.InRoom ? PhotonNetwork.PlayerList : new Player[0];
            Room currentRoom = PhotonNetwork.CurrentRoom;
            string serverName = currentRoom != null ? (string.IsNullOrEmpty(currentRoom.Name) ? "Unnamed Room" : currentRoom.Name) : "Not connected";

            Color prevOverlayColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f);

            float pad = 6f;
            float headerH = 20f;
            float serverLineH = 16;
            float lineH = 20f;
            float footer = 6f;
            float desiredH = pad * 2f + headerH + serverLineH + 6f + players.Length * lineH + footer;
            float maxH = Screen.height - 20f;
            bool needsScroll = desiredH > maxH;
            float drawH = needsScroll ? maxH : desiredH;

            float overlayW = playerOverlayRect.width;
            float rightMargin = 0f;
            float overlayX = Mathf.Clamp(Screen.width - overlayW - rightMargin, 2f, Screen.width - 2f);
            float overlayY = playerOverlayRect.y;

            if (!showPlayerOverlay)
            {
                float collapsedW = 92f;
                Rect collapsedRect = new Rect(overlayX + overlayW - collapsedW - 1f, overlayY, collapsedW, 22f);
                if (GUI.Button(collapsedRect, new GUIContent("Players"), buttonStyle))
                    showPlayerOverlay = true;
                GUI.color = prevOverlayColor;
                return;
            }

            // close button reserves area on the far right so nothing draws under it
            float closeW = 16f;
            float closeGap = 4f;
            float usableRight = overlayX + overlayW - pad - closeW - closeGap;
            float nameX = overlayX + pad;
            float contentW = Mathf.Max(40f, usableRight - nameX);

            float y = overlayY + pad;

            GUI.Label(new Rect(nameX, y, contentW, headerH), new GUIContent("Players"), overlayHeaderStyle);
            y += headerH;

            string countLine = players.Length + (currentRoom != null && currentRoom.MaxPlayers > 0 ? "/" + currentRoom.MaxPlayers : "");

            GUIContent gcServer = new GUIContent(serverName);
            gcServer.tooltip = serverName;
            GUI.Label(new Rect(nameX, y, contentW - 40f, serverLineH * 2f), gcServer, overlayServerStyle);
            GUI.Label(new Rect(usableRight - 40f, y, 40f, serverLineH), new GUIContent(countLine), overlayInfoStyle);

            y += serverLineH * 2f + 4f;

            if (!needsScroll) playerOverlayScroll.y = 0f;
            float listH = drawH - (y - overlayY) - footer;
            Rect listScreenRect = new Rect(overlayX, y, overlayW, listH);
            Event ev = Event.current;
            if (ev != null && ev.type == EventType.ScrollWheel && listScreenRect.Contains(ev.mousePosition))
            {
                float contentHeight = players.Length * lineH;
                playerOverlayScroll.y = Mathf.Clamp(playerOverlayScroll.y + ev.delta.y * 25f, 0f, Mathf.Max(0f, contentHeight - listH));
                ev.Use();
            }

            float startY = y - playerOverlayScroll.y;
            Player master = PhotonNetwork.InRoom ? PhotonNetwork.MasterClient : null;

            // Single compact line: [Stop?] NAME | HOST | #ACTOR
            // While spectating: left-click a name to switch target; Stop button beside the current target
            for (int i = 0; i < players.Length; i++)
            {
                Player p = players[i];
                if (p == null) continue;

                string name = string.IsNullOrEmpty(p.NickName) ? "Player " + p.ActorNumber : p.NickName;
                bool isHost = master != null && p.ActorNumber == master.ActorNumber;
                bool isSpecTarget = spectating && spectateActorId == p.ActorNumber;
                bool isFriend = friendActorIds != null && friendActorIds.Contains(p.ActorNumber);
                bool isZex = PlayerHasZexClient(p);
                string colorTag;
                if (isSpecTarget) colorTag = "#ffcc66";
                else if (isFriend)
                {
                    Color fc = espColorOptions[Mathf.Clamp(friendColorIndex, 0, espColorOptions.Length - 1)];
                    colorTag = "#" +
                        ((int)(fc.r * 255f)).ToString("X2") +
                        ((int)(fc.g * 255f)).ToString("X2") +
                        ((int)(fc.b * 255f)).ToString("X2");
                }
                else if (isHost) colorTag = "#ff6666";
                else if (p.IsLocal) colorTag = "#66ccff";
                else if (isZex) colorTag = "#c084fc";
                else colorTag = "#d9d9d9";
                string hostPart = isHost ? "HOST" : (isFriend ? "FRIEND" : (isZex ? "˚ʚ♡ɞ˚" : "-"));
                string line = $"<color={colorTag}>{name}</color>  |  {hostPart}  |  #{p.ActorNumber}";

                float rowY = startY + i * lineH;
                float stopW = 56f;
                float stopGap = 4f;
                Rect stopRect = new Rect(nameX, rowY + 1f, stopW, lineH - 2f);
                Rect nameRect = isSpecTarget
                    ? new Rect(nameX + stopW + stopGap, rowY, contentW - stopW - stopGap, lineH)
                    : new Rect(nameX, rowY, contentW, lineH);
                Rect rowHit = new Rect(nameX, rowY, contentW, lineH);

                if (isSpecTarget)
                {
                    if (GUI.Button(stopRect, new GUIContent("Stop"), buttonStyle))
                        StopSpectating();
                }

                Event rowEvent = Event.current;
                if (rowEvent != null && rowEvent.type == EventType.MouseDown && rowHit.Contains(rowEvent.mousePosition))
                {
                    // Don't steal clicks from the Stop button
                    if (isSpecTarget && stopRect.Contains(rowEvent.mousePosition))
                    {
                        // button handles it
                    }
                    else if (rowEvent.button == 1)
                    {
                        SelectPlayer(p, force: true);
                        contextPlayer = p;
                        playerContextMenuPosition = rowEvent.mousePosition;
                        playerContextMenuVisible = true;
                        rowEvent.Use();
                    }
                    else if (rowEvent.button == 0)
                    {
                        // Left-click: select; if already spectating, switch to this player
                        SelectPlayer(p, force: true);
                        if (spectating && !p.IsLocal)
                            StartSpectating();
                        rowEvent.Use();
                    }
                }

                GUI.Label(nameRect, new GUIContent(line), overlayPlayerStyle);
            }

            if (GUI.Button(new Rect(overlayX + overlayW - closeW - closeGap, overlayY + 2f, closeW, closeW), new GUIContent("X"), buttonStyle))
                showPlayerOverlay = false;

            GUI.color = prevOverlayColor;
        }

        // ============================================================
        // PLAYER CONTEXT MENU
        // ============================================================
        private void DrawPlayerContextMenu()
        {
            if (!playerContextMenuVisible || contextPlayer == null)
                return;

            const float w = 200f;
            const float pad = 8f;
            const float gap = 4f;
            const float bh = 26f;
            const float headerH = 36f;

            bool isHostBtn = PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient &&
                             contextPlayer != null && !contextPlayer.IsLocal;
            bool canMessage = contextPlayer != null && !contextPlayer.IsLocal;
            // Spectate + tele + follow + friend + note + message + lock (+ kick/bring/freeze)
            int rows = 7 + (isHostBtn ? 3 : 0);
            float h = headerH + pad + rows * (bh + gap) + pad;

            float x = Mathf.Clamp(playerContextMenuPosition.x, 4f, Screen.width - w - 4f);
            float y = Mathf.Clamp(playerContextMenuPosition.y, 4f, Screen.height - h - 4f);

            // Card background
            BeginCardUI();
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, GUI.skin.box);
            EndUIColor();

            // Accent header strip
            BeginAccentUI();
            GUI.Box(new Rect(x, y, w, headerH), GUIContent.none, GUI.skin.box);
            EndUIColor();

            // Support Unity rich-text nicknames (<color>, <b>, etc.) like the player list
            string pname = GetPlayerName(contextPlayer) ?? "";
            string plainName = StripRichText(pname);
            if (plainName.Length > 18)
            {
                // Don't cut mid-tag — fall back to plain truncated text
                pname = plainName.Substring(0, 16) + "…";
            }

            string role = contextPlayer.IsMasterClient ? "HOST" : (contextPlayer.IsLocal ? "YOU" : "");
            string sub = "#" + contextPlayer.ActorNumber + (string.IsNullOrEmpty(role) ? "" : " · " + role);

            GUIStyle nameStyle = overlayPlayerStyle != null ? new GUIStyle(overlayPlayerStyle) : new GUIStyle(labelStyle);
            nameStyle.richText = true;
            nameStyle.alignment = TextAnchor.MiddleLeft;
            nameStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(x + pad, y + 3f, w - pad * 2f - 22f, 18f),
                new GUIContent(pname), nameStyle);
            GUI.Label(new Rect(x + pad, y + 18f, w - pad * 2f - 22f, 14f),
                new GUIContent(sub), smallStyle != null ? smallStyle : GUI.skin.label);

            // Close
            if (GUI.Button(new Rect(x + w - 26f, y + 6f, 20f, 20f), new GUIContent("×"), buttonStyle))
            {
                playerContextMenuVisible = false;
                return;
            }

            float by = y + headerH + pad;
            float fullW = w - pad * 2f;

            // Spectate — stop/switch live on the player list (Stop button + left-click name)
            bool isSpecThis = spectating && spectateActorId == contextPlayer.ActorNumber;
            if (GUI.Button(new Rect(x + pad, by, fullW, bh),
                new GUIContent(isSpecThis ? "Spectating…" : "Spectate"), buttonStyle))
            {
                if (!isSpecThis)
                {
                    SelectPlayer(contextPlayer, force: true);
                    StartSpectating();
                }
                playerContextMenuVisible = false;
            }
            by += bh + gap;

            // Teleport trio
            float tw = (fullW - gap * 2f) / 3f;
            if (GUI.Button(new Rect(x + pad, by, tw, bh), new GUIContent("Behind"), buttonStyle))
            {
                SelectPlayer(contextPlayer);
                TeleportBehindTarget();
                playerContextMenuVisible = false;
            }
            if (GUI.Button(new Rect(x + pad + tw + gap, by, tw, bh), new GUIContent("Front"), buttonStyle))
            {
                SelectPlayer(contextPlayer);
                TeleportInFrontOfTarget();
                playerContextMenuVisible = false;
            }
            if (GUI.Button(new Rect(x + pad + (tw + gap) * 2f, by, tw, bh), new GUIContent("Above"), buttonStyle))
            {
                SelectPlayer(contextPlayer);
                TeleportAboveTarget();
                playerContextMenuVisible = false;
            }
            by += bh + gap;

            // Follow
            bool following = followPlayerActorId == contextPlayer.ActorNumber;
            if (GUI.Button(new Rect(x + pad, by, fullW, bh),
                new GUIContent(following ? "Stop follow" : "Follow"), buttonStyle))
            {
                followPlayerActorId = following ? -1 : contextPlayer.ActorNumber;
                playerContextMenuVisible = false;
            }
            by += bh + gap;

            // Friend
            bool isFriend = friendActorIds.Contains(contextPlayer.ActorNumber);
            if (GUI.Button(new Rect(x + pad, by, fullW, bh),
                new GUIContent(isFriend ? "Unfriend" : "Add friend"), buttonStyle))
            {
                if (isFriend) friendActorIds.Remove(contextPlayer.ActorNumber);
                else friendActorIds.Add(contextPlayer.ActorNumber);
                playerContextMenuVisible = false;
            }
            by += bh + gap;

            // Note
            if (GUI.Button(new Rect(x + pad, by, fullW, bh), new GUIContent("Edit note"), buttonStyle))
            {
                SelectPlayer(contextPlayer);
                tab = 4;
                menuVisible = true;
                playerContextMenuVisible = false;
            }
            by += bh + gap;

            // Private message (˚ʚ♡ɞ˚ clients)
            if (canMessage)
            {
                bool hasZex = PlayerHasZexClient(contextPlayer);
                string msgLabel = hasZex ? "Message ˚ʚ♡ɞ˚" : "Message (needs client)";
                if (GUI.Button(new Rect(x + pad, by, fullW, bh), new GUIContent(msgLabel), buttonStyle))
                {
                    OpenPmWithPlayer(contextPlayer);
                    playerContextMenuVisible = false;
                }
                by += bh + gap;
            }

            // Host actions
            if (isHostBtn)
            {
                if (GUI.Button(new Rect(x + pad, by, fullW, bh), new GUIContent("Kick"), buttonStyle))
                {
                    pendingKickPlayer = contextPlayer;
                    ConfirmKickPlayer();
                    playerContextMenuVisible = false;
                }
                by += bh + gap;

                if (GUI.Button(new Rect(x + pad, by, fullW, bh), new GUIContent("Bring to me"), buttonStyle))
                {
                    BringPlayerToMe(contextPlayer);
                    playerContextMenuVisible = false;
                }
                by += bh + gap;

                bool frozenCtx = IsPlayerFrozen(contextPlayer);
                if (GUI.Button(new Rect(x + pad, by, fullW, bh),
                    new GUIContent(frozenCtx ? "Unfreeze" : "Freeze"), buttonStyle))
                {
                    ToggleFreezePlayer(contextPlayer);
                    playerContextMenuVisible = false;
                }
                by += bh + gap;
            }

            // Lock
            bool locked = targetLocked && selectedActorId == contextPlayer.ActorNumber;
            if (GUI.Button(new Rect(x + pad, by, fullW, bh),
                new GUIContent(locked ? "Unlock target" : "Lock target"), buttonStyle))
            {
                SelectPlayer(contextPlayer);
                targetLocked = !locked;
                playerContextMenuVisible = false;
            }

            // Click outside closes
            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0)
            {
                Rect r = new Rect(x, y, w, h);
                if (!r.Contains(e.mousePosition))
                {
                    playerContextMenuVisible = false;
                    e.Use();
                }
            }
        }

        // ============================================================
        // PLAYER RADAR (rounded, no title / no +/-)
        // ============================================================
        private void DrawPlayerRadar()
        {
            if (!PhotonNetwork.InRoom)
                return;

            if (!showPlayerRadar)
            {
                ClampRadar();

                float collapsedW = 72f;
                float collapsedH = 22f;

                Rect collapsedRect = new Rect(
                    playerRadarRect.x,
                    playerRadarRect.y,
                    collapsedW,
                    collapsedH
                );

                if (GUI.Button(
                    collapsedRect,
                    new GUIContent("Radar"),
                    buttonStyle))
                {
                    showPlayerRadar = true;
                }

                return;
            }

            playerRadarRect.width = Mathf.Clamp(playerRadarRect.width, RadarMinSize, RadarMaxSize);
            playerRadarRect.height = playerRadarRect.width;
            ClampRadar();

            // Transparent / chrome-free window so we can draw a circle ourselves.
            playerRadarRect = GUI.Window(
                9002,
                playerRadarRect,
                DrawPlayerRadarWindow,
                GUIContent.none,
                GUIStyle.none);
        }

        private void DrawPlayerRadarWindow(int id)
        {
            float size = playerRadarRect.width;
            float center = size * 0.5f;
            float radius = Mathf.Max(40f, center - 10f);

            // Circular background + ring
            float ro = Mathf.Clamp01(radarOpacity);
            Color discColor = GetMenuPanelColor(0.08f, ro);
            Color ringColor = GetMenuAccentColor(0.75f * ro + 0.25f);
            Color crossColor = GetMenuButtonTint(0.35f * ro);

            DrawFilledCircle(center, center, radius, discColor);
            DrawCircleOutline(center, center, radius, ringColor, 2.0f);
            DrawCircleOutline(center, center, radius * 0.66f, crossColor, 1.0f);
            DrawCircleOutline(center, center, radius * 0.33f, crossColor, 1.0f);

            // Crosshair
            // DrawThickLine(
            // new Vector2(center - radius, center),
            // new Vector2(center + radius, center),
            // crossColor,
            // 1f);
            // DrawThickLine(
            // new Vector2(center, center - radius),
            // new Vector2(center, center + radius),
            // crossColor,
            // 1f);

            // Local player indicator (center) — follows menu hue
            GUI.color = GetMenuAccentColor(1f);
            GUI.Label(new Rect(center - 8f, center - 10f, 16f, 20f),
                new GUIContent("▲"), overlayHeaderStyle);
            GUI.color = Color.white;

            GameObject local = GetLocalPlayer();
            if (local != null)
            {
                Camera cam = Camera.main;
                float yaw = radarRotateWithCamera && cam != null ? cam.transform.eulerAngles.y : 0f;
                Quaternion inverseYaw = Quaternion.Euler(0f, -yaw, 0f);
                Player[] players = PhotonNetwork.PlayerList;

                for (int i = 0; i < players.Length; i++)
                {
                    Player p = players[i];
                    if (p == null || p.IsLocal)
                        continue;
                    if (friendsOnlyMode && (friendActorIds == null || !friendActorIds.Contains(p.ActorNumber)))
                        continue;

                    GameObject obj = FindPlayerObject(p);
                    if (obj == null)
                        continue;

                    Vector3 worldOffset = obj.transform.position - local.transform.position;
                    float distance = new Vector2(worldOffset.x, worldOffset.z).magnitude;
                    if (distance > radarRange)
                        continue;

                    Vector3 relative = inverseYaw * worldOffset;
                    float px = center + (relative.x / radarRange) * radius;
                    float py = center - (relative.z / radarRange) * radius;

                    // Keep dots inside the circle
                    float dx = px - center;
                    float dy = py - center;
                    float distFromCenter = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distFromCenter > radius - 4f)
                    {
                        float s = (radius - 4f) / distFromCenter;
                        px = center + dx * s;
                        py = center + dy * s;
                    }

                    Color color = GetESPColor(p);
                    bool selected = p.ActorNumber == selectedActorId;
                    string dot = selected ? "◆" : "●";
                    GUI.color = color;

                    float dotSize = selected ? 18f : 14f;
                    Rect dotRect = new Rect(px - dotSize * 0.5f, py - dotSize * 0.5f, dotSize, dotSize);
                    if (GUI.Button(dotRect, new GUIContent(dot), GUI.skin.label))
                    {
                        SelectPlayer(p);
                        targetLocked = selected ? targetLocked : false;
                    }

                    GUI.color = Color.white;
                    if (radarShowNames)
                    {
                        string text = GetPlayerName(p);
                        if (radarShowDistance)
                            text += " " + distance.ToString("0") + "m";

                        GUIStyle radarTextStyle = new GUIStyle(overlayInfoStyle);
                        radarTextStyle.alignment = TextAnchor.MiddleLeft;

                        const float labelWidth = 90f;
                        float labelX = px + 8f;
                        if (labelX + labelWidth > size - 4f)
                            labelX = px - labelWidth - 8f;

                        GUI.Label(
                            new Rect(labelX, py - 8f, labelWidth, 20f),
                            new GUIContent(text),
                            radarTextStyle);
                    }
                    else if (radarShowDistance)
                    {
                        GUIStyle radarTextStyle = new GUIStyle(overlayInfoStyle);
                        radarTextStyle.alignment = TextAnchor.MiddleLeft;

                        const float labelWidth = 48f;
                        float labelX = px + 8f;
                        if (labelX + labelWidth > size - 4f)
                            labelX = px - labelWidth - 8f;

                        GUI.Label(
                            new Rect(labelX, py - 8f, labelWidth, 20f),
                            new GUIContent(distance.ToString("0") + "m"),
                            radarTextStyle);
                    }
                }
            }

            // Compact footer + close only (no title, no +/-)
            GUI.Label(
                new Rect(10f, size - 22f, size - 40f, 18f),
                new GUIContent((radarRotateWithCamera ? "DIST" : "N") + "  " + radarRange.ToString("0") + "m"),
                smallStyle);

            if (GUI.Button(new Rect(size - 26f, 4f, 20f, 18f), new GUIContent("X"), buttonStyle))
                showPlayerRadar = false;

            // Drag anywhere on the radar
            GUI.DragWindow(new Rect(0f, 0f, size, size));
        }

        private void DrawFilledCircle(float cx, float cy, float radius, Color color)
        {
            EnsureTracerMaterial();
            if (tracerMaterial == null)
                return;

            tracerMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);

            // Convert window-local coords to screen (window is already at playerRadarRect)
            float sx = playerRadarRect.x + cx;
            float sy = playerRadarRect.y + cy;

            GL.Begin(4);
            GL.Color(color);
            const int segments = 48;
            const float Pi = 3.14159265f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Pi * 2f;
                float a1 = ((i + 1) / (float)segments) * Pi * 2f;
                GL.Vertex3(sx, sy, 0f);
                GL.Vertex3(sx + Mathf.Cos(a0) * radius, sy + Mathf.Sin(a0) * radius, 0f);
                GL.Vertex3(sx + Mathf.Cos(a1) * radius, sy + Mathf.Sin(a1) * radius, 0f);
            }
            GL.End();
            GL.PopMatrix();
        }

        private void DrawCircleOutline(float cx, float cy, float radius, Color color, float thickness)
        {
            EnsureTracerMaterial();
            if (tracerMaterial == null)
                return;

            float sx = playerRadarRect.x + cx;
            float sy = playerRadarRect.y + cy;

            const int segments = 48;
            const float Pi = 3.14159265f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Pi * 2f;
                float a1 = ((i + 1) / (float)segments) * Pi * 2f;
                Vector2 p0 = new Vector2(sx + Mathf.Cos(a0) * radius, sy + Mathf.Sin(a0) * radius);
                Vector2 p1 = new Vector2(sx + Mathf.Cos(a1) * radius, sy + Mathf.Sin(a1) * radius);
                DrawThickLine(p0, p1, color, thickness);
            }
        }

        private void ClampRadar()
        {
            playerRadarRect.x = Mathf.Clamp(playerRadarRect.x, 2f, Mathf.Max(2f, Screen.width - playerRadarRect.width - 2f));
            playerRadarRect.y = Mathf.Clamp(playerRadarRect.y, 2f, Mathf.Max(2f, Screen.height - playerRadarRect.height - 2f));
            SaveUiLayoutToConfig();
        }

        private void UpdateFollowPlayer()
        {
            if (followPlayerActorId < 0 || !PhotonNetwork.InRoom)
                return;

            Player targetPlayer = GetPlayerByActorId(followPlayerActorId);
            GameObject local = GetLocalPlayer();
            GameObject target = targetPlayer == null ? null : FindPlayerObject(targetPlayer);

            if (targetPlayer == null || targetPlayer.IsLocal || local == null || target == null)
            {
                followPlayerActorId = -1;
                return;
            }

            Vector3 destination = target.transform.position - target.transform.forward * followDistance + Vector3.up * followHeight;
            Vector3 next = Vector3.Lerp(local.transform.position, destination, Mathf.Clamp01(Time.unscaledDeltaTime * 8f));
            TeleportLocalPlayer(next);
        }

        private readonly Dictionary<int, GameObject> playerObjectCache = new Dictionary<int, GameObject>();

        // ============================================================
        // SPECTATE / PLAYER SELECTION
        // ============================================================
        private Player GetPlayerByActorId(int actorId)
        {
            if (!PhotonNetwork.InRoom || actorId < 0) return null;
            Player[] players = PhotonNetwork.PlayerList;
            for (int i = 0; i < players.Length; i++)
                if (players[i] != null && players[i].ActorNumber == actorId) return players[i];
            return null;
        }

        private void SelectPlayer(Player p, bool force = false)
        {
            if (p == null) return;
            // targetLocked blocks casual selection; force=true for context-menu spectate switches
            if (!force && targetLocked && selectedActorId >= 0 && p.ActorNumber != selectedActorId)
                return;
            selectedActorId = p.ActorNumber;
            selectedPlayer = p;
            notesInputFocused = false;
            if (spectating) StartSpectating();
        }

        private void StartSpectating()
        {
            if (selectedPlayer == null || selectedPlayer.IsLocal) return;

            GameObject target = FindPlayerObject(selectedPlayer);
            Camera cam = Camera.main;
            // Only commit spectate state once we actually have a target
            if (target == null || cam == null) return;

            if (!cameraStateSaved)
            {
                savedCameraPosition = cam.transform.position;
                savedCameraRotation = cam.transform.rotation;
                cameraStateSaved = true;
            }

            spectateActorId = selectedPlayer.ActorNumber;
            spectateTarget = target.transform;
            spectating = true;
            sessionSpectateCount++;
        }

        private void StopSpectating()
        {
            Camera cam = Camera.main;
            if (cam != null && cameraStateSaved)
            {
                cam.transform.position = savedCameraPosition;
                cam.transform.rotation = savedCameraRotation;
            }
            spectating = false;
            spectateTarget = null;
            spectateActorId = -1;
            cameraStateSaved = false;
        }

        private void CycleSpectatePlayer(int direction)
        {
            if (!PhotonNetwork.InRoom) return;
            Player[] players = PhotonNetwork.PlayerList;
            if (players == null || players.Length == 0) return;

            int current = -1;
            for (int i = 0; i < players.Length; i++)
                if (players[i] != null && players[i].ActorNumber == selectedActorId) { current = i; break; }

            for (int step = 1; step <= players.Length; step++)
            {
                int index = current < 0 ? (direction > 0 ? 0 : players.Length - 1) :
                    (current + direction * step) % players.Length;
                if (index < 0) index += players.Length;
                Player p = players[index];
                if (p == null || p.IsLocal) continue;
                if (FindPlayerObject(p) == null) continue;
                SelectPlayer(p);
                return;
            }
        }

        // ============================================================
        // PLAYER NOTES
        // ============================================================
        private string GetPlayerNoteKey(Player player)
        {
            if (player == null)
                return null;

            return !string.IsNullOrEmpty(player.UserId) ? player.UserId : GetPlayerName(player);
        }

        private string GetPlayerName(Player player)
        {
            return player == null ? "Unknown" : (string.IsNullOrEmpty(player.NickName) ? "Player " + player.ActorNumber : player.NickName);
        }

        /// <summary>
        /// Strip Unity rich-text tags (&lt;color&gt;, &lt;b&gt;, …) for length checks / plain display.
        /// </summary>
        private static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>' && inTag) { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        private string GetPlayerNote(Player player)
        {
            string key = GetPlayerNoteKey(player);
            if (key == null)
                return "";

            string note;
            return playerNotes.TryGetValue(key, out note) ? note : "";
        }

        private void SetPlayerNote(Player player, string note)
        {
            string key = GetPlayerNoteKey(player);
            if (key == null)
                return;

            if (string.IsNullOrEmpty(note))
                playerNotes.Remove(key);
            else
                playerNotes[key] = note;
        }

        // ============================================================
    }
}