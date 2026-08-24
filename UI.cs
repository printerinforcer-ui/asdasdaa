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
        // ============================================================
        // GUI
        // ============================================================
        private void OnGUI()
        {
            CreateStyles();
            ApplyMenuHueToStyles();

            if (cleanUiActive)
                return;

            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                DrawESP(GetEspStyle());
            }

            DrawPlayerOverlay();
            DrawPlayerRadar();
            DrawPlayerContextMenu();
            DrawPresetsPopup();
            DrawWaypointsPopup();
            DrawClientFeaturesOverlays();
            DrawPartyPingMark();
            DrawPingMark();

            HandleInput();

            // Toast draws above everything (works with menu closed)
            DrawStatusToast();

            // Always-on HUD waypoint bar (even with menu closed)
            DrawHudWaypoints();
            DrawQuickActionBar();

            if (!menuVisible)
                return;

            // Smooth scale toward target (feels less jumpy on the slider)
            float targetScale = Mathf.Clamp(uiScale, UiScaleMin, UiScaleMax);
            if (uiScaleSmoothed < 0.01f) uiScaleSmoothed = targetScale;
            uiScaleSmoothed = Mathf.Lerp(uiScaleSmoothed, targetScale, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            if (Mathf.Abs(uiScaleSmoothed - targetScale) < 0.001f)
                uiScaleSmoothed = targetScale;

            ClampMenu();

            Matrix4x4 prevMatrix = GUI.matrix;
            float scale = uiScaleSmoothed;
            if (Mathf.Abs(scale - 1f) > 0.001f)
            {
                // Scale UI from top-left of menu rect
                Vector2 origin = new Vector2(menuRect.x, menuRect.y);
                GUI.matrix = Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(scale, scale, 1f))
                    * Matrix4x4.TRS(-origin, Quaternion.identity, Vector3.one);
            }

            menuRect = GUI.Window(
                9001,
                menuRect,
                DrawMainWindow,
                GUIContent.none,
                windowStyle
            );

            // Re-clamp after drag using scaled bounds (lets small UI reach bottom-right)
            ClampMenu();
            SaveUiLayoutToConfig();

            GUI.matrix = prevMatrix;
        }

        private void HandleInput()
        {
            Event e = Event.current;
            if (e == null)
                return;

            // Key / mouse rebind capture
            if (waitingForKeyRebind)
            {
                KeyCode captured = KeyCode.None;
                if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
                {
                    if (e.keyCode == KeyCode.Escape)
                    {
                        waitingForKeyRebind = false;
                        rebindTarget = null;
                        e.Use();
                        return;
                    }
                    captured = e.keyCode;
                }
                else if (e.type == EventType.MouseDown)
                {
                    // Allow binding mouse buttons (stop spectate / free mouse)
                    if (e.button == 0) captured = KeyCode.Mouse0;
                    else if (e.button == 1) captured = KeyCode.Mouse1;
                    else if (e.button == 2) captured = KeyCode.Mouse2;
                    else if (e.button == 3) captured = KeyCode.Mouse3;
                    else if (e.button == 4) captured = KeyCode.Mouse4;
                }
                else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.None)
                {
                    // ignore
                }

                if (captured != KeyCode.None)
                {
                    if (rebindTarget == "menu" && menuToggleKey != null)
                        menuToggleKey.Value = new KeyboardShortcut(captured);
                    else if (rebindTarget == "noclip" && noclipToggleKey != null)
                        noclipToggleKey.Value = captured;
                    else if (rebindTarget == "waypoint" && waypointQuickSaveKey != null)
                        waypointQuickSaveKey.Value = captured;
                    else if (rebindTarget == "flyUp" && flySpeedUpKey != null)
                        flySpeedUpKey.Value = captured;
                    else if (rebindTarget == "flyDown" && flySpeedDownKey != null)
                        flySpeedDownKey.Value = captured;
                    else if (rebindTarget == "specNext" && spectateNextKey != null)
                        spectateNextKey.Value = captured;
                    else if (rebindTarget == "specPrev" && spectatePrevKey != null)
                        spectatePrevKey.Value = captured;
                    else if (rebindTarget == "stopSpec" && stopSpectateKey != null)
                        stopSpectateKey.Value = captured;
                    else if (rebindTarget == "freeMouse" && freeMouseHoldKey != null)
                        freeMouseHoldKey.Value = captured;

                    waitingForKeyRebind = false;
                    rebindTarget = null;
                    e.Use();
                    return;
                }
            }

            // Spawner search input.
            if (tab == 1 && searchFocused && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace)
                {
                    if (searchText.Length > 0)
                        searchText = searchText.Substring(0, searchText.Length - 1);

                    ApplySearch();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    searchFocused = false;
                    e.Use();
                }
                else if (e.character != '\0' && !char.IsControl(e.character))
                {
                    if (searchText.Length < 128)
                        searchText += e.character;

                    ApplySearch();
                    e.Use();
                }
            }

            // Sillys name input. We intentionally use Event.current instead
            // of GUI.TextField/TextArea because this game's GUI reference set
            // does not expose those APIs.
            if (tab == 6 && sillysNameEditing && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace)
                {
                    if (sillysName.Length > 0)
                        sillysName = sillysName.Substring(0, sillysName.Length - 1);

                    e.Use();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    sillysNameEditing = false;
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    sillysNameEditing = false;
                    e.Use();
                }
                else if (e.character != '\0' && !char.IsControl(e.character))
                {
                    if (sillysName.Length < 32)
                        sillysName += e.character;

                    e.Use();
                }
            }

            // Hotkeys are handled in Update via ProcessHotkeys() so rebinds work
            // and IsDown() does not double-fire during OnGUI Layout/Repaint.
        }

        private void ClampMenu()
        {
            // Use *visual* size (logical size * scale) so a smaller UI can sit in any corner
            float scale = Mathf.Clamp(uiScaleSmoothed > 0.01f ? uiScaleSmoothed : uiScale, UiScaleMin, UiScaleMax);
            float visW = menuRect.width * scale;
            float visH = menuRect.height * scale;
            float pad = 4f;

            menuRect.x = Mathf.Clamp(
                menuRect.x,
                pad,
                Mathf.Max(pad, Screen.width - visW - pad)
            );

            menuRect.y = Mathf.Clamp(
                menuRect.y,
                pad,
                Mathf.Max(pad, Screen.height - visH - pad)
            );
        }

        /// <summary>
        /// Menu chrome colors driven by backgroundHue so the whole UI shifts together.
        /// Base palette is a cool blue at hue ~0.61; we rotate that hue by the slider.
        /// </summary>
        private float MenuSat(float sat)
        {
            return menuColorGreyscale ? 0f : sat;
        }

        /// <summary>
        /// Advances backgroundHue when RGB CYCLE is enabled. Call from Update with unscaled dt.
        /// Invalidates style cache so selected buttons / sidebar recolor every frame.
        /// </summary>
        private void TickMenuHueCycle(float dt)
        {
            if (menuColorGreyscale || !menuHueCycling)
                return;
            float period = Mathf.Clamp(menuHueCycleSeconds, MenuHueCycleSecondsMin, MenuHueCycleSecondsMax);
            if (period < 0.1f || dt <= 0f)
                return;

            backgroundHue = Mathf.Repeat(backgroundHue + dt / period, 1f);
            // Force ApplyMenuHueToStyles to rebuild accent textures this frame
            lastStyledHue = -1f;
        }

        /// <summary>
        /// Live RGB accent from current menu hue (cycles when RGB CYCLE is on).
        /// </summary>
        private Color GetMenuAccentColor(float alpha = 1f)
        {
            // Strong saturated accent (selected tabs, highlights) — grey when greyscale mode
            Color c = Color.HSVToRGB(Mathf.Repeat(backgroundHue, 1f), MenuSat(0.85f), 1f);
            c.a = alpha;
            return c;
        }

        /// <summary>
        /// Same as accent but slightly dimmer — list selection fills.
        /// </summary>
        private Color GetMenuSelectionColor(float alpha = 0.9f)
        {
            Color c = Color.HSVToRGB(Mathf.Repeat(backgroundHue, 1f), MenuSat(0.75f), 0.95f);
            c.a = alpha;
            return c;
        }

        private Color GetMenuButtonTint(float alpha = 1f)
        {
            // Lighter desaturated button wash (sidebar idle buttons etc.)
            Color c = Color.HSVToRGB(Mathf.Repeat(backgroundHue, 1f), MenuSat(0.28f), 1f);
            c.a = alpha;
            return c;
        }

        private Color GetMenuPanelColor(float value, float alpha = 1f)
        {
            // Dark panels slightly tinted toward the chosen hue
            Color c = Color.HSVToRGB(Mathf.Repeat(backgroundHue, 1f), MenuSat(0.35f), value);
            c.a = alpha;
            return c;
        }

        private void BeginDarkUI()
        {
            GUI.color = GetMenuPanelColor(0.12f, 1f);
        }

        private void BeginCardUI()
        {
            GUI.color = GetMenuPanelColor(0.18f, 1f);
        }

        private void BeginButtonUI()
        {
            GUI.color = GetMenuButtonTint(1f);
        }

        private void BeginAccentUI()
        {
            GUI.color = GetMenuAccentColor(1f);
        }

        private void EndUIColor()
        {
            GUI.color = Color.white;
        }

        private float lastStyledHue = -1f;
        private bool lastStyledGreyscale;

        private void ApplyMenuHueToStyles()
        {
            if (!stylesCreated)
                return;
            if (Mathf.Abs(lastStyledHue - backgroundHue) < 0.0005f && lastStyledGreyscale == menuColorGreyscale)
                return;
            lastStyledHue = backgroundHue;
            lastStyledGreyscale = menuColorGreyscale;

            Color accent = GetMenuAccentColor(1f);
            Color muted = Color.HSVToRGB(Mathf.Repeat(backgroundHue, 1f), MenuSat(0.18f), 0.72f);

            if (accentLabelStyle != null)
                accentLabelStyle.normal.textColor = accent;
            if (modernSmallStyle != null)
                modernSmallStyle.normal.textColor = muted;
            if (sectionStyle != null)
                sectionStyle.normal.textColor = Color.Lerp(Color.white, accent, 0.35f);
            if (headerStyle != null)
                headerStyle.normal.textColor = Color.Lerp(Color.white, accent, 0.25f);
            // KK's IMGUI strip has no GUIStyle.hover/active/on* or GUIStyleState.background.
            // Selection RGB is applied via GUI.backgroundColor / GetMenuSelectionColor at draw time.
            if (selectedButtonStyle != null)
                selectedButtonStyle.normal.textColor = Color.Lerp(Color.white, accent, 0.15f);
            if (modernSelectedButtonStyle != null)
                modernSelectedButtonStyle.normal.textColor = Color.white;
            if (sidebarSelectedStyle != null)
                sidebarSelectedStyle.normal.textColor = Color.white;
            if (valueStyle != null)
                valueStyle.normal.textColor = Color.Lerp(new Color(0.90f, 0.91f, 0.96f), accent, 0.2f);
        }

        private void DrawMainWindow(int id)
        {
            GUI.color = Color.white;

            // Animated / tinted background (hue + opacity apply here).
            // Avoid GUI.skin.window chrome so no black title bar is drawn.
            DrawMenuBackground();

            // Soft dark fill so content stays readable if the sprite sheet is bright (hue-tinted).
            Color prevBg = GUI.color;
            Color fill = GetMenuPanelColor(0.10f, 0.55f * backgroundOpacity);
            GUI.color = fill;
            GUI.Box(
                new Rect(0f, 0f, menuRect.width, menuRect.height),
                GUIContent.none,
                GUI.skin.box
            );
            GUI.color = prevBg;

            float sidebarTargetW = sidebarCollapsed ? 40f : 190f;
            float animT = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            sidebarAnimW = Mathf.Lerp(sidebarAnimW, sidebarTargetW, animT);
            if (Mathf.Abs(sidebarAnimW - sidebarTargetW) < 0.5f)
                sidebarAnimW = sidebarTargetW;
            float sidebarW = sidebarAnimW;
            float topH = 62f;
            // Extra right inset so lists/buttons don't sit flush against the window edge.
            const float contentPadL = 18f;
            const float contentPadR = 32f;
            float contentX = sidebarW + contentPadL;
            float contentY = topH + 18f;
            float contentW = menuRect.width - contentX - contentPadR;

            DrawModernTopBar(sidebarW, topH);
            DrawModernSidebar(sidebarW, topH);

            // Page area (card slightly larger than content for a soft inset).
            BeginCardUI();
            GUI.Box(
                new Rect(
                    contentX - 8f,
                    contentY - 8f,
                    contentW + 16f,
                    menuRect.height - contentY - 50f
                ),
                new GUIContent(""),
                cardStyle
            );
            EndUIColor();

            string pageTitle = GetModernPageTitle();
            string pageSubtitle = GetModernPageSubtitle();

            GUI.Label(
                new Rect(contentX + 10f, contentY + 10f, contentW - 20f, 30f),
                new GUIContent(pageTitle),
                headerStyle
            );

            GUI.Label(
                new Rect(contentX + 10f, contentY + 40f, contentW - 20f, 22f),
                new GUIContent(pageSubtitle),
                modernSmallStyle
            );

            float panelY = contentY + 70f;
            // Leave room for the bottom status bar so panel content does not clip into it.
            float panelMaxH = Mathf.Max(200f, menuRect.height - panelY - 48f);

            if (tab == 0)
                DrawESPPanel(contentX, panelY, contentW);
            else if (tab == 1)
                DrawSpawnerPanel(contentX, panelY, contentW);
            else if (tab == 2)
                DrawTeleportPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 3)
                DrawHostToolsPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 4)
                DrawMiscPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 5)
                DrawServersPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 7)
                DrawHostLogsPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 8)
                DrawGenesPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 9)
                DrawTestingPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 10)
                DrawSettingsPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 11)
                DrawQoLPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 12)
                DrawModsPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 13)
                DrawChatPanel(contentX, panelY, contentW, panelMaxH);
            else if (tab == 14)
                DrawNotificationsPanel(contentX, panelY, contentW, panelMaxH);
            else
                DrawSillysPanel(contentX, panelY, contentW);

            DrawModernStatusBar(
                contentX,
                menuRect.height - 38f,
                contentW
            );

            // Only the top bar is draggable.
            GUI.DragWindow(
                new Rect(
                    0f,
                    0f,
                    menuRect.width,
                    62f
                )
            );

            GUI.color = Color.white;
        }

        private string GetModernPageTitle()
        {
            switch (tab)
            {
                case 0: return "Visuals";
                case 1: return "Spawner";
                case 2: return "Teleport/Spectate";
                case 3: return "Host Tools";
                case 4: return "Miscellaneous";
                case 5: return "Better Server List";
                case 6: return "Sillys / Credits";
                case 7: return "Host Logs";
                case 8: return "Character Editor / Loader";
                case 9: return "Every update there will be features to test and vote to add";
                case 10: return "Settings";
                case 11: return "QoL";
                case 12: return PhotonNetwork.InRoom ? "Room mods (read-only) — save presets OK, no apply/load in-lobby" : "Browse & apply mods offline only";
                case 13: return "Chat";
                case 14: return "Notifications";
            }

            return "Zex's QoL";
        }

        private string GetModernPageSubtitle()
        {
            switch (tab)
            {
                case 0: return "ESP, tracers, colors and player visibility.";
                case 1: return "Browse, favorite and spawn available prefabs.";
                case 2: return "Player movement, waypoints and spectate controls.";
                case 3: return "Room administration and host-only actions.";
                case 4: return "Leftover misc — most tools moved to QoL / Settings.";
                case 5: return "Browse and switch between Photon rooms.";
                case 6: return "Completely unnecessary tools will be here xD alot of local shit.";
                case 7: return "Join/leave events and banned player list.";
                case 8: return "Credits to Komar for the Inspo. This Menu May Change";
                case 10: return "Keybinds, soft teleport, menu background.";
                case 11: return "Qol Is a Bit Empty ATM. Next Comminuity Vote Will Be Qol Additions and Will Be Added Here";
                case 12: return PhotonNetwork.InRoom ? "Viewing mods for this room. Apply/Load disabled while joined." : "Browse, select, and apply local/Workshop mods.";
                case 13: return "˚ʚ♡ɞ˚ private messages with clients in this room.";
                case 14: return "Customize toast position, size, duration, animation, colors, and filters.";
                case 9: return "Experimental features for community voting.";
            }

            return "Basically Minecrafts Community Voting.";
        }

        private void DrawModernTopBar(float sidebarW, float height)
        {
            GUI.Box(
                new Rect(
                    sidebarW,
                    0f,
                    menuRect.width - sidebarW,
                    height
                ),
                new GUIContent(""),
                topBarStyle
            );

            GUI.Label(
                new Rect(
                    sidebarW + 24f,
                    16f,
                    200f,
                    28f
                ),
                new GUIContent("˚ʚ♡ɞ˚ Zex's QoL ˚ʚ♡ɞ˚"),
                accentLabelStyle
            );

            string roomName =
                PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null
                    ? PhotonNetwork.CurrentRoom.Name
                    : "Not connected";

            string roomText =
                PhotonNetwork.InRoom
                    ? "●  " + roomName
                    : "○  " + roomName;

            GUI.Label(
                new Rect(
                    sidebarW + 220f,
                    19f,
                    320f,
                    24f
                ),
                new GUIContent(roomText),
                valueStyle
            );

            string masterText =
                PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient
                    ? "HOST"
                    : "CLIENT";

            GUI.Label(
                new Rect(
                    menuRect.width - 190f,
                    19f,
                    90f,
                    24f
                ),
                new GUIContent(masterText),
                PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient
                    ? accentLabelStyle
                    : modernSmallStyle
            );

            BeginButtonUI();
            if (GUI.Button(
                new Rect(
                    menuRect.width - 88f,
                    14f,
                    68f,
                    34f
                ),
                new GUIContent("SETTINGS"),
                modernButtonStyle
            ))
            {
                tab = 10;
            }
            EndUIColor();
        }

        private void DrawModernSidebar(float width, float topH)
        {
            GUI.Box(
                new Rect(
                    0f,
                    0f,
                    width,
                    menuRect.height
                ),
                new GUIContent(""),
                sidebarStyle
            );

            // Collapse control — bottom of sidebar so it never covers BETA branding
            float collapseH = 28f;
            float collapseY = menuRect.height - collapseH - 10f;
            string collapseLabel = sidebarCollapsed || width < 70f ? "»" : "« Hide";
            if (GUI.Button(new Rect(4f, collapseY, width - 8f, collapseH),
                new GUIContent(collapseLabel), buttonStyle))
                sidebarCollapsed = !sidebarCollapsed;

            // While animating closed, fade out nav items
            if (width < 72f)
                return;

            GUI.Label(
                new Rect(
                    18f,
                    18f,
                    width - 36f,
                    28f
                ),
                new GUIContent("BETA"),
                headerStyle
            );

            float y = 56f;

            DrawModernSidebarGroup(
                ref y,
                "WILL",
                new string[] { "ESP", "SPAWNER" },
                new int[] { 0, 1 },
                width
            );

            DrawModernSidebarGroup(
                ref y,
                "ORDER",
                new string[] { "TELEPORT/SPECTATE", "HOST TOOLS", "HOST LOGS", "SERVERS" },
                new int[] { 2, 3, 7, 5 },
                width
            );

            DrawModernSidebarGroup(
                ref y,
                "SOON",
                new string[] { "QOL", "CHARACTER EDITOR", "MOD LOADER", "SILLYS", "TESTING", "CHAT", "NOTIFY" },
                new int[] { 11, 8, 12, 6, 9, 13, 14 },
                width
            );

            // Sit above the collapse button (button is ~38px from bottom)
            GUI.Label(
                new Rect(
                    18f,
                    menuRect.height - 68f,
                    width - 36f,
                    18f
                ),
                new GUIContent("Public Beta V1.1 ( CRASH FIX )"),
                modernSmallStyle
            );
        }

        private void DrawModernSidebarGroup(
            ref float y,
            string title,
            string[] names,
            int[] indexes,
            float width
        )
        {
            GUI.Label(
                new Rect(
                    18f,
                    y,
                    width - 36f,
                    18f
                ),
                new GUIContent(title),
                modernSmallStyle
            );

            y += 24f;

            for (int i = 0; i < names.Length; i++)
            {
                int page = indexes[i];

                GUIStyle style =
                    tab == page
                        ? sidebarSelectedStyle
                        : sidebarStyle;

                if (tab == page)
                    BeginAccentUI();
                else
                    BeginButtonUI();

                bool clicked = GUI.Button(
                    new Rect(
                        12f,
                        y,
                        width - 24f,
                        36f
                    ),
                    new GUIContent(names[i]),
                    style
                );

                EndUIColor();

                if (clicked)
                {
                    tab = page;
                    searchFocused = false;
                    sillysNameEditing = false;
                }

                y += 40f;
            }

            y += 10f;
        }

        private void DrawModernStatusBar(float x, float y, float width)
        {
            GUI.Box(
                new Rect(
                    x,
                    y,
                    width,
                    26f
                ),
                new GUIContent(""),
                topBarStyle
            );

            int players =
                PhotonNetwork.InRoom && PhotonNetwork.PlayerList != null
                    ? PhotonNetwork.PlayerList.Length
                    : 0;

            string host =
                PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient
                    ? "MASTER"
                    : "CLIENT";

            string fly = flyingNoclipActive ? "     FLY" : "";
            string froze = frozenActorIds.Count > 0 ? "     FROZEN " + frozenActorIds.Count : "";
            string text =
                "FPS " + Mathf.RoundToInt(fpsValue) +
                "     PLAYERS " + players +
                "     SPAWNED " + spawnedObjects.Count +
                "     " + host + fly + froze;

            GUI.Label(
                new Rect(
                    x + 10f,
                    y + 4f,
                    width - 20f,
                    20f
                ),
                new GUIContent(text),
                modernSmallStyle
            );
        }

        private void DrawTabs(float x, float y, float width)
        {
            // Kept for compatibility with older calls elsewhere.
            // The new menu uses the left sidebar instead.
        }

        private delegate void BoolSetter(bool value);
        private delegate void FloatSetter(float value);

        private void DrawToggle(float x, float y, bool value, String text, BoolSetter setter)
        {
            if (GUI.Button(new Rect(x, y, 26f, 26f), new GUIContent(value ? "♥" : "♡"), buttonStyle))
                setter(!value);
            GUI.Label(new Rect(x + 34f, y, 280f, 26f), new GUIContent(text), labelStyle);
        }

        private void DrawSlider(float x, float y, String text, float value, float min, float max, FloatSetter setter, String format)
        {
            GUI.Label(new Rect(x, y, 250f, 20f), new GUIContent(text), labelStyle);
            float v = GUI.HorizontalSlider(new Rect(x, y + 24f, 250f, 18f), value, min, max,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            setter(v);
            GUI.Label(new Rect(x + 260f, y + 20f, 80f, 24f), new GUIContent(v.ToString(format)), labelStyle);
        }

        private void ShowToast(string msg)
        {
            ShowToast(msg, "system");
        }

        /// <summary>category: system | social | gameplay | host | scan | screenshot</summary>
        private void ShowToast(string msg, string category)
        {
            if (string.IsNullOrEmpty(msg))
                return;
            if (!toastEnabled)
                return;
            if (!IsToastCategoryEnabled(category))
                return;

            toastQueue.Enqueue(msg);
            toastCategoryQueue.Enqueue(string.IsNullOrEmpty(category) ? "system" : category.ToLowerInvariant());
            if (string.IsNullOrEmpty(toastMessage) || Time.unscaledTime > toastUntil)
                PromoteNextToast();
        }

        private bool IsToastCategoryEnabled(string category)
        {
            if (string.IsNullOrEmpty(category))
                return toastNotifySystem;
            switch (category.ToLowerInvariant())
            {
                case "social": return toastNotifySocial;
                case "gameplay": return toastNotifyGameplay;
                case "host": return toastNotifyHost;
                case "scan": return toastNotifyScan;
                case "screenshot": return toastNotifyScreenshot;
                default: return toastNotifySystem;
            }
        }

        private void PromoteNextToast()
        {
            while (toastQueue.Count > 0)
            {
                string next = toastQueue.Dequeue();
                string cat = toastCategoryQueue.Count > 0 ? toastCategoryQueue.Dequeue() : "system";
                if (string.IsNullOrEmpty(next))
                    continue;
                if (!IsToastCategoryEnabled(cat))
                    continue;
                toastMessage = next;
                toastCategory = cat;
                toastShownAt = Time.unscaledTime;
                toastUntil = Time.unscaledTime + Mathf.Clamp(toastDurationSec, 0.5f, 12f);
                return;
            }
            toastMessage = "";
            toastCategory = "system";
        }

        private void DrawStatusToast()
        {
            if (!toastEnabled)
                return;

            if (Time.unscaledTime > toastUntil)
            {
                if (toastQueue.Count > 0)
                    PromoteNextToast();
                else if (!string.IsNullOrEmpty(toastMessage))
                    toastMessage = "";
            }
            if (string.IsNullOrEmpty(toastMessage) || Time.unscaledTime > toastUntil)
                return;

            float baseW = Mathf.Min(520f, Screen.width - 40f);
            float tw = Mathf.Clamp(baseW * toastWidthScale, 180f, Screen.width - 16f);
            float th = Mathf.Clamp(36f * toastHeightScale, 28f, 72f);
            float margin = Mathf.Clamp(toastMargin, 4f, 80f);

            float tx, ty;
            switch (toastPosition)
            {
                case 1: tx = margin; ty = margin; break;
                case 2: tx = Screen.width - tw - margin; ty = margin; break;
                case 3: tx = (Screen.width - tw) * 0.5f; ty = Screen.height - th - margin; break;
                case 4: tx = margin; ty = Screen.height - th - margin; break;
                case 5: tx = Screen.width - tw - margin; ty = Screen.height - th - margin; break;
                default: tx = (Screen.width - tw) * 0.5f; ty = margin; break;
            }

            float anim = Mathf.Max(0.05f, toastAnimSeconds);
            float age = Time.unscaledTime - toastShownAt;
            float left = toastUntil - Time.unscaledTime;
            float alphaMul = 1f;
            float slide = 0f;
            bool doFade = toastAnimMode == 1 || toastAnimMode == 3;
            bool doSlide = toastAnimMode == 2 || toastAnimMode == 3;
            if (doFade)
            {
                if (age < anim) alphaMul = Mathf.Clamp01(age / anim);
                else if (left < anim) alphaMul = Mathf.Clamp01(left / anim);
            }
            if (doSlide)
            {
                float slideAmt = 28f;
                bool fromTop = toastPosition <= 2;
                if (age < anim)
                {
                    float t = 1f - Mathf.Clamp01(age / anim);
                    slide = fromTop ? -slideAmt * t : slideAmt * t;
                }
                else if (left < anim)
                {
                    float t = 1f - Mathf.Clamp01(left / anim);
                    slide = fromTop ? -slideAmt * t : slideAmt * t;
                }
            }
            ty += slide;

            Color bg = Color.HSVToRGB(Mathf.Repeat(toastBgHue, 1f), 0.45f, 0.14f);
            bg.a = Mathf.Clamp01(toastBgOpacity) * alphaMul;
            Color textCol = new Color(toastTextBrightness, toastTextBrightness, toastTextBrightness, alphaMul);

            Color prev = GUI.color;
            GUI.color = bg;
            GUI.Box(new Rect(tx, ty, tw, th), "");
            GUI.color = textCol;
            GUIStyle style = new GUIStyle(labelStyle != null ? labelStyle : GUI.skin.label);
            style.fontSize = Mathf.RoundToInt(13f * Mathf.Clamp(toastFontScale, 0.8f, 1.5f));
            style.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(tx + 12f, ty + 4f, tw - 24f, th - 8f), new GUIContent(toastMessage), style);
            GUI.color = prev;
        }

        private void DrawNotificationsPanel(float x, float y, float width, float maxHeight)
        {
            float leftW = width * 0.48f;
            float gap = 14f;
            float rightX = x + leftW + gap;
            float rightW = width - leftW - gap;
            float startY = y;

            GUI.Label(new Rect(x, y, leftW, 22f), new GUIContent("LAYOUT"), headerStyle);
            y += 26f;

            if (GUI.Button(new Rect(x, y, leftW, 28f),
                new GUIContent(toastEnabled ? "TOASTS: ON" : "TOASTS: OFF"),
                toastEnabled ? selectedButtonStyle : buttonStyle))
            {
                toastEnabled = !toastEnabled;
                if (configToastEnabled != null) configToastEnabled.Value = toastEnabled;
            }
            y += 34f;

            string[] posNames = { "TOP CENTER", "TOP LEFT", "TOP RIGHT", "BOTTOM CENTER", "BOTTOM LEFT", "BOTTOM RIGHT" };
            if (GUI.Button(new Rect(x, y, leftW, 28f),
                new GUIContent("POSITION: " + posNames[Mathf.Clamp(toastPosition, 0, 5)]), buttonStyle))
            {
                toastPosition = (toastPosition + 1) % 6;
                if (configToastPosition != null) configToastPosition.Value = toastPosition;
            }
            y += 34f;

            GUI.Label(new Rect(x, y, leftW, 18f),
                new GUIContent("DURATION: " + toastDurationSec.ToString("0.0") + "s"), labelStyle);
            y += 20f;
            float nd = GUI.HorizontalSlider(new Rect(x, y, leftW, 18f), toastDurationSec, 0.5f, 12f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(nd, toastDurationSec))
            {
                toastDurationSec = nd;
                if (configToastDuration != null) configToastDuration.Value = toastDurationSec;
            }
            y += 26f;

            GUI.Label(new Rect(x, y, leftW, 18f),
                new GUIContent("WIDTH: " + toastWidthScale.ToString("0.00") + "x"), labelStyle);
            y += 20f;
            float nw = GUI.HorizontalSlider(new Rect(x, y, leftW, 18f), toastWidthScale, 0.6f, 1.4f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(nw, toastWidthScale))
            {
                toastWidthScale = nw;
                if (configToastWidthScale != null) configToastWidthScale.Value = toastWidthScale;
            }
            y += 24f;

            GUI.Label(new Rect(x, y, leftW, 18f),
                new GUIContent("HEIGHT: " + toastHeightScale.ToString("0.00") + "x"), labelStyle);
            y += 20f;
            float nh = GUI.HorizontalSlider(new Rect(x, y, leftW, 18f), toastHeightScale, 0.8f, 1.6f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(nh, toastHeightScale))
            {
                toastHeightScale = nh;
                if (configToastHeightScale != null) configToastHeightScale.Value = toastHeightScale;
            }
            y += 24f;

            GUI.Label(new Rect(x, y, leftW, 18f),
                new GUIContent("FONT: " + toastFontScale.ToString("0.00") + "x"), labelStyle);
            y += 20f;
            float nf = GUI.HorizontalSlider(new Rect(x, y, leftW, 18f), toastFontScale, 0.8f, 1.5f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(nf, toastFontScale))
            {
                toastFontScale = nf;
                if (configToastFontScale != null) configToastFontScale.Value = toastFontScale;
            }
            y += 24f;

            GUI.Label(new Rect(x, y, leftW, 18f),
                new GUIContent("MARGIN: " + Mathf.RoundToInt(toastMargin) + "px"), labelStyle);
            y += 20f;
            float nm = GUI.HorizontalSlider(new Rect(x, y, leftW, 18f), toastMargin, 4f, 80f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(nm, toastMargin))
            {
                toastMargin = nm;
                if (configToastMargin != null) configToastMargin.Value = toastMargin;
            }
            y += 30f;

            GUI.Label(new Rect(x + 2f, y, leftW - 4f, 22f), new GUIContent("COLOR"), headerStyle);
            y += 24f;
            GUI.Label(new Rect(x, y, leftW, 18f),
                new GUIContent("BG HUE: " + toastBgHue.ToString("0.00")), labelStyle);
            y += 20f;
            float nhue = GUI.HorizontalSlider(new Rect(x, y, leftW, 18f), toastBgHue, 0f, 1f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(nhue, toastBgHue))
            {
                toastBgHue = nhue;
                if (configToastBgHue != null) configToastBgHue.Value = toastBgHue;
            }
            y += 24f;
            GUI.Label(new Rect(x, y, leftW, 18f),
                new GUIContent("BG OPACITY: " + Mathf.RoundToInt(toastBgOpacity * 100f) + "%"), labelStyle);
            y += 20f;
            float nop = GUI.HorizontalSlider(new Rect(x, y, leftW, 18f), toastBgOpacity, 0.2f, 1f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(nop, toastBgOpacity))
            {
                toastBgOpacity = nop;
                if (configToastBgOpacity != null) configToastBgOpacity.Value = toastBgOpacity;
            }
            y += 24f;
            GUI.Label(new Rect(x, y, leftW, 18f),
                new GUIContent("TEXT BRIGHTNESS: " + Mathf.RoundToInt(toastTextBrightness * 100f) + "%"), labelStyle);
            y += 20f;
            float ntb = GUI.HorizontalSlider(new Rect(x, y, leftW, 18f), toastTextBrightness, 0.4f, 1f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(ntb, toastTextBrightness))
            {
                toastTextBrightness = ntb;
                if (configToastTextBrightness != null) configToastTextBrightness.Value = toastTextBrightness;
            }

            float ry = startY;
            GUI.Label(new Rect(rightX, ry, rightW, 22f), new GUIContent("ANIMATION"), headerStyle);
            ry += 26f;

            string[] animNames = { "NONE", "FADE", "SLIDE", "FADE + SLIDE" };
            if (GUI.Button(new Rect(rightX, ry, rightW, 28f),
                new GUIContent("MODE: " + animNames[Mathf.Clamp(toastAnimMode, 0, 3)]), buttonStyle))
            {
                toastAnimMode = (toastAnimMode + 1) % 4;
                if (configToastAnimMode != null) configToastAnimMode.Value = toastAnimMode;
            }
            ry += 34f;

            GUI.Label(new Rect(rightX, ry, rightW, 18f),
                new GUIContent("ANIM TIME: " + toastAnimSeconds.ToString("0.00") + "s"), labelStyle);
            ry += 20f;
            float na = GUI.HorizontalSlider(new Rect(rightX, ry, rightW, 18f), toastAnimSeconds, 0.05f, 1f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(na, toastAnimSeconds))
            {
                toastAnimSeconds = na;
                if (configToastAnimSeconds != null) configToastAnimSeconds.Value = toastAnimSeconds;
            }
            ry += 32f;

            GUI.Label(new Rect(rightX, ry, rightW, 22f), new GUIContent("WHAT TO SHOW"), headerStyle);
            ry += 26f;

            ry = DrawToastFilterToggle(rightX, ry, rightW, "System (join/leave/errors)", ref toastNotifySystem, configToastNotifySystem);
            ry = DrawToastFilterToggle(rightX, ry, rightW, "Social (PM / chat)", ref toastNotifySocial, configToastNotifySocial);
            ry = DrawToastFilterToggle(rightX, ry, rightW, "Gameplay (spec / TP / fly)", ref toastNotifyGameplay, configToastNotifyGameplay);
            ry = DrawToastFilterToggle(rightX, ry, rightW, "Host tools", ref toastNotifyHost, configToastNotifyHost);
            ry = DrawToastFilterToggle(rightX, ry, rightW, "Server scan / browser", ref toastNotifyScan, configToastNotifyScan);
            ry = DrawToastFilterToggle(rightX, ry, rightW, "Screenshots", ref toastNotifyScreenshot, configToastNotifyScreenshot);
            ry += 8f;

            if (GUI.Button(new Rect(rightX, ry, rightW, 32f),
                new GUIContent("PREVIEW TOAST"), buttonStyle))
            {
                toastQueue.Enqueue("˚ʚ♡ɞ˚ Preview notification");
                toastCategoryQueue.Enqueue("system");
                if (string.IsNullOrEmpty(toastMessage) || Time.unscaledTime > toastUntil)
                    PromoteNextToast();
            }
            ry += 40f;

            GUI.Label(new Rect(rightX, ry, rightW, 60f),
                new GUIContent(
                    "Settings save to BepInEx config.\n" +
                    "Toasts still queue if several fire at once."),
                smallStyle);
        }


        private void DrawQuickActionBar()
        {
            if (!showQuickActionBar || cleanUiActive)
                return;

            string[] labels = {
                flyingNoclipActive ? "FLY ON" : "FLY",
                "WP",
                spectating ? "STOP SPEC" : "SPEC",
                "REJOIN",
                "SHOT",
                friendsOnlyMode ? "FRIENDS" : "ALL"
            };
            float btnW = 72f;
            float btnH = 26f;
            float gap = 4f;
            float totalW = labels.Length * (btnW + gap) - gap;
            float x = (Screen.width - totalW) * 0.5f;
            float y = Screen.height - btnH - 12f;

            Color prev = GUI.color;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect r = new Rect(x + i * (btnW + gap), y, btnW, btnH);
                GUIStyle st = buttonStyle != null ? buttonStyle : GUI.skin.button;
                if (GUI.Button(r, new GUIContent(labels[i]), st))
                {
                    switch (i)
                    {
                        case 0: ToggleFlyingNoclip(); break;
                        case 1: QuickSaveWaypoint(); ShowToast("Waypoint saved", "gameplay"); break;
                        case 2:
                            if (spectating) { StopSpectating(); ShowToast("Spectate stopped", "gameplay"); }
                            else SpectateCycleFromHotkey(1);
                            break;
                        case 3: TryRejoinLastRoom(); break;
                        case 4: ToggleCleanUiAndScreenshot(); break;
                        case 5:
                            friendsOnlyMode = !friendsOnlyMode;
                            ShowToast(friendsOnlyMode ? "Friends only" : "Show all players", "gameplay");
                            break;
                    }
                }
            }
            GUI.color = prev;
        }

        private float DrawToastFilterToggle(float x, float y, float w, string label, ref bool value, ConfigEntry<bool> cfg)
        {
            if (GUI.Button(new Rect(x, y, w, 26f),
                new GUIContent((value ? "ON  ·  " : "OFF ·  ") + label),
                value ? selectedButtonStyle : buttonStyle))
            {
                value = !value;
                if (cfg != null) cfg.Value = value;
            }
            return y + 30f;
        }

        private bool TryCreateTextureFromBytes(byte[] data, out Texture2D tex)
        {
            tex = null;
            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;

                // Try instance LoadImage(byte[], bool) or LoadImage(byte[])

                var inst = typeof(Texture2D).GetMethod("LoadImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(byte[]), typeof(bool) }, null);
                bool loaded = false;
                if (inst != null)
                {
                    loaded = (bool)inst.Invoke(tex, new object[] { data, false });
                }
                else
                {
                    inst = typeof(Texture2D).GetMethod("LoadImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(byte[]) }, null);
                    if (inst != null)
                    {
                        loaded = (bool)inst.Invoke(tex, new object[] { data });
                    }
                    else
                    {
                        // Fallback: UnityEngine.ImageConversion static helper (some Unity versions)
                        Type ic = Type.GetType("UnityEngine.ImageConversion, UnityEngine");
                        if (ic != null)
                        {
                            var mi = ic.GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                            if (mi != null)
                            {
                                loaded = (bool)mi.Invoke(null, new object[] { tex, data, false });
                            }
                            else
                            {
                                mi = ic.GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]) });
                                if (mi != null) loaded = (bool)mi.Invoke(null, new object[] { tex, data });
                            }
                        }
                    }
                }

                if (loaded)
                {
                    // Re-apply wrap/filter mode after LoadImage, since it can reset them.
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    return true;
                }
            }
            catch { }
            if (tex != null) UnityEngine.Object.Destroy(tex);
            tex = null;
            return false;
        }

        // ============================================================
        // SETTINGS PANEL (top-right SETTINGS button → tab 10)
        // ============================================================
        private void DrawSettingsPanel(float x, float y, float width, float maxHeight)
        {
            float startY = y;
            float leftW = width * 0.48f;
            float gap = 16f;
            float rightX = x + leftW + gap;
            float rightW = width - leftW - gap;

            // ---- LEFT: KEYBINDS (also still on Testing — left alone) ----
            GUI.Label(new Rect(x, y, leftW, 22f), new GUIContent("Binds"), headerStyle);
            y += 28f;

            string menuKeyLabel = menuToggleKey != null ? menuToggleKey.Value.ToString() : "Insert";
            string noclipKeyLabel = noclipToggleKey != null ? noclipToggleKey.Value.ToString() : "F1";
            string wpKeyLabel = waypointQuickSaveKey != null ? waypointQuickSaveKey.Value.ToString() : "F6";
            string flyUpLabel = flySpeedUpKey != null ? flySpeedUpKey.Value.ToString() : "F3";
            string flyDownLabel = flySpeedDownKey != null ? flySpeedDownKey.Value.ToString() : "F2";

            if (waitingForKeyRebind)
            {
                GUI.Label(new Rect(x, y, leftW, 28f),
                    new GUIContent("Press key…"),
                    accentLabelStyle != null ? accentLabelStyle : labelStyle);
                y += 36f;
            }
            else
            {
                if (GUI.Button(new Rect(x, y, leftW, 28f), new GUIContent("Menu toggle: " + menuKeyLabel), buttonStyle))
                {
                    waitingForKeyRebind = true;
                    rebindTarget = "menu";
                }
                y += 32f;
                if (GUI.Button(new Rect(x, y, leftW, 28f), new GUIContent("Fly noclip: " + noclipKeyLabel), buttonStyle))
                {
                    waitingForKeyRebind = true;
                    rebindTarget = "noclip";
                }
                y += 32f;
                if (GUI.Button(new Rect(x, y, leftW, 28f), new GUIContent("Fly speed +: " + flyUpLabel), buttonStyle))
                {
                    waitingForKeyRebind = true;
                    rebindTarget = "flyUp";
                }
                y += 32f;
                if (GUI.Button(new Rect(x, y, leftW, 28f), new GUIContent("Fly speed -: " + flyDownLabel), buttonStyle))
                {
                    waitingForKeyRebind = true;
                    rebindTarget = "flyDown";
                }
                y += 32f;
                if (GUI.Button(new Rect(x, y, leftW, 28f), new GUIContent("Quick waypoint: " + wpKeyLabel), buttonStyle))
                {
                    waitingForKeyRebind = true;
                    rebindTarget = "waypoint";
                }
                y += 32f;
                string specNextLabel = spectateNextKey != null ? spectateNextKey.Value.ToString() : "]";
                string specPrevLabel = spectatePrevKey != null ? spectatePrevKey.Value.ToString() : "[";
                if (GUI.Button(new Rect(x, y, leftW, 28f), new GUIContent("Spec next: " + specNextLabel), buttonStyle))
                {
                    waitingForKeyRebind = true;
                    rebindTarget = "specNext";
                }
                y += 32f;
                if (GUI.Button(new Rect(x, y, leftW, 28f), new GUIContent("Spec prev: " + specPrevLabel), buttonStyle))
                {
                    waitingForKeyRebind = true;
                    rebindTarget = "specPrev";
                }
                y += 32f;
                string stopSpecLabel = stopSpectateKey != null ? stopSpectateKey.Value.ToString() : "Mouse2";
                if (GUI.Button(new Rect(x, y, leftW, 28f), new GUIContent("Stop spectate: " + stopSpecLabel), buttonStyle))
                {
                    waitingForKeyRebind = true;
                    rebindTarget = "stopSpec";
                }
                y += 32f;
                string freeMouseLabel = freeMouseHoldKey != null ? freeMouseHoldKey.Value.ToString() : "LeftAlt";
                if (GUI.Button(new Rect(x, y, leftW, 28f), new GUIContent("Free mouse (hold): " + freeMouseLabel), buttonStyle))
                {
                    waitingForKeyRebind = true;
                    rebindTarget = "freeMouse";
                }
                y += 36f;
            }

            GUI.Label(new Rect(x, y, leftW, 40f),
                new GUIContent("Click, then press a key or mouse btn · Esc cancels"),
                smallStyle);
            y += 48f;

            // Soft teleport (moved from Testing)
            GUI.Label(new Rect(x, y, leftW, 22f), new GUIContent("Soft TP"), headerStyle);
            y += 26f;
            if (GUI.Button(new Rect(x, y, leftW, 28f),
                new GUIContent(softTeleportEnabled ? "SOFT TELEPORT: ON" : "SOFT TELEPORT: OFF"), buttonStyle))
            {
                softTeleportEnabled = !softTeleportEnabled;
                if (configSoftTeleport != null)
                    configSoftTeleport.Value = softTeleportEnabled;
            }
            y += 34f;
            GUI.Label(new Rect(x, y, leftW, 20f),
                new GUIContent("DURATION: " + softTeleportDuration.ToString("0.00") + "s"), labelStyle);
            y += 22f;
            softTeleportDuration = GUI.HorizontalSlider(
                new Rect(x, y, leftW, 18f),
                softTeleportDuration, 0.05f, 1.5f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);

            // ---- RIGHT: MENU LOOK ----
            float ry = startY;
            GUI.Label(new Rect(rightX, ry, rightW, 22f), new GUIContent("Look"), headerStyle);
            ry += 28f;

            string hueState = menuColorGreyscale
                ? "greyscale"
                : (menuHueCycling ? "cycling" : "locked");
            string hueLabel = "HUE: " + Mathf.RoundToInt(backgroundHue * 360f) + "° · " + hueState;
            GUI.Label(new Rect(rightX, ry, rightW, 20f), new GUIContent(hueLabel), labelStyle);
            ry += 22f;

            float newHue = GUI.HorizontalSlider(
                new Rect(rightX, ry, rightW, 18f),
                backgroundHue, 0f, 1f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(newHue, backgroundHue))
            {
                backgroundHue = newHue;
                lastStyledHue = -1f;
                if (configBackgroundHue != null)
                    configBackgroundHue.Value = backgroundHue;
            }
            ry += 28f;

            GUI.Label(new Rect(rightX, ry, rightW, 20f),
                new GUIContent("OPACITY: " + Mathf.RoundToInt(backgroundOpacity * 100f) + "%"), labelStyle);
            ry += 22f;
            float newOpacity = GUI.HorizontalSlider(
                new Rect(rightX, ry, rightW, 18f),
                backgroundOpacity, 0f, 1f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(newOpacity, backgroundOpacity))
            {
                backgroundOpacity = newOpacity;
                if (configBackgroundOpacity != null)
                    configBackgroundOpacity.Value = backgroundOpacity;
            }
            ry += 28f;
            // UI Scale in TESTING

            GUI.Label(new Rect(rightX, ry, rightW, 20f),
                new GUIContent("BG ANIM FPS: " + BackgroundFramesPerSecond.ToString("0")), labelStyle);
            ry += 22f;
            float newFps = GUI.HorizontalSlider(
                new Rect(rightX, ry, rightW, 18f),
                BackgroundFramesPerSecond, 1f, 60f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(newFps, BackgroundFramesPerSecond))
            {
                BackgroundFramesPerSecond = newFps;
                if (configBackgroundFPS != null)
                    configBackgroundFPS.Value = BackgroundFramesPerSecond;
            }
            ry += 32f;

            float modeGap = 6f;
            float modeBtnW = (rightW - modeGap * 2f) / 3f;
            GUIStyle sel = selectedButtonStyle != null ? selectedButtonStyle : buttonStyle;
            GUIStyle nrm = buttonStyle;

            bool cycleActive = !menuColorGreyscale && menuHueCycling;
            if (cycleActive) BeginAccentUI(); else BeginButtonUI();
            if (GUI.Button(new Rect(rightX, ry, modeBtnW, 28f), new GUIContent("RGB CYCLE"), cycleActive ? sel : nrm))
            {
                menuColorGreyscale = false;
                menuHueCycling = true;
                lastStyledHue = -1f;
                if (configMenuGreyscale != null)
                    configMenuGreyscale.Value = false;
            }
            EndUIColor();

            bool lockActive = !menuColorGreyscale && !menuHueCycling;
            if (lockActive) BeginAccentUI(); else BeginButtonUI();
            if (GUI.Button(new Rect(rightX + modeBtnW + modeGap, ry, modeBtnW, 28f), new GUIContent("LOCK"), lockActive ? sel : nrm))
            {
                menuColorGreyscale = false;
                menuHueCycling = false;
                lastStyledHue = -1f;
                if (configBackgroundHue != null)
                    configBackgroundHue.Value = backgroundHue;
                if (configMenuGreyscale != null)
                    configMenuGreyscale.Value = false;
            }
            EndUIColor();

            if (menuColorGreyscale) BeginAccentUI(); else BeginButtonUI();
            if (GUI.Button(new Rect(rightX + (modeBtnW + modeGap) * 2f, ry, modeBtnW, 28f), new GUIContent("GREY"), menuColorGreyscale ? sel : nrm))
            {
                menuColorGreyscale = true;
                menuHueCycling = false;
                lastStyledHue = -1f;
                if (configBackgroundHue != null)
                    configBackgroundHue.Value = backgroundHue;
                if (configMenuGreyscale != null)
                    configMenuGreyscale.Value = true;
            }
            EndUIColor();
            ry += 34f;

            // Cycle speed (only meaningful while RGB CYCLE is on)
            GUI.Label(new Rect(rightX, ry, rightW, 18f),
                new GUIContent("Cycle " + Mathf.RoundToInt(menuHueCycleSeconds) + "s"), smallStyle);
            ry += 18f;
            float newCycle = GUI.HorizontalSlider(
                new Rect(rightX, ry, rightW, 16f),
                menuHueCycleSeconds, MenuHueCycleSecondsMin, MenuHueCycleSecondsMax,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(newCycle, menuHueCycleSeconds))
                menuHueCycleSeconds = newCycle;
            ry += 28f;

            GUI.Label(new Rect(rightX, ry, rightW, 18f), new GUIContent("Config"), smallStyle);
            ry += 20f;
            float half = (rightW - 8f) * 0.5f;
            if (GUI.Button(new Rect(rightX, ry, half, 26f), new GUIContent("Export"), buttonStyle))
                ExportQoLConfig();
            if (GUI.Button(new Rect(rightX + half + 8f, ry, half, 26f), new GUIContent("Import"), buttonStyle))
                ImportQoLConfig();
        }

        /// <summary>
        /// Export binds, favorites, and mod presets to a single text file next to the plugin config.
        /// </summary>

        // ============================================================
        // QOL PANEL (sidebar QOL → tab 11)
        // ============================================================
        private void DrawQoLPanel(float x, float y, float width, float maxHeight)
        {
            float startY = y;
            float leftW = width * 0.48f;
            float gap = 16f;
            float rightX = x + leftW + gap;
            float rightW = width - leftW - gap;

            // ---- LEFT: FLIGHT ----
            GUI.Label(new Rect(x, y, leftW, 22f), new GUIContent("NOCLIP"), headerStyle);
            y += 28f;

            if (GUI.Button(new Rect(x, y, leftW, 32f),
                new GUIContent(flyingNoclipActive ? "FLY NOCLIP: ON" : "FLY NOCLIP: OFF"),
                flyingNoclipActive ? selectedButtonStyle : buttonStyle))
            {
                ToggleFlyingNoclip();
            }
            y += 38f;

            GUI.Label(new Rect(x, y, leftW, 20f),
                new GUIContent("SPEED: " + flySpeed.ToString("0")), labelStyle);
            y += 22f;
            float newFly = GUI.HorizontalSlider(
                new Rect(x, y, leftW, 18f),
                flySpeed, 5f, 500f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(newFly, flySpeed))
            {
                flySpeed = newFly;
                if (configFlySpeed != null) configFlySpeed.Value = flySpeed;
            }
            y += 26f;

            if (GUI.Button(new Rect(x, y, leftW * 0.48f, 28f), new GUIContent("SPEED -10"), buttonStyle))
                AdjustFlySpeed(-10f);
            if (GUI.Button(new Rect(x + leftW * 0.52f, y, leftW * 0.48f, 28f), new GUIContent("SPEED +10"), buttonStyle))
                AdjustFlySpeed(10f);
            y += 34f;

            string flyUpHint = flySpeedUpKey != null ? flySpeedUpKey.Value.ToString() : "F3";
            string flyDownHint = flySpeedDownKey != null ? flySpeedDownKey.Value.ToString() : "F2";
            string flyToggleHint = noclipToggleKey != null ? noclipToggleKey.Value.ToString() : "F1";
            GUI.Label(new Rect(x, y, leftW, 56f),
                new GUIContent(
                    "WASD · Space up · Shift down.\n" +
                    "Hotkeys: toggle " + flyToggleHint + " · +" + flyUpHint + " · -" + flyDownHint + "\n" +
                    flyDebugStatus),
                smallStyle);
            y += 64f;

            GUI.Label(new Rect(x, y, leftW, 22f), new GUIContent("Player List"), headerStyle);
            y += 26f;
            if (GUI.Button(new Rect(x, y, leftW, 28f),
                new GUIContent(showPlayerOverlay ? "Player List: ON" : "Player List: OFF"), buttonStyle))
                showPlayerOverlay = !showPlayerOverlay;
            y += 34f;

            // ---- LEAVE / AUTO DESYNC QoL ----
            GUI.Label(new Rect(x, y, leftW, 22f), new GUIContent("Misc Features"), headerStyle);
            y += 26f;

            if (GUI.Button(new Rect(x, y, leftW, 28f),
                new GUIContent(destroyBodyOnLeave ? "Clean Left Users : ON" : "Clean Left Users : OFF"),
                buttonStyle))
            {
                destroyBodyOnLeave = !destroyBodyOnLeave;
                if (configDestroyBodyOnLeave != null)
                    configDestroyBodyOnLeave.Value = destroyBodyOnLeave;
            }
            y += 32f;

            if (GUI.Button(new Rect(x, y, leftW, 28f),
                new GUIContent(welcomeMessageOnJoin ? "Welcome ON" : "Welcome OFF"),
                buttonStyle))
            {
                welcomeMessageOnJoin = !welcomeMessageOnJoin;
                if (configWelcomeMessageOnJoin != null)
                    configWelcomeMessageOnJoin.Value = welcomeMessageOnJoin;
            }
            y += 32f;

            if (GUI.Button(new Rect(x, y, leftW, 28f),
                new GUIContent(publishRoomPlayers ? "Publish: ON" : "Publish: OFF"),
                buttonStyle))
            {
                publishRoomPlayers = !publishRoomPlayers;
                if (configPublishRoomPlayers != null)
                    configPublishRoomPlayers.Value = publishRoomPlayers;
                if (publishRoomPlayers && PhotonNetwork.IsMasterClient)
                    PublishRoomPlayerList(true);
            }
            y += 30f;
            GUI.Label(new Rect(x, y, leftW, 48f),
                new GUIContent(
                    "Hover names need: host runs mod + Publish ON.\n" +
                    "Best when the room was CREATED with this mod\n" +
                    "(lobby prop injected on CreateRoom)."),
                smallStyle);

            // ---- RIGHT: RADAR + REWARDS ----
            float ry = startY;
            GUI.Label(new Rect(rightX, ry, rightW, 22f), new GUIContent("(WIP) Radar"), headerStyle);
            ry += 28f;

            if (GUI.Button(new Rect(rightX, ry, rightW * 0.48f, 28f),
                new GUIContent(showPlayerRadar ? "RADAR: ON" : "RADAR: OFF"), buttonStyle))
                showPlayerRadar = !showPlayerRadar;
            if (GUI.Button(new Rect(rightX + rightW * 0.52f, ry, rightW * 0.48f, 28f),
                new GUIContent(radarRotateWithCamera ? "ROTATE: CAM" : "ROTATE: N"), buttonStyle))
                radarRotateWithCamera = !radarRotateWithCamera;
            ry += 34f;

            if (GUI.Button(new Rect(rightX, ry, rightW * 0.48f, 28f),
                new GUIContent(radarShowNames ? "NAMES: ON" : "NAMES: OFF"), buttonStyle))
                radarShowNames = !radarShowNames;
            if (GUI.Button(new Rect(rightX + rightW * 0.52f, ry, rightW * 0.48f, 28f),
                new GUIContent(radarShowDistance ? "DIST: ON" : "DIST: OFF"), buttonStyle))
                radarShowDistance = !radarShowDistance;
            ry += 34f;

            GUI.Label(new Rect(rightX, ry, 90f, 20f),
                new GUIContent("RANGE: " + radarRange.ToString("0") + "m"), labelStyle);
            radarRange = GUI.HorizontalSlider(
                new Rect(rightX + 95f, ry + 2f, rightW - 95f, 18f),
                radarRange, 5f, 100f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            ry += 36f;

            GUI.Label(new Rect(rightX, ry, rightW, 22f), new GUIContent("(WIP) Cash/Stars"), headerStyle);
            ry += 26f;

            float rewardButtonWidth = (rightW - 8f) / 2f;
            float rewardButtonHeight = 30f;

            if (GUI.Button(new Rect(rightX, ry, rewardButtonWidth, rewardButtonHeight),
                new GUIContent("MAX MONEY"), buttonStyle))
                GiveMyMaxMoney();
            if (GUI.Button(new Rect(rightX + rewardButtonWidth + 8f, ry, rewardButtonWidth, rewardButtonHeight),
                new GUIContent("MAX STARS"), buttonStyle))
                GiveMyMaxStars();
            ry += rewardButtonHeight + 6f;

            bool canRoomReward = PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
            GUIStyle rewardButtonStyle = canRoomReward ? buttonStyle : GUI.skin.button;

            if (GUI.Button(new Rect(rightX, ry, rewardButtonWidth, rewardButtonHeight),
                new GUIContent("ALL MONEY"), rewardButtonStyle) && canRoomReward)
                GiveAllMaxMoney();
            if (GUI.Button(new Rect(rightX + rewardButtonWidth + 8f, ry, rewardButtonWidth, rewardButtonHeight),
                new GUIContent("ALL STARS"), rewardButtonStyle) && canRoomReward)
                StartGiveAllMaxStars();
            ry += rewardButtonHeight + 10f;

            if (!string.IsNullOrEmpty(rewardStatus) && Time.unscaledTime < rewardStatusUntil)
            {
                GUI.Label(new Rect(rightX, ry, rightW, 22f), new GUIContent(rewardStatus), smallStyle);
            }
            else if (!canRoomReward)
            {
                GUI.Label(new Rect(rightX, ry, rightW, 32f),
                    new GUIContent("ALL MONEY / ALL STARS need host."),
                    smallStyle);
            }
        }

        private void DrawMiscPanel(float x, float y, float width, float maxHeight)
        {
            GUI.Label(new Rect(x, y, width, 24f), new GUIContent("MISC"), headerStyle);
            y += 36f;

            GUI.Label(new Rect(x, y, width, 80f),
                new GUIContent(
                    "This tab is mostly retired.\n\n" +
                    "• Player Radar, Overlay, Rewards → QOL (sidebar)\n" +
                    "• Background color & Keybinds → SETTINGS (top-right)"),
                smallStyle);
            y += 100f;

            if (GUI.Button(new Rect(x, y, 160f, 32f), new GUIContent("OPEN QOL"), buttonStyle))
                tab = 11;
            if (GUI.Button(new Rect(x + 170f, y, 160f, 32f), new GUIContent("OPEN SETTINGS"), buttonStyle))
                tab = 10;
        }

        private IEnumerator LoadBackgroundAfterStartup()
        {
            yield return null;
            yield return new WaitForEndOfFrame();

            LoadMenuBackground();
        }

        private void LoadMenuBackground()
        {
            // Disabled: optimized build does not load or decode the embedded PNG background.
            // Keep menuBackground null and draw a tint only in DrawMenuBackground.
            if (menuBackground != null)
            {
                UnityEngine.Object.Destroy(menuBackground);
                menuBackground = null;
            }
        }

        private static MethodInfo drawTextureMethod;
        private static bool drawTextureMethodResolved;

        private static void InvokeDrawTexture(Rect position, Texture image, ScaleMode scaleMode)
        {
            if (!drawTextureMethodResolved)
            {
                drawTextureMethodResolved = true;
                try
                {
                    drawTextureMethod = typeof(GUI).GetMethod(
                        "DrawTexture",
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        new Type[] { typeof(Rect), typeof(Texture), typeof(ScaleMode) },
                        null);
                }
                catch { drawTextureMethod = null; }
            }

            if (drawTextureMethod != null)
            {
                try
                {
                    drawTextureMethod.Invoke(null, new object[] { position, image, scaleMode });
                }
                catch { }
            }
        }

        private static MethodInfo drawTextureWithTexCoordsMethod;
        private static bool drawTextureWithTexCoordsMethodResolved;

        private static void InvokeDrawTextureWithTexCoords(Rect position, Texture image, Rect texCoords)
        {
            if (!drawTextureWithTexCoordsMethodResolved)
            {
                drawTextureWithTexCoordsMethodResolved = true;
                try
                {
                    drawTextureWithTexCoordsMethod = typeof(GUI).GetMethod(
                        "DrawTextureWithTexCoords",
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        new Type[] { typeof(Rect), typeof(Texture), typeof(Rect) },
                        null);
                }
                catch { drawTextureWithTexCoordsMethod = null; }
            }

            if (drawTextureWithTexCoordsMethod != null)
            {
                try
                {
                    drawTextureWithTexCoordsMethod.Invoke(null, new object[] { position, image, texCoords });
                    return;
                }
                catch { }
            }

            // Fallback: draw the whole texture if tex-coord drawing isn't available.
            InvokeDrawTexture(position, image, ScaleMode.StretchToFill);
        }

        private void DrawMenuBackground()
        {
            Color prev = GUI.color;
            Color tint = Color.HSVToRGB(backgroundHue, MenuSat(0.5f), 0.15f);
            tint.a = backgroundOpacity;
            GUI.color = tint;
            GUI.Box(new Rect(0f, 0f, menuRect.width, menuRect.height), "");
            GUI.color = prev;
        }

        // ============================================================
        // COLLAPSIBLE SECTIONS HELPER
        // ============================================================
        private bool DrawCollapsibleHeader(string key, string title, float x, float y, float width)
        {
            if (!sectionCollapsed.ContainsKey(key))
                sectionCollapsed[key] = false;

            bool collapsed = sectionCollapsed[key];
            string label = (collapsed ? "▶ " : "▼ ") + title;
            if (GUI.Button(new Rect(x, y, width, 26f), new GUIContent(label), buttonStyle))
                sectionCollapsed[key] = !collapsed;

            return !sectionCollapsed[key];
        }

        private Texture2D CreateUIColor(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void CreateStyles()
        {
            if (stylesCreated)
                return;

            /*
             * IMPORTANT:
             * KoboldKare's referenced UnityEngine GUI API is more limited
             * than the normal Unity IMGUI API. In this project:
             *
             *   GUIStyle.border       -> read-only
             *   GUIStyle.padding      -> read-only
             *   GUIStyle.hover        -> unavailable
             *   GUIStyle.active       -> unavailable
             *   GUIStyleState.background -> unavailable
             *   RectOffset(4 args)    -> unavailable
             *
             * Therefore the UI uses only the GUIStyle members that this
             * game's actual references expose. Visual color/background
             * differences are applied with GUI.color in the drawing code.
             */

            Color accent = Color.HSVToRGB(Mathf.Repeat(backgroundHue, 1f), 0.72f, 1f);
            Color muted = Color.HSVToRGB(Mathf.Repeat(backgroundHue, 1f), 0.18f, 0.72f);
            lastStyledHue = backgroundHue;

            // Window — use box (not skin.window) to avoid the default black title bar chrome.
            windowStyle = new GUIStyle(GUI.skin.box);
            windowStyle.fontSize = 12;
            windowStyle.alignment = TextAnchor.UpperLeft;
            windowStyle.normal.textColor = Color.white;

            // Sidebar
            sidebarStyle = new GUIStyle(GUI.skin.button);
            sidebarStyle.fontSize = 12;
            sidebarStyle.alignment = TextAnchor.MiddleLeft;
            sidebarStyle.normal.textColor = new Color(0.72f, 0.74f, 0.80f);

            // Selected sidebar item.
            sidebarSelectedStyle = new GUIStyle(sidebarStyle);
            sidebarSelectedStyle.normal.textColor = Color.white;
            sidebarSelectedStyle.fontStyle = FontStyle.Bold;

            // Top bar.
            topBarStyle = new GUIStyle(GUI.skin.box);
            topBarStyle.fontSize = 11;
            topBarStyle.alignment = TextAnchor.MiddleLeft;
            topBarStyle.normal.textColor = new Color(0.78f, 0.80f, 0.86f);

            // Cards.
            cardStyle = new GUIStyle(GUI.skin.box);
            cardStyle.fontSize = 11;
            cardStyle.alignment = TextAnchor.UpperLeft;
            cardStyle.normal.textColor = new Color(0.88f, 0.89f, 0.93f);

            // Generic labels used by existing feature panels.
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 13;
            labelStyle.normal.textColor = Color.white;

            // Standard button.
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 12;
            buttonStyle.alignment = TextAnchor.MiddleCenter;
            buttonStyle.normal.textColor = new Color(0.88f, 0.89f, 0.93f);

            // Selected button.
            selectedButtonStyle = new GUIStyle(buttonStyle);
            selectedButtonStyle.normal.textColor = Color.white;
            selectedButtonStyle.fontStyle = FontStyle.Bold;

            // Modern button.
            modernButtonStyle = new GUIStyle(buttonStyle);
            modernButtonStyle.fontSize = 11;

            modernSelectedButtonStyle = new GUIStyle(modernButtonStyle);
            modernSelectedButtonStyle.normal.textColor = Color.white;
            modernSelectedButtonStyle.fontStyle = FontStyle.Bold;

            // Headers.
            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 18;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = Color.white;

            sectionStyle = new GUIStyle(GUI.skin.label);
            sectionStyle.fontSize = 13;
            sectionStyle.fontStyle = FontStyle.Bold;
            sectionStyle.normal.textColor = Color.white;

            valueStyle = new GUIStyle(GUI.skin.label);
            valueStyle.fontSize = 12;
            valueStyle.fontStyle = FontStyle.Bold;
            valueStyle.normal.textColor = new Color(0.90f, 0.91f, 0.96f);

            accentLabelStyle = new GUIStyle(GUI.skin.label);
            accentLabelStyle.fontSize = 14;
            accentLabelStyle.fontStyle = FontStyle.Bold;
            accentLabelStyle.normal.textColor = accent;

            smallStyle = new GUIStyle(GUI.skin.label);
            smallStyle.fontSize = 11;
            smallStyle.normal.textColor = new Color(0.78f, 0.79f, 0.84f);

            modernSmallStyle = new GUIStyle(GUI.skin.label);
            modernSmallStyle.fontSize = 10;
            modernSmallStyle.normal.textColor = muted;

            // ESP.
            espStyle = new GUIStyle(GUI.skin.label);
            espStyle.fontSize = espFontSize;
            espStyle.fontStyle = FontStyle.Bold;
            espStyle.alignment = TextAnchor.MiddleCenter;
            espStyle.normal.textColor = Color.white;

            // Overlay styles.
            overlayHeaderStyle = new GUIStyle(GUI.skin.label);
            overlayHeaderStyle.fontSize = 14;
            overlayHeaderStyle.fontStyle = FontStyle.Bold;
            overlayHeaderStyle.alignment = TextAnchor.UpperRight;
            overlayHeaderStyle.normal.textColor = Color.white;

            overlayInfoStyle = new GUIStyle(GUI.skin.label);
            overlayInfoStyle.fontSize = 11;
            overlayInfoStyle.alignment = TextAnchor.MiddleRight;
            overlayInfoStyle.normal.textColor = new Color(0.90f, 0.90f, 0.93f);

            overlayRoleStyle = new GUIStyle(GUI.skin.label);
            overlayRoleStyle.fontSize = 12;
            overlayRoleStyle.alignment = TextAnchor.MiddleCenter;
            overlayRoleStyle.normal.textColor = new Color(0.90f, 0.90f, 0.93f);

            overlayPlayerStyle = new GUIStyle(GUI.skin.label);
            overlayPlayerStyle.fontSize = 13;
            overlayPlayerStyle.richText = true;
            overlayPlayerStyle.alignment = TextAnchor.MiddleRight;
            overlayPlayerStyle.normal.textColor = Color.white;

            overlayServerStyle = new GUIStyle(GUI.skin.label);
            overlayServerStyle.fontSize = 12;
            overlayServerStyle.fontStyle = FontStyle.Bold;
            overlayServerStyle.alignment = TextAnchor.UpperRight;
            overlayServerStyle.wordWrap = true;
            overlayServerStyle.normal.textColor = Color.white;

            stylesCreated = true;
        }

        private void DestroyUITexture(ref Texture2D texture)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
        }

        private GUIStyle GetEspStyle()
        {
            return espStyle;
        }
    }
}