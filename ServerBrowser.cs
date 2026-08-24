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
        private void DrawModsPanel(float x, float y, float width, float maxHeight)
        {
            // In a lobby/room: only show room mods + save preset (applying mid-session breaks KK)
            if (PhotonNetwork.InRoom)
            {
                DrawModsPanelInRoom(x, y, width, maxHeight);
                return;
            }

            RefreshModListIfNeeded();

            Event e = Event.current;
            const float rowH = 20f;
            Color prevBg = GUI.backgroundColor;
            Color accentSel = GetMenuSelectionColor(0.92f);

            // Header row: title + counts
            GUI.Label(new Rect(x, y, width * 0.4f, 22f), new GUIContent("MODS"), headerStyle);
            GUI.Label(new Rect(x + width * 0.4f, y + 2f, width * 0.6f, 18f),
                new GUIContent(CountEnabledMods() + " selected  ·  " + quickLobbyModTitles.Count + " total"),
                smallStyle);
            y += 26f;

            // Search + clear on one line
            Rect modFilterRect = new Rect(x, y, width - 64f, 22f);
            GUI.Box(modFilterRect, "");
            string modFilterShown = string.IsNullOrEmpty(quickLobbyModFilter) ? "Search…" : quickLobbyModFilter;
            if (quickLobbyModFilterFocused) modFilterShown += "|";
            GUI.Label(new Rect(modFilterRect.x + 6f, modFilterRect.y + 2f, modFilterRect.width - 12f, 18f),
                new GUIContent(modFilterShown), labelStyle);
            if (e != null && e.type == EventType.MouseDown && modFilterRect.Contains(e.mousePosition))
            {
                quickLobbyModFilterFocused = true;
                modPresetNameFocused = false;
                e.Use();
            }
            if (quickLobbyModFilterFocused && e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && quickLobbyModFilter.Length > 0)
                {
                    quickLobbyModFilter = quickLobbyModFilter.Substring(0, quickLobbyModFilter.Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    quickLobbyModFilterFocused = false;
                    e.Use();
                }
                else if (e.character != '\0' && !char.IsControl(e.character) && quickLobbyModFilter.Length < 48)
                {
                    quickLobbyModFilter += e.character;
                    e.Use();
                }
            }
            if (GUI.Button(new Rect(x + width - 60f, y, 60f, 22f), new GUIContent("✕"), buttonStyle))
            {
                quickLobbyModFilter = "";
                quickLobbyModFilterFocused = false;
            }
            y += 28f;

            // Lists
            float listH = Mathf.Min(300f, Mathf.Max(180f, maxHeight - 200f));
            float gap = 8f;
            float half = (width - gap) * 0.5f;
            int visibleModCount = CountModsMatchingFilter();

            GUI.Label(new Rect(x, y, half, 16f),
                new GUIContent("Available (" + visibleModCount + ")"), smallStyle);
            GUI.Label(new Rect(x + half + gap, y, half, 16f),
                new GUIContent("Selected (" + CountEnabledMods() + ")"), smallStyle);
            y += 16f;

            Rect modAllRect = new Rect(x, y, half, listH);
            Rect modSelRect = new Rect(x + half + gap, y, half, listH);
            GUI.Box(modAllRect, "");
            GUI.Box(modSelRect, "");

            float modContent = visibleModCount * rowH;
            float modMax = Mathf.Max(0f, modContent - (listH - 4f));
            if (e != null && e.type == EventType.ScrollWheel && modAllRect.Contains(e.mousePosition))
            {
                quickLobbyModScroll.y = Mathf.Clamp(quickLobbyModScroll.y + e.delta.y * 20f, 0f, modMax);
                e.Use();
            }
            quickLobbyModScroll.y = Mathf.Clamp(quickLobbyModScroll.y, 0f, modMax);

            GUI.BeginGroup(new Rect(modAllRect.x + 2f, modAllRect.y + 2f, half - 4f, listH - 4f), GUIContent.none, GUIStyle.none);
            float ay = -quickLobbyModScroll.y;
            if (quickLobbyModTitles.Count == 0)
            {
                GUI.Label(new Rect(4f, 4f, half - 12f, 36f), new GUIContent("Empty — hit Refresh"), smallStyle);
            }
            else if (visibleModCount == 0)
            {
                GUI.Label(new Rect(4f, 4f, half - 12f, 36f), new GUIContent("No matches"), smallStyle);
            }
            else
            {
                for (int i = 0; i < quickLobbyModTitles.Count; i++)
                {
                    if (!ModMatchesFilter(i))
                        continue;
                    bool on = i < quickLobbyModEnabled.Count && quickLobbyModEnabled[i];
                    if (on) GUI.backgroundColor = accentSel;
                    else GUI.backgroundColor = prevBg;
                    GUIStyle st = on && selectedButtonStyle != null ? selectedButtonStyle : buttonStyle;
                    if (GUI.Button(new Rect(0f, ay, half - 8f, 18f),
                        new GUIContent(quickLobbyModTitles[i]), st))
                    {
                        if (i < quickLobbyModEnabled.Count)
                            quickLobbyModEnabled[i] = !quickLobbyModEnabled[i];
                    }
                    ay += rowH;
                }
            }
            GUI.EndGroup();
            GUI.backgroundColor = prevBg;

            float selContent = CountEnabledMods() * rowH;
            float selMax = Mathf.Max(0f, selContent - (listH - 4f));
            if (e != null && e.type == EventType.ScrollWheel && modSelRect.Contains(e.mousePosition))
            {
                quickLobbyModSelectedScroll.y = Mathf.Clamp(
                    quickLobbyModSelectedScroll.y + e.delta.y * 20f, 0f, selMax);
                e.Use();
            }
            quickLobbyModSelectedScroll.y = Mathf.Clamp(quickLobbyModSelectedScroll.y, 0f, selMax);

            GUI.BeginGroup(new Rect(modSelRect.x + 2f, modSelRect.y + 2f, half - 4f, listH - 4f), GUIContent.none, GUIStyle.none);
            float sy = -quickLobbyModSelectedScroll.y;
            int drawn = 0;
            for (int i = 0; i < quickLobbyModTitles.Count; i++)
            {
                if (i >= quickLobbyModEnabled.Count || !quickLobbyModEnabled[i])
                    continue;
                GUI.backgroundColor = accentSel;
                GUIStyle st = selectedButtonStyle != null ? selectedButtonStyle : buttonStyle;
                if (GUI.Button(new Rect(0f, sy, half - 8f, 18f),
                    new GUIContent(quickLobbyModTitles[i]), st))
                {
                    quickLobbyModEnabled[i] = false;
                }
                sy += rowH;
                drawn++;
            }
            GUI.backgroundColor = prevBg;
            if (drawn == 0)
            {
                GUI.Label(new Rect(4f, 4f, half - 12f, 36f), new GUIContent("None"), smallStyle);
            }
            GUI.EndGroup();

            y += listH + 10f;

            // Actions — three equal buttons
            float bw = (width - 16f) / 3f;
            if (GUI.Button(new Rect(x, y, bw, 28f), new GUIContent("Refresh"), buttonStyle))
            {
                nextModListRefresh = 0f;
                RefreshModListIfNeeded(true);
                ShowToast(quickLobbyModTitles.Count > 0
                    ? (quickLobbyModTitles.Count + " mods")
                    : "No mods");
            }
            if (GUI.Button(new Rect(x + bw + 8f, y, bw, 28f), new GUIContent("Clear"), buttonStyle))
            {
                for (int i = 0; i < quickLobbyModEnabled.Count; i++)
                    quickLobbyModEnabled[i] = false;
            }
            string applyLabel = applyModsRunning ? "…" : "Apply";
            if (GUI.Button(new Rect(x + 2f * (bw + 8f), y, bw, 28f), new GUIContent(applyLabel), buttonStyle))
            {
                if (!applyModsRunning)
                    ApplySelectedMods();
            }
            y += 32f;
            if (!string.IsNullOrEmpty(applyModsStatus) && Time.unscaledTime < applyModsStatusUntil)
            {
                GUI.Label(new Rect(x, y, width, 18f), new GUIContent(applyModsStatus), smallStyle);
                y += 22f;
            }
            else
                y += 4f;

            // Presets — compact
            GUI.Label(new Rect(x, y, 48f, 18f), new GUIContent("Presets"), smallStyle);
            Rect pnRect = new Rect(x + 52f, y - 1f, width * 0.28f, 22f);
            GUI.Box(pnRect, "");
            string pnShown = string.IsNullOrEmpty(modPresetName) ? "name" : modPresetName;
            if (modPresetNameFocused) pnShown += "|";
            GUI.Label(new Rect(pnRect.x + 4f, pnRect.y + 2f, pnRect.width - 8f, 18f), new GUIContent(pnShown), labelStyle);
            Event pe = Event.current;
            if (pe != null && pe.type == EventType.MouseDown && pnRect.Contains(pe.mousePosition))
            {
                modPresetNameFocused = true;
                quickLobbyModFilterFocused = false;
                pe.Use();
            }
            if (modPresetNameFocused && pe != null && pe.type == EventType.KeyDown)
            {
                if (pe.keyCode == KeyCode.Backspace && modPresetName.Length > 0)
                { modPresetName = modPresetName.Substring(0, modPresetName.Length - 1); pe.Use(); }
                else if (pe.keyCode == KeyCode.Escape || pe.keyCode == KeyCode.Return)
                { modPresetNameFocused = false; pe.Use(); }
                else if (pe.character != '\0' && !char.IsControl(pe.character) && modPresetName.Length < 24)
                { modPresetName += pe.character; pe.Use(); }
            }
            float pbx = x + 52f + width * 0.28f + 6f;
            float pbw = 54f;
            if (GUI.Button(new Rect(pbx, y - 1f, pbw, 22f), new GUIContent("Save"), buttonStyle))
                SaveCurrentModsAsPreset(modPresetName);
            if (GUI.Button(new Rect(pbx + pbw + 4f, y - 1f, pbw, 22f), new GUIContent("Load"), buttonStyle) &&
                selectedModPreset >= 0 && selectedModPreset < modPresetNames.Count)
                ApplyModPreset(modPresetNames[selectedModPreset]);
            if (GUI.Button(new Rect(pbx + 2f * (pbw + 4f), y - 1f, pbw, 22f), new GUIContent("Del"), buttonStyle) &&
                selectedModPreset >= 0 && selectedModPreset < modPresetNames.Count)
                DeleteModPreset(modPresetNames[selectedModPreset]);
            y += 26f;

            float presetH = 40f;
            GUI.Box(new Rect(x, y, width, presetH), "");
            if (modPresetNames.Count == 0)
            {
                GUI.Label(new Rect(x + 6f, y + 10f, width - 12f, 18f),
                    new GUIContent("No presets"), smallStyle);
            }
            else
            {
                float px = 4f;
                for (int i = 0; i < modPresetNames.Count; i++)
                {
                    bool sel = i == selectedModPreset;
                    Color pb = GUI.backgroundColor;
                    if (sel) GUI.backgroundColor = accentSel;
                    float chip = Mathf.Min(110f, (width - 12f) / Mathf.Max(1, modPresetNames.Count));
                    if (GUI.Button(new Rect(x + px, y + 6f, chip - 4f, 26f), new GUIContent(modPresetNames[i]),
                        sel && selectedButtonStyle != null ? selectedButtonStyle : buttonStyle))
                    {
                        selectedModPreset = i;
                        modPresetName = modPresetNames[i];
                    }
                    GUI.backgroundColor = pb;
                    px += chip;
                    if (px > width - 40f) break;
                }
            }
        }

        /// <summary>
        /// Load the currently selected mods via ModManager.SetLoadedMods (game's real loader).
        /// </summary>

        /// <summary>
        /// In-room: list the room's modList, allow saving as a preset, block apply/load.
        /// </summary>
        private void DrawModsPanelInRoom(float x, float y, float width, float maxHeight)
        {
            Event e = Event.current;
            Color prevBg = GUI.backgroundColor;

            GUI.Label(new Rect(x, y, width, 22f), new GUIContent("ROOM MODS (read-only)"), headerStyle);
            y += 24f;
            GUI.Label(new Rect(x, y, width, 36f),
                new GUIContent(
                    "You are in a lobby. Listing this room's mods only.\n" +
                    "Save as preset is OK — Apply / Load are disabled here."),
                smallStyle);
            y += 40f;

            List<string> roomMods = GetCurrentRoomModTitles();
            GUI.Label(new Rect(x, y, width, 18f),
                new GUIContent(roomMods.Count + " mod(s) in this room"), smallStyle);
            y += 20f;

            float listH = Mathf.Min(280f, Mathf.Max(120f, maxHeight - 160f));
            Rect listRect = new Rect(x, y, width, listH);
            GUI.Box(listRect, "");

            float contentH = Mathf.Max(roomMods.Count * 20f, 1f);
            float maxScroll = Mathf.Max(0f, contentH - (listH - 4f));
            if (e != null && e.type == EventType.ScrollWheel && listRect.Contains(e.mousePosition))
            {
                quickLobbyModScroll.y = Mathf.Clamp(quickLobbyModScroll.y + e.delta.y * 20f, 0f, maxScroll);
                e.Use();
            }
            quickLobbyModScroll.y = Mathf.Clamp(quickLobbyModScroll.y, 0f, maxScroll);

            GUI.BeginGroup(new Rect(listRect.x + 4f, listRect.y + 4f, width - 8f, listH - 8f), GUIContent.none, GUIStyle.none);
            float ry = -quickLobbyModScroll.y;
            if (roomMods.Count == 0)
            {
                GUI.Label(new Rect(4f, 4f, width - 16f, 40f),
                    new GUIContent("No modList on this room (vanilla or host didn't publish mods)."), smallStyle);
            }
            else
            {
                for (int i = 0; i < roomMods.Count; i++)
                {
                    GUI.Label(new Rect(2f, ry, width - 16f, 18f), new GUIContent(roomMods[i]), labelStyle);
                    ry += 20f;
                }
            }
            GUI.EndGroup();
            y += listH + 12f;

            GUI.Label(new Rect(x, y, 48f, 18f), new GUIContent("Presets"), smallStyle);
            Rect pnRect = new Rect(x + 52f, y - 1f, width * 0.35f, 22f);
            GUI.Box(pnRect, "");
            string pnShown = string.IsNullOrEmpty(modPresetName) ? "preset name" : modPresetName;
            if (modPresetNameFocused) pnShown += "|";
            GUI.Label(new Rect(pnRect.x + 4f, pnRect.y + 2f, pnRect.width - 8f, 18f), new GUIContent(pnShown), labelStyle);
            if (e != null && e.type == EventType.MouseDown && pnRect.Contains(e.mousePosition))
            {
                modPresetNameFocused = true;
                e.Use();
            }
            if (modPresetNameFocused && e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && modPresetName.Length > 0)
                { modPresetName = modPresetName.Substring(0, modPresetName.Length - 1); e.Use(); }
                else if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return)
                { modPresetNameFocused = false; e.Use(); }
                else if (e.character != '\0' && !char.IsControl(e.character) && modPresetName.Length < 24)
                { modPresetName += e.character; e.Use(); }
            }
            float pbx = x + 52f + width * 0.35f + 6f;
            if (GUI.Button(new Rect(pbx, y - 1f, 70f, 22f), new GUIContent("Save"), buttonStyle))
            {
                try
                {
                    if (PhotonNetwork.CurrentRoom != null)
                        ImportModsFromRoomProperties(PhotonNetwork.CurrentRoom.CustomProperties, true);
                }
                catch { }
                SaveCurrentModsAsPreset(modPresetName);
            }
            if (GUI.Button(new Rect(pbx + 76f, y - 1f, 70f, 22f), new GUIContent("Load"), buttonStyle))
                ShowToast("Load disabled while in a room");
            if (GUI.Button(new Rect(pbx + 152f, y - 1f, 70f, 22f), new GUIContent("Apply"), buttonStyle))
                ShowToast("Apply disabled while in a room");
            y += 28f;

            float presetH = 40f;
            GUI.Box(new Rect(x, y, width, presetH), "");
            if (modPresetNames.Count == 0)
            {
                GUI.Label(new Rect(x + 6f, y + 10f, width - 12f, 18f), new GUIContent("No saved presets"), smallStyle);
            }
            else
            {
                float px = 4f;
                Color accentSel = GetMenuSelectionColor(0.92f);
                for (int i = 0; i < modPresetNames.Count; i++)
                {
                    bool sel = i == selectedModPreset;
                    Color pb = GUI.backgroundColor;
                    if (sel) GUI.backgroundColor = accentSel;
                    float chip = Mathf.Min(110f, (width - 12f) / Mathf.Max(1, modPresetNames.Count));
                    if (GUI.Button(new Rect(x + px, y + 6f, chip - 4f, 26f), new GUIContent(modPresetNames[i]),
                        sel && selectedButtonStyle != null ? selectedButtonStyle : buttonStyle))
                    {
                        selectedModPreset = i;
                        modPresetName = modPresetNames[i];
                    }
                    GUI.backgroundColor = pb;
                    px += chip;
                    if (px > width - 40f) break;
                }
            }
            GUI.backgroundColor = prevBg;
        }

        private List<string> GetCurrentRoomModTitles()
        {
            List<string> titles = new List<string>();
            try
            {
                if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
                    return titles;
                var props = PhotonNetwork.CurrentRoom.CustomProperties;
                if (props == null || !props.ContainsKey("modList"))
                    return titles;

                ImportModsFromRoomProperties(props, false);
                for (int i = 0; i < quickLobbyModTitles.Count; i++)
                {
                    if (!string.IsNullOrEmpty(quickLobbyModTitles[i]))
                        titles.Add(quickLobbyModTitles[i]);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("GetCurrentRoomModTitles: " + ex.Message);
            }
            return titles;
        }

        private void ApplySelectedMods()
        {
            if (PhotonNetwork.InRoom)
            {
                ShowToast("Cannot apply mods while in a room");
                applyModsStatus = "Blocked: leave the room first";
                applyModsStatusUntil = Time.unscaledTime + 3f;
                return;
            }
            if (applyModsRunning)
            {
                ShowToast("Already applying");
                return;
            }
            if (CountEnabledMods() <= 0)
            {
                ShowToast("Nothing selected");
                return;
            }
            StartCoroutine(ApplySelectedModsRoutine());
        }

        private void SetApplyModsStatus(string msg)
        {
            applyModsStatus = msg ?? "";
            applyModsStatusUntil = Time.unscaledTime + 8f;
        }

        private System.Collections.IEnumerator ApplySelectedModsRoutine()
        {
            applyModsRunning = true;
            int n = CountEnabledMods();
            SetApplyModsStatus("Loading " + n + "…");
            ShowToast("Loading " + n + "…");

            string modsErr = null;
            object setModsEnum = null;
            try
            {
                setModsEnum = BeginSetLoadedModsForSelection(out modsErr);
            }
            catch (Exception ex)
            {
                modsErr = ex.Message;
            }

            if (setModsEnum == null)
            {
                string fail = string.IsNullOrEmpty(modsErr) ? "Apply failed" : modsErr;
                SetApplyModsStatus(fail);
                ShowToast(fail);
                applyModsRunning = false;
                yield break;
            }

            IEnumerator en = setModsEnum as IEnumerator;
            bool modsOk = false;
            float waitMods = Time.unscaledTime + 180f;
            float nextStatus = 0f;
            if (en != null)
            {
                while (Time.unscaledTime < waitMods)
                {
                    if (Time.unscaledTime >= nextStatus)
                    {
                        float left = Mathf.Max(0f, waitMods - Time.unscaledTime);
                        SetApplyModsStatus("Loading " + n + "…");
                        nextStatus = Time.unscaledTime + 1f;
                    }
                    bool moved = false;
                    try { moved = en.MoveNext(); }
                    catch (Exception ex)
                    {
                        modsErr = ex.Message;
                        break;
                    }
                    if (!moved)
                    {
                        modsOk = true;
                        break;
                    }
                    yield return en.Current;
                }
            }

            if (!string.IsNullOrEmpty(modsErr))
            {
                Logger.LogWarning("Apply mods: " + modsErr);
                SetApplyModsStatus("Failed: " + modsErr);
                ShowToast("Failed");
            }
            else if (modsOk)
            {
                bool someFailed = false;
                try
                {
                    Type mm = SafeGameType("ModManager");
                    MethodInfo failed = mm != null ? AccessTools.Method(mm, "GetFailedToLoadMods") : null;
                    if (failed != null)
                    {
                        object f = failed.Invoke(null, null);
                        if (f is bool && (bool)f)
                            someFailed = true;
                    }
                }
                catch { }

                if (someFailed)
                {
                    SetApplyModsStatus("Done — some failed");
                    ShowToast("Some failed");
                }
                else
                {
                    SetApplyModsStatus("Applied (" + n + ")");
                    ShowToast("Applied");
                }
            }
            else
            {
                SetApplyModsStatus("Timed out");
                ShowToast("Timed out");
            }
            applyModsRunning = false;
        }

        private int CountEnabledMods()
        {
            int n = 0;
            for (int i = 0; i < quickLobbyModEnabled.Count; i++)
                if (quickLobbyModEnabled[i]) n++;
            return n;
        }

        private bool ModMatchesFilter(int index)
        {
            if (string.IsNullOrEmpty(quickLobbyModFilter))
                return true;
            if (index < 0 || index >= quickLobbyModTitles.Count)
                return false;
            string f = quickLobbyModFilter.Trim();
            if (f.Length == 0)
                return true;
            string title = quickLobbyModTitles[index] ?? "";
            if (title.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (index < quickLobbyModFolders.Count)
            {
                string folder = quickLobbyModFolders[index] ?? "";
                if (folder.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            if (index < quickLobbyModIds.Count)
            {
                string id = quickLobbyModIds[index] ?? "";
                if (id.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private int CountModsMatchingFilter()
        {
            if (string.IsNullOrEmpty(quickLobbyModFilter) || quickLobbyModFilter.Trim().Length == 0)
                return quickLobbyModTitles.Count;
            int n = 0;
            for (int i = 0; i < quickLobbyModTitles.Count; i++)
                if (ModMatchesFilter(i)) n++;
            return n;
        }

        private void RefreshModListIfNeeded(bool force = false)
        {
            if (!force && Time.unscaledTime < nextModListRefresh && quickLobbyModTitles.Count > 0)
                return;
            nextModListRefresh = Time.unscaledTime + 8f;

            System.Collections.Generic.Dictionary<string, bool> prevEnabled =
                new System.Collections.Generic.Dictionary<string, bool>();
            for (int i = 0; i < quickLobbyModIds.Count; i++)
            {
                string k = quickLobbyModIds[i] + "|" + (i < quickLobbyModFolders.Count ? quickLobbyModFolders[i] : "");
                if (i < quickLobbyModEnabled.Count)
                    prevEnabled[k] = quickLobbyModEnabled[i];
            }

            quickLobbyModTitles.Clear();
            quickLobbyModIds.Clear();
            quickLobbyModFolders.Clear();
            quickLobbyModEnabled.Clear();

            // 1) ModManager reflection — every static IEnumerable that looks like ModStubs
            try
            {
                Type mm = SafeGameType("ModManager");
                if (mm != null)
                {
                    string[] methodNames = new string[]
                    {
                        "GetModsWithLoadedAssets",
                        "GetPlayerConfig",
                        "GetAllMods",
                        "GetInstalledMods",
                        "GetSubscribedMods",
                        "GetAvailableMods"
                    };
                    for (int m = 0; m < methodNames.Length; m++)
                    {
                        MethodInfo mi = AccessTools.Method(mm, methodNames[m]);
                        if (mi == null) continue;
                        object listObj = null;
                        try { listObj = mi.Invoke(null, null); } catch { continue; }
                        AddModStubsFromEnumerable(listObj, prevEnabled);
                    }

                    // Static fields / props that are lists
                    foreach (FieldInfo fi in mm.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (fi.FieldType == typeof(string) || fi.FieldType.IsPrimitive) continue;
                        try
                        {
                            object val = fi.GetValue(null);
                            AddModStubsFromEnumerable(val, prevEnabled);
                        }
                        catch { }
                    }
                    foreach (PropertyInfo pi in mm.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!pi.CanRead || pi.GetIndexParameters().Length > 0) continue;
                        try
                        {
                            object val = pi.GetValue(null, null);
                            AddModStubsFromEnumerable(val, prevEnabled);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("RefreshModList ModManager: " + ex.Message);
            }

            // 2) Disk scan — subscribed/local mods even if not enabled in-game
            try { ScanDiskForMods(prevEnabled); }
            catch (Exception ex) { Logger.LogWarning("ScanDiskForMods: " + ex.Message); }

            // 3) Current room modList
            try
            {
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                    ImportModsFromRoomProperties(PhotonNetwork.CurrentRoom.CustomProperties, false);
            }
            catch { }

            SortModListAlphabetical();
        }

        /// <summary>
        /// Sort ALL MODS lists by title (case-insensitive), keeping id/folder/enabled in sync.
        /// </summary>
        private void SortModListAlphabetical()
        {
            int n = quickLobbyModTitles.Count;
            if (n <= 1)
                return;

            // Parallel arrays → list of indices, sort by title, reorder all four lists
            int[] order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = i;

            System.Array.Sort(order, (a, b) =>
            {
                string ta = a < quickLobbyModTitles.Count ? (quickLobbyModTitles[a] ?? "") : "";
                string tb = b < quickLobbyModTitles.Count ? (quickLobbyModTitles[b] ?? "") : "";
                return string.Compare(ta, tb, StringComparison.OrdinalIgnoreCase);
            });

            System.Collections.Generic.List<string> titles = new System.Collections.Generic.List<string>(n);
            System.Collections.Generic.List<string> ids = new System.Collections.Generic.List<string>(n);
            System.Collections.Generic.List<string> folders = new System.Collections.Generic.List<string>(n);
            System.Collections.Generic.List<bool> enabled = new System.Collections.Generic.List<bool>(n);

            for (int i = 0; i < n; i++)
            {
                int src = order[i];
                titles.Add(src < quickLobbyModTitles.Count ? quickLobbyModTitles[src] : "");
                ids.Add(src < quickLobbyModIds.Count ? quickLobbyModIds[src] : "0");
                folders.Add(src < quickLobbyModFolders.Count ? quickLobbyModFolders[src] : "");
                enabled.Add(src < quickLobbyModEnabled.Count && quickLobbyModEnabled[src]);
            }

            quickLobbyModTitles.Clear();
            quickLobbyModIds.Clear();
            quickLobbyModFolders.Clear();
            quickLobbyModEnabled.Clear();
            quickLobbyModTitles.AddRange(titles);
            quickLobbyModIds.AddRange(ids);
            quickLobbyModFolders.AddRange(folders);
            quickLobbyModEnabled.AddRange(enabled);
        }

        private void AddModStubsFromEnumerable(object listObj,
            System.Collections.Generic.Dictionary<string, bool> prevEnabled)
        {
            if (listObj == null) return;
            System.Collections.IEnumerable en = listObj as System.Collections.IEnumerable;
            if (en == null) return;
            // Avoid iterating strings char-by-char
            if (listObj is string) return;
            foreach (object stub in en)
            {
                if (stub == null) continue;
                // Nested enumerables (dict values)
                if (stub is System.Collections.IEnumerable && !(stub is string))
                {
                    Type st = stub.GetType();
                    if (!st.IsValueType && st != typeof(string) && AccessTools.Field(st, "title") == null
                        && AccessTools.Field(st, "Title") == null && AccessTools.Field(st, "folderTitle") == null)
                    {
                        // might be a list of stubs
                        if (st.Name.IndexOf("ModStub", StringComparison.OrdinalIgnoreCase) < 0
                            && st.Name.IndexOf("KeyValue", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            try { AddModStubsFromEnumerable(stub, prevEnabled); } catch { }
                            continue;
                        }
                    }
                }
                TryAddModStub(stub, prevEnabled);
            }
        }

        private void ScanDiskForMods(
            System.Collections.Generic.Dictionary<string, bool> prevEnabled)
        {
            System.Collections.Generic.List<string> roots = new System.Collections.Generic.List<string>();

            try
            {
                string persistent = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(persistent))
                {
                    roots.Add(Path.Combine(persistent, "mods"));
                    // parent user folders: .../KoboldKare/<id>/mods
                    DirectoryInfo pd = new DirectoryInfo(persistent);
                    if (pd.Parent != null)
                    {
                        foreach (DirectoryInfo sub in pd.Parent.GetDirectories())
                        {
                            roots.Add(Path.Combine(sub.FullName, "mods"));
                        }
                    }
                }
            }
            catch { }

            // LocalLow\Naelstrof\KoboldKare
            try
            {
                string localLow = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "Naelstrof", "KoboldKare");
                if (Directory.Exists(localLow))
                {
                    roots.Add(Path.Combine(localLow, "mods"));
                    foreach (string dir in Directory.GetDirectories(localLow))
                        roots.Add(Path.Combine(dir, "mods"));
                }
            }
            catch { }

            // Steam workshop content/1102930
            try
            {
                string[] steamRoots = new string[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                    @"C:\Program Files (x86)\Steam",
                    @"C:\Program Files\Steam",
                    @"D:\Steam",
                    @"D:\SteamLibrary",
                    @"E:\SteamLibrary"
                };
                for (int i = 0; i < steamRoots.Length; i++)
                {
                    if (string.IsNullOrEmpty(steamRoots[i])) continue;
                    string ws = Path.Combine(steamRoots[i], "steamapps", "workshop", "content", "1102930");
                    roots.Add(ws);
                    // libraryfolders.vdf sibling workshop paths
                    string steamapps = Path.Combine(steamRoots[i], "steamapps");
                    if (Directory.Exists(steamapps))
                    {
                        foreach (string lib in Directory.GetDirectories(steamapps, "appmanifest_*"))
                        { }
                    }
                }
                // Parse libraryfolders.vdf for extra libraries
                try
                {
                    string vdf = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "Steam", "steamapps", "libraryfolders.vdf");
                    if (!File.Exists(vdf))
                        vdf = Path.Combine(@"C:\Program Files (x86)\Steam", "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                    {
                        foreach (string line in File.ReadAllLines(vdf))
                        {
                            // "path"		"D:\\SteamLibrary"
                            int p = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
                            if (p < 0) continue;
                            int q1 = line.IndexOf('"', p + 6);
                            if (q1 < 0) continue;
                            int q2 = line.IndexOf('"', q1 + 1);
                            if (q2 < 0) continue;
                            string libPath = line.Substring(q1 + 1, q2 - q1 - 1).Replace(@"\\", @"\");
                            roots.Add(Path.Combine(libPath, "steamapps", "workshop", "content", "1102930"));
                        }
                    }
                }
                catch { }
            }
            catch { }

            System.Collections.Generic.HashSet<string> seenFolders =
                new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int r = 0; r < roots.Count; r++)
            {
                string root = roots[r];
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                string[] dirs;
                try { dirs = Directory.GetDirectories(root); }
                catch { continue; }

                for (int d = 0; d < dirs.Length; d++)
                {
                    string dir = dirs[d];
                    if (!seenFolders.Add(dir)) continue;

                    string folderName = Path.GetFileName(dir);
                    string title = folderName;
                    string idStr = folderName;

                    // Try read title from common metadata files
                    string[] metaNames = new string[] { "mod.json", "info.json", "package.json", "modinfo.json", "workshop.json" };
                    for (int m = 0; m < metaNames.Length; m++)
                    {
                        string metaPath = Path.Combine(dir, metaNames[m]);
                        if (!File.Exists(metaPath)) continue;
                        try
                        {
                            string json = File.ReadAllText(metaPath);
                            string t = ExtractJsonString(json, "title")
                                ?? ExtractJsonString(json, "name")
                                ?? ExtractJsonString(json, "Name");
                            if (!string.IsNullOrEmpty(t)) title = t;
                            string id = ExtractJsonString(json, "id")
                                ?? ExtractJsonString(json, "workshopId");
                            if (!string.IsNullOrEmpty(id)) idStr = id;
                        }
                        catch { }
                        break;
                    }

                    // Build a fake stub-like add via TryAddModFromDisk
                    TryAddModFromDisk(title, idStr, folderName, prevEnabled);
                }
            }
        }

        private void TryAddModFromDisk(string title, string idStr, string folder,
            System.Collections.Generic.Dictionary<string, bool> prevEnabled)
        {
            string idKey = string.IsNullOrEmpty(idStr) ? "0" : idStr;
            for (int i = 0; i < quickLobbyModIds.Count; i++)
            {
                if (quickLobbyModIds[i] == idKey &&
                    (i < quickLobbyModFolders.Count ? quickLobbyModFolders[i] : "") == (folder ?? ""))
                    return;
            }

            string key = idKey + "|" + (folder ?? "");
            bool en = false; // disk-found defaults OFF until user selects (not necessarily enabled in game)
            if (prevEnabled != null && prevEnabled.ContainsKey(key))
                en = prevEnabled[key];

            quickLobbyModTitles.Add(!string.IsNullOrEmpty(title) ? title : (folder ?? idKey));
            quickLobbyModIds.Add(idKey);
            quickLobbyModFolders.Add(folder ?? "");
            quickLobbyModEnabled.Add(en);
        }

        private void TryAddModStub(object stub, System.Collections.Generic.Dictionary<string, bool> prevEnabled)
        {
            if (stub == null) return;
            string title = null, folder = null, idStr = null;
            try
            {
                Type st = stub.GetType();
                // Try common field/property names (ModStub is often a struct)
                string[] titleNames = new string[] { "title", "Title", "name", "Name" };
                string[] folderNames = new string[] { "folderTitle", "FolderTitle", "folder", "Folder" };
                string[] idNames = new string[] { "id", "Id", "workshopId", "WorkshopId", "fileId" };

                for (int i = 0; i < titleNames.Length && string.IsNullOrEmpty(title); i++)
                {
                    FieldInfo f = AccessTools.Field(st, titleNames[i]);
                    if (f != null) title = f.GetValue(stub) as string;
                    if (string.IsNullOrEmpty(title))
                    {
                        PropertyInfo p = AccessTools.Property(st, titleNames[i]);
                        if (p != null) title = p.GetValue(stub, null) as string;
                    }
                }
                for (int i = 0; i < folderNames.Length && string.IsNullOrEmpty(folder); i++)
                {
                    FieldInfo f = AccessTools.Field(st, folderNames[i]);
                    if (f != null) folder = f.GetValue(stub) as string;
                    if (string.IsNullOrEmpty(folder))
                    {
                        PropertyInfo p = AccessTools.Property(st, folderNames[i]);
                        if (p != null) folder = p.GetValue(stub, null) as string;
                    }
                }
                for (int i = 0; i < idNames.Length && string.IsNullOrEmpty(idStr); i++)
                {
                    FieldInfo f = AccessTools.Field(st, idNames[i]);
                    object idVal = f != null ? f.GetValue(stub) : null;
                    if (idVal == null)
                    {
                        PropertyInfo p = AccessTools.Property(st, idNames[i]);
                        if (p != null) idVal = p.GetValue(stub, null);
                    }
                    if (idVal != null) idStr = idVal.ToString();
                }

                // Public instance fields via reflection scan
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(idStr))
                {
                    foreach (FieldInfo f in st.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        object v = f.GetValue(stub);
                        if (v == null) continue;
                        string fn = f.Name.ToLowerInvariant();
                        if (string.IsNullOrEmpty(title) && v is string && (fn.Contains("title") || fn == "name"))
                            title = (string)v;
                        else if (string.IsNullOrEmpty(folder) && v is string && fn.Contains("folder"))
                            folder = (string)v;
                        else if (string.IsNullOrEmpty(idStr) && (fn == "id" || fn.Contains("workshop") || fn.Contains("fileid")))
                            idStr = v.ToString();
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(folder) && string.IsNullOrEmpty(idStr))
                return;

            // De-dupe
            string idKey = idStr ?? "0";
            for (int i = 0; i < quickLobbyModIds.Count; i++)
            {
                if (quickLobbyModIds[i] == idKey &&
                    (i < quickLobbyModFolders.Count ? quickLobbyModFolders[i] : "") == (folder ?? ""))
                    return;
            }

            string key = idKey + "|" + (folder ?? "");
            bool en = true;
            if (prevEnabled != null && prevEnabled.ContainsKey(key))
                en = prevEnabled[key];

            quickLobbyModTitles.Add(!string.IsNullOrEmpty(title) ? title : (folder ?? idKey));
            quickLobbyModIds.Add(idKey);
            quickLobbyModFolders.Add(folder ?? "");
            quickLobbyModEnabled.Add(en);
        }

        /// <summary>
        /// Parse room CustomProperties["modList"] JSON into the quick-lobby mod lists.
        /// </summary>
        private void ImportModsFromRoomProperties(ExitGames.Client.Photon.Hashtable props, bool selectAll)
        {
            if (props == null || !props.ContainsKey("modList"))
                return;
            object raw = props["modList"];
            string json = raw as string;
            if (string.IsNullOrEmpty(json))
                return;

            // Minimal array-of-objects parse: {"id":"...","folderTitle":"...","title":"..."}
            int pos = 0;
            while (pos < json.Length)
            {
                int objStart = json.IndexOf('{', pos);
                if (objStart < 0) break;
                int objEnd = json.IndexOf('}', objStart);
                if (objEnd < 0) break;
                string obj = json.Substring(objStart, objEnd - objStart + 1);
                pos = objEnd + 1;

                string id = ExtractJsonString(obj, "id");
                string folder = ExtractJsonString(obj, "folderTitle");
                string title = ExtractJsonString(obj, "title");
                if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(folder) && string.IsNullOrEmpty(id))
                    continue;

                // de-dupe
                bool exists = false;
                for (int i = 0; i < quickLobbyModIds.Count; i++)
                {
                    if (quickLobbyModIds[i] == (id ?? "0") &&
                        quickLobbyModFolders[i] == (folder ?? ""))
                    {
                        if (selectAll) quickLobbyModEnabled[i] = true;
                        exists = true;
                        break;
                    }
                }
                if (exists) continue;

                quickLobbyModTitles.Add(!string.IsNullOrEmpty(title) ? title : (folder ?? id ?? "?"));
                quickLobbyModIds.Add(id ?? "0");
                quickLobbyModFolders.Add(folder ?? "");
                quickLobbyModEnabled.Add(selectAll || true);
            }

            SortModListAlphabetical();
        }

        private static string ExtractJsonString(string obj, string key)
        {
            string pattern = "\"" + key + "\"";
            int k = obj.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (k < 0) return null;
            int colon = obj.IndexOf(':', k + pattern.Length);
            if (colon < 0) return null;
            int q1 = obj.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = obj.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return obj.Substring(q1 + 1, q2 - q1 - 1);
        }

        private void CopyModsFromCurrentRoom()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            {
                ShowToast("Join a room first to copy its mods");
                return;
            }
            try
            {
                int before = quickLobbyModTitles.Count;
                ImportModsFromRoomProperties(PhotonNetwork.CurrentRoom.CustomProperties, true);
                // Select all that came from room
                for (int i = 0; i < quickLobbyModEnabled.Count; i++)
                    quickLobbyModEnabled[i] = true;
                int n = CountEnabledMods();
                ShowToast(n > 0
                    ? ("Copied " + n + " mod(s) from room → lobby maker")
                    : "Room has no modList property");
                Logger.LogInfo("CopyModsFromCurrentRoom: " + n + " mods (was " + before + ")");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("CopyModsFromCurrentRoom: " + ex.Message);
                ShowToast("Copy failed: " + ex.Message);
            }
        }

        private void LoadModPresetsFromConfig()
        {
            modPresetNames.Clear();
            if (configModPresets == null || string.IsNullOrEmpty(configModPresets.Value))
                return;
            string[] parts = configModPresets.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                string name = parts[i].Substring(0, eq).Trim();
                if (!string.IsNullOrEmpty(name) && !modPresetNames.Contains(name))
                    modPresetNames.Add(name);
            }
        }

        private string GetModPresetPayload(string name)
        {
            if (configModPresets == null || string.IsNullOrEmpty(name)) return null;
            string[] parts = configModPresets.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(parts[i].Substring(0, eq).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return parts[i].Substring(eq + 1);
            }
            return null;
        }

        private void SaveCurrentModsAsPreset(string name)
        {
            if (string.IsNullOrEmpty(name) || configModPresets == null)
            {
                ShowToast("Need a name");
                return;
            }
            name = name.Trim().Replace("=", "").Replace(";", "");
            string json = BuildModListJson();
            // rewrite config
            System.Collections.Generic.List<string> entries = new System.Collections.Generic.List<string>();
            string[] parts = (configModPresets.Value ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            bool replaced = false;
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                string n = parts[i].Substring(0, eq).Trim();
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(name + "=" + json);
                    replaced = true;
                }
                else
                    entries.Add(parts[i]);
            }
            if (!replaced)
                entries.Add(name + "=" + json);
            configModPresets.Value = string.Join(";", entries.ToArray());
            LoadModPresetsFromConfig();
            ShowToast("Saved: " + name);
        }

        private void ApplyModPreset(string name)
        {
            if (PhotonNetwork.InRoom)
            {
                ShowToast("Cannot load mod presets while in a room");
                return;
            }
            string payload = GetModPresetPayload(name);
            if (string.IsNullOrEmpty(payload))
            {
                ShowToast("Not found");
                return;
            }
            // Clear selection then import JSON as if room property
            for (int i = 0; i < quickLobbyModEnabled.Count; i++)
                quickLobbyModEnabled[i] = false;
            ExitGames.Client.Photon.Hashtable fake = new ExitGames.Client.Photon.Hashtable();
            fake["modList"] = payload;
            ImportModsFromRoomProperties(fake, true);
            // enable only those in payload
            ShowToast("Loaded: " + name);
        }

        private void DeleteModPreset(string name)
        {
            if (configModPresets == null || string.IsNullOrEmpty(name)) return;
            System.Collections.Generic.List<string> entries = new System.Collections.Generic.List<string>();
            string[] parts = (configModPresets.Value ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                string n = parts[i].Substring(0, eq).Trim();
                if (!string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                    entries.Add(parts[i]);
            }
            configModPresets.Value = string.Join(";", entries.ToArray());
            LoadModPresetsFromConfig();
            selectedModPreset = -1;
            ShowToast("Deleted: " + name);
        }

        private string BuildModListJson()
        {
            // NetworkManager.TryParseMods expects JSON array of { id, folderTitle, title }
            // Never advertise map packs here — joiners treat modList as Workshop content to download.
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append('[');
            bool first = true;
            for (int i = 0; i < quickLobbyModTitles.Count; i++)
            {
                if (i >= quickLobbyModEnabled.Count || !quickLobbyModEnabled[i])
                    continue;
                string id = i < quickLobbyModIds.Count ? quickLobbyModIds[i] : "0";
                string folder = i < quickLobbyModFolders.Count ? quickLobbyModFolders[i] : "";
                string title = quickLobbyModTitles[i];
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":\"");
                sb.Append(EscapeJson(id));
                sb.Append("\",\"folderTitle\":\"");
                sb.Append(EscapeJson(folder));
                sb.Append("\",\"title\":\"");
                sb.Append(EscapeJson(title));
                sb.Append("\"}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private object BeginSetLoadedModsForSelection(out string error)
        {
            error = null;
            Type mm = SafeGameType("ModManager");
            if (mm == null)
            {
                error = "ModManager type missing";
                return null;
            }

            MethodInfo setLoaded = AccessTools.Method(mm, "SetLoadedMods");
            if (setLoaded == null)
            {
                error = "SetLoadedMods missing";
                return null;
            }

            // Nested ModStub type
            Type stubType = mm.GetNestedType("ModStub", BindingFlags.Public | BindingFlags.NonPublic);
            if (stubType == null)
                stubType = SafeGameType("ModManager+ModStub");
            if (stubType == null)
            {
                // Search all nested
                foreach (Type nt in mm.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (nt.Name.IndexOf("ModStub", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        stubType = nt;
                        break;
                    }
                }
            }
            if (stubType == null)
            {
                error = "ModStub type missing";
                return null;
            }

            // List<ModStub>
            Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(stubType);
            object list = Activator.CreateInstance(listType);
            MethodInfo listAdd = listType.GetMethod("Add");

            Type publishedIdType = null;
            try { publishedIdType = AccessTools.TypeByName("Steamworks.PublishedFileId_t"); } catch { }
            if (publishedIdType == null)
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        Type t = asm.GetType("Steamworks.PublishedFileId_t");
                        if (t != null) { publishedIdType = t; break; }
                    }
                    catch { }
                }
            }

            // ModSource enum (prefer Any)
            Type modSourceType = mm.GetNestedType("ModSource", BindingFlags.Public | BindingFlags.NonPublic);
            object modSourceAny = null;
            if (modSourceType != null && modSourceType.IsEnum)
            {
                try { modSourceAny = Enum.Parse(modSourceType, "Any"); }
                catch
                {
                    try { modSourceAny = Enum.GetValues(modSourceType).GetValue(0); } catch { }
                }
            }

            ConstructorInfo bestCtor = null;
            ConstructorInfo[] ctors = stubType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            // Prefer (string title, PublishedFileId_t id, ModSource source, string folderTitle)
            for (int c = 0; c < ctors.Length; c++)
            {
                ParameterInfo[] ps = ctors[c].GetParameters();
                if (ps.Length >= 2)
                {
                    bestCtor = ctors[c];
                    if (ps.Length == 4) break;
                }
            }

            int added = 0;
            for (int i = 0; i < quickLobbyModTitles.Count; i++)
            {
                if (i >= quickLobbyModEnabled.Count || !quickLobbyModEnabled[i])
                    continue;

                string title = quickLobbyModTitles[i];
                string folder = i < quickLobbyModFolders.Count ? quickLobbyModFolders[i] : "";
                string idStr = i < quickLobbyModIds.Count ? quickLobbyModIds[i] : "0";
                ulong idNum = 0;
                ulong.TryParse(idStr, out idNum);

                object stub = null;
                try
                {
                    if (bestCtor != null)
                    {
                        ParameterInfo[] ps = bestCtor.GetParameters();
                        object[] args = new object[ps.Length];
                        for (int p = 0; p < ps.Length; p++)
                        {
                            Type pt = ps[p].ParameterType;
                            if (pt == typeof(string))
                            {
                                // first string = title, later = folder
                                if (p == 0) args[p] = title ?? "";
                                else args[p] = folder ?? title ?? "";
                            }
                            else if (publishedIdType != null && pt == publishedIdType)
                            {
                                args[p] = Activator.CreateInstance(publishedIdType, new object[] { idNum });
                            }
                            else if (pt.IsEnum || (modSourceType != null && pt == modSourceType))
                            {
                                args[p] = modSourceAny ?? Activator.CreateInstance(pt);
                            }
                            else if (pt == typeof(ulong) || pt == typeof(long))
                            {
                                args[p] = idNum;
                            }
                            else if (pt == typeof(uint) || pt == typeof(int))
                            {
                                args[p] = (int)idNum;
                            }
                            else
                            {
                                args[p] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
                            }
                        }
                        stub = bestCtor.Invoke(args);
                    }
                    else
                    {
                        stub = Activator.CreateInstance(stubType);
                        // try set fields
                        foreach (FieldInfo f in stubType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            string fn = f.Name.ToLowerInvariant();
                            if (fn.Contains("title") && f.FieldType == typeof(string) && !fn.Contains("folder"))
                                f.SetValue(stub, title);
                            else if (fn.Contains("folder") && f.FieldType == typeof(string))
                                f.SetValue(stub, folder);
                            else if ((fn == "id" || fn.Contains("workshop")) && publishedIdType != null && f.FieldType == publishedIdType)
                                f.SetValue(stub, Activator.CreateInstance(publishedIdType, new object[] { idNum }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Build ModStub " + title + ": " + ex.Message);
                }

                if (stub != null)
                {
                    listAdd.Invoke(list, new object[] { stub });
                    added++;
                }
            }

            if (added == 0)
            {
                error = "No ModStub instances could be built";
                return null;
            }

            // SetLoadedMods(List<ModStub>) or SetLoadedMods(IEnumerable)
            object result = null;
            try
            {
                result = setLoaded.Invoke(null, new object[] { list });
            }
            catch (Exception ex)
            {
                // try instance method on ModManager.instance
                try
                {
                    object mmInst = null;
                    PropertyInfo ip = AccessTools.Property(mm, "instance") ?? AccessTools.Property(mm, "Instance");
                    if (ip != null) mmInst = ip.GetValue(null, null);
                    if (mmInst != null)
                        result = setLoaded.Invoke(mmInst, new object[] { list });
                    else
                        throw ex;
                }
                catch (Exception ex2)
                {
                    error = "SetLoadedMods invoke: " + ex2.Message;
                    return null;
                }
            }

            Logger.LogInfo("SetLoadedMods started with " + added + " mod(s)");
            return result; // IEnumerator / IEnumerable coroutine
        }

        private int CountOrphanKobolds()
        {
            if (!PhotonNetwork.InRoom) return 0;
            try
            {
                var liveActors = new System.Collections.Generic.HashSet<int>();
                Player[] players = PhotonNetwork.PlayerList;
                if (players != null)
                    for (int i = 0; i < players.Length; i++)
                        if (players[i] != null) liveActors.Add(players[i].ActorNumber);

                PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
                if (views == null) return 0;
                int count = 0;
                for (int i = 0; i < views.Length; i++)
                {
                    PhotonView view = views[i];
                    if (view == null || view.gameObject == null) continue;
                    if (GetKoboldOn(view.gameObject) == null && !IsValidPlayerKoboldObject(view.gameObject))
                        continue;
                    int ownerActor = 0;
                    try
                    {
                        if (view.Owner != null) ownerActor = view.Owner.ActorNumber;
                        else if (view.OwnerActorNr > 0) ownerActor = view.OwnerActorNr;
                        else if (view.CreatorActorNr > 0) ownerActor = view.CreatorActorNr;
                    }
                    catch { }
                    if (ownerActor <= 0 || !liveActors.Contains(ownerActor))
                        count++;
                }
                return count;
            }
            catch { return 0; }
        }

        private int PurgeOrphanKobolds()
        {

            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            {
                ShowToast("Must be room host to purge orphans");
                return 0;
            }

            try
            {
                System.Collections.Generic.HashSet<int> liveActors =
                    new System.Collections.Generic.HashSet<int>();
                Player[] players = PhotonNetwork.PlayerList;
                if (players != null)
                {
                    for (int i = 0; i < players.Length; i++)
                    {
                        if (players[i] != null)
                            liveActors.Add(players[i].ActorNumber);
                    }
                }

                PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
                if (views == null)
                    return 0;

                int destroyed = 0;
                for (int i = 0; i < views.Length; i++)
                {
                    PhotonView view = views[i];
                    if (view == null || view.gameObject == null) continue;
                    if (GetKoboldOn(view.gameObject) == null && !IsValidPlayerKoboldObject(view.gameObject))
                        continue;

                    int ownerActor = 0;
                    try
                    {
                        if (view.Owner != null)
                            ownerActor = view.Owner.ActorNumber;
                        else if (view.OwnerActorNr > 0)
                            ownerActor = view.OwnerActorNr;
                        else if (view.CreatorActorNr > 0)
                            ownerActor = view.CreatorActorNr;
                    }
                    catch { }

                    // Orphan: no owner, or owner not in room
                    bool orphan = ownerActor <= 0 || !liveActors.Contains(ownerActor);
                    if (!orphan) continue;

                    try
                    {
                        Logger.LogInfo("Purge orphan kobold " + view.gameObject.name +
                                       " ownerActor=" + ownerActor + " view=" + view.ViewID);
                        PhotonNetwork.Destroy(view.gameObject);
                        destroyed++;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Purge orphan destroy: " + ex.Message);
                    }
                }

                return destroyed;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("PurgeOrphanKobolds: " + ex.Message);
                ShowToast("Purge failed: " + ex.Message);
                return 0;
            }
        }

        // ============================================================
        // SERVER BROWSER UI + LOGIC
        // ============================================================
        private void DrawServersPanel(float x, float y, float width, float maxHeight)
        {
            float startY = y;
            GUI.Label(new Rect(x, y, width, 24f), new GUIContent("SERVERS (BSL)"), headerStyle);
            y += 28f;

            // Status + buttons
            GUI.Label(new Rect(x, y, width - 220f, 22f), new GUIContent("STATUS: " + serverListStatus), labelStyle);

            if (GUI.Button(new Rect(x + width - 300f, y - 2f, 90f, 28f), new GUIContent("Pull cache"), buttonStyle))
            {
                isBrowsingServers = true;
                if (TryPullRoomListFromGameSpawner())
                    serverListStatus = "PULLED • " + cachedRooms.Count + " rooms (stayed in room)";
                else
                    serverListStatus = "NO CACHE — need lobby for live list";
            }
            if (GUI.Button(new Rect(x + width - 200f, y - 2f, 90f, 28f), new GUIContent("Refresh"), buttonStyle))
                StartServerBrowse();

            if (GUI.Button(new Rect(x + width - 100f, y - 2f, 100f, 28f), new GUIContent("STOP"), buttonStyle))
                StopServerBrowse();

            y += 32f;

            // Current connection info
            string connInfo = PhotonNetwork.InRoom
                ? "IN ROOM: " + (PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : "?")
                : PhotonNetwork.InLobby
                    ? "IN LOBBY"
                    : PhotonNetwork.IsConnected
                        ? "CONNECTED (not in lobby/room)"
                        : "NOT CONNECTED";

            GUI.Label(new Rect(x, y, width, 18f), new GUIContent(connInfo), smallStyle);
            y += 22f;

            // ---- Filters ----
            Event e = Event.current;

            // Name filter
            GUI.Label(new Rect(x, y, 50f, 20f), new GUIContent("Name"), smallStyle);
            Rect nameFilterRect = new Rect(x + 50f, y - 1f, Mathf.Min(220f, width * 0.35f), 22f);
            GUI.Box(nameFilterRect, "");
            string nameShown = string.IsNullOrEmpty(serverNameFilter) ? "contains..." : serverNameFilter;
            if (serverNameFilterFocused) nameShown += "|";
            GUI.Label(new Rect(nameFilterRect.x + 4f, nameFilterRect.y + 2f, nameFilterRect.width - 8f, 18f),
                new GUIContent(nameShown), labelStyle);
            if (e != null && e.type == EventType.MouseDown && nameFilterRect.Contains(e.mousePosition))
            {
                serverNameFilterFocused = true;
                e.Use();
            }
            if (serverNameFilterFocused && e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && serverNameFilter.Length > 0)
                {
                    serverNameFilter = serverNameFilter.Substring(0, serverNameFilter.Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    serverNameFilterFocused = false;
                    e.Use();
                }
                else if (e.character != '\0' && !char.IsControl(e.character) && serverNameFilter.Length < 48)
                {
                    serverNameFilter += e.character;
                    e.Use();
                }
            }

            float fx = nameFilterRect.xMax + 8f;
            if (GUI.Button(new Rect(fx, y - 1f, 88f, 22f),
                new GUIContent(serverFilterOpenOnly ? "OPEN ONLY" : "ALL ROOMS"), buttonStyle))
                serverFilterOpenOnly = !serverFilterOpenOnly;
            fx += 92f;
            if (GUI.Button(new Rect(fx, y - 1f, 88f, 22f),
                new GUIContent(serverShowFavoritesOnly ? "FAVS ONLY" : "ALL/FAVS"), buttonStyle))
                serverShowFavoritesOnly = !serverShowFavoritesOnly;
            fx += 92f;
            string modFilt = serverModFilter == 1 ? "MODDED" : (serverModFilter == 2 ? "VANILLA" : "ALL MODS");
            if (GUI.Button(new Rect(fx, y - 1f, 88f, 22f), new GUIContent(modFilt), buttonStyle))
                serverModFilter = (serverModFilter + 1) % 3;

            y += 26f;
            GUI.Label(new Rect(x, y, 90f, 18f), new GUIContent("Min players " + serverFilterMinPlayers), smallStyle);
            serverFilterMinPlayers = Mathf.RoundToInt(GUI.HorizontalSlider(
                new Rect(x + 95f, y + 2f, 120f, 16f), serverFilterMinPlayers, 0f, 32f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb));
            GUI.Label(new Rect(x + 230f, y, 90f, 18f), new GUIContent("Max " + (serverFilterMaxPlayers >= 255 ? "any" : serverFilterMaxPlayers.ToString())), smallStyle);
            serverFilterMaxPlayers = Mathf.RoundToInt(GUI.HorizontalSlider(
                new Rect(x + 320f, y + 2f, 120f, 16f), serverFilterMaxPlayers, 1f, 255f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb));
            y += 24f;

            // Room list
            const float footerReserve = 110f;
            float listH = Mathf.Max(100f, maxHeight - (y - startY) - footerReserve);
            Rect listRect = new Rect(x, y, width, listH);
            GUI.Box(listRect, "");

            List<RoomInfo> rooms = GetFilteredServerRooms();

            const float rowH = 36f;
            float contentH = rooms.Count * rowH;
            float maxScroll = Mathf.Max(0f, contentH - listH + 10f);

            if (e != null && e.type == EventType.ScrollWheel && listRect.Contains(e.mousePosition))
            {
                serverListScroll.y = Mathf.Clamp(serverListScroll.y + e.delta.y * 30f, 0f, maxScroll);
                e.Use();
            }
            serverListScroll.y = Mathf.Clamp(serverListScroll.y, 0f, maxScroll);

            GUI.BeginGroup(
                new Rect(x + 4f, y + 4f, width - 8f, listH - 8f),
                GUIContent.none,
                GUIStyle.none
            );

            float rowY = -serverListScroll.y;

            if (rooms.Count == 0)
            {
                GUI.Label(new Rect(8f, 12f, width - 24f, 24f),
                    new GUIContent(isBrowsingServers ? "Waiting…" : "No rooms — Refresh"),
                    labelStyle);
            }
            else
            {
                for (int i = 0; i < rooms.Count; i++)
                {
                    RoomInfo room = rooms[i];
                    if (room == null) continue;

                    bool selected = room.Name == selectedRoomName;
                    bool joinable = room.IsOpen && room.PlayerCount < room.MaxPlayers;
                    bool isFav = favoriteRoomNames.Contains(room.Name);

                    string lockIcon = room.IsVisible ? "" : "🔒 ";
                    string favIcon = isFav ? "★ " : "";
                    string status = joinable ? "" : (room.IsOpen ? " [FULL]" : " [CLOSED]");
                    string label = GetRoomInfoLabel(room);
                    string labelPart = string.IsNullOrEmpty(label) ? "" : "  · " + label;
                    bool hasScan = !string.IsNullOrEmpty(room.Name) && peekedRoomPlayers.ContainsKey(room.Name);
                    bool hasPub = !string.IsNullOrEmpty(GetRoomPlayersFromInfo(room));
                    string whoIcon = hasPub ? " 👥" : (hasScan ? " 👁" : "");
                    string text = favIcon + lockIcon + room.Name + "   " + room.PlayerCount + "/" + room.MaxPlayers + status + labelPart + whoIcon;

                    GUIStyle style = selected ? selectedButtonStyle : buttonStyle;
                    if (!joinable) style = GUI.skin.button;

                    float btnW = width - 16f - 36f;
                    Rect rowBtnRect = new Rect(0f, rowY, btnW, 32f);
                    if (GUI.Button(rowBtnRect, new GUIContent(text), style))
                        selectedRoomName = room.Name;

                    // Hover → show published player list (ZexQoLPlayers room prop)
                    if (e != null && rowBtnRect.Contains(e.mousePosition))
                    {
                        string playersCsv = GetRoomPlayersFromInfo(room);
                        serverHoverRoomName = room.Name ?? "";
                        if (!string.IsNullOrEmpty(playersCsv))
                        {
                            serverHoverPlayersText = FormatPlayerListMultiline(playersCsv);
                        }
                        else if (!string.IsNullOrEmpty(room.Name) && peekedRoomPlayers.ContainsKey(room.Name))
                        {
                            serverHoverPlayersText = peekedRoomPlayers[room.Name] + "\n(scanned)";
                        }
                        else
                        {
                            serverHoverPlayersText =
                                "(no list — host publish or SCAN SELECTED)";
                        }
                        // Convert group-local mouse to screen GUI for tooltip after EndGroup
                        serverHoverGuiPos = new Vector2(
                            x + 4f + e.mousePosition.x + 14f,
                            y + 4f + e.mousePosition.y + 18f);
                    }

                    if (GUI.Button(new Rect(btnW + 2f, rowY, 32f, 32f), new GUIContent(isFav ? "★" : "☆"), buttonStyle))
                    {
                        if (isFav) favoriteRoomNames.Remove(room.Name);
                        else favoriteRoomNames.Add(room.Name);
                        SaveFavoriteRoomNames();
                    }

                    rowY += rowH;
                }
            }
            GUI.EndGroup();

            // Clear hover when mouse leaves the list
            if (e != null && e.type != EventType.Layout && !listRect.Contains(e.mousePosition))
            {
                serverHoverRoomName = "";
                serverHoverPlayersText = "";
            }

            y += listH + 10f;

            bool canJoin = !string.IsNullOrEmpty(selectedRoomName) &&
                           cachedRooms.ContainsKey(selectedRoomName) &&
                           cachedRooms[selectedRoomName].IsOpen;

            float joinW = Mathf.Min(180f, (width - 12f) * 0.5f);
            if (GUI.Button(new Rect(x, y, joinW, 32f),
                new GUIContent(canJoin ? "JOIN SELECTED" : "SELECT A ROOM"),
                canJoin ? buttonStyle : GUI.skin.button) && canJoin)
            {
                JoinSelectedServer();
            }

            float half = Mathf.Min(140f, (width - 16f) / 3f);
            bool busy = scanRunning;
            if (GUI.Button(new Rect(x + joinW + 8f, y, half, 32f),
                new GUIContent(busy ? "SCANNING…" : "SCAN 1"),
                (!busy && !string.IsNullOrEmpty(selectedRoomName)) ? buttonStyle : GUI.skin.button)
                && !busy && !string.IsNullOrEmpty(selectedRoomName))
            {
                EnqueueRoomScan(selectedRoomName, clearQueue: true);
            }
            if (GUI.Button(new Rect(x + joinW + 12f + half, y, half, 32f),
                new GUIContent(busy ? ("Q:" + scanQueue.Count) : "SCAN ALL"),
                !busy ? buttonStyle : GUI.skin.button) && !busy)
            {
                EnqueueScanAllOpenRooms();
            }
            if (busy && GUI.Button(new Rect(x + joinW + 16f + half * 2f, y, 56f, 32f),
                new GUIContent("STOP"), buttonStyle))
            {
                scanAbort = true;
                peekStatus = "Stopping scan…";
                peekStatusUntil = Time.unscaledTime + 3f;
            }

            y += 38f;
            string peekLine = (!string.IsNullOrEmpty(peekStatus) && Time.unscaledTime < peekStatusUntil)
                ? peekStatus
                : "SCAN 1 = selected room. SCAN ALL = every open joinable room. Hover row for names.";
            GUI.Label(new Rect(x, y, width, 40f),
                new GUIContent(
                    "Names need a free slot to join. Full rooms can only show names if host has this mod.\n" + peekLine),
                smallStyle);

            // Hover tooltip — draw rich-text names with real colors
            if (!string.IsNullOrEmpty(serverHoverRoomName) && !string.IsNullOrEmpty(serverHoverPlayersText))
            {
                DrawServerHoverTooltip(x, startY, width, maxHeight);
            }
        }

        private void DrawServerHoverTooltip(float panelX, float startY, float width, float maxHeight)
        {
            string body = serverHoverPlayersText ?? "";
            string header = serverHoverRoomName ?? "";

            // Split into lines (names already newline-separated)
            string[] rawLines = body.Split(new char[] { '\n' }, StringSplitOptions.None);
            List<string> lines = new List<string>();
            lines.Add(header);
            for (int i = 0; i < rawLines.Length; i++)
            {
                string ln = rawLines[i];
                if (ln == null) continue;
                // Keep empty lines out; keep "(scanned)" footer
                if (ln.Length == 0) continue;
                lines.Add(SanitizeUnityRichText(ln));
            }

            // Minimal style — this game's Unity IMGUI is stripped (no hover/active/padding).
            GUIStyle tipStyle = new GUIStyle(GUI.skin.label);
            tipStyle.richText = true;
            tipStyle.wordWrap = false;
            tipStyle.alignment = TextAnchor.UpperLeft;
            tipStyle.fontSize = 13;
            tipStyle.normal.textColor = Color.white;

            float tipW = Mathf.Clamp(width * 0.62f, 280f, Mathf.Min(520f, width));
            float lineH = 18f;
            float tipH = Mathf.Clamp(14f + lines.Count * lineH + 10f, 48f, Mathf.Min(360f, maxHeight * 0.85f));

            float tipX = Mathf.Clamp(serverHoverGuiPos.x, panelX, panelX + width - tipW);
            float tipY = Mathf.Clamp(serverHoverGuiPos.y, startY, startY + maxHeight - tipH);

            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.97f);
            GUI.Box(new Rect(tipX, tipY, tipW, tipH), GUIContent.none, GUI.skin.box);
            GUI.color = Color.white;

            float ly = tipY + 8f;
            float lx = tipX + 10f;
            float lw = tipW - 20f;
            int maxLines = Mathf.Max(1, Mathf.FloorToInt((tipH - 16f) / lineH));

            for (int i = 0; i < lines.Count && i < maxLines; i++)
            {
                string line = lines[i];
                // Header (room name) in accent; footer muted; names with rich text
                if (i == 0)
                {
                    Color c = GUI.color;
                    GUI.color = new Color(0.75f, 0.85f, 1f, 1f);
                    GUI.Label(new Rect(lx, ly, lw, lineH), line, tipStyle);
                    GUI.color = c;
                }
                else if (line == "(scanned)" || line.StartsWith("(full") || line.StartsWith("(join"))
                {
                    Color c = GUI.color;
                    GUI.color = new Color(0.65f, 0.65f, 0.7f, 1f);
                    // plain text — strip any tags
                    GUI.Label(new Rect(lx, ly, lw, lineH), StripRichTextTags(line), tipStyle);
                    GUI.color = c;
                }
                else
                {
                    // Force richText path: draw sanitized <color> line
                    GUI.Label(new Rect(lx, ly, lw, lineH), new GUIContent(line), tipStyle);
                }
                ly += lineH;
            }

            GUI.color = prev;
        }

        /// <summary>
        /// Unity IMGUI only renders well-formed &lt;color=#RRGGBB&gt;…&lt;/color&gt;.
        /// Closes unclosed tags and drops broken fragments so colors actually show.
        /// </summary>
        private static string SanitizeUnityRichText(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Fast path: no tags
            if (input.IndexOf('<') < 0)
                return input;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(input.Length + 32);
            int openColors = 0;
            int i = 0;
            while (i < input.Length)
            {
                if (input[i] == '<')
                {
                    int close = input.IndexOf('>', i);
                    if (close < 0)
                    {
                        // dangling '<' — escape rest as text
                        sb.Append(input.Substring(i));
                        break;
                    }

                    string tag = input.Substring(i, close - i + 1);
                    string tagLower = tag.ToLowerInvariant();

                    if (tagLower.StartsWith("<color=") && tag.EndsWith(">"))
                    {
                        // Normalize #RGB / #RRGGBB / #RRGGBBAA
                        string fixedTag = NormalizeColorTag(tag);
                        if (fixedTag != null)
                        {
                            sb.Append(fixedTag);
                            openColors++;
                            i = close + 1;
                            continue;
                        }
                        // bad color tag — skip it
                        i = close + 1;
                        continue;
                    }

                    if (tagLower == "</color>")
                    {
                        if (openColors > 0)
                        {
                            sb.Append("</color>");
                            openColors--;
                        }
                        i = close + 1;
                        continue;
                    }

                    // Other tags (b, i, size, …) — pass through if closed
                    sb.Append(tag);
                    i = close + 1;
                    continue;
                }

                sb.Append(input[i]);
                i++;
            }

            while (openColors > 0)
            {
                sb.Append("</color>");
                openColors--;
            }

            return sb.ToString();
        }

        private static string NormalizeColorTag(string tag)
        {
            // tag like <color=#80FFFF> or <color=#80FFFFFF> or <color=red>
            if (string.IsNullOrEmpty(tag) || tag.Length < 8)
                return null;

            int eq = tag.IndexOf('=');
            if (eq < 0) return null;
            string val = tag.Substring(eq + 1, tag.Length - eq - 2).Trim(); // strip <color= and >
            if (val.Length == 0) return null;

            if (val[0] == '#')
            {
                string hex = val.Substring(1);
                // Expand #RGB → #RRGGBB
                if (hex.Length == 3)
                {
                    hex = string.Concat(
                        hex[0], hex[0],
                        hex[1], hex[1],
                        hex[2], hex[2]);
                }
                // Keep only valid hex length 6 or 8
                if (hex.Length != 6 && hex.Length != 8)
                    return null;
                for (int h = 0; h < hex.Length; h++)
                {
                    char c = hex[h];
                    bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                    if (!ok) return null;
                }
                return "<color=#" + hex + ">";
            }

            // named colors Unity supports: red, green, blue, white, black, yellow, cyan, magenta, grey/gray
            string lower = val.ToLowerInvariant();
            switch (lower)
            {
                case "red":
                case "green":
                case "blue":
                case "white":
                case "black":
                case "yellow":
                case "cyan":
                case "magenta":
                case "grey":
                case "gray":
                    return "<color=" + lower + ">";
                default:
                    return null;
            }
        }

        private static string StripRichTextTags(string input)
        {
            if (string.IsNullOrEmpty(input) || input.IndexOf('<') < 0)
                return input;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '<')
                {
                    int close = input.IndexOf('>', i);
                    if (close >= 0) { i = close; continue; }
                }
                sb.Append(input[i]);
            }
            return sb.ToString();
        }

        private static string FormatPlayerListMultiline(string csv)
        {
            if (string.IsNullOrEmpty(csv))
                return "";
            string[] parts = csv.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> lines = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length > 0)
                    lines.Add(SanitizeUnityRichText(p));
            }
            return string.Join("\n", lines.ToArray());
        }

        // ============================================================
        // ROOM PLAYER SCANNER (coroutine queue)
        // Photon only exposes nicknames after you join. We join, read
        // PlayerList, leave, then process the next queued room.
        // ============================================================

        private void EnqueueRoomScan(string roomName, bool clearQueue)
        {
            if (string.IsNullOrEmpty(roomName))
                return;

            if (clearQueue)
                scanQueue.Clear();

            // Already in that room — just snapshot
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null &&
                string.Equals(PhotonNetwork.CurrentRoom.Name, roomName, StringComparison.Ordinal))
            {
                int n = SnapshotRoomPlayers(roomName);
                peekStatus = "Cached " + n + " name(s) from current room";
                peekStatusUntil = Time.unscaledTime + 4f;
                serverListStatus = "SCANNED · " + roomName + " (" + n + ")";
                return;
            }

            // Skip full rooms (cannot join → cannot read names)
            if (cachedRooms.TryGetValue(roomName, out RoomInfo info) && info != null)
            {
                if (!info.IsOpen)
                {
                    peekStatus = roomName + " is closed — cannot scan";
                    peekStatusUntil = Time.unscaledTime + 3f;
                    return;
                }
                if (info.PlayerCount >= info.MaxPlayers)
                {
                    peekStatus = roomName + " is full — cannot join to read names";
                    peekStatusUntil = Time.unscaledTime + 3f;
                    peekedRoomPlayers[roomName] = "(full — " + info.PlayerCount + "/" + info.MaxPlayers + ", names unknown)";
                    return;
                }
            }

            if (!scanQueue.Contains(roomName))
                scanQueue.Enqueue(roomName);

            EnsureScanRunner();
        }

        private void EnqueueScanAllOpenRooms()
        {
            scanQueue.Clear();
            List<RoomInfo> rooms = GetFilteredServerRooms();
            int added = 0;
            for (int i = 0; i < rooms.Count; i++)
            {
                RoomInfo r = rooms[i];
                if (r == null || string.IsNullOrEmpty(r.Name)) continue;
                if (!r.IsOpen) continue;
                if (r.PlayerCount >= r.MaxPlayers)
                {
                    peekedRoomPlayers[r.Name] = "(full — " + r.PlayerCount + "/" + r.MaxPlayers + ", names unknown)";
                    continue;
                }
                // Prefer published list when already present
                if (!string.IsNullOrEmpty(GetRoomPlayersFromInfo(r)))
                    continue;
                scanQueue.Enqueue(r.Name);
                added++;
            }

            if (added == 0)
            {
                peekStatus = "Nothing to scan (all full, closed, or already published)";
                peekStatusUntil = Time.unscaledTime + 4f;
                return;
            }

            peekStatus = "Queued " + added + " room(s) to scan";
            peekStatusUntil = Time.unscaledTime + 4f;
            EnsureScanRunner();
        }

        private void EnsureScanRunner()
        {
            if (scanRunning)
                return;
            if (scanCoroutine != null)
            {
                StopCoroutine(scanCoroutine);
                scanCoroutine = null;
            }
            scanAbort = false;
            scanCoroutine = StartCoroutine(RoomScanQueueRoutine());
        }

        private IEnumerator RoomScanQueueRoutine()
        {
            scanRunning = true;
            peekInProgress = true;
            scanAbort = false;
            scanLastError = "";

            // Cancel browse auto-rejoin so we own the connection
            pendingRejoinPrevious = false;
            pendingJoinRoomName = "";
            browseRestoreActive = false;
            isBrowsingServers = false;
            if (rejoinCoroutine != null)
            {
                StopCoroutine(rejoinCoroutine);
                rejoinCoroutine = null;
            }
            if (restoreBrowsePositionCoroutine != null)
            {
                StopCoroutine(restoreBrowsePositionCoroutine);
                restoreBrowsePositionCoroutine = null;
            }

            // Remember home room + position (same as REFRESH)
            scanHomeRoom = "";
            scanShouldRejoinHome = false;
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            {
                scanHomeRoom = PhotonNetwork.CurrentRoom.Name;
                previousRoomName = scanHomeRoom;
                scanShouldRejoinHome = true;
                CaptureBrowseTransform();
                SetScanStatus("Saved pos · leaving " + scanHomeRoom + "…");
                Logger.LogInfo("Scan: home room saved = " + scanHomeRoom);
                try
                {
                    if (destroyBodyOnLeave)
                        DestroyLocalPlayerBodyForBrowse();
                }
                catch { }
                try { PhotonNetwork.LeaveRoom(); } catch (Exception ex) { scanLastError = ex.Message; }

                float leaveDeadline = Time.unscaledTime + 4f;
                while (PhotonNetwork.InRoom && Time.unscaledTime < leaveDeadline)
                    yield return null;
            }
            else
            {
                // Scanning from menu/lobby — cannot rejoin a play session afterward
                SetScanStatus("Scan from lobby (no home room to return to)");
                Logger.LogInfo("Scan: not in a room at start — will not auto-rejoin a play room");
            }

            while (scanQueue.Count > 0 && !scanAbort)
            {
                string room = scanQueue.Dequeue();
                scanCurrentRoom = room;
                peekTargetRoom = room;
                scanLastError = "";

                // Skip known-full / closed from cache
                if (cachedRooms.TryGetValue(room, out RoomInfo preInfo) && preInfo != null)
                {
                    if (!preInfo.IsOpen)
                    {
                        peekedRoomPlayers[room] = "(closed)";
                        continue;
                    }
                    if (preInfo.PlayerCount >= preInfo.MaxPlayers)
                    {
                        peekedRoomPlayers[room] = "(full — " + preInfo.PlayerCount + "/" + preInfo.MaxPlayers + ")";
                        continue;
                    }
                }

                if (!PhotonNetwork.IsConnected)
                {
                    SetScanStatus("Disconnected — aborting scan");
                    break;
                }

                // Wait until fully out of previous room AND Photon is ready to send ops
                yield return StartCoroutine(WaitUntilScanCanJoin());

                peekStatus = "SCAN " + room + " (" + scanQueue.Count + " left)";
                peekStatusUntil = Time.unscaledTime + 3f;
                serverListStatus = peekStatus;

                // Up to 2 join attempts — first often fails if master wasn't ready yet
                bool joined = false;
                for (int attempt = 0; attempt < 2 && !joined && !scanAbort; attempt++)
                {
                    if (attempt > 0)
                    {
                        yield return StartCoroutine(WaitUntilScanCanJoin());
                        yield return new WaitForSecondsRealtime(0.15f);
                    }

                    scanLastError = "";
                    bool sent = false;
                    try
                    {
                        sent = PhotonNetwork.JoinRoom(room);
                    }
                    catch (Exception ex)
                    {
                        scanLastError = ex.Message;
                        sent = false;
                    }

                    if (!sent)
                    {
                        // JoinRoom returned false = client not ready; wait and retry
                        scanLastError = string.IsNullOrEmpty(scanLastError) ? "not ready" : scanLastError;
                        yield return new WaitForSecondsRealtime(0.2f);
                        continue;
                    }

                    float joinDeadline = Time.unscaledTime + 5f;
                    while (!PhotonNetwork.InRoom && Time.unscaledTime < joinDeadline && !scanAbort)
                    {
                        // OnJoinRoomFailed sets scanLastError
                        if (!string.IsNullOrEmpty(scanLastError) &&
                            scanLastError.IndexOf("not ready", StringComparison.OrdinalIgnoreCase) < 0)
                            break;
                        yield return null;
                    }

                    if (PhotonNetwork.InRoom)
                        joined = true;
                    else if (string.IsNullOrEmpty(scanLastError))
                        scanLastError = "timeout";
                }

                if (!joined)
                {
                    string why = string.IsNullOrEmpty(scanLastError) ? "unknown" : scanLastError;
                    peekedRoomPlayers[room] = "(join failed: " + why + ")";
                    peekStatus = room + " fail: " + why;
                    peekStatusUntil = Time.unscaledTime + 3f;
                    yield return new WaitForSecondsRealtime(0.1f);
                    continue;
                }

                // Grab names ASAP and leave before full spawn
                int best = 0;
                float readStart = Time.unscaledTime;
                float readDeadline = readStart + 0.55f;
                int expected = 0;
                if (cachedRooms.TryGetValue(room, out RoomInfo ri) && ri != null)
                    expected = ri.PlayerCount;
                int target = expected > 0 ? expected + 1 : 0;

                yield return null;
                best = SnapshotRoomPlayers(room);

                while (Time.unscaledTime < readDeadline && PhotonNetwork.InRoom && !scanAbort)
                {
                    int count = SnapshotRoomPlayers(room);
                    if (count > best)
                        best = count;

                    if (target > 0 && count >= target)
                        break;
                    if (expected > 0 && count >= expected)
                        break;
                    if (count > 0 && Time.unscaledTime > readStart + 0.12f)
                        break;

                    yield return null;
                }

                peekStatus = room + " → " + best + " (" + scanQueue.Count + " left)";
                peekStatusUntil = Time.unscaledTime + 3f;
                if (best <= 0 && !peekedRoomPlayers.ContainsKey(room))
                    peekedRoomPlayers[room] = "(no names read)";

                try
                {
                    if (PhotonNetwork.InRoom)
                        PhotonNetwork.LeaveRoom();
                }
                catch { }

                float leftDeadline = Time.unscaledTime + 4f;
                while (PhotonNetwork.InRoom && Time.unscaledTime < leftDeadline)
                    yield return null;

                // Brief settle so next JoinRoom isn't rejected
                yield return new WaitForSecondsRealtime(0.12f);
            }

            // Always try to return home (even if STOP / fail)
            scanCurrentRoom = "";
            peekTargetRoom = "";

            // CRITICAL: clear scanRunning BEFORE rejoin so OnJoinedRoom runs the normal
            // auto-rejoin + position restore path (not the scan snapshot early-out).
            string home = !string.IsNullOrEmpty(scanHomeRoom) ? scanHomeRoom : previousRoomName;
            bool wantHome = scanShouldRejoinHome && !string.IsNullOrEmpty(home);

            scanRunning = false;
            peekInProgress = false;
            scanQueue.Clear();

            if (wantHome)
            {
                previousRoomName = home;
                pendingRejoinPrevious = true;
                yield return StartCoroutine(ScanRejoinHomeAndRestore());
            }
            else
            {
                SetScanStatus("Scan done (no home room to rejoin)");
                if (!PhotonNetwork.InRoom)
                {
                    try { PhotonNetwork.JoinLobby(); } catch { }
                }
            }

            scanCoroutine = null;
            SetScanStatus(scanAbort
                ? "Scan stopped · " + (wantHome ? ("back in " + home) : "done")
                : "Scan finished · hover for names");
            serverListStatus = "SCAN DONE · " + peekedRoomPlayers.Count + " cached";
        }

        /// <summary>
        /// Rejoin home room after scan using the same path as REFRESH browse rejoin.
        /// </summary>
        private IEnumerator ScanRejoinHomeAndRestore()
        {
            string home = !string.IsNullOrEmpty(scanHomeRoom) ? scanHomeRoom : previousRoomName;
            if (string.IsNullOrEmpty(home))
            {
                SetScanStatus("No home room saved");
                yield break;
            }

            previousRoomName = home;
            pendingRejoinPrevious = true;

            // Leave any room we might still be sitting in
            if (PhotonNetwork.InRoom)
            {
                try { PhotonNetwork.LeaveRoom(); } catch { }
                float leaveUntil = Time.unscaledTime + 6f;
                while (PhotonNetwork.InRoom && Time.unscaledTime < leaveUntil)
                    yield return null;
            }

            if (!PhotonNetwork.IsConnected)
            {
                pendingRejoinPrevious = false;
                SetScanStatus("Disconnected — cannot rejoin " + home);
                yield break;
            }

            // Wait for master/lobby ready
            yield return StartCoroutine(WaitUntilScanCanJoin());

            // Use the same delayed rejoin coroutine as REFRESH
            if (rejoinCoroutine != null)
            {
                StopCoroutine(rejoinCoroutine);
                rejoinCoroutine = null;
            }
            pendingRejoinPrevious = true;
            rejoinCoroutine = StartCoroutine(RejoinPreviousRoomAfterDelay());

            // Wait for that coroutine to finish + for InRoom
            float waitUntil = Time.unscaledTime + 10f;
            while (Time.unscaledTime < waitUntil)
            {
                if (PhotonNetwork.InRoom)
                {
                    string cur = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : "";
                    if (string.Equals(cur, home, StringComparison.Ordinal))
                        break;
                }
                if (rejoinCoroutine == null && !pendingRejoinPrevious)
                {
                    // RejoinPreviousRoomAfterDelay finished without success — try direct join
                    break;
                }
                yield return null;
            }

            // Hard retries if still not home
            for (int attempt = 0; attempt < 3 && !(PhotonNetwork.InRoom &&
                    PhotonNetwork.CurrentRoom != null &&
                    string.Equals(PhotonNetwork.CurrentRoom.Name, home, StringComparison.Ordinal)); attempt++)
            {
                if (PhotonNetwork.InRoom)
                {
                    try { PhotonNetwork.LeaveRoom(); } catch { }
                    float w = Time.unscaledTime + 4f;
                    while (PhotonNetwork.InRoom && Time.unscaledTime < w)
                        yield return null;
                }

                yield return StartCoroutine(WaitUntilScanCanJoin());
                SetScanStatus("Rejoin try " + (attempt + 1) + " → " + home);
                serverListStatus = "SCAN · rejoin " + home + " (" + (attempt + 1) + "/3)";

                bool sent = false;
                try { sent = PhotonNetwork.JoinRoom(home); }
                catch (Exception ex)
                {
                    Logger.LogWarning("Scan hard rejoin: " + ex.Message);
                    sent = false;
                }

                float rj = Time.unscaledTime + 8f;
                while (!PhotonNetwork.InRoom && Time.unscaledTime < rj)
                    yield return null;
            }

            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null &&
                string.Equals(PhotonNetwork.CurrentRoom.Name, home, StringComparison.Ordinal))
            {
                pendingRejoinPrevious = false;
                // Position restore (OnJoinedRoom may have started it; ensure it runs)
                if (browseHasSavedTransform && browsePositionRestoreEnabled)
                {
                    browseRestoreActive = true;
                    browseRestoreUntil = Time.unscaledTime + 6f;
                    if (restoreBrowsePositionCoroutine != null)
                        StopCoroutine(restoreBrowsePositionCoroutine);
                    restoreBrowsePositionCoroutine = StartCoroutine(RestoreBrowseTransformAfterSpawn());
                    SetScanStatus("Back in " + home + " · restoring pos");
                }
                else
                {
                    SetScanStatus(browseHasSavedTransform && !browsePositionRestoreEnabled
                        ? ("Back in " + home + " · pos restore OFF")
                        : ("Back in " + home));
                }
            }
            else
            {
                pendingRejoinPrevious = false;
                SetScanStatus("Rejoin failed — try joining " + home + " manually");
                serverListStatus = "SCAN · rejoin FAILED · " + home;
                // Do NOT JoinLobby here — that can dump to main menu in this game.
                // Stay on master so user can pick a room from the browser.
            }
        }

        private void SetScanStatus(string msg)
        {
            peekStatus = msg;
            peekStatusUntil = Time.unscaledTime + 6f;
            // Avoid LogInfo on hot path — only warn on real failures elsewhere
        }

        /// <summary>
        /// After LeaveRoom Photon is often "connecting to master" and JoinRoom returns false.
        /// Wait until we are out of a room and IsConnectedAndReady (or in lobby).
        /// </summary>
        private IEnumerator WaitUntilScanCanJoin()
        {
            float deadline = Time.unscaledTime + 5f;

            while (PhotonNetwork.InRoom && Time.unscaledTime < deadline)
                yield return null;

            if (!PhotonNetwork.IsConnected)
                yield break;

            // Prefer ConnectedAndReady; JoinLobby helps some builds reach a joinable state
            if (!PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InLobby)
            {
                try { PhotonNetwork.JoinLobby(); } catch { }
            }

            while (Time.unscaledTime < deadline)
            {
                if (PhotonNetwork.InRoom)
                {
                    try { PhotonNetwork.LeaveRoom(); } catch { }
                    yield return null;
                    continue;
                }

                if (PhotonNetwork.IsConnectedAndReady || PhotonNetwork.InLobby)
                    yield break;

                yield return null;
            }
        }

        private int SnapshotRoomPlayers(string roomKey)
        {
            try
            {
                if (string.IsNullOrEmpty(roomKey) && PhotonNetwork.CurrentRoom != null)
                    roomKey = PhotonNetwork.CurrentRoom.Name;

                Player[] players = PhotonNetwork.PlayerList;
                List<string> names = new List<string>();
                if (players != null)
                {
                    for (int i = 0; i < players.Length; i++)
                    {
                        Player p = players[i];
                        if (p == null) continue;
                        // Prefer raw NickName (keeps <color> tags for hover rich text)
                        string n = p.NickName;
                        if (string.IsNullOrEmpty(n))
                            n = GetPlayerName(p);
                        if (string.IsNullOrEmpty(n))
                            n = "Player" + p.ActorNumber;
                        n = n.Replace(',', ' ').Replace(';', ' ').Trim();
                        n = SanitizeUnityRichText(n);
                        if (n.Length > 96) n = n.Substring(0, 96);
                        if (n.Length > 0)
                            names.Add(n);
                    }
                }

                string multiline = names.Count > 0
                    ? string.Join("\n", names.ToArray())
                    : "(empty / no names)";

                if (!string.IsNullOrEmpty(roomKey))
                    peekedRoomPlayers[roomKey] = multiline;

                // Skip network publish while bulk-scanning (extra lag)
                if (!scanRunning && PhotonNetwork.IsMasterClient && publishRoomPlayers)
                    PublishRoomPlayerList(true);

                return names.Count;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("SnapshotRoomPlayers: " + ex.Message);
                return 0;
            }
        }

        // Compatibility shims (old callback paths)
        private void StartPeekSelectedRoom()
        {
            if (!string.IsNullOrEmpty(selectedRoomName))
                EnqueueRoomScan(selectedRoomName, clearQueue: true);
        }

        private void BeginPeekJoin() { }

        private void CachePeekNamesFromCurrentRoom(string roomKey)
        {
            SnapshotRoomPlayers(roomKey);
        }

        private void FinishPeekAndLeave()
        {
            if (PhotonNetwork.InRoom)
            {
                SnapshotRoomPlayers(scanCurrentRoom);
                try { PhotonNetwork.LeaveRoom(); } catch { }
            }
        }

        private void TryReturnToLobbyAfterPeek()
        {
            if (PhotonNetwork.InRoom) return;
            try { PhotonNetwork.JoinLobby(); } catch { }
        }

        private void CheckFavoriteRoomOpenSlots()
        {
            if (favoriteRoomNames == null || favoriteRoomNames.Count == 0) return;
            if (Time.unscaledTime < favRoomNotifyCooldown) return;
            if (cachedRooms == null || cachedRooms.Count == 0) return;
            try
            {
                foreach (var kv in cachedRooms)
                {
                    RoomInfo info = kv.Value;
                    if (info == null || string.IsNullOrEmpty(info.Name)) continue;
                    if (!favoriteRoomNames.Contains(info.Name)) continue;
                    if (!info.IsOpen) continue;
                    if (info.PlayerCount >= info.MaxPlayers) continue;
                    if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null &&
                        string.Equals(PhotonNetwork.CurrentRoom.Name, info.Name, StringComparison.Ordinal))
                        continue;
                    favRoomNotifyCooldown = Time.unscaledTime + 60f;
                    ShowToast("Favorite open: " + info.Name + " (" + info.PlayerCount + "/" + info.MaxPlayers + ")", "scan");
                    return;
                }
            }
            catch { }
        }

        private List<RoomInfo> GetFilteredServerRooms()
        {
            List<RoomInfo> rooms = new List<RoomInfo>();
            foreach (RoomInfo info in cachedRooms.Values)
            {
                if (info == null) continue;
                if (serverFilterOpenOnly && !info.IsOpen) continue;
                if (serverShowFavoritesOnly && !favoriteRoomNames.Contains(info.Name)) continue;
                if (serverModFilter != 0)
                {
                    bool hasMods = false;
                    try
                    {
                        if (info.CustomProperties != null && info.CustomProperties.ContainsKey("modList"))
                        {
                            object ml = info.CustomProperties["modList"];
                            hasMods = ml != null && ml.ToString().Length > 2;
                        }
                    }
                    catch { }
                    if (serverModFilter == 1 && !hasMods) continue;
                    if (serverModFilter == 2 && hasMods) continue;
                }
                if (info.PlayerCount < serverFilterMinPlayers) continue;
                if (serverFilterMaxPlayers < 255 && info.PlayerCount > serverFilterMaxPlayers) continue;
                if (!string.IsNullOrEmpty(serverNameFilter))
                {
                    string label = GetRoomInfoLabel(info);
                    string hay = (info.Name ?? "") + " " + (label ?? "");
                    if (hay.IndexOf(serverNameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                rooms.Add(info);
            }

            rooms.Sort((a, b) =>
            {
                bool aFav = favoriteRoomNames.Contains(a.Name);
                bool bFav = favoriteRoomNames.Contains(b.Name);
                if (aFav != bFav) return aFav ? -1 : 1;
                bool aJoinable = a.IsOpen && a.PlayerCount < a.MaxPlayers;
                bool bJoinable = b.IsOpen && b.PlayerCount < b.MaxPlayers;
                if (aJoinable != bJoinable) return aJoinable ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return rooms;
        }

        private string GetRoomInfoLabel(RoomInfo info)
        {
            if (info == null || info.CustomProperties == null) return "";
            object v;
            if (info.CustomProperties.TryGetValue(RoomLabelPropertyKey, out v) && v != null)
                return v.ToString();
            return "";
        }

        private void StartServerBrowse()
        {
            if (isBrowsingServers) return;

            isBrowsingServers = true;
            pendingRejoinPrevious = false;
            cachedRooms.Clear();
            selectedRoomName = "";
            previousRoomName = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null
                ? PhotonNetwork.CurrentRoom.Name
                : "";

            if (rejoinCoroutine != null)
            {
                StopCoroutine(rejoinCoroutine);
                rejoinCoroutine = null;
            }
            if (restoreBrowsePositionCoroutine != null)
            {
                StopCoroutine(restoreBrowsePositionCoroutine);
                restoreBrowsePositionCoroutine = null;
            }

            serverListStatus = "STARTING...";

            // Stop spectating / following so we don't leave the player in a weird state
            if (spectating) StopSpectating();
            followPlayerActorId = -1;

            if (PhotonNetwork.InRoom)
            {
                // Prefer staying in-room: pull from game spawner / last lobby cache
                if (TryPullRoomListFromGameSpawner() && cachedRooms.Count > 0)
                {
                    isBrowsingServers = true;
                    serverListStatus = "IN-ROOM CACHE • " + cachedRooms.Count +
                        " rooms (Photon only streams live lists in lobby)";
                    ShowToast("Room list from cache (" + cachedRooms.Count + ")", "scan");
                    return;
                }

                // Live list requires lobby — soft leave/rejoin (existing path)
                CaptureBrowseTransform();
                DestroyLocalPlayerBodyForBrowse();
                pendingRejoinPrevious = !string.IsNullOrEmpty(previousRoomName);
                serverListStatus = browseHasSavedTransform
                    ? ("BODY GONE · leaving for live list (pos " + browseSavedPosition.x.ToString("0.0") + ", " + browseSavedPosition.z.ToString("0.0") + ")")
                    : "LEAVING FOR LIVE LIST…";
                try { PhotonNetwork.LeaveRoom(); } catch (Exception ex) { Logger.LogWarning("Browse LeaveRoom: " + ex.Message); }
            }
            else if (PhotonNetwork.InLobby)
            {
                serverListStatus = "IN LOBBY - WAITING FOR LIST...";
                try { PhotonNetwork.JoinLobby(); } catch { }
            }
            else if (PhotonNetwork.IsConnected)
            {
                serverListStatus = "CONNECTED → JOINING LOBBY...";
                TryJoinLobbyForBrowse();
            }
            else
            {
                serverListStatus = "NOT CONNECTED TO PHOTON";
                isBrowsingServers = false;
            }
        }

        private void TryJoinLobbyForBrowse()
        {
            if (!isBrowsingServers) return;
            if (PhotonNetwork.InLobby) return;
            if (PhotonNetwork.InRoom) return;

            try
            {
                serverListStatus = "JOINING LOBBY...";
                PhotonNetwork.JoinLobby();
            }
            catch (Exception ex)
            {
                serverListStatus = "JOIN LOBBY FAILED: " + ex.Message;
                Logger.LogWarning("Server browse JoinLobby failed: " + ex);
            }
        }

        private void StopServerBrowse()
        {
            isBrowsingServers = false;
            pendingRejoinPrevious = false;
            if (rejoinCoroutine != null)
            {
                StopCoroutine(rejoinCoroutine);
                rejoinCoroutine = null;
            }
            serverListStatus = cachedRooms.Count > 0
                ? "STOPPED • " + cachedRooms.Count + " rooms still cached"
                : "STOPPED";
        }

        private void JoinSelectedServer()
        {
            if (string.IsNullOrEmpty(selectedRoomName)) return;
            if (!cachedRooms.TryGetValue(selectedRoomName, out RoomInfo info)) return;
            if (!info.IsOpen)
            {
                serverListStatus = "ROOM IS CLOSED";
                return;
            }

            // Already in that room
            if (PhotonNetwork.InRoom &&
                PhotonNetwork.CurrentRoom != null &&
                PhotonNetwork.CurrentRoom.Name == selectedRoomName)
            {
                serverListStatus = "ALREADY IN THAT ROOM";
                return;
            }

            // Cancel any pending auto-rejoin — user is switching rooms intentionally
            isBrowsingServers = false;
            pendingRejoinPrevious = false;
            browseHasSavedTransform = false;
            browseRestoreActive = false;
            if (rejoinCoroutine != null)
            {
                StopCoroutine(rejoinCoroutine);
                rejoinCoroutine = null;
            }
            if (restoreBrowsePositionCoroutine != null)
            {
                StopCoroutine(restoreBrowsePositionCoroutine);
                restoreBrowsePositionCoroutine = null;
            }

            pendingJoinRoomName = selectedRoomName;

            if (PhotonNetwork.InRoom)
            {
                // Must leave current room before joining another
                serverListStatus = "LEAVING → JOINING " + selectedRoomName + "...";
                LeaveRoomSafe();
            }
            else if (PhotonNetwork.InLobby || PhotonNetwork.IsConnectedAndReady)
            {
                serverListStatus = "JOINING " + selectedRoomName + "...";
                PhotonNetwork.JoinRoom(selectedRoomName);
            }
            else
            {
                serverListStatus = "NOT READY TO JOIN";
                pendingJoinRoomName = "";
            }
        }

        private IEnumerator RejoinPreviousRoomAfterDelay()
        {
            // Wait until we have rooms (or timeout) so the list isn't empty when we rejoin.
            float start = Time.unscaledTime;
            const float minWait = 1.25f;
            const float maxWait = 4.0f;

            while (Time.unscaledTime - start < maxWait)
            {
                if (!pendingRejoinPrevious || string.IsNullOrEmpty(previousRoomName))
                {
                    rejoinCoroutine = null;
                    yield break;
                }

                bool haveList = cachedRooms.Count > 0;
                bool minElapsed = Time.unscaledTime - start >= minWait;

                if (haveList && minElapsed)
                    break;

                yield return null;
            }

            if (!pendingRejoinPrevious || string.IsNullOrEmpty(previousRoomName))
            {
                rejoinCoroutine = null;
                yield break;
            }

            if (PhotonNetwork.InRoom)
            {
                pendingRejoinPrevious = false;
                rejoinCoroutine = null;
                yield break;
            }

            if (!PhotonNetwork.InLobby)
            {
                TryJoinLobbyForBrowse();
                yield return new WaitForSecondsRealtime(0.03f);
            }

            serverListStatus = "REJOINING " + previousRoomName + " • " + cachedRooms.Count + " rooms cached";
            pendingRejoinPrevious = false;

            try
            {
                PhotonNetwork.JoinRoom(previousRoomName);
            }
            catch (Exception ex)
            {
                serverListStatus = "REJOIN ERROR: " + ex.Message;
                Logger.LogWarning("Auto-rejoin failed: " + ex);
            }

            rejoinCoroutine = null;
        }
    }
}