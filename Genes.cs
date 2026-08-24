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
        // GENES PANEL
        // ============================================================
        private void DrawGenesPanel(float x, float y, float width, float maxHeight)
        {
            ResolveGeneTypes();
            float startY = y;
            Event e = Event.current;

            // ---- Top bar ----
            GUI.Label(new Rect(x, y, 160f, 22f), new GUIContent("KOBOLD"), headerStyle);

            // Right-aligned action buttons (presets lives up here so it never gets clipped)
            float bx = x + width;
            float bw;
            bw = 88f; bx -= bw;
            if (GUI.Button(new Rect(bx, y - 2f, bw, 26f), new GUIContent("APPLY"), buttonStyle))
                ApplyGenesToKobold();
            bx -= 4f;
            bw = 72f; bx -= bw;
            if (GUI.Button(new Rect(bx, y - 2f, bw, 26f), new GUIContent("EQUIP"), buttonStyle))
                TryEquipSelectedDick();
            bx -= 4f;
            bw = 86f; bx -= bw;
            if (GUI.Button(new Rect(bx, y - 2f, bw, 26f), new GUIContent("SET CHAR"), buttonStyle))
                TryApplySelectedCharacter();
            bx -= 4f;
            bw = 80f; bx -= bw;
            if (GUI.Button(new Rect(bx, y - 2f, bw, 26f), new GUIContent("Refresh"), buttonStyle))
            {
                RefreshGenesFromKobold();
                RefreshDickOptions();
                RefreshEquipmentCatalog();
                RefreshEquipmentList();
                RefreshCharacterList();
            }
            bx -= 4f;
            int totalPresetsTop = fullPresetNames.Count + statsPresetNames.Count + equipPresetNames.Count;
            string presetsTopLabel = presetsPopupVisible
                ? "CLOSE PRESETS"
                : ("PRESETS (" + totalPresetsTop + ")");
            bw = 130f; bx -= bw;
            if (GUI.Button(new Rect(bx, y - 2f, bw, 26f), new GUIContent(presetsTopLabel), buttonStyle))
            {
                presetsPopupVisible = !presetsPopupVisible;
                if (presetsPopupVisible)
                {
                    presetsPopupRect.x = menuRect.xMax + 12f;
                    presetsPopupRect.y = menuRect.y + 80f;
                    if (presetsPopupRect.xMax > Screen.width - 8f)
                        presetsPopupRect.x = Mathf.Max(8f, menuRect.x - presetsPopupRect.width - 12f);
                }
            }

            y += 28f;
            GUI.Label(new Rect(x, y, width - 8f, 20f), new GUIContent(geneStatus), smallStyle);
            y += 22f;

            // ---- Character picker (modded server avatars) ----
            float charBlockH = Mathf.Min(150f, maxHeight * 0.22f);
            GUI.Label(new Rect(x, y, 160f, 18f), new GUIContent("CHARACTERS"), sectionStyle);
            GUI.Label(new Rect(x + 170f, y, 40f, 18f), new GUIContent("Filter"), smallStyle);
            float filterW = Mathf.Min(280f, width - 400f);
            if (filterW < 160f) filterW = 160f;
            Rect filterRect = new Rect(x + 215f, y - 1f, filterW, 22f);
            GUI.Box(filterRect, "");
            string charFilterShown = string.IsNullOrEmpty(characterFilter) ? "..." : characterFilter;
            if (characterFilterEditing)
                charFilterShown = characterFilter + "|";
            GUI.Label(new Rect(filterRect.x + 6f, filterRect.y + 2f, filterRect.width - 12f, 18f),
                new GUIContent(charFilterShown), labelStyle);
            if (e != null && e.type == EventType.MouseDown && filterRect.Contains(e.mousePosition))
            {
                characterFilterEditing = true;
                geneEditIndex = -1;
                speciesEditing = false;
                presetNameEditing = false;
                equipFilterEditing = false;
                e.Use();
            }
            GUI.Label(new Rect(filterRect.xMax + 10f, y, 160f, 18f),
                new GUIContent(characterOptions.Count + " available"), smallStyle);
            y += 22f;

            Rect charRect = new Rect(x, y, width, charBlockH);
            GUI.Box(charRect, "");
            List<string> filteredChars = GetFilteredCharacters();
            if (filteredChars.Count == 0)
            {
                GUI.Label(new Rect(x + 10f, y + 8f, width - 20f, 40f),
                    new GUIContent(characterOptions.Count == 0
                        ? "Press REFRESH to load characters from Player/Prefab database (modded servers add many more)."
                        : "No characters match filter."),
                    smallStyle);
            }
            else
            {
                const float cRow = 22f;
                // grid-ish: 3 columns
                int cols = 3;
                float colW = (width - 16f) / cols;
                float cContent = Mathf.Ceil(filteredChars.Count / (float)cols) * cRow;
                float cMax = Mathf.Max(0f, cContent - charBlockH + 6f);
                if (e != null && e.type == EventType.ScrollWheel && charRect.Contains(e.mousePosition))
                {
                    characterScroll.y = Mathf.Clamp(characterScroll.y + e.delta.y * 22f, 0f, cMax);
                    e.Use();
                }
                characterScroll.y = Mathf.Clamp(characterScroll.y, 0f, cMax);
                GUI.BeginGroup(new Rect(x + 4f, y + 4f, width - 8f, charBlockH - 8f), GUIContent.none, GUIStyle.none);
                for (int i = 0; i < filteredChars.Count; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    float charX = col * colW;
                    float charY = row * cRow - characterScroll.y;
                    if (charY + cRow < 0f || charY > charBlockH)
                        continue;
                    int realIdx = characterOptions.IndexOf(filteredChars[i]);
                    GUIStyle st = (realIdx == selectedCharacterIndex) ? selectedButtonStyle : buttonStyle;
                    if (GUI.Button(new Rect(charX, charY, colW - 4f, 20f), new GUIContent(filteredChars[i]), st))
                        selectedCharacterIndex = realIdx >= 0 ? realIdx : i;
                }
                GUI.EndGroup();
            }
            y += charBlockH + 10f;

            float gap = 12f;
            float leftW = width * 0.52f;
            float rightW = width - leftW - gap;
            float rightX = x + leftW + gap;
            float bodyH = Mathf.Max(180f, maxHeight - (y - startY) - 8f);

            // ================= LEFT: GENES =================
            float leftY = y;
            GUI.Label(new Rect(x, leftY, leftW, 18f), new GUIContent("GENES"), sectionStyle);
            leftY += 20f;

            float colGene = 78f;
            float colCur = 58f;
            float colSet = leftW - colGene - colCur - 8f;
            GUI.Label(new Rect(x, leftY, colGene, 16f), new GUIContent("Gene"), smallStyle);
            GUI.Label(new Rect(x + colGene, leftY, colCur, 16f), new GUIContent("Cur"), smallStyle);
            GUI.Label(new Rect(x + colGene + colCur, leftY, colSet, 16f), new GUIContent("To set"), smallStyle);
            leftY += 18f;

            float genesListH = Mathf.Min(bodyH * 0.55f, geneFieldDefs.Length * 28f + 8f);
            Rect genesRect = new Rect(x, leftY, leftW, genesListH);
            GUI.Box(genesRect, "");

            const float rowH = 28f;
            float genesContentH = geneFieldDefs.Length * rowH;
            float genesMaxScroll = Mathf.Max(0f, genesContentH - genesListH + 6f);
            if (e != null && e.type == EventType.ScrollWheel && genesRect.Contains(e.mousePosition))
            {
                genesScroll.y = Mathf.Clamp(genesScroll.y + e.delta.y * 22f, 0f, genesMaxScroll);
                e.Use();
            }
            genesScroll.y = Mathf.Clamp(genesScroll.y, 0f, genesMaxScroll);

            // Labels must fit inside the row; labelStyle + short rect was chopping digit tops
            GUI.BeginGroup(new Rect(x + 3f, leftY + 3f, leftW - 6f, genesListH - 6f), GUIContent.none, GUIStyle.none);
            float rowY = -genesScroll.y;
            for (int i = 0; i < geneFieldDefs.Length; i++)
            {
                GUI.Label(new Rect(2f, rowY + 4f, colGene - 4f, 20f), new GUIContent(geneFieldDefs[i].Label), smallStyle);
                GUI.Label(new Rect(colGene, rowY + 4f, colCur - 2f, 20f),
                    new GUIContent(geneCurrent[i].ToString("0.##")), smallStyle);

                Rect fieldRect = new Rect(colGene + colCur, rowY + 3f, Mathf.Max(64f, colSet - 8f), 22f);
                GUI.Box(fieldRect, "");
                string display = geneToSetText[i] ?? geneToSet[i].ToString("0.##");
                if (geneEditIndex == i)
                    display = display + "|";
                GUI.Label(new Rect(fieldRect.x + 4f, fieldRect.y, fieldRect.width - 8f, fieldRect.height),
                    new GUIContent(display), smallStyle);
                if (e != null && e.type == EventType.MouseDown && fieldRect.Contains(e.mousePosition))
                {
                    geneEditIndex = i;
                    speciesEditing = false;
                    presetNameEditing = false;
                    e.Use();
                }
                rowY += rowH;
            }
            GUI.EndGroup();
            leftY += genesListH + 10f;

            // Species + thickness under genes
            GUI.Label(new Rect(x, leftY, 70f, 18f), new GUIContent("Species"), labelStyle);
            Rect spRect = new Rect(x + 72f, leftY, 88f, 22f);
            GUI.Box(spRect, "");
            string spShown = speciesEditing ? speciesEditText + "|" : speciesEditText;
            GUI.Label(new Rect(spRect.x + 4f, spRect.y, spRect.width - 8f, spRect.height),
                new GUIContent(TruncateForDisplay(spShown, 12)), smallStyle);
            if (e != null && e.type == EventType.MouseDown && spRect.Contains(e.mousePosition))
            {
                speciesEditing = true;
                geneEditIndex = -1;
                presetNameEditing = false;
                e.Use();
            }
            GUI.Label(new Rect(x + 128f, leftY, leftW - 130f, 18f), new GUIContent(speciesName), smallStyle);
            leftY += 24f;

            GUI.Label(new Rect(x, leftY, 90f, 18f),
                new GUIContent("CockThick " + cockThickness.ToString("0.00")), labelStyle);
            cockThickness = GUI.HorizontalSlider(
                new Rect(x + 100f, leftY + 2f, leftW - 108f, 16f),
                cockThickness, 0.1f, 3f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            leftY += 28f;

            // ================= RIGHT: DICK + EQUIP + PRESETS =================
            float rightY = y;

            // Dick list  (presets moved to pop-out → give dick/clothes more room)
            GUI.Label(new Rect(rightX, rightY, rightW, 18f), new GUIContent("DICK / EQUIP"), sectionStyle);
            rightY += 20f;
            float dickH = bodyH * 0.38f;
            Rect dickRect = new Rect(rightX, rightY, rightW, dickH);
            GUI.Box(dickRect, "");
            if (dickOptions.Count == 0)
            {
                GUI.Label(new Rect(rightX + 8f, rightY + 8f, rightW - 16f, 40f),
                    new GUIContent("Refresh to load"), smallStyle);
            }
            else
            {
                const float dRow = 24f;
                float dContent = dickOptions.Count * dRow;
                float dMax = Mathf.Max(0f, dContent - dickH + 6f);
                if (e != null && e.type == EventType.ScrollWheel && dickRect.Contains(e.mousePosition))
                {
                    dickScroll.y = Mathf.Clamp(dickScroll.y + e.delta.y * 22f, 0f, dMax);
                    e.Use();
                }
                dickScroll.y = Mathf.Clamp(dickScroll.y, 0f, dMax);
                GUI.BeginGroup(new Rect(rightX + 3f, rightY + 3f, rightW - 6f, dickH - 6f), GUIContent.none, GUIStyle.none);
                float dy = -dickScroll.y;
                for (int i = 0; i < dickOptions.Count; i++)
                {
                    GUIStyle st = (i == selectedDickIndex) ? selectedButtonStyle : buttonStyle;
                    if (GUI.Button(new Rect(0f, dy, rightW - 12f, 22f), new GUIContent(dickOptions[i]), st))
                        selectedDickIndex = i;
                    dy += dRow;
                }
                GUI.EndGroup();
            }
            rightY += dickH + 8f;

            // Clothing catalog + worn list
            GUI.Label(new Rect(rightX, rightY, rightW - 160f, 18f), new GUIContent("CLOTHES"), sectionStyle);
            if (GUI.Button(new Rect(rightX + rightW - 150f, rightY - 2f, 70f, 20f), new GUIContent("WEAR"), buttonStyle))
                TryWearSelectedEquipment();
            if (GUI.Button(new Rect(rightX + rightW - 75f, rightY - 2f, 75f, 20f), new GUIContent("REMOVE"), buttonStyle))
                TryRemoveSelectedEquipment();
            rightY += 22f;

            // Filter
            GUI.Label(new Rect(rightX, rightY, 40f, 18f), new GUIContent("Find"), smallStyle);
            Rect eqFilterRect = new Rect(rightX + 42f, rightY - 1f, rightW - 50f, 22f);
            GUI.Box(eqFilterRect, "");
            string eqFilterShown = string.IsNullOrEmpty(equipFilter) ? "..." : equipFilter;
            if (equipFilterEditing)
                eqFilterShown = equipFilter + "|";
            GUI.Label(new Rect(eqFilterRect.x + 6f, eqFilterRect.y + 2f, eqFilterRect.width - 12f, 18f),
                new GUIContent(eqFilterShown), labelStyle);
            if (e != null && e.type == EventType.MouseDown && eqFilterRect.Contains(e.mousePosition))
            {
                equipFilterEditing = true;
                geneEditIndex = -1;
                speciesEditing = false;
                presetNameEditing = false;
                characterFilterEditing = false;
                e.Use();
            }
            rightY += 22f;

            // Leave ~36px at the bottom for status + PRESETS button
            float eqH = Mathf.Max(90f, bodyH * 0.42f);
            float halfEq = (rightW - 6f) * 0.5f;

            // Catalog (left)
            GUI.Label(new Rect(rightX, rightY, halfEq, 16f),
                new GUIContent("All (" + equipCatalog.Count + ")"), smallStyle);
            GUI.Label(new Rect(rightX + halfEq + 6f, rightY, halfEq, 16f),
                new GUIContent("Worn (" + equipNames.Count + ")"), smallStyle);
            rightY += 16f;

            Rect catalogRect = new Rect(rightX, rightY, halfEq, eqH);
            Rect wornRect = new Rect(rightX + halfEq + 6f, rightY, halfEq, eqH);
            GUI.Box(catalogRect, "");
            GUI.Box(wornRect, "");

            List<string> filteredEquip = GetFilteredEquipCatalog();
            const float eRow = 20f;

            // Catalog scroll
            float catContent = filteredEquip.Count * eRow;
            float catMax = Mathf.Max(0f, catContent - eqH + 4f);
            if (e != null && e.type == EventType.ScrollWheel && catalogRect.Contains(e.mousePosition))
            {
                equipCatalogScroll.y = Mathf.Clamp(equipCatalogScroll.y + e.delta.y * 20f, 0f, catMax);
                e.Use();
            }
            equipCatalogScroll.y = Mathf.Clamp(equipCatalogScroll.y, 0f, catMax);
            GUI.BeginGroup(new Rect(catalogRect.x + 2f, catalogRect.y + 2f, halfEq - 4f, eqH - 4f), GUIContent.none, GUIStyle.none);
            float cy = -equipCatalogScroll.y;
            if (filteredEquip.Count == 0)
            {
                GUI.Label(new Rect(4f, 4f, halfEq - 12f, 40f),
                    new GUIContent(equipCatalog.Count == 0 ? "Refresh" : "No match"), smallStyle);
            }
            else
            {
                for (int i = 0; i < filteredEquip.Count; i++)
                {
                    int realIdx = equipCatalog.IndexOf(filteredEquip[i]);
                    GUIStyle st = (realIdx == selectedCatalogEquip) ? selectedButtonStyle : buttonStyle;
                    if (GUI.Button(new Rect(0f, cy, halfEq - 8f, 18f), new GUIContent(filteredEquip[i]), st))
                        selectedCatalogEquip = realIdx >= 0 ? realIdx : i;
                    cy += eRow;
                }
            }
            GUI.EndGroup();

            // Worn scroll
            float wornContent = equipNames.Count * eRow;
            float wornMax = Mathf.Max(0f, wornContent - eqH + 4f);
            if (e != null && e.type == EventType.ScrollWheel && wornRect.Contains(e.mousePosition))
            {
                equipScroll.y = Mathf.Clamp(equipScroll.y + e.delta.y * 20f, 0f, wornMax);
                e.Use();
            }
            equipScroll.y = Mathf.Clamp(equipScroll.y, 0f, wornMax);
            GUI.BeginGroup(new Rect(wornRect.x + 2f, wornRect.y + 2f, halfEq - 4f, eqH - 4f), GUIContent.none, GUIStyle.none);
            float wy = -equipScroll.y;
            if (equipNames.Count == 0)
            {
                GUI.Label(new Rect(4f, 4f, halfEq - 12f, 40f),
                    new GUIContent(string.IsNullOrEmpty(equipStatus) ? "Nothing worn" : equipStatus), smallStyle);
            }
            else
            {
                for (int i = 0; i < equipNames.Count; i++)
                {
                    GUIStyle st = (i == selectedWornEquip) ? selectedButtonStyle : buttonStyle;
                    if (GUI.Button(new Rect(0f, wy, halfEq - 8f, 18f), new GUIContent(equipNames[i]), st))
                        selectedWornEquip = i;
                    wy += eRow;
                }
            }
            GUI.EndGroup();
            rightY += eqH + 6f;

            // Auto-clear stale wear/remove status so it doesn't permanently eat layout
            if (!string.IsNullOrEmpty(equipStatus) && Time.unscaledTime > equipStatusUntil)
                equipStatus = "";

            if (!string.IsNullOrEmpty(equipStatus))
            {
                GUI.Label(new Rect(rightX, rightY, rightW, 16f), new GUIContent(equipStatus), smallStyle);
                rightY += 18f;
            }

            // Presets button is in the top bar now (see above)

            // ---- Keyboard input for gene / species / filters / preset name ----
            HandleGenesTextInput(e);
        }

        private void HandleGenesTextInput(Event e)
        {
            if (e == null || e.type != EventType.KeyDown)
                return;

            if (geneEditIndex >= 0 && geneEditIndex < geneFieldDefs.Length)
            {
                int i = geneEditIndex;
                if (geneToSetText[i] == null)
                    geneToSetText[i] = geneToSet[i].ToString("0.##");
                if (e.keyCode == KeyCode.Backspace)
                {
                    if (geneToSetText[i].Length > 0)
                        geneToSetText[i] = geneToSetText[i].Substring(0, geneToSetText[i].Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape)
                {
                    float parsed;
                    if (float.TryParse(geneToSetText[i], out parsed))
                        geneToSet[i] = Mathf.Clamp(parsed, geneFieldDefs[i].Min, geneFieldDefs[i].Max);
                    geneToSetText[i] = geneToSet[i].ToString("0.##");
                    geneEditIndex = -1;
                    e.Use();
                }
                else if (e.character != '\0' && !char.IsControl(e.character))
                {
                    char c = e.character;
                    if ((char.IsDigit(c) || c == '.' || c == '-' || c == ',') && geneToSetText[i].Length < 12)
                    {
                        geneToSetText[i] += c == ',' ? '.' : c;
                        e.Use();
                    }
                }
                return;
            }

            if (speciesEditing)
            {
                if (e.keyCode == KeyCode.Backspace)
                {
                    if (speciesEditText.Length > 0)
                        speciesEditText = speciesEditText.Substring(0, speciesEditText.Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape)
                {
                    int sid;
                    if (int.TryParse(speciesEditText, out sid))
                        speciesId = sid;
                    speciesEditText = speciesId.ToString();
                    speciesEditing = false;
                    e.Use();
                }
                else if (e.character != '\0' && char.IsDigit(e.character) && speciesEditText.Length < 4)
                {
                    speciesEditText += e.character;
                    e.Use();
                }
                return;
            }

            if (presetNameEditing)
            {
                if (e.keyCode == KeyCode.Backspace)
                {
                    if (newPresetName.Length > 0)
                        newPresetName = newPresetName.Substring(0, newPresetName.Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape)
                {
                    presetNameEditing = false;
                    e.Use();
                }
                else if (e.character != '\0' && !char.IsControl(e.character) && newPresetName.Length < 24)
                {
                    newPresetName += e.character;
                    e.Use();
                }
                return;
            }

            if (characterFilterEditing)
            {
                if (e.keyCode == KeyCode.Backspace)
                {
                    if (characterFilter.Length > 0)
                        characterFilter = characterFilter.Substring(0, characterFilter.Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape)
                {
                    characterFilterEditing = false;
                    e.Use();
                }
                else if (e.character != '\0' && !char.IsControl(e.character) && characterFilter.Length < 32)
                {
                    characterFilter += e.character;
                    e.Use();
                }
                return;
            }

            if (equipFilterEditing)
            {
                if (e.keyCode == KeyCode.Backspace)
                {
                    if (equipFilter.Length > 0)
                        equipFilter = equipFilter.Substring(0, equipFilter.Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape)
                {
                    equipFilterEditing = false;
                    e.Use();
                }
                else if (e.character != '\0' && !char.IsControl(e.character) && equipFilter.Length < 32)
                {
                    equipFilter += e.character;
                    e.Use();
                }
            }
        }

        // ============================================================
        // PRESETS POP-OUT WINDOW
        // ============================================================
        private void DrawPresetsPopup()
        {
            if (!presetsPopupVisible)
                return;

            // Keep on screen
            presetsPopupRect.width = Mathf.Clamp(presetsPopupRect.width, 280f, 520f);
            presetsPopupRect.height = Mathf.Clamp(presetsPopupRect.height, 300f, 640f);
            presetsPopupRect.x = Mathf.Clamp(presetsPopupRect.x, 4f, Mathf.Max(4f, Screen.width - presetsPopupRect.width - 4f));
            presetsPopupRect.y = Mathf.Clamp(presetsPopupRect.y, 4f, Mathf.Max(4f, Screen.height - presetsPopupRect.height - 4f));

            presetsPopupRect = GUI.Window(
                9010,
                presetsPopupRect,
                DrawPresetsPopupWindow,
                GUIContent.none,
                windowStyle != null ? windowStyle : GUI.skin.window
            );
        }

        private void DrawPresetsPopupWindow(int id)
        {
            float pad = 12f;
            float x = pad;
            float y = pad;
            float w = presetsPopupRect.width - pad * 2f;
            GUIStyle hdr = headerStyle != null ? headerStyle : GUI.skin.label;
            GUIStyle lbl = labelStyle != null ? labelStyle : GUI.skin.label;
            GUIStyle sm = smallStyle != null ? smallStyle : GUI.skin.label;
            GUIStyle btn = buttonStyle != null ? buttonStyle : GUI.skin.button;
            GUIStyle selBtn = selectedButtonStyle != null ? selectedButtonStyle : GUI.skin.button;

            GUI.Label(new Rect(x, y, w - 70f, 22f), new GUIContent("FULL PRESETS"), hdr);
            if (GUI.Button(new Rect(presetsPopupRect.width - pad - 64f, y - 2f, 64f, 24f),
                new GUIContent("CLOSE"), btn))
            {
                presetsPopupVisible = false;
                presetNameEditing = false;
            }
            y += 26f;

            GUI.Label(new Rect(x, y, w, 32f),
                new GUIContent("Character + genes + clothes"),
                sm);
            y += 34f;

            // Name field
            GUI.Label(new Rect(x, y, 44f, 18f), new GUIContent("Name"), sm);
            Rect nameRect = new Rect(x + 48f, y - 1f, w - 52f, 22f);
            GUI.Box(nameRect, "");
            string nameShown = string.IsNullOrEmpty(newPresetName) ? "Preset name..." : newPresetName;
            if (presetNameEditing) nameShown = newPresetName + "|";
            if (nameShown.Length > 28) nameShown = nameShown.Substring(0, 26) + "…";
            GUI.Label(new Rect(nameRect.x + 6f, nameRect.y + 2f, nameRect.width - 12f, 18f),
                new GUIContent(nameShown), lbl);

            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && nameRect.Contains(e.mousePosition))
            {
                presetNameEditing = true;
                geneEditIndex = -1;
                speciesEditing = false;
                characterFilterEditing = false;
                equipFilterEditing = false;
                e.Use();
            }
            y += 28f;

            // Primary actions
            if (GUI.Button(new Rect(x, y, w, 28f), new GUIContent("SAVE FULL PRESET"), btn))
                SaveCurrentFullPreset();
            y += 32f;

            if (GUI.Button(new Rect(x, y, w, 28f), new GUIContent("APPLY SELECTED (1-click)"), btn))
                ApplySelectedFullPreset();
            y += 32f;

            // Secondary: legacy partial presets
            float third = (w - 12f) / 3f;
            if (GUI.Button(new Rect(x, y, third, 22f), new GUIContent("Save Stats"), btn))
                SaveCurrentStatsPreset();
            if (GUI.Button(new Rect(x + third + 6f, y, third, 22f), new GUIContent("Save Equip"), btn))
                SaveCurrentEquipPreset();
            if (GUI.Button(new Rect(x + (third + 6f) * 2f, y, third, 22f), new GUIContent("Delete"), btn))
                DeleteSelectedPreset();
            y += 26f;

            // Import / export / clone
            if (GUI.Button(new Rect(x, y, third, 22f), new GUIContent("Import CharCon"), btn))
                ImportDefaultCharConPresets();
            if (GUI.Button(new Rect(x + third + 6f, y, third, 22f), new GUIContent("Export All"), btn))
                ExportAllPresetsToLog();
            if (GUI.Button(new Rect(x + (third + 6f) * 2f, y, third, 22f), new GUIContent("Clone Near"), btn))
                CloneNearbyPlayerToPreset();
            y += 28f;

            GUI.Label(new Rect(x, y, w, 16f),
                new GUIContent("[F] full  [S] stats  [E] equip  · CharCon = old KK format"), sm);
            y += 18f;

            // List
            float listH = Mathf.Max(80f, presetsPopupRect.height - y - pad - 8f);
            Rect listRect = new Rect(x, y, w, listH);
            GUI.Box(listRect, "");

            List<string> allPresetLines = new List<string>();
            List<string> allKinds = new List<string>(); // "F" / "S" / "E"
            List<int> allIdx = new List<int>();
            for (int i = 0; i < fullPresetNames.Count; i++)
            {
                allPresetLines.Add("[F] " + fullPresetNames[i]);
                allKinds.Add("F");
                allIdx.Add(i);
            }
            for (int i = 0; i < statsPresetNames.Count; i++)
            {
                allPresetLines.Add("[S] " + statsPresetNames[i]);
                allKinds.Add("S");
                allIdx.Add(i);
            }
            for (int i = 0; i < equipPresetNames.Count; i++)
            {
                allPresetLines.Add("[E] " + equipPresetNames[i]);
                allKinds.Add("E");
                allIdx.Add(i);
            }

            if (allPresetLines.Count == 0)
            {
                GUI.Label(new Rect(x + 8f, y + 10f, w - 16f, 50f),
                    new GUIContent("No presets"),
                    sm);
            }
            else
            {
                const float pRow = 24f;
                float pContent = allPresetLines.Count * pRow;
                float pMax = Mathf.Max(0f, pContent - listH + 4f);
                if (e != null && e.type == EventType.ScrollWheel && listRect.Contains(e.mousePosition))
                {
                    presetScroll.y = Mathf.Clamp(presetScroll.y + e.delta.y * 22f, 0f, pMax);
                    e.Use();
                }
                presetScroll.y = Mathf.Clamp(presetScroll.y, 0f, pMax);

                GUI.BeginGroup(new Rect(x + 3f, y + 3f, w - 6f, listH - 6f), GUIContent.none, GUIStyle.none);
                float py = -presetScroll.y;
                for (int i = 0; i < allPresetLines.Count; i++)
                {
                    string kind = allKinds[i];
                    int idx = allIdx[i];
                    bool sel =
                        (kind == "F" && selectedFullPreset == idx) ||
                        (kind == "S" && selectedStatsPreset == idx) ||
                        (kind == "E" && selectedEquipPreset == idx);
                    if (GUI.Button(new Rect(0f, py, w - 12f, 22f), new GUIContent(allPresetLines[i]), sel ? selBtn : btn))
                    {
                        selectedFullPreset = -1;
                        selectedStatsPreset = -1;
                        selectedEquipPreset = -1;
                        if (kind == "F") selectedFullPreset = idx;
                        else if (kind == "S") selectedStatsPreset = idx;
                        else selectedEquipPreset = idx;
                    }
                    py += pRow;
                }
                GUI.EndGroup();
            }

            GUI.DragWindow(new Rect(0f, 0f, presetsPopupRect.width, 28f));

            if (presetNameEditing)
                HandleGenesTextInput(Event.current);
        }

        private static string TruncateForDisplay(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (maxChars < 4) maxChars = 4;
            if (s.Length <= maxChars) return s;
            return s.Substring(0, maxChars - 1) + "…";
        }

        private List<string> GetFilteredCharacters()
        {
            if (string.IsNullOrEmpty(characterFilter))
                return new List<string>(characterOptions);
            string f = characterFilter.Trim();
            List<string> result = new List<string>();
            for (int i = 0; i < characterOptions.Count; i++)
            {
                string name = characterOptions[i];
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(name);
                    continue;
                }
                // also match without underscores/spaces
                string compact = name.Replace("_", "").Replace(" ", "");
                string fCompact = f.Replace("_", "").Replace(" ", "");
                if (compact.IndexOf(fCompact, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(name);
            }
            return result;
        }

        private static readonly string[] CharacterNameBlocklist =
        {
            "bandage", "banana", "apple", "food", "seed", "weapon", "prop", "item",
            "bottle", "potion", "crate", "barrel", "chair", "table", "door", "wall",
            "floor", "cube", "sphere", "plane", "camera", "light", "audio", "ui_",
            "debug", "test_", "tmp", "particle", "effect", "vfx", "sfx", "hud",
            "projectile", "bullet", "arrow", "tool", "hammer", "shovel", "bucket",
            "plant", "tree", "rock", "stone", "grass", "water", "lava", "fire",
            "prefab", "spawn", "pool", "manager", "system", "network", "photon"
        };

        private bool IsLikelyCharacterName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.Length < 3 || name.Length > 48)
                return false;
            if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
                return false;

            string lower = name.ToLowerInvariant();

            // Hard reject obvious items / props
            for (int i = 0; i < CharacterNameBlocklist.Length; i++)
            {
                if (lower.Contains(CharacterNameBlocklist[i]))
                    return false;
            }

            // Arm_Bandages style: underscore + lowercase segment often = item
            if (name.IndexOf('_') >= 0)
            {
                // Allow a few character packs that use underscores, but reject
                // "Something_Something" that looks like equipment pieces
                string[] parts = name.Split('_');
                for (int p = 0; p < parts.Length; p++)
                {
                    string pl = parts[p].ToLowerInvariant();
                    if (pl == "arm" || pl == "leg" || pl == "head" || pl == "body" ||
                        pl == "hand" || pl == "foot" || pl == "left" || pl == "right" ||
                        pl == "item" || pl == "gear" || pl == "cloth" || pl == "hat")
                        return false;
                }
            }

            // Prefer PascalCase / letter-starting avatar names (ArgonianMaid, Ralsei, Kobold)
            if (!char.IsLetter(name[0]))
                return false;

            // Reject pure numbers / very generic
            if (lower == "player" || lower == "default" || lower == "null" || lower == "none")
                return false;

            return true;
        }

        private void AddCharacterName(string name, HashSet<string> seen, List<string> into)
        {
            if (!IsLikelyCharacterName(name))
                return;
            if (seen.Add(name))
                into.Add(name);
        }

        /// <summary>
        /// Official path: GameManager.GetPlayerDatabase().GetValidPrefabReferenceInfos()
        /// Each entry has GetKey() — species byte indexes this list.
        /// </summary>
        private object GetGamePlayerDatabase()
        {
            Type gm = SafeGameType("GameManager");
            if (gm == null) return null;
            MethodInfo m = AccessTools.Method(gm, "GetPlayerDatabase");
            if (m == null || !m.IsStatic) return null;
            return m.Invoke(null, null);
        }

        private object GetGamePenisDatabase()
        {
            Type gm = SafeGameType("GameManager");
            if (gm == null) return null;
            MethodInfo m = AccessTools.Method(gm, "GetPenisDatabase");
            if (m == null || !m.IsStatic) return null;
            return m.Invoke(null, null);
        }

        private List<object> GetValidPrefabInfos(object database)
        {
            List<object> result = new List<object>();
            if (database == null) return result;
            MethodInfo getValid = AccessTools.Method(database.GetType(), "GetValidPrefabReferenceInfos");
            if (getValid == null) return result;
            object list = getValid.Invoke(database, null);
            IEnumerable en = list as IEnumerable;
            if (en == null) return result;
            foreach (object item in en)
            {
                if (item != null) result.Add(item);
            }
            return result;
        }

        private string GetPrefabInfoKey(object info)
        {
            if (info == null) return null;
            MethodInfo getKey = AccessTools.Method(info.GetType(), "GetKey");
            if (getKey != null)
            {
                object k = getKey.Invoke(info, null);
                return k != null ? k.ToString() : null;
            }
            FieldInfo nf = AccessTools.Field(info.GetType(), "name")
                ?? AccessTools.Field(info.GetType(), "key");
            if (nf != null)
            {
                object v = nf.GetValue(info);
                return v != null ? v.ToString() : null;
            }
            return info.ToString();
        }

        private void RefreshCharacterList()
        {
            characterOptions.Clear();

            try
            {
                // Official: GameManager.GetPlayerDatabase() → GetValidPrefabReferenceInfos() → GetKey()
                object playerDb = GetGamePlayerDatabase();
                List<object> infos = GetValidPrefabInfos(playerDb);
                for (int i = 0; i < infos.Count; i++)
                {
                    string key = GetPrefabInfoKey(infos[i]);
                    if (!string.IsNullOrEmpty(key))
                        characterOptions.Add(key);
                }

                if (characterOptions.Count == 0)
                {
                    characterOptions.Add("Kobold");
                    geneStatus = "Player DB empty — only default. Join modded room + REFRESH.";
                }
                else
                {
                    geneStatus = "Player DB characters: " + characterOptions.Count + " (species index = list order)";
                }
                geneStatusUntil = Time.unscaledTime + 4f;

                if (selectedCharacterIndex >= characterOptions.Count)
                    selectedCharacterIndex = 0;

                // Sync species label from current genes if possible
                if (speciesId >= 0 && speciesId < characterOptions.Count)
                    speciesName = characterOptions[speciesId];
            }
            catch (Exception ex)
            {
                geneStatus = "Character list error: " + ex.Message;
                geneStatusUntil = Time.unscaledTime + 5f;
                Logger.LogWarning("RefreshCharacterList: " + ex);
            }
        }

        private void CollectCharacterNamesFromEnumerable(object source, HashSet<string> seen, List<string> into)
        {
            if (source == null)
                return;

            IDictionary dict = source as IDictionary;
            if (dict != null)
            {
                foreach (object key in dict.Keys)
                {
                    if (key == null) continue;
                    AddCharacterName(key.ToString(), seen, into);
                }
                return;
            }

            IEnumerable list = source as IEnumerable;
            if (list == null || source is string)
                return;

            foreach (object item in list)
            {
                if (item == null) continue;
                string name = null;
                Type it = item.GetType();
                FieldInfo nf = AccessTools.Field(it, "name")
                    ?? AccessTools.Field(it, "Name")
                    ?? AccessTools.Field(it, "prefabName")
                    ?? AccessTools.Field(it, "key")
                    ?? AccessTools.Field(it, "id");
                if (nf != null)
                {
                    object v = nf.GetValue(item);
                    if (v != null) name = v.ToString();
                }
                if (name == null)
                {
                    PropertyInfo np = AccessTools.Property(it, "name")
                        ?? AccessTools.Property(it, "Name");
                    if (np != null)
                    {
                        object v = np.GetValue(item, null);
                        if (v != null) name = v.ToString();
                    }
                }
                if (name == null)
                    name = item.ToString();
                AddCharacterName(name, seen, into);
            }
        }

        private void CollectNamesFromEnumerable(object source, HashSet<string> seen, List<string> into)
        {
            CollectCharacterNamesFromEnumerable(source, seen, into);
        }

        private void TryApplySelectedCharacter()
        {
            if (selectedCharacterIndex < 0 || selectedCharacterIndex >= characterOptions.Count)
            {
                geneStatus = "Select a character first";
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }

            // species byte = index in player DB (genetics). Mesh comes from the spawned prefab —
            // we also try a soft respawn with the selected player prefab when possible.
            string charName = characterOptions[selectedCharacterIndex];
            byte speciesByte = (byte)Mathf.Clamp(selectedCharacterIndex, 0, 255);

            Component kob = FindLocalKobold();
            if (kob == null)
            {
                geneStatus = "No local kobold";
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }

            try
            {
                ResolveGeneTypes();
                object genes = getGenesMethod != null ? getGenesMethod.Invoke(kob, null) : null;
                if (genes == null)
                {
                    geneStatus = "GetGenes returned null";
                    geneStatusUntil = Time.unscaledTime + 3f;
                    return;
                }

                // Always update species gene on a CLONE (in-place mutate breaks change detection)
                object work = CloneGenes(genes);
                FieldInfo spField = AccessTools.Field(work.GetType(), "species");
                if (spField != null)
                    spField.SetValue(work, speciesByte);

                if (setGenesMethod != null)
                    setGenesMethod.Invoke(kob, new object[] { work });

                speciesId = selectedCharacterIndex;
                speciesEditText = speciesId.ToString();
                speciesName = charName;

                // Mesh swap: different character = different networked prefab.
                // Attempt Photon destroy + instantiate using player DB prefab key.
                bool respawned = TryRespawnAsCharacter(kob, selectedCharacterIndex, work);

                geneStatus = respawned
                    ? ("Respawned as " + charName)
                    : ("Species gene → " + charName + " (mesh may need leave/rejoin if body didn't change)");
                geneStatusUntil = Time.unscaledTime + 5f;
            }
            catch (Exception ex)
            {
                geneStatus = "SET CHAR failed: " + ex.Message;
                geneStatusUntil = Time.unscaledTime + 5f;
                Logger.LogWarning("TryApplySelectedCharacter: " + ex);
            }
        }

        private bool TryRespawnAsCharacter(Component kob, int speciesIndex, object genesObj)
        {
            try
            {
                PhotonView pv = kob.GetComponent<PhotonView>() ?? kob.GetComponentInParent<PhotonView>();
                if (pv == null || !pv.IsMine)
                    return false;

                object playerDb = GetGamePlayerDatabase();
                List<object> infos = GetValidPrefabInfos(playerDb);
                if (speciesIndex < 0 || speciesIndex >= infos.Count)
                    return false;

                // Photon name must be PrefabReferenceInfo.GetKey() (same as selectedPlayerPrefab.GetPrefab())
                string photonName = GetPrefabInfoKey(infos[speciesIndex]);
                if (string.IsNullOrEmpty(photonName))
                    return false;

                Vector3 pos = kob.transform.position;
                Quaternion rot = kob.transform.rotation;

                // Point the in-game character setting at this prefab first
                TrySetSelectedPlayerPrefab(speciesIndex, photonName);

                PhotonNetwork.Destroy(pv.gameObject);
                cachedLocalPlayer = null;
                if (PhotonNetwork.LocalPlayer != null)
                    PhotonNetwork.LocalPlayer.TagObject = null;

                // Match NetworkManager.SpawnControllablePlayerRoutine:
                // BitBuffer + AddKoboldGenes + AddBool(true) then PhotonNetwork.Instantiate
                if (TryOfficialSpawnPlayer(pos, rot, photonName, genesObj, speciesIndex))
                    return true;

                // Last resort bare instantiate
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
                    {
                        object g2 = CloneGenes(genesObj);
                        FieldInfo sp = AccessTools.Field(g2.GetType(), "species");
                        if (sp != null)
                            sp.SetValue(g2, (byte)Mathf.Clamp(speciesIndex, 0, 255));
                        setGenesMethod.Invoke(newKob, new object[] { g2 });
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("TryRespawnAsCharacter: " + ex.Message);
            }
            return false;
        }

        private void TrySetSelectedPlayerPrefab(int index, string prefabKey)
        {
            try
            {
                Type nmType = SafeGameType("NetworkManager");
                if (nmType == null) return;

                object nm = null;
                FieldInfo instF = AccessTools.Field(nmType, "instance") ?? AccessTools.Field(nmType, "Instance");
                if (instF != null)
                    nm = instF.GetValue(null);
                if (nm == null)
                {
                    UnityEngine.Object[] found = UnityEngine.Object.FindObjectsOfType(nmType);
                    if (found != null && found.Length > 0)
                        nm = found[0];
                }
                if (nm == null) return;

                FieldInfo prefabField = AccessTools.Field(nmType, "selectedPlayerPrefab");
                if (prefabField == null) return;
                object setting = prefabField.GetValue(nm);
                if (setting == null) return;

                MethodInfo setVal = AccessTools.Method(setting.GetType(), "SetValue", new Type[] { typeof(int) });
                if (setVal != null)
                    setVal.Invoke(setting, new object[] { index });

                FieldInfo selPrefab = AccessTools.Field(setting.GetType(), "selectedPrefab");
                if (selPrefab != null && selPrefab.FieldType == typeof(string))
                    selPrefab.SetValue(setting, prefabKey);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("TrySetSelectedPlayerPrefab: " + ex.Message);
            }
        }

        private bool TryOfficialSpawnPlayer(Vector3 pos, Quaternion rot, string photonName, object genesObj, int speciesIndex)
        {
            try
            {
                Type bitBufferType = SafeGameType("NetStack.Serialization.BitBuffer")
                    ?? SafeGameType("BitBuffer");
                if (bitBufferType == null)
                    return false;

                object buffer = Activator.CreateInstance(bitBufferType, new object[] { 16 });
                if (buffer == null)
                    return false;

                object genesToWrite = genesObj;
                Type loaderType = SafeGameType("PlayerKoboldLoader");
                if (loaderType != null)
                {
                    MethodInfo getPlayerGenes = AccessTools.Method(loaderType, "GetPlayerGenes");
                    if (getPlayerGenes != null && getPlayerGenes.IsStatic)
                    {
                        object pg = getPlayerGenes.Invoke(null, null);
                        if (pg != null)
                            genesToWrite = pg;
                    }
                }

                if (genesToWrite != null)
                {
                    genesToWrite = CloneGenes(genesToWrite);
                    FieldInfo sp = AccessTools.Field(genesToWrite.GetType(), "species");
                    if (sp != null)
                        sp.SetValue(genesToWrite, (byte)Mathf.Clamp(speciesIndex, 0, 255));

                    Type extType = SafeGameType("KoboldGenesBitBufferExtension");
                    MethodInfo addGenes = extType != null
                        ? AccessTools.Method(extType, "AddKoboldGenes")
                        : null;
                    if (addGenes == null)
                        addGenes = AccessTools.Method(bitBufferType, "AddKoboldGenes");

                    if (addGenes != null)
                    {
                        if (addGenes.IsStatic)
                            addGenes.Invoke(null, new object[] { buffer, genesToWrite });
                        else
                            addGenes.Invoke(buffer, new object[] { genesToWrite });
                    }
                }

                MethodInfo addBool = AccessTools.Method(bitBufferType, "AddBool", new Type[] { typeof(bool) });
                if (addBool != null)
                    addBool.Invoke(buffer, new object[] { true }); // Is player kobold

                GameObject spawned = PhotonNetwork.Instantiate(photonName, pos, rot, 0, new object[] { buffer });
                if (spawned == null)
                    return false;

                cachedLocalPlayer = spawned;
                Component newKob = GetKoboldOn(spawned);
                if (newKob != null && PhotonNetwork.LocalPlayer != null)
                    PhotonNetwork.LocalPlayer.TagObject = newKob;

                try
                {
                    Type cdType = SafeGameType("CharacterDescriptor");
                    if (cdType != null)
                    {
                        Component cd = spawned.GetComponentInChildren(cdType, true);
                        if (cd != null)
                        {
                            MethodInfo setEye = AccessTools.Method(cdType, "SetEyeDir");
                            if (setEye != null)
                                setEye.Invoke(cd, new object[] { rot * Vector3.forward });
                        }
                    }
                }
                catch { }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("TryOfficialSpawnPlayer: " + ex.Message);
                return false;
            }
        }

        private void ResolveGeneTypes()
        {
            if (geneTypesResolved)
                return;
            geneTypesResolved = true;

            try
            {
                koboldType = SafeGameType("Kobold");
                koboldGenesType = SafeGameType("KoboldGenes");
                if (koboldGenesType == null)
                    koboldGenesType = SafeGameType("KoboldKare.KoboldGenes");

                if (koboldType != null)
                {
                    getGenesMethod = AccessTools.Method(koboldType, "GetGenes");
                    setGenesMethod = AccessTools.Method(koboldType, "SetGenes", new Type[] { koboldGenesType });
                    if (setGenesMethod == null && koboldGenesType != null)
                        setGenesMethod = AccessTools.Method(koboldType, "SetGenes");
                }

                if (koboldType == null || koboldGenesType == null)
                    geneStatus = "Gene types not found — open game with Assembly-CSharp loaded";
                else if (getGenesMethod == null)
                    geneStatus = "Kobold found, but GetGenes missing";
                else
                    geneStatus = "Ready — press REFRESH";
            }
            catch (Exception ex)
            {
                geneStatus = "Resolve failed: " + ex.Message;
                Logger.LogWarning("Genes resolve: " + ex);
            }

            for (int i = 0; i < geneFieldDefs.Length; i++)
            {
                geneCurrent[i] = geneFieldDefs[i].DefaultValue;
                geneToSet[i] = geneFieldDefs[i].DefaultValue;
                geneToSetText[i] = geneFieldDefs[i].DefaultValue.ToString("0.##");
            }
        }

        /// <summary>
        /// True only for actual player kobold bodies — not bananas, doors, props, etc.
        /// Official game stores the Kobold component on Player.TagObject.
        /// </summary>
        private bool IsValidPlayerKoboldObject(GameObject go)
        {
            if (go == null)
                return false;

            ResolveGeneTypes();
            if (koboldType == null)
                return false;

            // Must have a Kobold on this object (not only buried deep under a prop hierarchy)
            Component kob = go.GetComponent(koboldType);
            if (kob == null)
            {
                // TagObject is sometimes the Kobold component's gameObject already
                kob = go.GetComponentInChildren(koboldType, true);
                if (kob == null)
                    return false;
                // Prefer the kobold's own gameObject as root
                go = kob.gameObject;
            }

            PhotonView view = go.GetComponent<PhotonView>();
            if (view == null)
                view = go.GetComponentInParent<PhotonView>();
            if (view == null)
                return false;

            // Must be owned/controlled by a real Photon player (not scene objects)
            if (view.Owner == null && view.Controller == null && !view.IsMine)
                return false;

            return true;
        }

        private Component GetKoboldOn(GameObject go)
        {
            if (go == null)
                return null;
            ResolveGeneTypes();
            if (koboldType == null)
                return null;

            Component k = go.GetComponent(koboldType);
            if (k != null)
                return k;
            return go.GetComponentInChildren(koboldType, true);
        }

        private Component FindLocalKobold()
        {
            // 1) Official path: TagObject is the Kobold itself
            if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
            {
                object tag = PhotonNetwork.LocalPlayer.TagObject;
                if (tag != null)
                {
                    if (koboldType != null && koboldType.IsInstanceOfType(tag))
                        return (Component)tag;

                    Component asComp = tag as Component;
                    if (asComp != null)
                    {
                        Component k = GetKoboldOn(asComp.gameObject);
                        if (k != null)
                            return k;
                    }

                    GameObject asGo = tag as GameObject;
                    if (asGo != null)
                    {
                        Component k = GetKoboldOn(asGo);
                        if (k != null)
                            return k;
                    }
                }
            }

            // 2) Cached root (do NOT call GetLocalPlayer here — it can call us)
            if (cachedLocalPlayer != null && IsValidPlayerKoboldObject(cachedLocalPlayer))
            {
                Component k = GetKoboldOn(cachedLocalPlayer);
                if (k != null)
                    return k;
            }

            // 3) Last resort: PhotonView.IsMine that actually has a Kobold
            PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
            if (views != null)
            {
                for (int i = 0; i < views.Length; i++)
                {
                    PhotonView view = views[i];
                    if (view == null || !view.IsMine)
                        continue;
                    Component k = GetKoboldOn(view.gameObject);
                    if (k != null)
                    {
                        cachedLocalPlayer = k.gameObject;
                        return k;
                    }
                }
            }

            return null;
        }

        private FieldInfo FindGeneField(object genes, string[] names)
        {
            if (genes == null)
                return null;
            Type t = genes.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                FieldInfo f = AccessTools.Field(t, names[i]);
                if (f != null)
                    return f;
            }
            // Property fallback
            return null;
        }

        private float ReadGeneValue(object genes, GeneFieldDef def)
        {
            FieldInfo f = FindGeneField(genes, def.FieldNames);
            if (f == null)
                return def.DefaultValue;
            try
            {
                object v = f.GetValue(genes);
                if (v is float) return (float)v;
                if (v is double) return (float)(double)v;
                if (v is int) return (int)v;
                if (v is byte) return (byte)v;
                if (v is short) return (short)v;
                float parsed;
                if (v != null && float.TryParse(v.ToString(), out parsed))
                    return parsed;
            }
            catch { }
            return def.DefaultValue;
        }

        private void WriteGeneValue(object genes, GeneFieldDef def, float value)
        {
            FieldInfo f = FindGeneField(genes, def.FieldNames);
            if (f == null)
                return;
            try
            {
                value = Mathf.Clamp(value, def.Min, def.Max);
                Type ft = f.FieldType;
                if (ft == typeof(float))
                    f.SetValue(genes, value);
                else if (ft == typeof(double))
                    f.SetValue(genes, (double)value);
                else if (ft == typeof(int))
                    f.SetValue(genes, Mathf.RoundToInt(value));
                else if (ft == typeof(byte))
                    f.SetValue(genes, (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255));
                else if (ft == typeof(short))
                    f.SetValue(genes, (short)Mathf.RoundToInt(value));
                else
                    f.SetValue(genes, Convert.ChangeType(value, ft));
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Write gene " + def.Label + ": " + ex.Message);
            }
        }

        private void RefreshGenesFromKobold()
        {
            ResolveGeneTypes();
            Component kob = FindLocalKobold();
            if (kob == null)
            {
                geneStatus = "No local kobold found (join a room / spawn in)";
                geneStatusUntil = Time.unscaledTime + 4f;
                return;
            }

            object genes = null;
            try
            {
                if (getGenesMethod != null)
                    genes = getGenesMethod.Invoke(kob, null);
            }
            catch (Exception ex)
            {
                geneStatus = "GetGenes failed: " + ex.Message;
                geneStatusUntil = Time.unscaledTime + 4f;
                return;
            }

            if (genes == null)
            {
                // Try public field "genes" / "Genes"
                FieldInfo gf = AccessTools.Field(kob.GetType(), "genes")
                    ?? AccessTools.Field(kob.GetType(), "Genes");
                if (gf != null)
                    genes = gf.GetValue(kob);
            }

            if (genes == null)
            {
                geneStatus = "Could not read genes object";
                geneStatusUntil = Time.unscaledTime + 4f;
                return;
            }

            for (int i = 0; i < geneFieldDefs.Length; i++)
            {
                float v = ReadGeneValue(genes, geneFieldDefs[i]);
                geneCurrent[i] = v;
                geneToSet[i] = v;
                geneToSetText[i] = v.ToString("0.##");
            }

            float thick = ReadGeneValue(genes, new GeneFieldDef("thick",
                new[] { "dickThickness", "DickThickness", "cockThickness" }, cockThickness, 0.1f, 3f));
            cockThickness = thick;

            FieldInfo spF = FindGeneField(genes, new[] { "species", "Species" });
            if (spF != null)
            {
                try
                {
                    object sv = spF.GetValue(genes);
                    if (sv is int) speciesId = (int)sv;
                    else if (sv is byte) speciesId = (byte)sv;
                    else if (sv is float) speciesId = Mathf.RoundToInt((float)sv);
                    else
                    {
                        int parsed;
                        if (sv != null && int.TryParse(sv.ToString(), out parsed))
                            speciesId = parsed;
                    }
                    speciesEditText = speciesId.ToString();
                    speciesName = speciesId == 0 ? "Kobold" : ("id " + speciesId);
                }
                catch { }
            }

            geneStatus = "Loaded from local kobold";
            geneStatusUntil = Time.unscaledTime + 3f;
        }

        private void ApplyGenesToKobold()
        {
            ResolveGeneTypes();
            Component kob = FindLocalKobold();
            if (kob == null)
            {
                geneStatus = "No local kobold found";
                geneStatusUntil = Time.unscaledTime + 4f;
                return;
            }

            object genes = null;
            try
            {
                if (getGenesMethod != null)
                    genes = getGenesMethod.Invoke(kob, null);
            }
            catch { }

            if (genes == null)
            {
                FieldInfo gf = AccessTools.Field(kob.GetType(), "genes")
                    ?? AccessTools.Field(kob.GetType(), "Genes");
                if (gf != null)
                    genes = gf.GetValue(kob);
            }

            if (genes == null)
            {
                geneStatus = "Could not get genes to write";
                geneStatusUntil = Time.unscaledTime + 4f;
                return;
            }

            // If genes is a struct, box-modify-unbox via SetGenes
            bool isValueType = genes.GetType().IsValueType;
            object work = genes;

            for (int i = 0; i < geneFieldDefs.Length; i++)
            {
                float parsed = geneToSet[i];
                if (geneToSetText[i] != null)
                {
                    float t;
                    if (float.TryParse(geneToSetText[i], out t))
                        parsed = t;
                }
                parsed = Mathf.Clamp(parsed, geneFieldDefs[i].Min, geneFieldDefs[i].Max);
                geneToSet[i] = parsed;
                WriteGeneValue(work, geneFieldDefs[i], parsed);
            }

            try
            {
                if (setGenesMethod != null)
                {
                    setGenesMethod.Invoke(kob, new object[] { work });
                }
                else
                {
                    FieldInfo gf = AccessTools.Field(kob.GetType(), "genes")
                        ?? AccessTools.Field(kob.GetType(), "Genes");
                    if (gf != null)
                        gf.SetValue(kob, work);
                }
            }
            catch (Exception ex)
            {
                geneStatus = "SetGenes failed: " + ex.Message;
                geneStatusUntil = Time.unscaledTime + 5f;
                Logger.LogWarning("ApplyGenes: " + ex);
                return;
            }

            // Also push thickness + species if fields exist
            try
            {
                FieldInfo thickF = FindGeneField(work, new[] { "dickThickness", "DickThickness", "cockThickness" });
                if (thickF != null)
                    WriteGeneValue(work, new GeneFieldDef("thick", new[] { thickF.Name }, cockThickness, 0.1f, 3f), cockThickness);

                FieldInfo spF = FindGeneField(work, new[] { "species", "Species" });
                if (spF != null)
                {
                    if (spF.FieldType == typeof(int) || spF.FieldType == typeof(byte))
                        spF.SetValue(work, Convert.ChangeType(speciesId, spF.FieldType));
                    else if (spF.FieldType == typeof(float))
                        spF.SetValue(work, (float)speciesId);
                }

                if (setGenesMethod != null)
                    setGenesMethod.Invoke(kob, new object[] { work });
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Apply species/thick: " + ex.Message);
            }

            // Re-read to confirm
            RefreshGenesFromKobold();
            geneStatus = "Applied to local kobold";
            geneStatusUntil = Time.unscaledTime + 3f;
        }

        /// <summary>
        /// Clone KoboldGenes so SetGenes sees a real change.
        /// Mutating the live GetGenes() object in place makes dickEquip != GetGenes().dickEquip always false.
        /// </summary>
        private object CloneGenes(object genes)
        {
            if (genes == null)
                return null;
            try
            {
                MethodInfo mc = AccessTools.Method(typeof(object), "MemberwiseClone");
                if (mc != null)
                    return mc.Invoke(genes, null);
            }
            catch { }

            try
            {
                object copy = Activator.CreateInstance(genes.GetType());
                FieldInfo[] fields = genes.GetType().GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (fields[i].IsStatic) continue;
                    fields[i].SetValue(copy, fields[i].GetValue(genes));
                }
                return copy;
            }
            catch
            {
                return genes;
            }
        }

        private void RefreshDickOptions()
        {
            dickOptions.Clear();
            // CommandDick.unEquipID = 0; real dicks are 1..Count (database[id-1])
            dickOptions.Add("None|id:0");
            try
            {
                object penisDb = GetGamePenisDatabase();
                List<object> infos = GetValidPrefabInfos(penisDb);
                for (int i = 0; i < infos.Count; i++)
                {
                    string key = GetPrefabInfoKey(infos[i]);
                    if (string.IsNullOrEmpty(key)) key = "Dick_" + i;
                    dickOptions.Add(key + "|id:" + (i + 1));
                }

                if (dickOptions.Count <= 1)
                    geneStatus = "Penis DB empty — only None.";
                else
                    geneStatus = "Penis DB: " + (dickOptions.Count - 1) + " (1-based ids)";
                geneStatusUntil = Time.unscaledTime + 3f;
            }
            catch (Exception ex)
            {
                geneStatus = "Dick refresh error: " + ex.Message;
                geneStatusUntil = Time.unscaledTime + 5f;
                Logger.LogWarning("RefreshDickOptions: " + ex);
            }

            if (selectedDickIndex >= dickOptions.Count)
                selectedDickIndex = 0;
        }

        private void TryEquipSelectedDick()
        {
            Component kob = FindLocalKobold();
            if (kob == null)
            {
                geneStatus = "No local kobold — can't equip";
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }

            if (selectedDickIndex < 0 || selectedDickIndex >= dickOptions.Count)
            {
                geneStatus = "Select a dick entry first";
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }

            string entry = dickOptions[selectedDickIndex];
            short dickId = 0; // unEquipID
            int idMark = entry.LastIndexOf("|id:", StringComparison.Ordinal);
            if (idMark >= 0)
            {
                short parsed;
                if (short.TryParse(entry.Substring(idMark + 4), out parsed))
                    dickId = parsed;
            }

            try
            {
                // Same path as /dick cheat: RPC SetDickRPC so all clients swap the mesh
                PhotonView pv = kob.GetComponent<PhotonView>();
                if (pv == null)
                    pv = kob.GetComponentInParent<PhotonView>();

                if (pv != null && pv.IsMine)
                {
                    pv.RPC("SetDickRPC", RpcTarget.All, dickId);
                }
                else
                {
                    ResolveGeneTypes();
                    object genes = getGenesMethod != null ? getGenesMethod.Invoke(kob, null) : null;
                    if (genes == null)
                    {
                        geneStatus = "GetGenes null";
                        return;
                    }
                    object work = CloneGenes(genes);
                    FieldInfo dickField = AccessTools.Field(work.GetType(), "dickEquip");
                    if (dickField != null)
                        dickField.SetValue(work, dickId);
                    FieldInfo thickField = AccessTools.Field(work.GetType(), "dickThickness");
                    if (thickField != null)
                        thickField.SetValue(work, cockThickness);
                    if (setGenesMethod != null)
                        setGenesMethod.Invoke(kob, new object[] { work });
                }

                // Apply thickness after RPC (SetDickRPC only sets dickEquip)
                {
                    ResolveGeneTypes();
                    object genes = getGenesMethod != null ? getGenesMethod.Invoke(kob, null) : null;
                    if (genes != null && setGenesMethod != null)
                    {
                        object work = CloneGenes(genes);
                        FieldInfo thickField = AccessTools.Field(work.GetType(), "dickThickness");
                        if (thickField != null)
                        {
                            thickField.SetValue(work, cockThickness);
                            setGenesMethod.Invoke(kob, new object[] { work });
                        }
                    }
                }

                geneStatus = "Equipped dick id=" + dickId + " (" + entry + ")";
                geneStatusUntil = Time.unscaledTime + 4f;
                RefreshEquipmentList();
            }
            catch (Exception ex)
            {
                geneStatus = "Equip failed: " + ex.Message;
                geneStatusUntil = Time.unscaledTime + 5f;
                Logger.LogWarning("TryEquipSelectedDick: " + ex);
            }
        }

        private Component GetLocalInventory()
        {
            Component kob = FindLocalKobold();
            if (kob == null) return null;
            Type invType = SafeGameType("KoboldInventory");
            if (invType == null) return null;
            return kob.GetComponent(invType) ?? kob.GetComponentInChildren(invType, true);
        }

        private List<string> GetFilteredEquipCatalog()
        {
            if (string.IsNullOrEmpty(equipFilter))
                return new List<string>(equipCatalog);
            string f = equipFilter.ToLowerInvariant();
            List<string> result = new List<string>();
            for (int i = 0; i < equipCatalog.Count; i++)
            {
                if (equipCatalog[i] != null && equipCatalog[i].ToLowerInvariant().Contains(f))
                    result.Add(equipCatalog[i]);
            }
            return result;
        }

        private void RefreshEquipmentCatalog()
        {
            equipCatalog.Clear();
            try
            {
                // EquipmentDatabase.GetAssetKeys() / GetAssets()
                Type dbType = SafeGameType("EquipmentDatabase");
                if (dbType == null)
                {
                    equipStatus = "EquipmentDatabase type missing";
                    return;
                }

                MethodInfo getKeys = AccessTools.Method(dbType, "GetAssetKeys");
                if (getKeys != null && getKeys.IsStatic)
                {
                    object keysObj = getKeys.Invoke(null, null);
                    IEnumerable keys = keysObj as IEnumerable;
                    if (keys != null)
                    {
                        foreach (object k in keys)
                        {
                            if (k == null) continue;
                            string name = k.ToString();
                            if (!string.IsNullOrEmpty(name))
                                equipCatalog.Add(name);
                        }
                    }
                }

                if (equipCatalog.Count == 0)
                {
                    MethodInfo getAssets = AccessTools.Method(dbType, "GetAssets");
                    if (getAssets != null && getAssets.IsStatic)
                    {
                        object assetsObj = getAssets.Invoke(null, null);
                        IEnumerable assets = assetsObj as IEnumerable;
                        if (assets != null)
                        {
                            foreach (object a in assets)
                            {
                                if (a == null) continue;
                                // ScriptableObject.name
                                string name = null;
                                try
                                {
                                    PropertyInfo np = a.GetType().GetProperty("name");
                                    if (np != null)
                                    {
                                        object v = np.GetValue(a, null);
                                        if (v != null) name = v.ToString();
                                    }
                                }
                                catch { }
                                if (string.IsNullOrEmpty(name))
                                    name = a.ToString();
                                if (!string.IsNullOrEmpty(name))
                                    equipCatalog.Add(name);
                            }
                        }
                    }
                }

                equipCatalog.Sort(StringComparer.OrdinalIgnoreCase);
                equipStatus = equipCatalog.Count > 0
                    ? ("Catalog: " + equipCatalog.Count + " items")
                    : "Equipment catalog empty";
                if (selectedCatalogEquip >= equipCatalog.Count)
                    selectedCatalogEquip = -1;
            }
            catch (Exception ex)
            {
                equipStatus = "Catalog error: " + ex.Message;
                Logger.LogWarning("RefreshEquipmentCatalog: " + ex);
            }
        }

        private void RefreshEquipmentList()
        {
            equipNames.Clear();
            Component inv = GetLocalInventory();
            if (inv == null)
            {
                equipStatus = "No KoboldInventory on local kobold";
                return;
            }

            try
            {
                MethodInfo getAll = AccessTools.Method(inv.GetType(), "GetAllEquipment");
                if (getAll != null)
                {
                    object result = getAll.Invoke(inv, null);
                    IEnumerable list = result as IEnumerable;
                    if (list != null)
                    {
                        foreach (object item in list)
                        {
                            if (item == null) continue;
                            string name = null;
                            try
                            {
                                PropertyInfo np = item.GetType().GetProperty("name");
                                if (np != null)
                                {
                                    object v = np.GetValue(item, null);
                                    if (v != null) name = v.ToString();
                                }
                            }
                            catch { }
                            if (string.IsNullOrEmpty(name))
                                name = item.ToString();
                            equipNames.Add(name);
                        }
                    }
                }

                if (selectedWornEquip >= equipNames.Count)
                    selectedWornEquip = -1;
            }
            catch (Exception ex)
            {
                equipStatus = "Worn list error: " + ex.Message;
                Logger.LogWarning("RefreshEquipmentList: " + ex);
            }
        }

        private void SetEquipStatus(string msg, float seconds = 4f)
        {
            equipStatus = msg ?? "";
            equipStatusUntil = Time.unscaledTime + Mathf.Max(0.5f, seconds);
        }

        private void TryWearSelectedEquipment()
        {
            if (selectedCatalogEquip < 0 || selectedCatalogEquip >= equipCatalog.Count)
            {
                SetEquipStatus("Select an item from All list first");
                return;
            }

            string equipName = equipCatalog[selectedCatalogEquip];
            if (!TryWearEquipmentByName(equipName))
            {
                // TryWearEquipmentByName already set status on failure paths it owns;
                // if it returned false without a message, leave a generic one.
                if (string.IsNullOrEmpty(equipStatus))
                    SetEquipStatus("Wear failed for: " + equipName, 5f);
            }
        }

        /// <summary>
        /// Official path: KoboldInventory.PickupEquipmentRPC(short equipmentID, int groundPrefabViewId)
        /// with groundPrefabViewId = 0 (no world prop). Falls back to local PickupEquipment(equip, null).
        /// </summary>
        private bool TryWearEquipmentCore(string equipName)
        {
            if (string.IsNullOrEmpty(equipName))
                return false;

            Component inv = GetLocalInventory();
            if (inv == null)
            {
                SetEquipStatus("No KoboldInventory");
                return false;
            }

            Type dbType = SafeGameType("EquipmentDatabase");
            if (dbType == null)
            {
                SetEquipStatus("EquipmentDatabase type missing");
                return false;
            }

            // Resolve Equipment asset by name (Database key == ScriptableObject.name)
            object equipAsset = null;
            MethodInfo[] methods = dbType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            MethodInfo tryGetByName = null;
            MethodInfo getIdMethod = null;
            for (int i = 0; i < methods.Length; i++)
            {
                ParameterInfo[] ps = methods[i].GetParameters();
                if (methods[i].Name == "TryGetAsset" && ps.Length == 2 && ps[0].ParameterType == typeof(string))
                    tryGetByName = methods[i];
                if (methods[i].Name == "GetID" && ps.Length == 1)
                    getIdMethod = methods[i];
            }

            if (tryGetByName != null)
            {
                object[] args = new object[] { equipName, null };
                bool ok = (bool)tryGetByName.Invoke(null, args);
                if (ok)
                    equipAsset = args[1];
            }

            // Case-insensitive fallback scan of GetAssets()
            if (equipAsset == null)
            {
                MethodInfo getAssets = AccessTools.Method(dbType, "GetAssets", Type.EmptyTypes);
                if (getAssets != null)
                {
                    object listObj = getAssets.Invoke(null, null);
                    IList list = listObj as IList;
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            object item = list[i];
                            if (item == null) continue;
                            string n = null;
                            try
                            {
                                // UnityEngine.Object.name
                                PropertyInfo np = item.GetType().GetProperty("name");
                                if (np != null)
                                {
                                    object v = np.GetValue(item, null);
                                    if (v != null) n = v.ToString();
                                }
                            }
                            catch { }
                            if (n != null && string.Equals(n, equipName, StringComparison.OrdinalIgnoreCase))
                            {
                                equipAsset = item;
                                break;
                            }
                        }
                    }
                }
            }

            if (equipAsset == null)
            {
                SetEquipStatus("Not in EquipmentDatabase: " + equipName);
                return false;
            }

            // Prefer network RPC so other clients see the equip (and matches game pickup path)
            short equipId = 0;
            bool haveId = false;
            if (getIdMethod != null)
            {
                try
                {
                    object idObj = getIdMethod.Invoke(null, new object[] { equipAsset });
                    if (idObj is short)
                    {
                        equipId = (short)idObj;
                        haveId = true;
                    }
                    else if (idObj is int)
                    {
                        equipId = (short)(int)idObj;
                        haveId = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("GetID failed: " + ex.Message);
                }
            }

            PhotonView invPv = inv.GetComponent<PhotonView>();
            if (invPv == null)
                invPv = inv.GetComponentInParent<PhotonView>();

            Exception lastEx = null;

            // Path A: official RPC — groundPrefabID 0 → no world object
            if (haveId && invPv != null && invPv.ViewID > 0)
            {
                try
                {
                    invPv.RPC("PickupEquipmentRPC", RpcTarget.AllBuffered, equipId, 0);
                    SetEquipStatus("Worn: " + equipName + " (rpc id=" + equipId + ")");
                    RefreshEquipmentList();
                    return true;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Logger.LogWarning("PickupEquipmentRPC failed, trying local: " + ex);
                }
            }

            // Path B: local PickupEquipment(equip, null) — same as ReplaceEquipmentWith
            try
            {
                MethodInfo pickup = AccessTools.Method(inv.GetType(), "PickupEquipment");
                if (pickup == null)
                {
                    SetEquipStatus("PickupEquipment missing");
                    return false;
                }

                // Some equipment OnEquip paths crash when groundPrefab is null AND
                // wearable internal refs are broken — still the official offline path.
                pickup.Invoke(inv, new object[] { equipAsset, null });
                SetEquipStatus("Worn: " + equipName + " (local)");
                RefreshEquipmentList();
                return true;
            }
            catch (Exception ex)
            {
                // Unwrap TargetInvocationException for the real message
                Exception root = ex;
                while (root.InnerException != null)
                    root = root.InnerException;

                string detail = root.Message;
                if (root is ArgumentNullException ane && !string.IsNullOrEmpty(ane.ParamName))
                    detail = "null arg '" + ane.ParamName + "' (often missing attach point / prefab on this body)";

                SetEquipStatus("WEAR failed: " + detail, 8f);
                Logger.LogWarning("TryWearEquipmentCore local failed for " + equipName + ": " + ex);
                if (lastEx != null)
                    Logger.LogWarning("  prior RPC error: " + lastEx);
                return false;
            }
        }

        private void TryRemoveSelectedEquipment()
        {
            if (selectedWornEquip < 0 || selectedWornEquip >= equipNames.Count)
            {
                SetEquipStatus("Select a worn item first");
                return;
            }

            string equipName = equipNames[selectedWornEquip];
            Component inv = GetLocalInventory();
            if (inv == null)
            {
                SetEquipStatus("No KoboldInventory");
                return;
            }

            try
            {
                MethodInfo getAll = AccessTools.Method(inv.GetType(), "GetAllEquipment");
                object listObj = getAll != null ? getAll.Invoke(inv, null) : null;
                IList list = listObj as IList;
                object target = null;
                if (list != null && selectedWornEquip < list.Count)
                    target = list[selectedWornEquip];

                if (target == null)
                {
                    // Match by name
                    IEnumerable en = listObj as IEnumerable;
                    if (en != null)
                    {
                        foreach (object item in en)
                        {
                            if (item == null) continue;
                            string n = null;
                            try
                            {
                                PropertyInfo np = item.GetType().GetProperty("name");
                                if (np != null)
                                {
                                    object v = np.GetValue(item, null);
                                    if (v != null) n = v.ToString();
                                }
                            }
                            catch { }
                            if (n == equipName)
                            {
                                target = item;
                                break;
                            }
                        }
                    }
                }

                if (target == null)
                {
                    SetEquipStatus("Couldn't resolve worn item");
                    return;
                }

                // RemoveEquipment(Equipment thing, bool dropOnGround)
                MethodInfo remove = null;
                MethodInfo[] methods = inv.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name != "RemoveEquipment") continue;
                    ParameterInfo[] ps = methods[i].GetParameters();
                    if (ps.Length == 2 && ps[1].ParameterType == typeof(bool))
                    {
                        // Prefer Equipment overload over EquipmentSlot
                        if (ps[0].ParameterType.Name == "Equipment" || ps[0].ParameterType.IsAssignableFrom(target.GetType()))
                        {
                            remove = methods[i];
                            break;
                        }
                        if (remove == null)
                            remove = methods[i];
                    }
                }

                if (remove == null)
                {
                    SetEquipStatus("RemoveEquipment missing");
                    return;
                }

                remove.Invoke(inv, new object[] { target, false });
                SetEquipStatus("Removed: " + equipName);
                selectedWornEquip = -1;
                RefreshEquipmentList();
            }
            catch (Exception ex)
            {
                SetEquipStatus("REMOVE failed: " + ex.Message, 6f);
                Logger.LogWarning("TryRemoveSelectedEquipment: " + ex);
            }
        }

        // ============================================================
        // FULL PRESETS (character + genes + clothing)
        // payload v1:
        //   v1|charName|speciesId|thickness|g0,g1,...|dickIndex|dickEntry|equip1,equip2,...
        // ============================================================
        private void SaveCurrentFullPreset()
        {
            if (string.IsNullOrEmpty(newPresetName))
            {
                geneStatus = "Enter a preset name first";
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }

            RefreshEquipmentList();

            string charName = "";
            if (selectedCharacterIndex >= 0 && selectedCharacterIndex < characterOptions.Count)
                charName = characterOptions[selectedCharacterIndex] ?? "";
            if (string.IsNullOrEmpty(charName) && !string.IsNullOrEmpty(speciesName))
                charName = speciesName;

            string dickEntry = "";
            if (selectedDickIndex >= 0 && selectedDickIndex < dickOptions.Count)
                dickEntry = dickOptions[selectedDickIndex] ?? "";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("v1|");
            sb.Append(SanitizePresetToken(charName)).Append('|');
            sb.Append(speciesId).Append('|');
            sb.Append(cockThickness.ToString("0.###")).Append('|');

            for (int i = 0; i < geneFieldDefs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                float v = geneToSet[i];
                if (geneToSetText[i] != null)
                {
                    float t;
                    if (float.TryParse(geneToSetText[i], out t))
                        v = t;
                }
                sb.Append(v.ToString("0.###"));
            }
            sb.Append('|');
            sb.Append(selectedDickIndex).Append('|');
            sb.Append(SanitizePresetToken(dickEntry)).Append('|');

            // worn clothes (comma-separated, tokens sanitized)
            for (int i = 0; i < equipNames.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(SanitizePresetToken(equipNames[i]));
            }

            string key = newPresetName.Trim();
            fullPresetData[key] = sb.ToString();
            if (!fullPresetNames.Contains(key))
                fullPresetNames.Add(key);
            selectedFullPreset = fullPresetNames.IndexOf(key);
            selectedStatsPreset = -1;
            selectedEquipPreset = -1;
            SaveGenePresetsToConfig();

            geneStatus = "Saved FULL preset: " + key +
                         " (char=" + (string.IsNullOrEmpty(charName) ? "?" : charName) +
                         ", genes, " + equipNames.Count + " clothes)";
            geneStatusUntil = Time.unscaledTime + 4f;
        }

        private static string SanitizePresetToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Keep payload pipe/semicolon safe
            return s.Replace("|", "/").Replace(";", ",").Replace("\n", " ").Replace("\r", "");
        }

        private void ApplySelectedFullPreset()
        {
            // Prefer [F]; if user selected legacy [S]/[E] alone, fall back
            if (selectedFullPreset >= 0 && selectedFullPreset < fullPresetNames.Count)
            {
                string key = fullPresetNames[selectedFullPreset];
                string payload;
                if (!fullPresetData.TryGetValue(key, out payload) || string.IsNullOrEmpty(payload))
                {
                    geneStatus = "Full preset data missing";
                    geneStatusUntil = Time.unscaledTime + 3f;
                    return;
                }

                if (applyFullPresetCoroutine != null)
                {
                    StopCoroutine(applyFullPresetCoroutine);
                    applyFullPresetCoroutine = null;
                }
                applyFullPresetCoroutine = StartCoroutine(ApplyFullPresetRoutine(key, payload));
                return;
            }

            if (selectedStatsPreset >= 0)
            {
                LoadSelectedStatsPreset();
                ApplyGenesToKobold();
                return;
            }

            if (selectedEquipPreset >= 0)
            {
                LoadSelectedEquipPreset();
                return;
            }

            geneStatus = "Select a [F] preset first";
            geneStatusUntil = Time.unscaledTime + 3f;
        }

        private IEnumerator ApplyFullPresetRoutine(string key, string payload)
        {
            geneStatus = "Applying full preset: " + key + "...";
            geneStatusUntil = Time.unscaledTime + 8f;

            string[] parts = payload.Split('|');
            // v1|char|species|thick|genes|dickIdx|dickEntry|equips
            if (parts.Length < 5 || parts[0] != "v1")
            {
                geneStatus = "Bad full preset format (need v1)";
                geneStatusUntil = Time.unscaledTime + 4f;
                applyFullPresetCoroutine = null;
                yield break;
            }

            string charName = parts.Length > 1 ? parts[1] : "";
            int sid = speciesId;
            if (parts.Length > 2) int.TryParse(parts[2], out sid);
            float thick = cockThickness;
            if (parts.Length > 3) float.TryParse(parts[3], out thick);
            string genesCsv = parts.Length > 4 ? parts[4] : "";
            int dickIdx = selectedDickIndex;
            if (parts.Length > 5) int.TryParse(parts[5], out dickIdx);
            string dickEntry = parts.Length > 6 ? parts[6] : "";
            string equipsCsv = parts.Length > 7 ? parts[7] : "";

            // --- 1) Character ---
            if (characterOptions.Count == 0)
                RefreshCharacterList();

            int charIdx = FindCharacterIndexForPreset(charName, key, sid);

            if (charIdx >= 0)
            {
                selectedCharacterIndex = charIdx;
                speciesId = charIdx;
                speciesEditText = speciesId.ToString();
                speciesName = characterOptions[charIdx];
                geneStatus = "Applying full preset: " + key + " → char " + speciesName + "...";
                TryApplySelectedCharacter();
                // Wait for possible respawn / gene write
                yield return new WaitForSecondsRealtime(0.75f);
            }
            else if (!string.IsNullOrEmpty(charName))
            {
                Logger.LogWarning("Full preset '" + key + "': no character match for '" + charName +
                                  "' (" + characterOptions.Count + " options loaded)");
            }
            else
            {
                speciesId = sid;
                speciesEditText = sid.ToString();
            }

            // --- 2) Genes + thickness ---
            cockThickness = thick;
            string[] gens = genesCsv.Split(',');
            for (int i = 0; i < geneFieldDefs.Length && i < gens.Length; i++)
            {
                float v;
                if (float.TryParse(gens[i], out v))
                {
                    geneToSet[i] = Mathf.Clamp(v, geneFieldDefs[i].Min, geneFieldDefs[i].Max);
                    geneToSetText[i] = geneToSet[i].ToString("0.##");
                }
            }
            // keep thickness gene in sync if present
            for (int i = 0; i < geneFieldDefs.Length; i++)
            {
                if (geneFieldDefs[i].FieldNames != null &&
                    geneFieldDefs[i].FieldNames.Length > 0 &&
                    geneFieldDefs[i].FieldNames[0] == "dickThickness")
                {
                    geneToSet[i] = thick;
                    geneToSetText[i] = thick.ToString("0.##");
                }
            }
            ApplyGenesToKobold();
            yield return new WaitForSecondsRealtime(0.25f);

            // --- 3) Dick ---
            if (dickOptions.Count == 0)
                RefreshDickOptions();

            if (!string.IsNullOrEmpty(dickEntry))
            {
                int found = -1;
                for (int i = 0; i < dickOptions.Count; i++)
                {
                    if (dickOptions[i] == dickEntry ||
                        (dickOptions[i] != null && dickOptions[i].IndexOf(dickEntry, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        found = i;
                        break;
                    }
                }
                if (found >= 0) dickIdx = found;
            }
            if (dickOptions.Count > 0)
            {
                selectedDickIndex = Mathf.Clamp(dickIdx, 0, dickOptions.Count - 1);
                TryEquipSelectedDick();
                yield return new WaitForSecondsRealtime(0.2f);
            }

            // --- 4) Clothing ---
            if (!string.IsNullOrEmpty(equipsCsv))
            {
                if (equipCatalog.Count == 0)
                    RefreshEquipmentCatalog();

                string[] wanted = equipsCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                int wornOk = 0;
                for (int i = 0; i < wanted.Length; i++)
                {
                    string name = wanted[i].Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (TryWearEquipmentByName(name))
                        wornOk++;
                    yield return new WaitForSecondsRealtime(0.05f);
                }
                RefreshEquipmentList();
                geneStatus = "Applied FULL preset: " + key +
                             " — char + genes + dick + " + wornOk + "/" + wanted.Length + " clothes";
            }
            else
            {
                geneStatus = "Applied FULL preset: " + key + " — char + genes + dick (no clothes stored)";
            }
            geneStatusUntil = Time.unscaledTime + 6f;
            applyFullPresetCoroutine = null;
        }

        private bool TryWearEquipmentByName(string equipName)
        {
            if (string.IsNullOrEmpty(equipName)) return false;

            // Resolve to catalog name when possible (exact / contains)
            string resolved = equipName;
            for (int i = 0; i < equipCatalog.Count; i++)
            {
                if (string.Equals(equipCatalog[i], equipName, StringComparison.OrdinalIgnoreCase))
                {
                    resolved = equipCatalog[i];
                    break;
                }
            }
            if (resolved == equipName)
            {
                for (int i = 0; i < equipCatalog.Count; i++)
                {
                    if (equipCatalog[i] != null &&
                        equipCatalog[i].IndexOf(equipName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        resolved = equipCatalog[i];
                        break;
                    }
                }
            }

            return TryWearEquipmentCore(resolved);
        }

        private void DeleteSelectedPreset()
        {
            if (selectedFullPreset >= 0 && selectedFullPreset < fullPresetNames.Count)
            {
                string key = fullPresetNames[selectedFullPreset];
                fullPresetNames.RemoveAt(selectedFullPreset);
                fullPresetData.Remove(key);
                selectedFullPreset = -1;
                SaveGenePresetsToConfig();
                geneStatus = "Deleted full preset: " + key;
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }
            if (selectedStatsPreset >= 0 && selectedStatsPreset < statsPresetNames.Count)
            {
                string key = statsPresetNames[selectedStatsPreset];
                statsPresetNames.RemoveAt(selectedStatsPreset);
                statsPresetData.Remove(key);
                selectedStatsPreset = -1;
                SaveGenePresetsToConfig();
                geneStatus = "Deleted stats preset: " + key;
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }
            if (selectedEquipPreset >= 0 && selectedEquipPreset < equipPresetNames.Count)
            {
                string key = equipPresetNames[selectedEquipPreset];
                equipPresetNames.RemoveAt(selectedEquipPreset);
                equipPresetData.Remove(key);
                selectedEquipPreset = -1;
                SaveGenePresetsToConfig();
                geneStatus = "Deleted equip preset: " + key;
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }
            geneStatus = "Select a preset to delete";
            geneStatusUntil = Time.unscaledTime + 3f;
        }

        private void SaveCurrentStatsPreset()
        {
            if (string.IsNullOrEmpty(newPresetName))
            {
                geneStatus = "Enter a preset name first";
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }

            // payload: v0|species|thick|gene0,gene1,...
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("v0|").Append(speciesId).Append('|').Append(cockThickness.ToString("0.###")).Append('|');
            for (int i = 0; i < geneFieldDefs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                float v = geneToSet[i];
                if (geneToSetText[i] != null)
                {
                    float t;
                    if (float.TryParse(geneToSetText[i], out t))
                        v = t;
                }
                sb.Append(v.ToString("0.###"));
            }

            string key = newPresetName.Trim();
            statsPresetData[key] = sb.ToString();
            if (!statsPresetNames.Contains(key))
                statsPresetNames.Add(key);
            selectedStatsPreset = statsPresetNames.IndexOf(key);
            SaveGenePresetsToConfig();
            geneStatus = "Saved stats preset: " + key;
            geneStatusUntil = Time.unscaledTime + 3f;
        }

        private void LoadSelectedStatsPreset()
        {
            if (selectedStatsPreset < 0 || selectedStatsPreset >= statsPresetNames.Count)
            {
                geneStatus = "Select a [S] preset first";
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }

            string key = statsPresetNames[selectedStatsPreset];
            string payload;
            if (!statsPresetData.TryGetValue(key, out payload) || string.IsNullOrEmpty(payload))
            {
                geneStatus = "Preset data missing";
                return;
            }

            try
            {
                string[] parts = payload.Split('|');
                if (parts.Length < 4)
                {
                    geneStatus = "Bad preset format";
                    return;
                }
                int sid;
                if (int.TryParse(parts[1], out sid))
                {
                    speciesId = sid;
                    speciesEditText = sid.ToString();
                }
                float thick;
                if (float.TryParse(parts[2], out thick))
                    cockThickness = thick;

                string[] gens = parts[3].Split(',');
                for (int i = 0; i < geneFieldDefs.Length && i < gens.Length; i++)
                {
                    float v;
                    if (float.TryParse(gens[i], out v))
                    {
                        geneToSet[i] = v;
                        geneToSetText[i] = v.ToString("0.##");
                    }
                }
                geneStatus = "Loaded stats preset: " + key + " (press APPLY)";
                geneStatusUntil = Time.unscaledTime + 4f;
            }
            catch (Exception ex)
            {
                geneStatus = "Load preset failed: " + ex.Message;
            }
        }

        private void SaveCurrentEquipPreset()
        {
            if (string.IsNullOrEmpty(newPresetName))
            {
                geneStatus = "Enter a preset name first";
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }

            RefreshEquipmentList();
            string key = newPresetName.Trim();
            string payload = selectedDickIndex + "|" + cockThickness.ToString("0.###") + "|" +
                             string.Join(",", equipNames.ToArray());
            equipPresetData[key] = payload;
            if (!equipPresetNames.Contains(key))
                equipPresetNames.Add(key);
            selectedEquipPreset = equipPresetNames.IndexOf(key);
            SaveGenePresetsToConfig();
            geneStatus = "Saved equip preset: " + key;
            geneStatusUntil = Time.unscaledTime + 3f;
        }

        private void LoadSelectedEquipPreset()
        {
            if (selectedEquipPreset < 0 || selectedEquipPreset >= equipPresetNames.Count)
            {
                geneStatus = "Select an [E] preset first";
                geneStatusUntil = Time.unscaledTime + 3f;
                return;
            }

            string key = equipPresetNames[selectedEquipPreset];
            string payload;
            if (!equipPresetData.TryGetValue(key, out payload))
            {
                geneStatus = "Equip preset missing";
                return;
            }

            try
            {
                string[] parts = payload.Split('|');
                int di;
                if (parts.Length > 0 && int.TryParse(parts[0], out di))
                    selectedDickIndex = Mathf.Clamp(di, 0, Mathf.Max(0, dickOptions.Count - 1));
                float thick;
                if (parts.Length > 1 && float.TryParse(parts[1], out thick))
                    cockThickness = thick;
                TryEquipSelectedDick();
                geneStatus = "Loaded equip preset: " + key;
                geneStatusUntil = Time.unscaledTime + 3f;
            }
            catch (Exception ex)
            {
                geneStatus = "Load equip failed: " + ex.Message;
            }
        }

        private void LoadGenePresetsFromConfig()
        {
            statsPresetNames.Clear();
            statsPresetData.Clear();
            equipPresetNames.Clear();
            equipPresetData.Clear();
            fullPresetNames.Clear();
            fullPresetData.Clear();

            ParsePresetConfig(configStatsPresets != null ? configStatsPresets.Value : "", statsPresetNames, statsPresetData);
            ParsePresetConfig(configEquipPresets != null ? configEquipPresets.Value : "", equipPresetNames, equipPresetData);
            ParsePresetConfig(configFullPresets != null ? configFullPresets.Value : "", fullPresetNames, fullPresetData);
        }

        private void ParsePresetConfig(string raw, List<string> names, Dictionary<string, string> data)
        {
            if (string.IsNullOrEmpty(raw))
                return;
            string[] entries = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                int eq = entries[i].IndexOf('=');
                if (eq <= 0) continue;
                string name = entries[i].Substring(0, eq).Trim();
                string payload = entries[i].Substring(eq + 1);
                if (name.Length == 0) continue;
                data[name] = payload;
                if (!names.Contains(name))
                    names.Add(name);
            }
        }

        private void SaveGenePresetsToConfig()
        {
            if (configStatsPresets != null)
                configStatsPresets.Value = JoinPresetConfig(statsPresetNames, statsPresetData);
            if (configEquipPresets != null)
                configEquipPresets.Value = JoinPresetConfig(equipPresetNames, equipPresetData);
            if (configFullPresets != null)
                configFullPresets.Value = JoinPresetConfig(fullPresetNames, fullPresetData);
        }

        // ============================================================
        // CHARCON IMPORT / EXPORT / CLONE NEARBY
        // ============================================================

        /// <summary>
        /// BepInEx config filename for the old Character Control cheat.
        /// Looked up next to this plugin's config on startup and on manual import.
        /// </summary>
        private const string CharConConfigFileName = "Komar.koboldkare.CharConCheat.cfg";

        /// <summary>
        /// On launch: if CharCon cfg exists, merge any presets we don't already have.
        /// Safe for new users — their CharCon outfits show up in this menu automatically.
        /// </summary>
        private void TryAutoImportCharConConfig()
        {
            try
            {
                string path = FindCharConConfigPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    Logger.LogInfo("CharCon auto-import: no " + CharConConfigFileName + " found (ok if CharCon isn't installed)");
                    return;
                }

                string equipments, stats;
                if (!TryReadCharConPresetStrings(path, out equipments, out stats))
                {
                    Logger.LogWarning("CharCon auto-import: found cfg but Equipments/Stats were empty");
                    return;
                }

                int before = fullPresetNames.Count + statsPresetNames.Count + equipPresetNames.Count;
                int n = ImportCharConPresets(equipments, stats, onlyAddMissing: true);
                int after = fullPresetNames.Count + statsPresetNames.Count + equipPresetNames.Count;
                Logger.LogInfo("CharCon auto-import from " + path + ": +" + n + " new (total presets " + before + " → " + after + ")");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("CharCon auto-import failed: " + ex.Message);
            }
        }

        private string FindCharConConfigPath()
        {
            // 1) Same folder as this plugin's config (normal BepInEx/config/)
            try
            {
                string dir = Path.GetDirectoryName(Config.ConfigFilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, CharConConfigFileName);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }

            // 2) Walk up from plugin path looking for BepInEx/config/
            try
            {
                string start = Path.GetDirectoryName(Info.Location);
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(start); i++)
                {
                    string candidate = Path.Combine(start, "config", CharConConfigFileName);
                    if (File.Exists(candidate)) return candidate;
                    candidate = Path.Combine(start, "BepInEx", "config", CharConConfigFileName);
                    if (File.Exists(candidate)) return candidate;
                    start = Path.GetDirectoryName(start);
                }
            }
            catch { }

            return null;
        }

        private bool TryReadCharConPresetStrings(string path, out string equipments, out string stats)
        {
            equipments = null;
            stats = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line) || line[0] == '#' || line[0] == '[')
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (key.Equals("Equipments", StringComparison.OrdinalIgnoreCase))
                    equipments = val;
                else if (key.Equals("Stats", StringComparison.OrdinalIgnoreCase))
                    stats = val;
            }

            return !string.IsNullOrEmpty(equipments) || !string.IsNullOrEmpty(stats);
        }

        /// <summary>
        /// Manual button: re-read CharCon cfg from disk (overwrites same names),
        /// or fall back to the built-in sample if the file is missing.
        /// </summary>
        private void ImportDefaultCharConPresets()
        {
            string path = FindCharConConfigPath();
            string equipments = null;
            string stats = null;

            if (!string.IsNullOrEmpty(path) && TryReadCharConPresetStrings(path, out equipments, out stats))
            {
                int n = ImportCharConPresets(equipments, stats, onlyAddMissing: false);
                geneStatus = "Imported " + n + " from CharCon cfg";
                geneStatusUntil = Time.unscaledTime + 5f;
                Logger.LogInfo("CharCon manual import from " + path + ": " + n);
                return;
            }

            // Fallback sample (your shared presets) if cfg isn't on disk
            equipments =
                "Flint FlintHair FlintBelt FlintBoots FlintEarringL FlintFishnetTop FlintPants FlintTop " +
                "$Zex_Kobo TopHat Tailbag SpikeyBracelets SpikeyBracelets SpikedCollar SpikedCollar NippleBarbells HighHeels Tailbag Tailbag " +
                "$Flint_Stipper FlintHair FlintBelt FlintBoots FlintEarringL FlintFishnetTop FlintFishnetLegL FlintFishnetLegR FlintThong FlintCollar FlintBarbells " +
                "$Krox_Stipper KroxBellyPiercing KroxBellySize25 KroxBreastShape25 KroxButt120 KroxFishnetArmStockings KroxFishnetLegStockings KroxNippleBarbellsGold KroxNosePiercing KroxSequinBraLift KroxSequinPantiesNSFW KroxVagPiercing " +
                "$Gemma GTop GJacket GEdgyThighs GEdgyPanties GEdgyBracelets MamaGGlasses SpikedCollar TagPuppyslut";

            stats =
                "Zex_Kobo 1 10 70 70 1 26 37 0 5 0.5 7.5 None 255 110 0 0 " +
                "Flint 1 5 20 20 1 24 30 0 10 0.7001953 10 None 0 108 0 255 " +
                "Flint_Stripper 1 10 70 70 1 26 20 0 5 0.7001953 7.5 None 255 110 0 0 " +
                "Krox_Stipper 1 5 20 20 1 24 30 0 10 0.7001953 10 None 0 108 0 255 " +
                "Gemma 1 10 70 70 1 26 15 0 5 0.7 7.5 None 175 100 120 0";

            int n2 = ImportCharConPresets(equipments, stats, onlyAddMissing: false);
            geneStatus = "No CharCon cfg found — imported " + n2 + " built-in sample(s)";
            geneStatusUntil = Time.unscaledTime + 5f;
        }

        /// <summary>
        /// Parse KKCharCon Equipments + Stats strings into this mod's presets.
        /// Equipments: "$Name item item $Name2 item..."
        /// Stats: "Name Energy MaxEn Belly Meta Grab Size Boobs Fat DickSize Thick Balls Dick Hue Bright Sat ClothHue" repeated.
        /// </summary>
        private int ImportCharConPresets(string equipmentsRaw, string statsRaw, bool onlyAddMissing = false)
        {
            Dictionary<string, List<string>> equipMap = ParseCharConEquipments(equipmentsRaw);
            Dictionary<string, CharConStats> statsMap = ParseCharConStats(statsRaw);

            int added = 0;
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Merge matching names into full presets
            foreach (var kv in statsMap)
            {
                string name = kv.Key;
                CharConStats st = kv.Value;

                if (onlyAddMissing && fullPresetData.ContainsKey(name))
                {
                    used.Add(name);
                    // still mark matching equip block as used so it isn't dual-added
                    if (equipMap.ContainsKey(name)) used.Add(name);
                    continue;
                }

                List<string> clothes;
                equipMap.TryGetValue(name, out clothes);
                // Also try loose match (Flint_Stripper vs Flint_Stipper typo)
                if (clothes == null)
                {
                    foreach (var ek in equipMap.Keys)
                    {
                        if (string.Equals(ek, name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ek.Replace("Stipper", "Stripper"), name.Replace("Stipper", "Stripper"), StringComparison.OrdinalIgnoreCase))
                        {
                            clothes = equipMap[ek];
                            used.Add(ek);
                            break;
                        }
                    }
                }
                else
                {
                    used.Add(name);
                }

                string payload = BuildFullPresetPayloadFromCharCon(name, st, clothes);
                fullPresetData[name] = payload;
                if (!fullPresetNames.Contains(name))
                    fullPresetNames.Add(name);
                used.Add(name);
                added++;
            }

            // Equip-only leftovers (same shape as SaveCurrentEquipPreset: dickIdx|thick|clothes)
            foreach (var kv in equipMap)
            {
                if (used.Contains(kv.Key)) continue;
                string key = kv.Key;
                if (onlyAddMissing && (equipPresetData.ContainsKey(key) || fullPresetData.ContainsKey(key)))
                    continue;

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("-1|0.5|");
                List<string> items = kv.Value;
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(SanitizePresetToken(items[i]));
                }
                equipPresetData[key] = sb.ToString();
                if (!equipPresetNames.Contains(key))
                    equipPresetNames.Add(key);
                added++;
            }

            if (added > 0)
                SaveGenePresetsToConfig();
            Logger.LogInfo("CharCon import: " + added + " presets (" + fullPresetNames.Count + " full, " +
                           statsPresetNames.Count + " stats, " + equipPresetNames.Count + " equip)" +
                           (onlyAddMissing ? " [missing-only]" : " [overwrite]"));
            return added;
        }

        private struct CharConStats
        {
            public float maxEnergy, belly, meta, grab, size, tits, fat, dickSize, thick, balls;
            public float hue, bright, satur, clothHue;
            public string dickName;
        }

        private static float ParseCharConFloat(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            s = s.Trim().Replace(',', '.');
            float v;
            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
            if (float.TryParse(s, out v))
                return v;
            return 0f;
        }

        private Dictionary<string, List<string>> ParseCharConEquipments(string raw)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return map;

            string[] blocks = raw.Split(new[] { '$' }, StringSplitOptions.RemoveEmptyEntries);
            for (int b = 0; b < blocks.Length; b++)
            {
                string[] tokens = blocks[b].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;
                string name = tokens[0].Trim();
                var items = new List<string>();
                for (int i = 1; i < tokens.Length; i++)
                    items.Add(tokens[i].Trim());
                map[name] = items;
            }
            return map;
        }

        private Dictionary<string, CharConStats> ParseCharConStats(string raw)
        {
            var map = new Dictionary<string, CharConStats>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return map;

            string[] tokens = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            // Each preset: Name + 16 fields
            const int stride = 17;
            for (int i = 0; i + stride - 1 < tokens.Length; i += stride)
            {
                string name = tokens[i];
                // indices after name:
                // 1 Energy (unused), 2 MaxEnergy, 3 Belly, 4 Meta, 5 Grab, 6 Size, 7 Boobs, 8 Fat,
                // 9 DickSize, 10 Thick, 11 Balls, 12 Dick, 13 Hue, 14 Bright, 15 Sat, 16 ClothHue
                CharConStats st = new CharConStats();
                st.maxEnergy = ParseCharConFloat(tokens[i + 2]);
                st.belly = ParseCharConFloat(tokens[i + 3]);
                st.meta = ParseCharConFloat(tokens[i + 4]);
                st.grab = ParseCharConFloat(tokens[i + 5]);
                st.size = ParseCharConFloat(tokens[i + 6]);
                st.tits = ParseCharConFloat(tokens[i + 7]);
                st.fat = ParseCharConFloat(tokens[i + 8]);
                st.dickSize = ParseCharConFloat(tokens[i + 9]);
                st.thick = ParseCharConFloat(tokens[i + 10]);
                st.balls = ParseCharConFloat(tokens[i + 11]);
                st.dickName = tokens[i + 12];
                if (string.Equals(st.dickName, "None", StringComparison.OrdinalIgnoreCase))
                    st.dickName = "";
                st.hue = ParseCharConFloat(tokens[i + 13]);
                st.bright = ParseCharConFloat(tokens[i + 14]);
                st.satur = ParseCharConFloat(tokens[i + 15]);
                st.clothHue = ParseCharConFloat(tokens[i + 16]);
                map[name] = st;
            }
            return map;
        }

        /// <summary>
        /// CharCon preset names are often outfits (Flint_Stripper, Zex_Kobo).
        /// Pull a likely character/mesh name for the full-preset char field.
        /// </summary>
        private static string GuessCharacterNameFromCharConPreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName))
                return "";

            // Known outfit → character aliases (expand as needed)
            string lower = presetName.ToLowerInvariant();
            if (lower.StartsWith("flint")) return "Flint";
            if (lower.StartsWith("gemma")) return "Gemma";
            if (lower.StartsWith("krox")) return "Krox";
            if (lower.StartsWith("zex") || lower.Contains("kobo")) return "Kobold"; // fallback base form

            // Flint_Stripper / Krox_Stipper → take token before '_'
            int us = presetName.IndexOf('_');
            if (us > 0)
                return presetName.Substring(0, us);

            return presetName;
        }

        private string BuildFullPresetPayloadFromCharCon(string name, CharConStats st, List<string> clothes)
        {
            // geneFieldDefs order: MaxEn,Belly,Meta,Grab,Size,Tits,Fat,Psize,Balls,Hue,Bright,Satur,ClthHue,Thick
            float[] genes = new float[geneFieldDefs.Length];
            for (int i = 0; i < genes.Length; i++)
                genes[i] = geneFieldDefs[i].DefaultValue;

            void SetGene(string label, float value)
            {
                for (int i = 0; i < geneFieldDefs.Length; i++)
                {
                    if (geneFieldDefs[i].Label == label)
                    {
                        genes[i] = value;
                        return;
                    }
                }
            }

            SetGene("MaxEn", st.maxEnergy);
            SetGene("Belly", st.belly);
            SetGene("Meta", st.meta);
            SetGene("Grab", st.grab);
            SetGene("Size", st.size);
            SetGene("Tits", st.tits);
            SetGene("Fat", st.fat);
            SetGene("Psize", st.dickSize);
            SetGene("Balls", st.balls);
            SetGene("Hue", st.hue);
            SetGene("Bright", st.bright);
            SetGene("Satur", st.satur);
            SetGene("Clth Hue", st.clothHue);
            SetGene("Thick", st.thick);

            // Prefer a real character name over the outfit title so APPLY can SET CHAR
            string charName = GuessCharacterNameFromCharConPreset(name);
            // If the player DB is already loaded, lock onto an exact option when possible
            charName = ResolveCharacterOptionName(charName, name);

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("v1|");
            sb.Append(SanitizePresetToken(charName)).Append('|');
            // species index if we can resolve it from the character list
            int sid = ResolveCharacterOptionIndex(charName);
            if (sid < 0) sid = 0;
            sb.Append(sid).Append('|');
            sb.Append(st.thick.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('|');
            for (int i = 0; i < genes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(genes[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.Append('|');
            sb.Append("-1|"); // dick index unknown
            sb.Append(SanitizePresetToken(st.dickName ?? "")).Append('|');
            if (clothes != null)
            {
                for (int i = 0; i < clothes.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(SanitizePresetToken(clothes[i]));
                }
            }
            return sb.ToString();
        }

        private string ResolveCharacterOptionName(string guessed, string presetName)
        {
            if (characterOptions == null || characterOptions.Count == 0)
            {
                try { RefreshCharacterList(); } catch { }
            }
            if (characterOptions == null || characterOptions.Count == 0)
                return guessed;

            // Try candidates in order
            string[] candidates = {
                guessed,
                presetName,
                GuessCharacterNameFromCharConPreset(presetName)
            };

            for (int c = 0; c < candidates.Length; c++)
            {
                string cand = candidates[c];
                if (string.IsNullOrEmpty(cand)) continue;

                for (int i = 0; i < characterOptions.Count; i++)
                {
                    if (string.Equals(characterOptions[i], cand, StringComparison.OrdinalIgnoreCase))
                        return characterOptions[i];
                }
                string lower = cand.ToLowerInvariant();
                for (int i = 0; i < characterOptions.Count; i++)
                {
                    if (characterOptions[i] == null) continue;
                    string opt = characterOptions[i];
                    string ol = opt.ToLowerInvariant();
                    if (ol.Contains(lower) || lower.Contains(ol) || ol.StartsWith(lower) || lower.StartsWith(ol))
                        return opt;
                }
            }
            return guessed;
        }

        private int ResolveCharacterOptionIndex(string charName)
        {
            if (string.IsNullOrEmpty(charName) || characterOptions == null)
                return -1;
            for (int i = 0; i < characterOptions.Count; i++)
            {
                if (string.Equals(characterOptions[i], charName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Match a full-preset character field (or outfit title) to an entry in characterOptions.
        /// </summary>
        private int FindCharacterIndexForPreset(string charName, string presetKey, int speciesHint)
        {
            if (characterOptions == null || characterOptions.Count == 0)
            {
                try { RefreshCharacterList(); } catch { }
            }
            if (characterOptions == null || characterOptions.Count == 0)
                return -1;

            List<string> tries = new List<string>();
            if (!string.IsNullOrEmpty(charName)) tries.Add(charName);
            if (!string.IsNullOrEmpty(presetKey)) tries.Add(presetKey);
            string guessed = GuessCharacterNameFromCharConPreset(
                !string.IsNullOrEmpty(charName) ? charName : presetKey);
            if (!string.IsNullOrEmpty(guessed)) tries.Add(guessed);
            // also base before underscore of each
            int n = tries.Count;
            for (int i = 0; i < n; i++)
            {
                int us = tries[i].IndexOf('_');
                if (us > 0)
                    tries.Add(tries[i].Substring(0, us));
            }

            // 1) Exact
            for (int t = 0; t < tries.Count; t++)
            {
                for (int i = 0; i < characterOptions.Count; i++)
                {
                    if (string.Equals(characterOptions[i], tries[t], StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            // 2) Starts with / contains
            for (int t = 0; t < tries.Count; t++)
            {
                string lower = tries[t].ToLowerInvariant();
                if (lower.Length < 2) continue;
                for (int i = 0; i < characterOptions.Count; i++)
                {
                    if (characterOptions[i] == null) continue;
                    string ol = characterOptions[i].ToLowerInvariant();
                    if (ol.StartsWith(lower) || lower.StartsWith(ol) || ol.Contains(lower))
                        return i;
                }
            }
            // 3) Species index hint (only if it looks like a real index, not the old "always 0")
            if (speciesHint > 0 && speciesHint < characterOptions.Count)
                return speciesHint;

            return -1;
        }

        private void ExportAllPresetsToLog()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== ZEX PRESET EXPORT ===");
            sb.AppendLine("FullPresets=" + (configFullPresets != null ? configFullPresets.Value : JoinPresetConfig(fullPresetNames, fullPresetData)));
            sb.AppendLine("StatsPresets=" + (configStatsPresets != null ? configStatsPresets.Value : JoinPresetConfig(statsPresetNames, statsPresetData)));
            sb.AppendLine("EquipPresets=" + (configEquipPresets != null ? configEquipPresets.Value : JoinPresetConfig(equipPresetNames, equipPresetData)));
            sb.AppendLine("=== END EXPORT ===");
            string text = sb.ToString();
            Logger.LogInfo(text);
            try
            {
                // Also write next to the plugin config for easy sharing
                string dir = Path.GetDirectoryName(Config.ConfigFilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    string path = Path.Combine(dir, "ZexPresets_export.txt");
                    File.WriteAllText(path, text);
                    geneStatus = "Exported to log + " + path;
                }
                else
                    geneStatus = "Exported to BepInEx log";
            }
            catch (Exception ex)
            {
                geneStatus = "Exported to log (file failed: " + ex.Message + ")";
            }
            geneStatusUntil = Time.unscaledTime + 6f;
        }

        private void CloneNearbyPlayerToPreset()
        {
            try
            {
                Component self = FindLocalKobold();
                Vector3 origin = self != null ? self.transform.position : Vector3.zero;
                if (self == null)
                {
                    Camera cam = Camera.main;
                    if (cam != null) origin = cam.transform.position;
                }

                Component best = null;
                float bestDist = float.MaxValue;
                string bestName = "Nearby";

                PhotonView[] views = UnityEngine.Object.FindObjectsOfType<PhotonView>();
                if (views != null)
                {
                    for (int i = 0; i < views.Length; i++)
                    {
                        PhotonView view = views[i];
                        if (view == null || view.IsMine) continue;
                        Component k = GetKoboldOn(view.gameObject);
                        if (k == null) continue;
                        float d = Vector3.Distance(origin, k.transform.position);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = k;
                            Player owner = view.Owner;
                            if (owner != null && !string.IsNullOrEmpty(owner.NickName))
                                bestName = owner.NickName;
                        }
                    }
                }

                if (best == null)
                {
                    geneStatus = "No nearby player kobold found";
                    geneStatusUntil = Time.unscaledTime + 3f;
                    return;
                }

                ResolveGeneTypes();
                object genes = getGenesMethod != null ? getGenesMethod.Invoke(best, null) : null;
                if (genes == null)
                {
                    geneStatus = "Nearby kobold has no genes";
                    geneStatusUntil = Time.unscaledTime + 3f;
                    return;
                }

                // Pull gene values into geneToSet
                for (int i = 0; i < geneFieldDefs.Length; i++)
                {
                    float v = ReadGeneValue(genes, geneFieldDefs[i]);
                    geneToSet[i] = v;
                    geneToSetText[i] = v.ToString("0.##");
                }

                int sid = 0;
                FieldInfo sp = AccessTools.Field(genes.GetType(), "species");
                if (sp != null && sp.GetValue(genes) != null)
                    sid = Convert.ToInt32(sp.GetValue(genes));
                speciesId = sid;
                speciesEditText = sid.ToString();

                // Equipment from their inventory if present
                List<string> clothes = new List<string>();
                try
                {
                    Component inv = null;
                    Type invType = SafeGameType("KoboldInventory");
                    if (invType != null)
                        inv = best.GetComponent(invType) ?? best.GetComponentInChildren(invType, true);
                    if (inv != null)
                    {
                        MethodInfo getAll = AccessTools.Method(inv.GetType(), "GetAllEquipment");
                        if (getAll != null)
                        {
                            object result = getAll.Invoke(inv, null);
                            IEnumerable list = result as IEnumerable;
                            if (list != null)
                            {
                                foreach (object item in list)
                                {
                                    if (item == null) continue;
                                    string name = null;
                                    try
                                    {
                                        PropertyInfo np = item.GetType().GetProperty("name");
                                        if (np != null)
                                        {
                                            object v = np.GetValue(item, null);
                                            if (v != null) name = v.ToString();
                                        }
                                    }
                                    catch { }
                                    if (string.IsNullOrEmpty(name)) name = item.ToString();
                                    if (!string.IsNullOrEmpty(name)) clothes.Add(name);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Clone clothes read: " + ex.Message);
                }

                float thick = cockThickness;
                for (int i = 0; i < geneFieldDefs.Length; i++)
                {
                    if (geneFieldDefs[i].Label == "Thick")
                    {
                        thick = geneToSet[i];
                        break;
                    }
                }

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("v1|");
                sb.Append(SanitizePresetToken(bestName)).Append('|');
                sb.Append(sid).Append('|');
                sb.Append(thick.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('|');
                for (int i = 0; i < geneFieldDefs.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(geneToSet[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
                sb.Append("|-1||");
                for (int i = 0; i < clothes.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(SanitizePresetToken(clothes[i]));
                }

                string key = "Clone_" + SanitizePresetToken(bestName);
                if (string.IsNullOrEmpty(key) || key == "Clone_")
                    key = "Clone_Near";
                // unique if exists
                string baseKey = key;
                int n = 2;
                while (fullPresetData.ContainsKey(key))
                {
                    key = baseKey + "_" + n;
                    n++;
                }

                fullPresetData[key] = sb.ToString();
                if (!fullPresetNames.Contains(key))
                    fullPresetNames.Add(key);
                selectedFullPreset = fullPresetNames.IndexOf(key);
                selectedStatsPreset = -1;
                selectedEquipPreset = -1;
                SaveGenePresetsToConfig();

                geneStatus = "Cloned " + bestName + " (" + bestDist.ToString("0.0") + "m) → preset " + key +
                             " · " + clothes.Count + " clothes. APPLY to use.";
                geneStatusUntil = Time.unscaledTime + 6f;
                Logger.LogInfo("Clone nearby: " + key + " from " + bestName + " dist=" + bestDist);
            }
            catch (Exception ex)
            {
                geneStatus = "Clone failed: " + ex.Message;
                geneStatusUntil = Time.unscaledTime + 4f;
                Logger.LogWarning("CloneNearbyPlayerToPreset: " + ex);
            }
        }

        private string JoinPresetConfig(List<string> names, Dictionary<string, string> data)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                string p;
                if (!data.TryGetValue(n, out p)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(n).Append('=').Append(p.Replace(";", ","));
            }
            return sb.ToString();
        }

        private void DrawSillysPanel(float x, float y, float width)
        {
            float colGap = 14f;
            float colW = (width - colGap) * 0.5f;

            GUI.Box(
                new Rect(x, y, colW, 190f),
                new GUIContent("Credits / Inspo.\n" +
                "Huge Thank You to Uwo for Introducing Me to this Lovely game\n" +
                "Credits to Komar for Inspo the Char Editor Section on This QoL\n\n\n\n\n" +
                "If You Find a Bug Let Me Know or if You Have Suggestions Ping/Dm me\n" +
                " Disc : AnUnknownFurry\n\n\n\n" +
                "Sorry to the People Ive Crashed and the Servers Ive Trolled\n" +
                "I Do Hope One Day I Can Make It up to Everyone ˚ʚ♡ɞ˚"),
                cardStyle);
        }
    }
}