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
        private void AdjustFlySpeed(float delta)
        {
            flySpeed = Mathf.Clamp(flySpeed + delta, 5f, 500f);
            if (configFlySpeed != null)
                configFlySpeed.Value = flySpeed;
            flyDebugStatus = "FLY SPD " + flySpeed.ToString("0");
        }

        // ============================================================
        // TELEPORT UI / LOGIC
        // ============================================================
        private void DrawTeleportPanel(float x, float y, float width, float maxHeight)
        {
            float leftWidth = width * .46f;
            float rightX = x + leftWidth + 12f;
            float rightWidth = width - leftWidth - 12f;

            GUI.Label(new Rect(x, y, leftWidth, 24f), new GUIContent("Players"), headerStyle);
            GUI.Label(new Rect(rightX, y, rightWidth - 130f, 24f), new GUIContent("Teleport"), headerStyle);

            string wpBtnLabel = waypointsPopupVisible
                ? "CLOSE WAYPOINTS"
                : ("WAYPOINTS (" + savedWaypoints.Count + ")");
            if (GUI.Button(new Rect(rightX + rightWidth - 128f, y - 2f, 128f, 26f),
                new GUIContent(wpBtnLabel), buttonStyle))
            {
                waypointsPopupVisible = !waypointsPopupVisible;
                if (waypointsPopupVisible)
                {
                    waypointsPopupRect.x = menuRect.xMax + 12f;
                    waypointsPopupRect.y = menuRect.y + 80f;
                    if (waypointsPopupRect.xMax > Screen.width - 8f)
                        waypointsPopupRect.x = Mathf.Max(8f, menuRect.x - waypointsPopupRect.width - 12f);
                }
            }

            y += 30f;

            // Cap height so empty box doesn't stretch into the status bar
            float listH = Mathf.Min(Mathf.Max(180f, maxHeight - 30f), 500f);
            DrawPlayerList(x, y, leftWidth, listH);
            DrawTeleportControls(rightX, y, rightWidth, listH);
        }


        private void DrawHudWaypoints()
        {
            if (!showHudWaypoints)
                return;
            if (savedWaypoints == null || savedWaypoints.Count == 0)
                return;

            const float btnW = 100f;
            const float btnH = 22f;
            const float pad = 6f;
            float totalW = savedWaypoints.Count * (btnW + 4f) + pad * 2f;
            float x = pad;
            float y = Screen.height - btnH - pad - 8f;
            if (y < 4f) y = 4f;

            // KK IMGUI has no GUI.DrawTexture — use Box for a simple bar backdrop
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.85f);
            GUI.Box(new Rect(x - 2f, y - 2f, totalW, btnH + 4f), "");
            GUI.backgroundColor = prevBg;

            int i = 0;
            foreach (var kv in savedWaypoints)
            {
                string name = kv.Key;
                string label = name;
                if (label.Length > 12) label = label.Substring(0, 11) + "…";
                Rect r = new Rect(x + pad + i * (btnW + 4f), y, btnW, btnH);
                if (GUI.Button(r, new GUIContent(label), buttonStyle != null ? buttonStyle : GUI.skin.button))
                {
                    TeleportToWaypoint(name);
                    ShowToast("TP → " + name);
                }
                i++;
                if (i >= 8) break; // cap HUD clutter
            }
        }

        private void DrawWaypointsPopup()
        {
            if (!waypointsPopupVisible)
                return;

            waypointsPopupRect.width = Mathf.Clamp(waypointsPopupRect.width, 280f, 480f);
            waypointsPopupRect.height = Mathf.Clamp(waypointsPopupRect.height, 280f, 620f);
            waypointsPopupRect.x = Mathf.Clamp(waypointsPopupRect.x, 4f, Mathf.Max(4f, Screen.width - waypointsPopupRect.width - 4f));
            waypointsPopupRect.y = Mathf.Clamp(waypointsPopupRect.y, 4f, Mathf.Max(4f, Screen.height - waypointsPopupRect.height - 4f));

            waypointsPopupRect = GUI.Window(
                9011,
                waypointsPopupRect,
                DrawWaypointsPopupWindow,
                GUIContent.none,
                windowStyle != null ? windowStyle : GUI.skin.window
            );
        }

        private void DrawWaypointsPopupWindow(int id)
        {
            float pad = 12f;
            float x = pad;
            float y = pad;
            float w = waypointsPopupRect.width - pad * 2f;
            Event e = Event.current;

            GUI.Label(new Rect(x, y, w - 70f, 22f), new GUIContent("Waypoints"), headerStyle);
            if (GUI.Button(new Rect(waypointsPopupRect.width - pad - 64f, y - 2f, 64f, 24f),
                new GUIContent("CLOSE"), buttonStyle))
            {
                waypointsPopupVisible = false;
                waypointNameFocused = false;
            }
            y += 28f;

            if (GUI.Button(new Rect(x, y, w, 30f),
                new GUIContent("SAVE POSITION"), buttonStyle))
            {
                QuickSaveWaypoint();
            }
            y += 36f;

            Rect wpField = new Rect(x, y, w - 70f, 24f);
            GUI.Box(wpField, "");
            string wpShown = string.IsNullOrEmpty(newWaypointName) ? "name..." : newWaypointName;
            if (waypointNameFocused) wpShown += "|";
            GUI.Label(new Rect(wpField.x + 4f, wpField.y + 3f, wpField.width - 8f, 18f),
                new GUIContent(wpShown), labelStyle);
            if (e != null && e.type == EventType.MouseDown && wpField.Contains(e.mousePosition))
            {
                waypointNameFocused = true;
                e.Use();
            }
            if (waypointNameFocused && e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && newWaypointName.Length > 0)
                {
                    newWaypointName = newWaypointName.Substring(0, newWaypointName.Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    SaveNamedWaypoint();
                    waypointNameFocused = false;
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    waypointNameFocused = false;
                    e.Use();
                }
                else if (e.character != '\0' && !char.IsControl(e.character) && newWaypointName.Length < 24)
                {
                    newWaypointName += e.character;
                    e.Use();
                }
            }
            if (GUI.Button(new Rect(x + w - 66f, y, 66f, 24f), new GUIContent("SAVE"), buttonStyle))
                SaveNamedWaypoint();
            y += 32f;

            GUI.Label(new Rect(x, y, w, 18f),
                new GUIContent(savedWaypoints.Count + " saved"),
                smallStyle);
            y += 22f;

            float listH = Mathf.Max(80f, waypointsPopupRect.height - y - pad);
            Rect wpListRect = new Rect(x, y, w, listH);
            GUI.Box(wpListRect, "");
            List<string> wpNames = new List<string>(savedWaypoints.Keys);
            const float wpRow = 26f;
            float wpContent = wpNames.Count * wpRow;
            float wpMax = Mathf.Max(0f, wpContent - listH + 4f);
            if (e != null && e.type == EventType.ScrollWheel && wpListRect.Contains(e.mousePosition))
            {
                waypointListScroll.y = Mathf.Clamp(waypointListScroll.y + e.delta.y * 20f, 0f, wpMax);
                e.Use();
            }
            waypointListScroll.y = Mathf.Clamp(waypointListScroll.y, 0f, wpMax);
            GUI.BeginGroup(
                new Rect(wpListRect.x + 2f, wpListRect.y + 2f, wpListRect.width - 4f, wpListRect.height - 4f),
                GUIContent.none,
                GUIStyle.none);
            float wpy = -waypointListScroll.y;
            for (int i = 0; i < wpNames.Count; i++)
            {
                string n = wpNames[i];
                bool sel = n == selectedShareWaypointName;
                float nameW = wpListRect.width - 148f;
                if (GUI.Button(new Rect(0f, wpy, nameW, 24f), new GUIContent((sel ? "● " : "") + n),
                    sel ? selectedButtonStyle : buttonStyle))
                {
                    selectedShareWaypointName = n;
                    TeleportToWaypoint(n);
                }
                if (GUI.Button(new Rect(nameW + 2f, wpy, 48f, 24f), new GUIContent("SHARE"), buttonStyle))
                {
                    selectedShareWaypointName = n;
                    ShareWaypointToZexClients(n);
                }
                if (GUI.Button(new Rect(nameW + 52f, wpy, 40f, 24f), new GUIContent("DEL"), buttonStyle))
                    savedWaypoints.Remove(n);
                wpy += wpRow;
            }
            if (wpNames.Count == 0)
                GUI.Label(new Rect(6f, 6f, w - 20f, 20f), new GUIContent("No waypoints yet."), smallStyle);
            GUI.EndGroup();

            GUI.DragWindow(new Rect(0f, 0f, waypointsPopupRect.width, 28f));
        }

        private void DrawTeleportControls(float x, float y, float width, float height)
        {
            GUI.Box(new Rect(x, y, width, height), "");
            string selected = selectedPlayer == null ? "None" : GetPlayerName(selectedPlayer);
            GUI.Label(new Rect(x + 12f, y + 12f, width - 24f, 28f), new GUIContent("Selected: " + selected), labelStyle);
            bool target = selectedPlayer != null && !selectedPlayer.IsLocal;

            // Spectate controls
            float specY = y + 48f;
            GUI.Label(new Rect(x + 12f, specY, width - 24f, 22f), new GUIContent("Spectate"), headerStyle);
            bool canSpectate = target && FindPlayerObject(selectedPlayer) != null;
            float sbw = (width - 48f) / 3f;
            if (GUI.Button(new Rect(x + 12f, specY + 28f, sbw, 32f), new GUIContent(spectating ? "SPECTATING" : "SPECTATE"), canSpectate ? selectedButtonStyle : GUI.skin.button) && canSpectate)
                StartSpectating();
            if (GUI.Button(new Rect(x + 18f + sbw, specY + 28f, sbw, 32f), new GUIContent("◀ PREV"), buttonStyle) && PhotonNetwork.InRoom)
                CycleSpectatePlayer(-1);
            if (GUI.Button(new Rect(x + 24f + sbw * 2f, specY + 28f, sbw, 32f), new GUIContent("NEXT ▶"), buttonStyle) && PhotonNetwork.InRoom)
                CycleSpectatePlayer(1);
            if (GUI.Button(new Rect(x + 12f, specY + 66f, width - 24f, 30f), new GUIContent("STOP SPECTATING"), spectating ? buttonStyle : GUI.skin.button) && spectating)
                StopSpectating();

            float camY = specY + 104f;
            GUI.Label(new Rect(x + 12f, camY, 130f, 20f), new GUIContent("CAM HEIGHT: " + spectateCameraHeight.ToString("0.00") + "m"), labelStyle);
            spectateCameraHeight = GUI.HorizontalSlider(new Rect(x + 147f, camY + 2f, width - 24f - 147f, 18f), spectateCameraHeight, 0f, 5f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            camY += 26f;

            GUI.Label(new Rect(x + 12f, camY, 130f, 20f), new GUIContent("CAM DISTANCE: " + spectateCameraDistance.ToString("0.00") + "m"), labelStyle);
            spectateCameraDistance = GUI.HorizontalSlider(new Rect(x + 147f, camY + 2f, width - 24f - 147f, 18f), spectateCameraDistance, 1f, 10f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            camY += 26f;

            GUI.Label(new Rect(x + 12f, camY, 130f, 20f), new GUIContent("CAM ROTATION: " + Mathf.RoundToInt(spectateCameraRotation) + "°"), labelStyle);
            spectateCameraRotation = GUI.HorizontalSlider(new Rect(x + 147f, camY + 2f, width - 24f - 147f, 18f), spectateCameraRotation, 0f, 360f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            camY += 30f;

            float friendY = camY;
            if (target)
            {
                bool isFriend = friendActorIds.Contains(selectedPlayer.ActorNumber);
                if (GUI.Button(new Rect(x + 12f, friendY, width - 24f, 30f), new GUIContent(isFriend ? "★ Friend" : "☆ Friend"), buttonStyle))
                {
                    if (isFriend) friendActorIds.Remove(selectedPlayer.ActorNumber);
                    else friendActorIds.Add(selectedPlayer.ActorNumber);
                }

                if (!notesInputFocused)
                    notesInput = GetPlayerNote(selectedPlayer);

                Rect noteFieldRect = new Rect(x + 12f, friendY + 34f, width - 24f - 62f, 26f);
                GUI.Box(noteFieldRect, new GUIContent(""), GUI.skin.box);
                string noteDisplay = string.IsNullOrEmpty(notesInput) ? "CLICK TO ADD NOTE..." : notesInput;
                GUI.Label(new Rect(noteFieldRect.x + 6f, noteFieldRect.y + 3f, noteFieldRect.width - 12f, 20f), new GUIContent(noteDisplay), labelStyle);

                Event noteEvent = Event.current;
                if (noteEvent != null && noteEvent.type == EventType.MouseDown && noteFieldRect.Contains(noteEvent.mousePosition))
                {
                    notesInputFocused = true;
                    noteEvent.Use();
                }
                else if (noteEvent != null && noteEvent.type == EventType.MouseDown && !noteFieldRect.Contains(noteEvent.mousePosition))
                {
                    notesInputFocused = false;
                }

                if (notesInputFocused && noteEvent != null && noteEvent.type == EventType.KeyDown)
                {
                    if (noteEvent.keyCode == KeyCode.Backspace)
                    {
                        if (notesInput.Length > 0) notesInput = notesInput.Substring(0, notesInput.Length - 1);
                        noteEvent.Use();
                    }
                    else if (noteEvent.keyCode == KeyCode.Return || noteEvent.keyCode == KeyCode.KeypadEnter)
                    {
                        SetPlayerNote(selectedPlayer, notesInput);
                        notesInputFocused = false;
                        noteEvent.Use();
                    }
                    else if (noteEvent.keyCode == KeyCode.Escape)
                    {
                        notesInput = GetPlayerNote(selectedPlayer);
                        notesInputFocused = false;
                        noteEvent.Use();
                    }
                    else if (noteEvent.character != '\0' && !char.IsControl(noteEvent.character))
                    {
                        if (notesInput.Length < 60) notesInput += noteEvent.character;
                        noteEvent.Use();
                    }
                }

                if (GUI.Button(
                    new Rect(x + 12f + width - 24f - 56f, friendY + 34f, 56f, 26f),
                    new GUIContent("SAVE"),
                    buttonStyle))
                {
                    SetPlayerNote(selectedPlayer, notesInput);
                    notesInputFocused = false;
                }
            }

            // Teleport controls
            float teleY = friendY + (target ? 70f : 10f);
            GUI.Label(new Rect(x + 12f, teleY, width - 24f, 22f), new GUIContent("Teleport"), headerStyle);
            teleY += 26f;

            float tbw = (width - 48f) / 3f;
            if (GUI.Button(new Rect(x + 12f, teleY, tbw, 32f), new GUIContent("BEHIND"), target ? buttonStyle : GUI.skin.button) && target)
                TeleportBehindTarget();
            if (GUI.Button(new Rect(x + 18f + tbw, teleY, tbw, 32f), new GUIContent("IN FRONT"), target ? buttonStyle : GUI.skin.button) && target)
                TeleportInFrontOfTarget();
            if (GUI.Button(new Rect(x + 24f + tbw * 2f, teleY, tbw, 32f), new GUIContent("ABOVE"), target ? buttonStyle : GUI.skin.button) && target)
                TeleportAboveTarget();
            teleY += 40f;

            GUI.Label(new Rect(x + 12f, teleY, 100f, 20f), new GUIContent("BEHIND: " + behindDistance.ToString("0.0") + "m"), labelStyle);
            behindDistance = GUI.HorizontalSlider(new Rect(x + 115f, teleY + 2f, width - 140f, 18f), behindDistance, 0.5f, 15f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            teleY += 26f;

            GUI.Label(new Rect(x + 12f, teleY, 100f, 20f), new GUIContent("FRONT: " + frontDistance.ToString("0.0") + "m"), labelStyle);
            frontDistance = GUI.HorizontalSlider(new Rect(x + 115f, teleY + 2f, width - 140f, 18f), frontDistance, 0.5f, 15f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            teleY += 26f;

            GUI.Label(new Rect(x + 12f, teleY, 100f, 20f), new GUIContent("ABOVE: " + aboveDistance.ToString("0.0") + "m"), labelStyle);
            aboveDistance = GUI.HorizontalSlider(new Rect(x + 115f, teleY + 2f, width - 140f, 18f), aboveDistance, 0.5f, 20f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            teleY += 34f;

            string originStatus = originCaptured ? "CAPTURED" : "WAITING";
            GUI.Label(new Rect(x + 12f, teleY, width - 24f - 150f, 26f), new GUIContent("Origin: " + originStatus), labelStyle);
            if (GUI.Button(new Rect(x + width - 150f, teleY, 56f, 26f), new GUIContent("SAVE"), buttonStyle))
            {
                GameObject local = GetLocalPlayer();
                if (local != null)
                {
                    originPosition = local.transform.position;
                    originCaptured = true;
                }
            }
            // Wider button so "TELEPORT" is not clipped to "ELEPORT"
            if (GUI.Button(new Rect(x + width - 90f, teleY, 78f, 26f), new GUIContent("Teleport"), originCaptured ? buttonStyle : GUI.skin.button) && originCaptured)
                TeleportToOrigin();
            teleY += 34f;

            // Position restore after server-browser rejoin
            if (GUI.Button(new Rect(x + 12f, teleY, width - 24f, 28f),
                new GUIContent(browsePositionRestoreEnabled
                    ? "POS RESTORE AFTER BROWSE: ON"
                    : "POS RESTORE AFTER BROWSE: OFF"),
                buttonStyle))
            {
                browsePositionRestoreEnabled = !browsePositionRestoreEnabled;
                if (configBrowsePositionRestore != null)
                    configBrowsePositionRestore.Value = browsePositionRestoreEnabled;
                if (!browsePositionRestoreEnabled)
                {
                    browseRestoreActive = false;
                    browseHasSavedTransform = false;
                }
            }
            teleY += 32f;

        }

        // ============================================================
        // TELEPORT EXECUTION
        // ============================================================
        private void TryCaptureOrigin()
        {
            if (originCaptured) return;
            GameObject local = GetLocalPlayer();
            if (local == null) return;
            originPosition = local.transform.position;
            originCaptured = true;
        }

        private void TeleportLocalPlayer(Vector3 destination)
        {
            TeleportLocalPlayer(destination, softTeleportEnabled);
        }

        private void TeleportLocalPlayer(Vector3 destination, bool soft)
        {
            sessionTeleportCount++;
            GameObject local = GetLocalPlayer();
            if (local == null) return;

            if (soft && softTeleportDuration > 0.01f && local.activeInHierarchy)
            {
                if (softTeleportCoroutine != null)
                    StopCoroutine(softTeleportCoroutine);
                softTeleportCoroutine = StartCoroutine(SoftTeleportRoutine(local, destination));
                return;
            }

            SnapTeleportLocalPlayer(local, destination);
        }

        private void SnapTeleportLocalPlayer(GameObject local, Vector3 destination)
        {
            if (local == null) return;

            CharacterController controller = local.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
                local.transform.position = destination;
                controller.enabled = true;
            }
            else
            {
                local.transform.position = destination;
            }

            // Also zero rigidbodies so momentum doesn't fling you
            Rigidbody[] rbs = local.GetComponentsInChildren<Rigidbody>(true);
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
                }
            }
        }

        private IEnumerator SoftTeleportRoutine(GameObject body, Vector3 destination)
        {
            if (body == null)
            {
                softTeleportCoroutine = null;
                yield break;
            }

            Vector3 start = body.transform.position;
            float t = 0f;
            float dur = Mathf.Max(0.05f, softTeleportDuration);

            CharacterController controller = body.GetComponent<CharacterController>();
            bool hadCc = controller != null;
            if (hadCc) controller.enabled = false;

            while (t < 1f && body != null)
            {
                t += Time.unscaledDeltaTime / dur;
                float s = t * t * (3f - 2f * t); // smoothstep
                Vector3 pos = Vector3.Lerp(start, destination, Mathf.Clamp01(s));
                body.transform.position = pos;
                yield return null;
            }

            if (body != null)
            {
                body.transform.position = destination;
                if (hadCc && controller != null)
                    controller.enabled = true;
            }

            softTeleportCoroutine = null;
        }

        private GameObject GetLocalPlayer()
        {
            if (!PhotonNetwork.InRoom)
            {
                cachedLocalPlayer = null;
                return null;
            }

            // Drop stale / non-kobold cache (this was picking up bananas, doors, etc.)
            if (cachedLocalPlayer != null)
            {
                if (cachedLocalPlayer == null || !IsValidPlayerKoboldObject(cachedLocalPlayer))
                    cachedLocalPlayer = null;
                else
                    return cachedLocalPlayer;
            }

            // Prefer official TagObject (Kobold component or its GameObject)
            if (PhotonNetwork.LocalPlayer != null)
            {
                object tag = PhotonNetwork.LocalPlayer.TagObject;
                if (tag != null)
                {
                    ResolveGeneTypes();
                    if (koboldType != null && koboldType.IsInstanceOfType(tag))
                    {
                        cachedLocalPlayer = ((Component)tag).gameObject;
                        return cachedLocalPlayer;
                    }

                    Component asComp = tag as Component;
                    if (asComp != null && IsValidPlayerKoboldObject(asComp.gameObject))
                    {
                        Component kob = GetKoboldOn(asComp.gameObject);
                        cachedLocalPlayer = kob != null ? kob.gameObject : asComp.gameObject;
                        return cachedLocalPlayer;
                    }

                    GameObject asGo = tag as GameObject;
                    if (asGo != null && IsValidPlayerKoboldObject(asGo))
                    {
                        Component kob = GetKoboldOn(asGo);
                        cachedLocalPlayer = kob != null ? kob.gameObject : asGo;
                        return cachedLocalPlayer;
                    }
                }
            }

            // Scan owned PhotonViews that are actual Kobolds (never props)
            PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
            if (views != null)
            {
                for (int i = 0; i < views.Length; i++)
                {
                    PhotonView view = views[i];
                    if (view == null || !view.IsMine)
                        continue;
                    Component kob = GetKoboldOn(view.gameObject);
                    if (kob != null)
                    {
                        cachedLocalPlayer = kob.gameObject;
                        return cachedLocalPlayer;
                    }
                }
            }

            return null;
        }

        private void TeleportBehindTarget()
        {
            if (selectedPlayer == null) return;
            GameObject target = FindPlayerObject(selectedPlayer);
            if (target == null) return;
            Vector3 dest = target.transform.position - target.transform.forward * behindDistance;
            TeleportLocalPlayer(dest);
        }

        private void TeleportInFrontOfTarget()
        {
            if (selectedPlayer == null) return;
            GameObject target = FindPlayerObject(selectedPlayer);
            if (target == null) return;
            Vector3 dest = target.transform.position + target.transform.forward * frontDistance;
            TeleportLocalPlayer(dest);
        }

        private void TeleportAboveTarget()
        {
            if (selectedPlayer == null) return;
            GameObject target = FindPlayerObject(selectedPlayer);
            if (target == null) return;
            Vector3 dest = target.transform.position + Vector3.up * aboveDistance;
            TeleportLocalPlayer(dest);
        }

        private void TeleportToOrigin()
        {
            if (!originCaptured) return;
            TeleportLocalPlayer(originPosition);
        }

        // FLYING NOCLIP (CharCon-style: Kobold.body.velocity)
        // ============================================================
        private void ToggleFlyingNoclip()
        {
            if (flyingNoclipActive)
                DisableFlyingNoclip();
            else
                EnableFlyingNoclip();
        }

        /// <summary>
        /// CharCon / BepInEx UnityInput wrapper.
        /// CharCon uses: UnityInput.Current.GetKey("W"), GetKeyDown(KeyCode), etc.
        /// Resolved once via reflection so we work with BepInEx.Core / BepInEx.UnityInput.
        /// </summary>
        private static class ZexInput
        {
            private static bool resolved;
            private static object current; // UnityInput.Current instance
            private static MethodInfo miGetKeyString;
            private static MethodInfo miGetKeyKeyCode;
            private static MethodInfo miGetKeyDownKeyCode;
            private static MethodInfo miGetKeyUpKeyCode;

            private static void Ensure()
            {
                if (resolved) return;
                resolved = true;
                try
                {
                    Type t = SafeGameType("UnityInput")
                          ?? SafeGameType("BepInEx.UnityInput");
                    if (t == null) return;

                    PropertyInfo prop = AccessTools.Property(t, "Current")
                                     ?? AccessTools.Property(t, "current");
                    if (prop == null) return;

                    current = prop.GetValue(null, null);
                    if (current == null) return;

                    Type ct = current.GetType();
                    miGetKeyString = AccessTools.Method(ct, "GetKey", new Type[] { typeof(string) });
                    miGetKeyKeyCode = AccessTools.Method(ct, "GetKey", new Type[] { typeof(KeyCode) });
                    miGetKeyDownKeyCode = AccessTools.Method(ct, "GetKeyDown", new Type[] { typeof(KeyCode) });
                    miGetKeyUpKeyCode = AccessTools.Method(ct, "GetKeyUp", new Type[] { typeof(KeyCode) });
                }
                catch { }
            }

            public static bool GetKey(string name)
            {
                Ensure();
                try
                {
                    if (current != null && miGetKeyString != null)
                    {
                        object r = miGetKeyString.Invoke(current, new object[] { name });
                        if (r is bool b) return b;
                    }
                }
                catch { }
                // Fallback: map common CharCon strings
                try
                {
                    if (name == "W") return Input.GetKey(KeyCode.W);
                    if (name == "A") return Input.GetKey(KeyCode.A);
                    if (name == "S") return Input.GetKey(KeyCode.S);
                    if (name == "D") return Input.GetKey(KeyCode.D);
                    if (name == "Space") return Input.GetKey(KeyCode.Space);
                    if (name == "LeftShift") return Input.GetKey(KeyCode.LeftShift);
                    if (name == "RightShift") return Input.GetKey(KeyCode.RightShift);
                    if (name == "LeftControl") return Input.GetKey(KeyCode.LeftControl);
                }
                catch { }
                return false;
            }

            public static bool GetKey(KeyCode code)
            {
                if (code == KeyCode.None) return false;
                Ensure();
                try
                {
                    if (current != null && miGetKeyKeyCode != null)
                    {
                        object r = miGetKeyKeyCode.Invoke(current, new object[] { code });
                        if (r is bool b) return b;
                    }
                }
                catch { }
                try { return Input.GetKey(code); } catch { return false; }
            }

            public static bool GetKeyDown(KeyCode code)
            {
                if (code == KeyCode.None) return false;
                Ensure();
                try
                {
                    if (current != null && miGetKeyDownKeyCode != null)
                    {
                        object r = miGetKeyDownKeyCode.Invoke(current, new object[] { code });
                        if (r is bool b) return b;
                    }
                }
                catch { }
                try { return Input.GetKeyDown(code); } catch { return false; }
            }

            public static bool GetKeyUp(KeyCode code)
            {
                // Game's UnityEngine.Input has no GetKeyUp — UnityInput only.
                if (code == KeyCode.None) return false;
                Ensure();
                try
                {
                    if (current != null && miGetKeyUpKeyCode != null)
                    {
                        object r = miGetKeyUpKeyCode.Invoke(current, new object[] { code });
                        if (r is bool b) return b;
                    }
                }
                catch { }
                return false;
            }

            /// <summary>
            /// KeyboardShortcut down — CharCon uses shortcut.IsDown() for multi-key;
            /// we evaluate MainKey + modifiers through UnityInput so it stays consistent.
            /// </summary>
            public static bool ShortcutDown(KeyboardShortcut shortcut)
            {
                if (shortcut.MainKey == KeyCode.None)
                    return false;

                if (!GetKeyDown(shortcut.MainKey))
                    return false;

                try
                {
                    System.Collections.Generic.IEnumerable<KeyCode> mods = shortcut.Modifiers;
                    if (mods != null)
                    {
                        foreach (KeyCode mod in mods)
                        {
                            if (mod != KeyCode.None && !GetKey(mod))
                                return false;
                        }
                    }
                }
                catch
                {
                    // Older BepInEx: fall back to built-in IsDown
                    try { return shortcut.IsDown(); } catch { return false; }
                }
                return true;
            }
        }

        private Rigidbody ResolveKoboldBody()
        {
            if (flyCachedBody != null)
                return flyCachedBody;

            ResolveGeneTypes();
            Component kob = FindLocalKobold();
            if (kob == null)
            {
                flyDebugStatus = "no Kobold";
                return null;
            }

            if (koboldBodyField == null && koboldType != null)
            {
                koboldBodyField = AccessTools.Field(koboldType, "body");
                if (koboldBodyField == null)
                    koboldBodyField = AccessTools.Field(koboldType, "Body");
            }

            if (koboldBodyField != null)
            {
                try
                {
                    object v = koboldBodyField.GetValue(kob);
                    Rigidbody rb = v as Rigidbody;
                    if (rb != null)
                    {
                        flyCachedBody = rb;
                        flyDebugStatus = "body field OK";
                        return rb;
                    }
                }
                catch (Exception ex)
                {
                    flyDebugStatus = "body field err: " + ex.Message;
                }
            }

            Rigidbody direct = kob.GetComponent<Rigidbody>();
            if (direct != null)
            {
                flyCachedBody = direct;
                flyDebugStatus = "RB on Kobold";
                return direct;
            }
            Rigidbody child = kob.GetComponentInChildren<Rigidbody>();
            if (child != null)
            {
                flyCachedBody = child;
                flyDebugStatus = "RB in children";
                return child;
            }

            flyDebugStatus = "no Rigidbody";
            return null;
        }

        private void CacheAndDisableKoboldController(Component kob)
        {
            flyCachedKoboldController = null;
            if (kob == null) return;

            Type kccType = SafeGameType("KoboldCharacterController");
            if (kccType == null) return;

            Component c = kob.GetComponent(kccType);
            if (c == null)
                c = kob.GetComponentInChildren(kccType, true);
            if (c == null) return;

            Behaviour b = c as Behaviour;
            if (b == null) return;

            flyCachedKoboldController = b;
            flyKoboldControllerWasEnabled = b.enabled;
            b.enabled = false; // stop grounded/walk from overwriting velocity
        }

        private void EnableFlyingNoclip()
        {
            if (flyingNoclipActive) return;

            if (spectating)
                StopSpectating();
            followPlayerActorId = -1;

            flyCachedBody = null;
            Rigidbody body = ResolveKoboldBody();
            if (body == null)
            {
                Logger.LogWarning("Fly: could not find Kobold.body Rigidbody (" + flyDebugStatus + ")");
                return;
            }

            Component kob = FindLocalKobold();
            CacheAndDisableKoboldController(kob);

            // Physics setup — velocity only works on non-kinematic bodies
            try
            {
                if (body.isKinematic)
                    body.isKinematic = false;
            }
            catch { }

            flySavedPosition = body.position;
            // CharCon: body.detectCollisions = noclipCheck (old) then flip → false on enable
            body.detectCollisions = false;

            flyingNoclipActive = true;
            flyHasPendingVelocity = false;
            flyDebugStatus = "FLYING spd=" + flySpeed.ToString("0") + " body=" + body.name;
            Logger.LogInfo("Flying noclip ON — " + flyDebugStatus);
        }

        private void DisableFlyingNoclip()
        {
            if (!flyingNoclipActive) return;
            flyingNoclipActive = false;
            flyHasPendingVelocity = false;

            Rigidbody body = flyCachedBody != null ? flyCachedBody : ResolveKoboldBody();
            if (body != null)
            {
                body.detectCollisions = true;
                try { body.velocity = Vector3.zero; } catch { }
            }

            if (flyCachedKoboldController != null)
            {
                flyCachedKoboldController.enabled = flyKoboldControllerWasEnabled;
                flyCachedKoboldController = null;
            }

            flyCachedBody = null;
            flyDebugStatus = "off";
            Logger.LogInfo("Flying noclip OFF");
        }

        /// <summary>
        /// Exact CharCon CharFunc.Noclip (Update):
        ///   UnityInput.Current.GetKey("W"/"A"/"S"/"D"/"Space"/"LeftShift")
        ///   velocity = cam.rotation * axis; velocity.y += 0.2f; body.velocity = velocity
        /// Camera = OrbitCamera look (Camera.main).
        /// </summary>
        private void UpdateFlyingNoclipInput()
        {
            if (!flyingNoclipActive) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            float speed = flySpeed;
            float x = 0f, y = 0f, z = 0f;

            // CharCon: UnityInput.Current.GetKey("D") etc.
            if (ZexInput.GetKey("D")) x += speed;
            if (ZexInput.GetKey("A")) x -= speed;
            if (ZexInput.GetKey("W")) z += speed;
            if (ZexInput.GetKey("S")) z -= speed;
            if (ZexInput.GetKey("LeftShift")) y -= speed;
            if (ZexInput.GetKey("Space")) y += speed;

            Vector3 axis = new Vector3(x, y, z);
            Vector3 moveThere = cam.transform.rotation * axis;
            moveThere.y += 0.2f;

            flyPendingVelocity = moveThere;
            flyHasPendingVelocity = true;
            ApplyFlyVelocity();
        }

        private void FixedUpdate()
        {
            if (flyingNoclipActive)
                ApplyFlyVelocity();
        }

        private void ApplyFlyVelocity()
        {
            if (!flyingNoclipActive || !flyHasPendingVelocity) return;

            Rigidbody body = flyCachedBody != null ? flyCachedBody : ResolveKoboldBody();
            if (body == null)
            {
                DisableFlyingNoclip();
                return;
            }

            if (flyCachedKoboldController != null && flyCachedKoboldController.enabled)
                flyCachedKoboldController.enabled = false;

            body.detectCollisions = false;
            try
            {
                if (body.isKinematic)
                    body.isKinematic = false;
                body.velocity = flyPendingVelocity;
            }
            catch (Exception ex)
            {
                flyDebugStatus = "vel err: " + ex.Message;
            }
        }

        // ============================================================
        // WAYPOINTS
        // ============================================================
        private void QuickSaveWaypoint()
        {
            GameObject local = GetLocalPlayer();
            Vector3 pos;
            if (local != null)
                pos = local.transform.position;
            else if (Camera.main != null)
                pos = Camera.main.transform.position;
            else
                return;

            string name = "WP" + waypointAutoIndex;
            while (savedWaypoints.ContainsKey(name))
            {
                waypointAutoIndex++;
                name = "WP" + waypointAutoIndex;
            }
            savedWaypoints[name] = pos;
            waypointAutoIndex++;
            newWaypointName = name;
            Logger.LogInfo("Waypoint saved: " + name + " @ " + pos);
        }

        private void PasteCoordsAsWaypoint()
        {
            try
            {
                string clip = GUIUtility.systemCopyBuffer;
                if (string.IsNullOrEmpty(clip))
                {
                    ShowToast("Clipboard empty", "system");
                    return;
                }
                clip = clip.Trim().Replace("(", "").Replace(")", "");
                string[] parts = clip.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    ShowToast("Need x,y,z on clipboard", "system");
                    return;
                }
                float x = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                Vector3 pos = new Vector3(x, y, z);
                string name = "CLIP" + waypointAutoIndex;
                while (savedWaypoints.ContainsKey(name))
                {
                    waypointAutoIndex++;
                    name = "CLIP" + waypointAutoIndex;
                }
                savedWaypoints[name] = pos;
                waypointAutoIndex++;
                newWaypointName = name;
                ShowToast("Waypoint " + name + " from clipboard", "gameplay");
            }
            catch (Exception ex)
            {
                ShowToast("Paste coords failed", "system");
                Logger.LogWarning("PasteCoordsAsWaypoint: " + ex.Message);
            }
        }

        private void CopyLocalCoordsToClipboard()
        {
            GameObject local = GetLocalPlayer();
            Vector3 pos;
            if (local != null) pos = local.transform.position;
            else if (Camera.main != null) pos = Camera.main.transform.position;
            else { ShowToast("No position", "system"); return; }
            string s = pos.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", " +
                       pos.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", " +
                       pos.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            GUIUtility.systemCopyBuffer = s;
            ShowToast("Copied " + s, "gameplay");
        }

                private void SaveNamedWaypoint()
        {
            string name = string.IsNullOrEmpty(newWaypointName) ? null : newWaypointName.Trim();
            if (string.IsNullOrEmpty(name))
            {
                QuickSaveWaypoint();
                return;
            }

            GameObject local = GetLocalPlayer();
            if (local == null && Camera.main == null) return;
            Vector3 pos = local != null ? local.transform.position : Camera.main.transform.position;
            savedWaypoints[name] = pos;
        }

        private void TeleportToWaypoint(string name)
        {
            Vector3 pos;
            if (!savedWaypoints.TryGetValue(name, out pos)) return;
            TeleportLocalPlayer(pos);
        }
    }
}