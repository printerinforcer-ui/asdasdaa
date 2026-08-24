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
        private IEnumerator InitialPrefabScan()
        {
            yield return new WaitForSeconds(2f);
            RefreshPrefabs();
        }

        // ============================================================
        // SPAWNER UI / LOGIC
        // ============================================================
        private void DrawSpawnerPanel(float x, float y, float width)
        {

            GUI.Label(new Rect(x, y, width - 120f, 22f), new GUIContent("SELECTED: " + GetSelectedPrefabName()), labelStyle);
            if (GUI.Button(new Rect(x + width - 110f, y - 2f, 110f, 28f), new GUIContent("RESCAN"), buttonStyle))
                RefreshPrefabs();
            y += 34f;

            Rect searchRect = new Rect(x, y, width, 30f);
            GUI.Box(searchRect, new GUIContent(""), GUI.skin.box);
            string display = string.IsNullOrEmpty(searchText) ? "CLICK TO SEARCH..." : searchText;
            GUI.Label(new Rect(x + 8f, y + 3f, width - 16f, 24f), new GUIContent(display), labelStyle);
            if (Event.current.type == EventType.MouseDown && searchRect.Contains(Event.current.mousePosition))
            {
                searchFocused = true;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseDown && !searchRect.Contains(Event.current.mousePosition))
                searchFocused = false;
            y += 36f;

            GUI.Label(new Rect(x, y, width, 22f), new GUIContent(prefabStatus + " | SHOWING " + filteredPrefabList.Count), smallStyle);
            y += 25f;

            float listH = 265f;
            GUI.Box(new Rect(x, y, width, listH), "");
            DrawPrefabList(x + 6f, y + 6f, width - 12f, listH - 12f);
            y += listH + 15f;

            GUI.Label(new Rect(x, y, 70f, 22f), new GUIContent("Amount"), labelStyle);
            amount = Mathf.Clamp(Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 70f, y + 4f, width - 130f, 18f), amount, 1f, 5f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb)), 1, 20);
            GUI.Label(new Rect(x + width - 50f, y, 50f, 22f), new GUIContent(amount.ToString()), labelStyle);
            y += 34f;

            GUI.Label(new Rect(x, y, 80f, 22f), new GUIContent("DISTANCE"), labelStyle);
            spawnDistance = GUI.HorizontalSlider(new Rect(x + 80f, y + 4f, width - 140f, 18f), spawnDistance, 1f, 10f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            GUI.Label(new Rect(x + width - 55f, y, 55f, 22f), new GUIContent(spawnDistance.ToString("0.0")), labelStyle);
            y += 38f;

            float gap = 8f;
            float bw = (width - gap) / 2f;
            if (GUI.Button(new Rect(x, y, bw, 38f), new GUIContent("Spawn"), buttonStyle)) Spawn();
            if (GUI.Button(new Rect(x + bw + gap, y, bw, 38f), new GUIContent("CLEAR ALL"), buttonStyle)) ClearAll();
            y += 44f;
            if (GUI.Button(new Rect(x, y, width, 32f), new GUIContent("SPAWN AT SELECTED WAYPOINT"), buttonStyle))
                SpawnAtSelectedWaypoint();
            y += 38f;
            GUI.Label(new Rect(x, y, width, 24f), new GUIContent("STATUS: " + spawnStatus), smallStyle);
        }

        private void DrawPrefabList(float x, float y, float width, float height)
        {
            const int visibleItems = 7;
            const float itemHeight = 32f;
            if (GUI.Button(new Rect(x, y, 70f, 26f), new GUIContent("UP"), buttonStyle)) MovePrefabList(-1);
            if (GUI.Button(new Rect(x + 76f, y, 70f, 26f), new GUIContent("DOWN"), buttonStyle)) MovePrefabList(1);
            GUI.Label(new Rect(x + 155f, y, width - 155f, 26f), new GUIContent(filteredPrefabList.Count + " MATCHES"), smallStyle);
            y += 31f;
            for (int i = 0; i < visibleItems; i++)
            {
                int index = prefabListOffset + i;
                if (index >= filteredPrefabList.Count) break;
                PrefabEntry entry = filteredPrefabList[index];
                bool isFavorite = favoritePrefabNames.Contains(entry.Name);

                if (GUI.Button(
                    new Rect(x, y + i * itemHeight, 34f, itemHeight - 3f),
                    new GUIContent(isFavorite ? "★" : "☆"),
                    buttonStyle))
                {
                    if (isFavorite) favoritePrefabNames.Remove(entry.Name);
                    else favoritePrefabNames.Add(entry.Name);
                    SaveFavoritePrefabNames();
                    ApplySearch();
                }

                GUIStyle style = index == selectedPrefabIndex ? selectedButtonStyle : buttonStyle;
                if (GUI.Button(new Rect(x + 40f, y + i * itemHeight, width - 40f, itemHeight - 3f), new GUIContent(entry.Name), style))
                {
                    selectedPrefabIndex = index;
                    spawnStatus = "SELECTED " + entry.Name;
                    searchFocused = false;
                }
            }
        }

        private bool FindPreparePool()
        {
            try
            {
                preparePoolType = typeof(PreparePool);
                preparePoolInstanceField = preparePoolType.GetField("instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                dynamicPrefabsField = preparePoolType.GetField("dynamicPrefabs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                return preparePoolInstanceField != null && dynamicPrefabsField != null;
            }
            catch { return false; }
        }

        private void RefreshPrefabs()
        {
            prefabList.Clear();
            filteredPrefabList.Clear();
            selectedPrefabIndex = -1;
            prefabListOffset = 0;
            prefabStatus = "SCANNING...";

            if (preparePoolInstanceField == null || dynamicPrefabsField == null)
                if (!FindPreparePool()) { prefabStatus = "PREPAREPOOL NOT FOUND"; return; }

            try
            {
                object pool = preparePoolInstanceField.GetValue(null);
                if (pool == null) { prefabStatus = "POOL NOT READY"; return; }
                object raw = dynamicPrefabsField.GetValue(pool);
                IDictionary dict = raw as IDictionary;
                if (dict == null) { prefabStatus = "INVALID PREFAB DICTIONARY"; return; }

                foreach (DictionaryEntry entry in dict)
                {
                    string name = entry.Key as string;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    GameObject prefab = GetPrefabFromDynamicEntry(entry.Value);
                    if (prefab != null) prefabList.Add(new PrefabEntry(name, prefab));
                }

                prefabList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.InvariantCultureIgnoreCase));
                ApplySearch();
                prefabStatus = prefabList.Count > 0 ? "FOUND " + prefabList.Count + " PREFABS" : "NO PREFABS FOUND";
            }
            catch (Exception ex)
            {
                prefabStatus = "SCAN FAILED";
                Logger.LogError("Prefab scan failed: " + ex);
            }
        }

        private GameObject GetPrefabFromDynamicEntry(object dynamicEntry)
        {
            try
            {
                IEnumerable list = dynamicEntry as IEnumerable;
                if (list == null) return null;
                GameObject best = null;
                foreach (object pair in list)
                {
                    if (pair == null) continue;
                    FieldInfo objField = pair.GetType().GetField("obj", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (objField == null) continue;
                    GameObject obj = objField.GetValue(pair) as GameObject;
                    if (obj != null) best = obj;
                }
                return best;
            }
            catch { return null; }
        }

        private void ApplySearch()
        {
            filteredPrefabList.Clear();
            string query = searchText == null ? "" : searchText.Trim();
            foreach (PrefabEntry entry in prefabList)
                if (query.Length == 0 || entry.Name.IndexOf(query, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    filteredPrefabList.Add(entry);

            filteredPrefabList.Sort((a, b) =>
            {
                bool aFav = favoritePrefabNames.Contains(a.Name);
                bool bFav = favoritePrefabNames.Contains(b.Name);
                if (aFav != bFav)
                    return aFav ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.InvariantCultureIgnoreCase);
            });

            prefabListOffset = 0;
            selectedPrefabIndex = filteredPrefabList.Count == 0 ? -1 : 0;
        }

        private void MovePrefabList(int direction)
        {
            const int visibleItems = 7;
            int maxOffset = Mathf.Max(0, filteredPrefabList.Count - visibleItems);
            prefabListOffset = Mathf.Clamp(prefabListOffset + direction, 0, maxOffset);
        }

        private PrefabEntry GetSelectedPrefab()
        {
            if (filteredPrefabList.Count == 0) return null;
            if (selectedPrefabIndex < 0 || selectedPrefabIndex >= filteredPrefabList.Count) selectedPrefabIndex = 0;
            return filteredPrefabList[selectedPrefabIndex];
        }

        private string GetSelectedPrefabName()
        {
            PrefabEntry e = GetSelectedPrefab();
            return e == null ? "NONE" : e.Name;
        }

        private void Spawn()
        {
            if (!PhotonNetwork.InRoom) { spawnStatus = "NOT IN ROOM"; return; }
            PrefabEntry entry = GetSelectedPrefab();
            if (entry == null) { spawnStatus = "NO PREFAB SELECTED"; return; }
            Camera cam = Camera.main;
            if (cam == null) { spawnStatus = "NO CAMERA"; return; }
            Vector3 position = cam.transform.position + cam.transform.forward * spawnDistance;
            int count = 0;
            for (int i = 0; i < amount; i++)
            {
                try
                {
                    GameObject obj = PhotonNetwork.Instantiate(entry.Name, position, Quaternion.identity, 0);
                    if (obj == null)
                    {
                        continue;
                    }
                    spawnedObjects.Add(obj); count++;
                }
                catch (Exception ex) { Logger.LogError("Spawn failed: " + ex); spawnStatus = "SPAWN FAILED"; break; }
            }
            if (count > 0) spawnStatus = "SPAWNED " + count + "x " + entry.Name;
        }

        private void SpawnAtSelectedWaypoint()
        {
            if (!PhotonNetwork.InRoom) { spawnStatus = "NOT IN ROOM"; return; }
            PrefabEntry entry = GetSelectedPrefab();
            if (entry == null) { spawnStatus = "NO PREFAB SELECTED"; return; }
            string wpName = selectedShareWaypointName;
            if (string.IsNullOrEmpty(wpName) || !savedWaypoints.ContainsKey(wpName))
            {
                // fallback: first waypoint
                wpName = null;
                foreach (var kv in savedWaypoints) { wpName = kv.Key; break; }
            }
            if (string.IsNullOrEmpty(wpName) || !savedWaypoints.ContainsKey(wpName))
            {
                spawnStatus = "NO WAYPOINT — save one or pick in Teleport";
                ShowToast("No waypoint to spawn at", "system");
                return;
            }
            Vector3 position = savedWaypoints[wpName];
            int count = 0;
            for (int i = 0; i < amount; i++)
            {
                try
                {
                    Vector3 jitter = position + new Vector3(
                        UnityEngine.Random.Range(-0.35f, 0.35f) * i,
                        0.05f * i,
                        UnityEngine.Random.Range(-0.35f, 0.35f) * i);
                    GameObject obj = PhotonNetwork.Instantiate(entry.Name, jitter, Quaternion.identity, 0);
                    if (obj != null) { spawnedObjects.Add(obj); count++; }
                }
                catch (Exception ex) { Logger.LogError("SpawnAtWaypoint: " + ex); spawnStatus = "SPAWN FAILED"; break; }
            }
            if (count > 0)
            {
                spawnStatus = "SPAWNED " + count + "x @ " + wpName;
                ShowToast("Spawned at " + wpName, "gameplay");
            }
        }

        private void ClearAll()
        {
            int cleared = 0;
            for (int i = spawnedObjects.Count - 1; i >= 0; i--)
            {
                GameObject obj = spawnedObjects[i];
                if (obj == null) { spawnedObjects.RemoveAt(i); continue; }
                try
                {
                    PhotonView view = obj.GetComponent<PhotonView>();
                    if (PhotonNetwork.InRoom && view != null) PhotonNetwork.Destroy(view);
                    else Destroy(obj);
                    cleared++;
                }
                catch (Exception ex) { Logger.LogError("Clear failed: " + ex); }
                spawnedObjects.RemoveAt(i);
            }
            spawnStatus = "CLEARED " + cleared + " OBJECTS";
        }
    }
}
