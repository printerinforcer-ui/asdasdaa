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
        private void BindConfig()
        {
            Logger.LogInfo("[ZexQoL] BindConfig: enter");
            // CharCon pattern: menu = KeyboardShortcut, action toggles = single KeyCode
            menuToggleKey = Config.Bind(
                "Controls",
                "Toggle_menu_visibility",
                new KeyboardShortcut(KeyCode.Insert),
                "Keybind to toggle QoL menu (supports modifiers).");

            noclipToggleKey = Config.Bind(
                "Controls",
                "Toggle_Noclip",
                KeyCode.F1,
                "Single key to toggle flying noclip (CharCon-style UnityInput).");

            waypointQuickSaveKey = Config.Bind(
                "Controls",
                "Quick_Waypoint",
                KeyCode.F6,
                "Single key to quick-save a waypoint.");

            flySpeedUpKey = Config.Bind(
                "Controls",
                "Fly_Speed_Up",
                KeyCode.F3,
                "Increase flying noclip speed by 10.");

            flySpeedDownKey = Config.Bind(
                "Controls",
                "Fly_Speed_Down",
                KeyCode.F2,
                "Decrease flying noclip speed by 10.");

            spectateNextKey = Config.Bind(
                "Keybinds",
                "SpectateNext",
                KeyCode.RightBracket,
                "Cycle spectate to next player.");

            spectatePrevKey = Config.Bind(
                "Keybinds",
                "SpectatePrev",
                KeyCode.LeftBracket,
                "Cycle spectate to previous player.");

            stopSpectateKey = Config.Bind(
                "Keybinds",
                "StopSpectate",
                KeyCode.Mouse2,
                "Stop spectating (click/key). Default: Middle Mouse.");

            freeMouseHoldKey = Config.Bind(
                "Keybinds",
                "FreeMouseHold",
                KeyCode.LeftAlt,
                "Hold to unlock mouse cursor (show & free pointer).");

            cleanScreenshotKey = Config.Bind(
                "Keybinds",
                "CleanScreenshot",
                KeyCode.F9,
                "Hide all UI and capture a high-res screenshot.");

            cheatSheetKey = Config.Bind(
                "Keybinds",
                "CheatSheet",
                KeyCode.F10,
                "Hold to show keybind cheat-sheet overlay.");

            configScreenshotSuperSize = Config.Bind(
                "QoL",
                "ScreenshotSuperSize",
                2,
                "Screenshot resolution multiplier (1=native, 2≈2x, 3≈3x, 4≈4x).");

            configNameplateScale = Config.Bind(
                "QoL",
                "NameplateScale",
                1f,
                "Player overlay name scale (0.7–1.8).");

            configNameplateOpacity = Config.Bind(
                "QoL",
                "NameplateOpacity",
                1f,
                "Player overlay name opacity (0.2–1).");

            configSoftTeleport = Config.Bind(
                "Teleport",
                "SoftTeleport",
                true,
                "When true, teleports lerp smoothly instead of snapping.");

            configBrowsePositionRestore = Config.Bind(
                "Teleport",
                "BrowsePositionRestore",
                true,
                "After server-browser refresh/rejoin, restore your saved position (and optional respawn).");

            configAutoSplashOnJoin = Config.Bind(
                "QoL",
                "AutoSplashOnJoin",
                false,
                "Deprecated — splash removed (laggy in large lobbies).");

            configWelcomeMessageOnJoin = Config.Bind(
                "QoL",
                "WelcomeMessageOnJoin",
                true,
                "When you join a room, show a single toast: You've Joined (room name).");

            configUiScale = Config.Bind(
                "QoL",
                "UiScale",
                1f,
                "Menu UI scale (0.75–1.35).");

            configMenuPosX = Config.Bind("UI", "MenuPosX", 90f, "Last menu window X position.");
            configMenuPosY = Config.Bind("UI", "MenuPosY", 45f, "Last menu window Y position.");
            configRadarPosX = Config.Bind("UI", "RadarPosX", 20f, "Last player radar X position.");
            configRadarPosY = Config.Bind("UI", "RadarPosY", 250f, "Last player radar Y position.");
            configRadarSize = Config.Bind("UI", "RadarSize", 230f, "Player radar size (width/height).");
            configShowHudWaypoints = Config.Bind(
                "QoL",
                "ShowHudWaypoints",
                true,
                "Show waypoint quick bar on the HUD.");
            configLastJoinedRoom = Config.Bind(
                "QoL",
                "LastJoinedRoom",
                "",
                "Last multiplayer room name for one-click rejoin.");

            configDestroyBodyOnLeave = Config.Bind(
                "QoL",
                "DestroyBodyOnLeave",
                true,
                "Destroy your local body before leaving a room to reduce clutter.");

            configPublishRoomPlayers = Config.Bind(
                "Servers",
                "PublishRoomPlayers",
                true,
                "When host, publish player names into room properties for server-browser hover.");

            configToastEnabled = Config.Bind("Notifications", "Enabled", true, "Master switch for on-screen toasts.");
            configToastPosition = Config.Bind("Notifications", "Position", 0, "0=TopCenter 1=TopLeft 2=TopRight 3=BottomCenter 4=BottomLeft 5=BottomRight");
            configToastDuration = Config.Bind("Notifications", "DurationSeconds", 2.8f, "How long each toast stays visible.");
            configToastWidthScale = Config.Bind("Notifications", "WidthScale", 1f, "Toast width scale (0.6–1.4).");
            configToastHeightScale = Config.Bind("Notifications", "HeightScale", 1f, "Toast height scale (0.8–1.6).");
            configToastFontScale = Config.Bind("Notifications", "FontScale", 1f, "Toast text scale (0.8–1.5).");
            configToastBgOpacity = Config.Bind("Notifications", "BgOpacity", 0.92f, "Toast background opacity.");
            configToastBgHue = Config.Bind("Notifications", "BgHue", 0.62f, "Toast background hue (0–1).");
            configToastTextBrightness = Config.Bind("Notifications", "TextBrightness", 1f, "Toast text brightness (0.4–1).");
            configToastMargin = Config.Bind("Notifications", "Margin", 18f, "Edge margin in pixels.");
            configToastAnimMode = Config.Bind("Notifications", "AnimMode", 1, "0=None 1=Fade 2=Slide 3=Fade+Slide");
            configToastAnimSeconds = Config.Bind("Notifications", "AnimSeconds", 0.22f, "Animation duration.");
            configToastNotifySystem = Config.Bind("Notifications", "NotifySystem", true, "Room leave/join/errors.");
            configToastNotifySocial = Config.Bind("Notifications", "NotifySocial", true, "Private messages / chat.");
            configToastNotifyGameplay = Config.Bind("Notifications", "NotifyGameplay", true, "Spectate, teleport, fly.");
            configToastNotifyHost = Config.Bind("Notifications", "NotifyHost", true, "Kick/ban/host tools.");
            configToastNotifyScan = Config.Bind("Notifications", "NotifyScan", true, "Server scan / browser.");
            configToastNotifyScreenshot = Config.Bind("Notifications", "NotifyScreenshot", true, "Screenshot results.");

            if (configToastEnabled != null) toastEnabled = configToastEnabled.Value;
            if (configToastPosition != null) toastPosition = Mathf.Clamp(configToastPosition.Value, 0, 5);
            if (configToastDuration != null) toastDurationSec = Mathf.Clamp(configToastDuration.Value, 0.5f, 12f);
            if (configToastWidthScale != null) toastWidthScale = Mathf.Clamp(configToastWidthScale.Value, 0.6f, 1.4f);
            if (configToastHeightScale != null) toastHeightScale = Mathf.Clamp(configToastHeightScale.Value, 0.8f, 1.6f);
            if (configToastFontScale != null) toastFontScale = Mathf.Clamp(configToastFontScale.Value, 0.8f, 1.5f);
            if (configToastBgOpacity != null) toastBgOpacity = Mathf.Clamp01(configToastBgOpacity.Value);
            if (configToastBgHue != null) toastBgHue = Mathf.Repeat(configToastBgHue.Value, 1f);
            if (configToastTextBrightness != null) toastTextBrightness = Mathf.Clamp(configToastTextBrightness.Value, 0.4f, 1f);
            if (configToastMargin != null) toastMargin = Mathf.Clamp(configToastMargin.Value, 4f, 80f);
            if (configToastAnimMode != null) toastAnimMode = Mathf.Clamp(configToastAnimMode.Value, 0, 3);
            if (configToastAnimSeconds != null) toastAnimSeconds = Mathf.Clamp(configToastAnimSeconds.Value, 0.05f, 1f);
            if (configToastNotifySystem != null) toastNotifySystem = configToastNotifySystem.Value;
            if (configToastNotifySocial != null) toastNotifySocial = configToastNotifySocial.Value;
            if (configToastNotifyGameplay != null) toastNotifyGameplay = configToastNotifyGameplay.Value;
            if (configToastNotifyHost != null) toastNotifyHost = configToastNotifyHost.Value;
            if (configToastNotifyScan != null) toastNotifyScan = configToastNotifyScan.Value;
            if (configToastNotifyScreenshot != null) toastNotifyScreenshot = configToastNotifyScreenshot.Value;

            configFlySpeed = Config.Bind(
                "Movement",
                "FlySpeed",
                25f,
                "Flying noclip speed (CharCon range ~5–500).");

            configCameraHeight = Config.Bind(
                "Spectate",
                "CameraHeight",
                0.85f,
                "Default spectate camera height in meters.");

            configCameraDistance = Config.Bind(
                "Spectate",
                "CameraDistance",
                3.25f,
                "Default spectate camera distance in meters.");

            configCameraRotation = Config.Bind(
                "Spectate",
                "CameraRotation",
                0f,
                "Default spectate camera rotation in degrees.");

            configBackgroundHue = Config.Bind(
                "Background",
                "Hue",
                0f,
                "Menu background hue (0-1).");

            configBackgroundOpacity = Config.Bind(
                "Background",
                "Opacity",
                1f,
                "Menu background opacity (0-1).");

            configBackgroundFPS = Config.Bind(
                "Background",
                "FramesPerSecond",
                24f,
                "Menu background animation speed in frames per second.");

            configMenuGreyscale = Config.Bind(
                "Background",
                "Greyscale",
                false,
                "When true, menu chrome is greyscale instead of RGB/hue-tinted.");

            configBannedUserIds = Config.Bind(
                "HostTools",
                "BannedUserIds",
                "",
                "Comma-separated list of banned Photon UserIds/names. Persists across sessions.");

            configFavoritePrefabNames = Config.Bind(
                "Spawner",
                "FavoritePrefabNames",
                "",
                "Comma-separated list of favorited/pinned prefab names. Persists across sessions.");

            configFavoriteRoomNames = Config.Bind(
                "Servers",
                "FavoriteRoomNames",
                "",
                "Comma-separated list of favorite room names. Persists across sessions.");

            configStatsPresets = Config.Bind(
                "Genes",
                "StatsPresets",
                "",
                "Saved gene/stat presets. Format: name=payload;name2=payload2");

            configEquipPresets = Config.Bind(
                "Genes",
                "EquipPresets",
                "",
                "Saved equipment presets. Format: name=payload;name2=payload2");

            configFullPresets = Config.Bind(
                "Genes",
                "FullPresets",
                "",
                "Full presets (character + genes + clothing). Format: name=payload;name2=payload2");

            // Apply loaded values to the live fields used by the UI/logic.
            spectateCameraHeight = configCameraHeight.Value;
            spectateCameraDistance = configCameraDistance.Value;
            spectateCameraRotation = configCameraRotation.Value;

            backgroundHue = configBackgroundHue.Value;
            backgroundOpacity = configBackgroundOpacity.Value;
            BackgroundFramesPerSecond = configBackgroundFPS.Value;
            menuColorGreyscale = configMenuGreyscale != null && configMenuGreyscale.Value;

            if (configSoftTeleport != null)
                softTeleportEnabled = configSoftTeleport.Value;
            if (configBrowsePositionRestore != null)
                browsePositionRestoreEnabled = configBrowsePositionRestore.Value;
            if (configAutoSplashOnJoin != null)
                autoSplashOnJoin = false; // Splash removed — laggy in big lobbies
            if (configUiScale != null)
                uiScale = Mathf.Clamp(configUiScale.Value, UiScaleMin, UiScaleMax);
            uiScaleSmoothed = uiScale;
            if (configMenuPosX != null && configMenuPosY != null)
            {
                menuRect.x = configMenuPosX.Value;
                menuRect.y = configMenuPosY.Value;
            }
            if (configRadarPosX != null && configRadarPosY != null)
            {
                playerRadarRect.x = configRadarPosX.Value;
                playerRadarRect.y = configRadarPosY.Value;
            }
            if (configRadarSize != null)
            {
                float rs = Mathf.Clamp(configRadarSize.Value, 120f, 420f);
                playerRadarRect.width = rs;
                playerRadarRect.height = rs;
            }
            if (configShowHudWaypoints != null)
                showHudWaypoints = configShowHudWaypoints.Value;

            if (configScreenshotSuperSize != null)
                screenshotSuperSize = Mathf.Clamp(configScreenshotSuperSize.Value, 1, 4);
            if (configLastJoinedRoom != null)
                lastJoinedRoomName = configLastJoinedRoom.Value ?? "";
            if (configWelcomeMessageOnJoin != null)
                welcomeMessageOnJoin = configWelcomeMessageOnJoin.Value;
            if (configFlySpeed != null)
                flySpeed = Mathf.Clamp(configFlySpeed.Value, 5f, 500f);
            if (configDestroyBodyOnLeave != null)
                destroyBodyOnLeave = configDestroyBodyOnLeave.Value;
            if (configPublishRoomPlayers != null)
                publishRoomPlayers = configPublishRoomPlayers.Value;

            configModPresets = Config.Bind(
                "Lobby",
                "ModPresets",
                "",
                "Named mod presets for quick lobby. Format: name=jsonarray;name2=jsonarray");
            LoadModPresetsFromConfig();
            LoadBannedUserIds();
            LoadFavoritePrefabNames();
            LoadFavoriteRoomNames();
            LoadGenePresetsFromConfig();
            // CharCon import runs in ZexDeferredInit (after load wave)
        }

        /// <summary>
        /// Resolve a type from game assemblies first (skip Unity*/System*).
        /// Falls back to AccessTools if needed so ModManager etc. still resolve.
        /// </summary>

        // ============================================================
        // CONFIG PERSISTENCE
        // ============================================================
        private void SaveConfig()
        {
            if (configCameraHeight == null)
                return;

            configCameraHeight.Value = spectateCameraHeight;
            configCameraDistance.Value = spectateCameraDistance;
            configCameraRotation.Value = spectateCameraRotation;

            configBackgroundHue.Value = backgroundHue;
            configBackgroundOpacity.Value = backgroundOpacity;
            configBackgroundFPS.Value = BackgroundFramesPerSecond;
        }

        private void ExportQoLConfig()
        {
            try
            {
                string dir = Paths.ConfigPath;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                string path = System.IO.Path.Combine(dir, "ZexQoLMenu_export.cfg");
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("# Zex QoL Menu export");
                sb.AppendLine("menu=" + (menuToggleKey != null ? menuToggleKey.Value.ToString() : ""));
                sb.AppendLine("noclip=" + (noclipToggleKey != null ? noclipToggleKey.Value.ToString() : ""));
                sb.AppendLine("flyUp=" + (flySpeedUpKey != null ? flySpeedUpKey.Value.ToString() : ""));
                sb.AppendLine("flyDown=" + (flySpeedDownKey != null ? flySpeedDownKey.Value.ToString() : ""));
                sb.AppendLine("waypoint=" + (waypointQuickSaveKey != null ? waypointQuickSaveKey.Value.ToString() : ""));
                sb.AppendLine("specNext=" + (spectateNextKey != null ? spectateNextKey.Value.ToString() : ""));
                sb.AppendLine("specPrev=" + (spectatePrevKey != null ? spectatePrevKey.Value.ToString() : ""));
                sb.AppendLine("favPrefabs=" + string.Join(",", favoritePrefabNames));
                sb.AppendLine("favRooms=" + string.Join(",", favoriteRoomNames));
                if (configModPresets != null)
                    sb.AppendLine("modPresets=" + (configModPresets.Value ?? ""));
                else
                {
                    // fallback from memory
                    System.Text.StringBuilder mp = new System.Text.StringBuilder();
                    for (int i = 0; i < modPresetNames.Count; i++)
                    {
                        if (i > 0) mp.Append(';');
                        mp.Append(modPresetNames[i]);
                    }
                    sb.AppendLine("modPresets=" + mp);
                }
                sb.AppendLine("autoSplash=" + autoSplashOnJoin);
                sb.AppendLine("welcome=" + welcomeMessageOnJoin);
                System.IO.File.WriteAllText(path, sb.ToString());
                ShowToast("Exported config");
                Logger.LogInfo("Exported QoL config to " + path);
            }
            catch (Exception ex)
            {
                ShowToast("Export failed");
                Logger.LogWarning("ExportQoLConfig: " + ex.Message);
            }
        }

        private void ImportQoLConfig()
        {
            try
            {
                string dir = Paths.ConfigPath;
                if (string.IsNullOrEmpty(dir))
                    dir = ".";
                string path = System.IO.Path.Combine(dir, "ZexQoLMenu_export.cfg");
                if (!System.IO.File.Exists(path))
                {
                    ShowToast("No export file");
                    return;
                }
                string[] lines = System.IO.File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line) || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "menu" && menuToggleKey != null && TryParseKeyCode(val, out KeyCode k0))
                        menuToggleKey.Value = new KeyboardShortcut(k0);
                    else if (key == "noclip" && noclipToggleKey != null && TryParseKeyCode(val, out KeyCode k1))
                        noclipToggleKey.Value = k1;
                    else if (key == "flyUp" && flySpeedUpKey != null && TryParseKeyCode(val, out KeyCode k2))
                        flySpeedUpKey.Value = k2;
                    else if (key == "flyDown" && flySpeedDownKey != null && TryParseKeyCode(val, out KeyCode k3))
                        flySpeedDownKey.Value = k3;
                    else if (key == "waypoint" && waypointQuickSaveKey != null && TryParseKeyCode(val, out KeyCode k4))
                        waypointQuickSaveKey.Value = k4;
                    else if (key == "specNext" && spectateNextKey != null && TryParseKeyCode(val, out KeyCode k5))
                        spectateNextKey.Value = k5;
                    else if (key == "specPrev" && spectatePrevKey != null && TryParseKeyCode(val, out KeyCode k6))
                        spectatePrevKey.Value = k6;
                    else if (key == "favPrefabs")
                    {
                        favoritePrefabNames.Clear();
                        if (!string.IsNullOrEmpty(val))
                            foreach (string s in val.Split(','))
                                if (!string.IsNullOrEmpty(s.Trim()))
                                    favoritePrefabNames.Add(s.Trim());
                        SaveFavoritePrefabNames();
                    }
                    else if (key == "favRooms")
                    {
                        favoriteRoomNames.Clear();
                        if (!string.IsNullOrEmpty(val))
                            foreach (string s in val.Split(','))
                                if (!string.IsNullOrEmpty(s.Trim()))
                                    favoriteRoomNames.Add(s.Trim());
                        SaveFavoriteRoomNames();
                    }
                    else if (key == "modPresets" && configModPresets != null)
                    {
                        configModPresets.Value = val;
                        LoadModPresetsFromConfig();
                    }
                    else if (key == "autoSplash")
                    {
                        autoSplashOnJoin = val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1";
                        if (configAutoSplashOnJoin != null)
                            configAutoSplashOnJoin.Value = autoSplashOnJoin;
                    }
                    else if (key == "welcome")
                    {
                        welcomeMessageOnJoin = val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1";
                        if (configWelcomeMessageOnJoin != null)
                            configWelcomeMessageOnJoin.Value = welcomeMessageOnJoin;
                    }
                }
                ShowToast("Imported config");
                Logger.LogInfo("Imported QoL config from " + path);
            }
            catch (Exception ex)
            {
                ShowToast("Import failed");
                Logger.LogWarning("ImportQoLConfig: " + ex.Message);
            }
        }

        private static bool TryParseKeyCode(string s, out KeyCode code)
        {
            code = KeyCode.None;
            if (string.IsNullOrEmpty(s)) return false;
            try
            {
                code = (KeyCode)System.Enum.Parse(typeof(KeyCode), s, true);
                return code != KeyCode.None;
            }
            catch { return false; }
        }

        // BAN PERSISTENCE
        // ============================================================
        private void LoadBannedUserIds()
        {
            bannedUserIds.Clear();

            if (configBannedUserIds == null || string.IsNullOrEmpty(configBannedUserIds.Value))
                return;

            string[] parts = configBannedUserIds.Value.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    bannedUserIds.Add(trimmed);
            }
        }

        private void SaveBannedUserIds()
        {
            if (configBannedUserIds == null) return;
            configBannedUserIds.Value = string.Join(",", new List<string>(bannedUserIds).ToArray());
        }

        private void LoadFavoritePrefabNames()
        {
            favoritePrefabNames.Clear();

            if (configFavoritePrefabNames == null || string.IsNullOrEmpty(configFavoritePrefabNames.Value))
                return;

            string[] parts = configFavoritePrefabNames.Value.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    favoritePrefabNames.Add(trimmed);
            }
        }

        private void SaveFavoritePrefabNames()
        {
            if (configFavoritePrefabNames == null) return;
            configFavoritePrefabNames.Value = string.Join(",", new List<string>(favoritePrefabNames).ToArray());
        }

        private void LoadFavoriteRoomNames()
        {
            favoriteRoomNames.Clear();
            if (configFavoriteRoomNames == null || string.IsNullOrEmpty(configFavoriteRoomNames.Value))
                return;
            string[] parts = configFavoriteRoomNames.Value.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    favoriteRoomNames.Add(trimmed);
            }
        }

        private void SaveFavoriteRoomNames()
        {
            if (configFavoriteRoomNames == null) return;
            configFavoriteRoomNames.Value = string.Join(",", new List<string>(favoriteRoomNames).ToArray());
        }

        private void SaveUiLayoutToConfig(bool force = false)
        {
            if (!force && Time.unscaledTime < nextUiLayoutSaveTime)
                return;
            nextUiLayoutSaveTime = Time.unscaledTime + 0.75f;
            try
            {
                if (configMenuPosX != null) configMenuPosX.Value = menuRect.x;
                if (configMenuPosY != null) configMenuPosY.Value = menuRect.y;
                if (configRadarPosX != null) configRadarPosX.Value = playerRadarRect.x;
                if (configRadarPosY != null) configRadarPosY.Value = playerRadarRect.y;
                if (configRadarSize != null)
                    configRadarSize.Value = Mathf.Clamp(playerRadarRect.width, 120f, 420f);
            }
            catch { }
            InitializeBackgroundAnimationConfig();
        }
    }
}