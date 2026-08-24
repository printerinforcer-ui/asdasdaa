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
        // ESP UI
        // ============================================================
        private void DrawESPPanel(float x, float y, float width)
        {
            float colW = (width - 10f) / 2f;
            DrawToggle(x, y, showNames, "PLAYER NAMES", v => showNames = v);
            DrawToggle(x, y + 32f, showDistance, "DISTANCE", v => showDistance = v);
            DrawToggle(x, y + 64f, showActorID, "ACTOR ID", v => showActorID = v);
            DrawToggle(x, y + 96f, hideSelf, "HIDE SELF", v => hideSelf = v);
            DrawToggle(x, y + 128f, tracersEnabled, "TRACERS", v => tracersEnabled = v);
            DrawToggle(x, y + 160f, visibilityCheck, "VISIBILITY CHECK", v => visibilityCheck = v);
            DrawToggle(x, y + 192f, offscreenArrows, "OFFSCREEN ARROWS", v => offscreenArrows = v);

            float rx = x + colW + 10f;
            DrawSlider(rx, y, "MAX DISTANCE", maxDistance, 10f, 1000f, v => maxDistance = v, "F0");
            DrawSlider(rx, y + 72f, "TRACER THICKNESS", tracerThickness, 1f, 5f, v => tracerThickness = v, "F1");

            GUI.Label(new Rect(rx, y + 150f, colW, 20f), new GUIContent("TRACER ORIGIN"), labelStyle);
            string origin = tracerOrigin == 0 ? "BOTTOM CENTER" : tracerOrigin == 1 ? "CENTER" : "TOP CENTER";
            if (GUI.Button(new Rect(rx, y + 174f, colW, 30f), new GUIContent(origin), buttonStyle))
                tracerOrigin = (tracerOrigin + 1) % 3;

            DrawToggle(rx, y + 214f, tracerDistanceFade, "DISTANCE FADE", v => tracerDistanceFade = v);
            DrawToggle(rx, y + 246f, scaleNames, "DISTANCE NAME SCALING", v => scaleNames = v);

            DrawESPColorControls(x, y + 290f, width);

            GUI.Label(new Rect(x, y + 420f, width, 55f), new GUIContent(
                "ESP : " +
                "Selected players and friends use their own colors."
            ), smallStyle);
        }

        private Color GetESPColor(Player player)
        {
            if (player != null && player.ActorNumber == selectedActorId)
                return espColorOptions[Mathf.Clamp(selectedColorIndex, 0, espColorOptions.Length - 1)];
            if (player != null && friendActorIds.Contains(player.ActorNumber))
                return espColorOptions[Mathf.Clamp(friendColorIndex, 0, espColorOptions.Length - 1)];
            return espColorOptions[Mathf.Clamp(normalColorIndex, 0, espColorOptions.Length - 1)];
        }

        private void DrawESPColorControls(float x, float y, float width)
        {
            GUI.Label(new Rect(x, y, width, 22f), new GUIContent("Colors"), headerStyle);
            y += 26f;

            float colW = width / 3f;

            GUI.Label(new Rect(x, y, colW - 8f, 20f), new GUIContent("NORMAL: " + espColorNames[normalColorIndex]), labelStyle);
            if (GUI.Button(new Rect(x, y + 22f, colW - 8f, 26f), new GUIContent("CYCLE"), buttonStyle))
                normalColorIndex = (normalColorIndex + 1) % espColorOptions.Length;

            GUI.Label(new Rect(x + colW, y, colW - 8f, 20f), new GUIContent("SELECTED: " + espColorNames[selectedColorIndex]), labelStyle);
            if (GUI.Button(new Rect(x + colW, y + 22f, colW - 8f, 26f), new GUIContent("CYCLE"), buttonStyle))
                selectedColorIndex = (selectedColorIndex + 1) % espColorOptions.Length;

            GUI.Label(new Rect(x + colW * 2f, y, colW - 8f, 20f), new GUIContent("FRIEND: " + espColorNames[friendColorIndex]), labelStyle);
            if (GUI.Button(new Rect(x + colW * 2f, y + 22f, colW - 8f, 26f), new GUIContent("CYCLE"), buttonStyle))
                friendColorIndex = (friendColorIndex + 1) % espColorOptions.Length;
        }

        // ============================================================
        // ESP RENDERING
        // ============================================================
        private void DrawESP(GUIStyle espStyle)
        {
            if (!PhotonNetwork.InRoom) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            RefreshPlayerObjectCache();

            Vector2 origin = GetTracerOrigin();
            Player[] players = PhotonNetwork.PlayerList;

            for (int i = 0; i < players.Length; i++)
            {
                Player player = players[i];
                if (player == null || (hideSelf && player.IsLocal)) continue;

                GameObject obj = FindPlayerObject(player);
                if (obj == null) continue;

                Vector3 head = obj.transform.position + Vector3.up * NameHeight;
                float distance = Vector3.Distance(cam.transform.position, head);
                if (maxDistance > 0f && distance > maxDistance) continue;

                Vector3 screen = cam.WorldToScreenPoint(head);
                bool behind = screen.z <= 0f;
                Vector2 guiScreen = new Vector2(screen.x, Screen.height - screen.y);
                bool outside = guiScreen.x < 0f || guiScreen.x > Screen.width || guiScreen.y < 0f || guiScreen.y > Screen.height;
                bool visible = !visibilityCheck || IsVisible(cam, head);

                Color color = GetESPColor(player);
                if (visibilityCheck && !visible) color = hiddenColor;

                if (behind || outside)
                {
                    if (offscreenArrows) DrawOffscreenArrow(cam, head, color);
                    continue;
                }

                if (tracersEnabled)
                {
                    Color tracerColor = color;

                    if (tracerDistanceFade && maxDistance > 0f)
                    {
                        float alpha = 1f - Mathf.Clamp01(distance / maxDistance);
                        tracerColor.a = Mathf.Clamp(alpha, 0.15f, 1f);
                    }

                    DrawTracerLine(origin, guiScreen, tracerColor);
                }

                if (showNames || showDistance || showActorID)
                {
                    float scale = 1f;
                    if (scaleNames && maxDistance > 0f)
                        scale = Mathf.Lerp(MaxNameScale, MinNameScale, Mathf.Clamp01(distance / maxDistance));

                    string label = string.IsNullOrEmpty(player.NickName) ? "Player " + player.ActorNumber : player.NickName;
                    if (showActorID) label += "  #" + player.ActorNumber;
                    if (showDistance) label += "  " + distance.ToString("0") + "m";

                    GUI.color = color;
                    int prevSize = espFontSize;
                    espStyle.fontSize = Mathf.RoundToInt(espFontSize * scale);
                    Vector2 size = espStyle.CalcSize(new GUIContent(label));
                    GUI.Label(new Rect(guiScreen.x - size.x / 2f, guiScreen.y - size.y, size.x, size.y), label, espStyle);
                    espStyle.fontSize = prevSize;
                    GUI.color = Color.white;
                }
            }
        }

        private Vector2 GetTracerOrigin()
        {
            if (tracerOrigin == 0) return new Vector2(Screen.width / 2f, Screen.height);
            if (tracerOrigin == 1) return new Vector2(Screen.width / 2f, Screen.height / 2f);
            return new Vector2(Screen.width / 2f, 0f);
        }

        private void DrawTracerLine(Vector2 from, Vector2 to, Color color)
        {
            DrawThickLine(from, to, color, tracerThickness);
        }

        private bool IsVisible(Camera cam, Vector3 worldPos)
        {
            RaycastHit hit;
            Vector3 origin = cam.transform.position;
            Vector3 dir = worldPos - origin;
            float dist = dir.magnitude;
            if (dist <= 0.01f) return true;
            if (Physics.Raycast(origin, dir.normalized, out hit, dist))
                return Vector3.Distance(hit.point, worldPos) < 0.75f;
            return true;
        }

        private Texture2D arrowTexture;

        private Texture2D GetArrowTexture()
        {
            if (arrowTexture != null)
                return arrowTexture;

            const int size = 32;
            arrowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            arrowTexture.wrapMode = TextureWrapMode.Clamp;
            arrowTexture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            int cx = size / 2;

            for (int y = 0; y < size; y++)
            {
                float t = y / (float)(size - 1); // 0 at bottom row, 1 at top row
                float halfWidth = (1f - t) * (size / 2f); // widest at base (bottom), narrows to apex (top)

                for (int x = 0; x < size; x++)
                {
                    bool inside = Mathf.Abs(x - cx) <= halfWidth;
                    pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }

            arrowTexture.SetPixels32(pixels);
            arrowTexture.Apply();
            return arrowTexture;
        }

        private void DrawOffscreenArrow(Camera cam, Vector3 worldPos, Color color)
        {
            Vector3 screen = cam.WorldToScreenPoint(worldPos);
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            if (screen.z < 0f)
            {
                screen.x = -screen.x;
                screen.y = -screen.y;
            }

            Vector2 target = new Vector2(screen.x, Screen.height - screen.y);
            Vector2 direction = target - screenCenter;

            if (direction.sqrMagnitude < 0.001f)
                return;

            direction.Normalize();

            float halfWidth = Screen.width * 0.5f - OffscreenArrowMargin;
            float halfHeight = Screen.height * 0.5f - OffscreenArrowMargin;

            float scaleX = Mathf.Abs(direction.x) > 0.001f ? halfWidth / Mathf.Abs(direction.x) : float.MaxValue;
            float scaleY = Mathf.Abs(direction.y) > 0.001f ? halfHeight / Mathf.Abs(direction.y) : float.MaxValue;
            float scale = Mathf.Min(scaleX, scaleY);

            Vector2 arrowCenter = screenCenter + direction * scale;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);

            float size = OffscreenArrowSize;

            Vector2 tip = arrowCenter + direction * size;
            Vector2 left = arrowCenter - direction * (size * 0.65f) + perpendicular * (size * 0.65f);
            Vector2 right = arrowCenter - direction * (size * 0.65f) - perpendicular * (size * 0.65f);

            DrawThickLine(tip, left, color, 3f);
            DrawThickLine(tip, right, color, 3f);
            DrawThickLine(left, right, color, 3f);
        }

        private void EnsureTracerMaterial()
        {
            if (tracerMaterial != null)
                return;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                return;

            tracerMaterial = new Material(shader);
            tracerMaterial.hideFlags = HideFlags.HideAndDontSave;

            tracerMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            tracerMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            tracerMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            tracerMaterial.SetInt("_ZWrite", 0);
            tracerMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            tracerMaterial.renderQueue = 5000;
        }

        private void DrawGLLine(Vector2 start, Vector2 end, Color color)
        {
            EnsureTracerMaterial();

            if (tracerMaterial == null)
                return;

            tracerMaterial.SetPass(0);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            GL.Begin(1); // GL.LINES
            GL.Color(color);
            GL.Vertex3(start.x, start.y, 0f);
            GL.Vertex3(end.x, end.y, 0f);
            GL.End();
            GL.PopMatrix();
        }

        private void DrawThickLine(Vector2 start, Vector2 end, Color color, float width)
        {
            float half = Mathf.Max(width * 0.5f, 0.5f);

            Vector2 delta = end - start;
            Vector2 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.right;
            Vector2 perpendicular = new Vector2(-dir.y, dir.x);

            DrawGLLine(start - perpendicular * half, end - perpendicular * half, color);
            DrawGLLine(start + perpendicular * half, end + perpendicular * half, color);

            if (width > 2f)
                DrawGLLine(start, end, color);
        }
    }
}
