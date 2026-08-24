using BepInEx;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ZexQoLMenu
{
    /// <summary>
    /// Client-side social + QoL: mod-user tags, private messages, clean screenshot,
    /// exposure, keybind cheat-sheet, nameplate scale/opacity.
    /// </summary>
    public partial class Plugin
    {
        // ---- Photon presence / PM ----
        private const string ZexClientPropKey = "ZQL"; // wire tag; display brand is ˚ʚ♡ɞ˚
        private const string ZexClientPropVal = "1";
        private const byte ZexPrivateMsgEventCode = 175;

        private readonly List<string> pmLog = new List<string>();
        private const int PmLogMax = 40;
        private string pmDraft = "";
        private bool pmDraftFocused;
        private int pmTargetActor = -1;
        private Vector2 pmScroll;

        // ---- Clean screenshot ----
        private bool cleanUiActive;
        private bool cleanUiRestoreMenu;
        private bool cleanUiRestoreOverlay;
        private bool cleanUiRestoreRadar;
        private bool cleanUiRestoreNames;
        private bool cleanUiRestoreTracers;
        private bool screenshotPending;
        private int screenshotSuperSize = 2; // 1=native, 2≈2x, 3≈3x …
        private string lastScreenshotPath = "";

        // ---- Cheat sheet ----
        private bool cheatSheetVisible;

        // ============================================================
        // Presence
        // ============================================================
        private void AnnounceZexClient()
        {
            try
            {
                if (PhotonNetwork.LocalPlayer == null) return;
                var props = new ExitGames.Client.Photon.Hashtable { { ZexClientPropKey, ZexClientPropVal } };
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("AnnounceZexClient: " + ex.Message);
            }
        }

        private static bool PlayerHasZexClient(Player p)
        {
            if (p == null || p.CustomProperties == null) return false;
            object v;
            if (!p.CustomProperties.TryGetValue(ZexClientPropKey, out v) || v == null) return false;
            string s = v as string ?? v.ToString();
            return !string.IsNullOrEmpty(s);
        }

        // ============================================================
        // Private messages
        // ============================================================
        private void HandleZexClientEvent(EventData photonEvent)
        {
            if (photonEvent == null || photonEvent.Code != ZexPrivateMsgEventCode)
                return;

            try
            {
                object data = photonEvent.CustomData;
                var arr = data as object[];
                if (arr != null && arr.Length >= 1)
                {
                    string kind = arr[0] as string;
                    if (string.Equals(kind, "PING", StringComparison.OrdinalIgnoreCase))
                    {
                        HandlePartyPingPayload(arr, photonEvent.Sender);
                        return;
                    }
                    if (string.Equals(kind, "WP", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleSharedWaypointPayload(arr, photonEvent.Sender);
                        return;
                    }
                }

                string msg = null;
                int fromActor = photonEvent.Sender;

                if (arr != null && arr.Length >= 2)
                {
                    // [0]= "PM", [1]= message, [2]= fromActor
                    msg = arr[1] as string;
                    if (arr.Length >= 3 && arr[2] is int)
                        fromActor = (int)arr[2];
                }
                else if (data is string)
                {
                    msg = (string)data;
                }

                if (string.IsNullOrEmpty(msg)) return;

                Player from = null;
                if (PhotonNetwork.CurrentRoom != null)
                    from = PhotonNetwork.CurrentRoom.GetPlayer(fromActor);

                string fromName = from != null && !string.IsNullOrEmpty(from.NickName)
                    ? from.NickName
                    : ("#" + fromActor);

                lastPmFromActor = fromActor;
                AppendPmLog("(Received from " + fromName + ") \"" + msg + "\"");
                ShowToast("˚ʚ♡ɞ˚ PM from " + fromName + "  [R reply]", "social");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("HandleZexClientEvent: " + ex.Message);
            }
        }

        private void HandleSharedWaypointPayload(object[] arr, int sender)
        {
            try
            {
                if (arr == null || arr.Length < 5) return;
                string wpName = arr[1] as string ?? "Shared";
                float px = Convert.ToSingle(arr[2]);
                float py = Convert.ToSingle(arr[3]);
                float pz = Convert.ToSingle(arr[4]);
                int fromActor = sender;
                if (arr.Length >= 6 && arr[5] is int) fromActor = (int)arr[5];
                if (PhotonNetwork.LocalPlayer != null && fromActor == PhotonNetwork.LocalPlayer.ActorNumber)
                    return;

                string baseName = "Zex_" + wpName;
                string name = baseName;
                int n = 1;
                while (savedWaypoints.ContainsKey(name))
                {
                    name = baseName + "_" + n;
                    n++;
                }
                savedWaypoints[name] = new Vector3(px, py, pz);
                Player from = PhotonNetwork.CurrentRoom != null
                    ? PhotonNetwork.CurrentRoom.GetPlayer(fromActor) : null;
                string fromName = from != null && !string.IsNullOrEmpty(from.NickName)
                    ? from.NickName : ("#" + fromActor);
                ShowToast("˚ʚ♡ɞ˚ WP \"" + name + "\" from " + fromName, "social");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("HandleSharedWaypointPayload: " + ex.Message);
            }
        }

        private void ShareWaypointToZexClients(string wpName)
        {
            if (!PhotonNetwork.InRoom)
            {
                ShowToast("Not in a room", "system");
                return;
            }
            if (string.IsNullOrEmpty(wpName) || !savedWaypoints.ContainsKey(wpName))
            {
                ShowToast("Pick a waypoint first", "system");
                return;
            }
            Vector3 pos = savedWaypoints[wpName];
            try
            {
                int self = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
                object[] payload = { "WP", wpName, pos.x, pos.y, pos.z, self };
                int[] targets = CollectZexClientActors(excludeLocal: true);
                RaiseEventOptions opts = targets != null && targets.Length > 0
                    ? new RaiseEventOptions { TargetActors = targets, Receivers = ReceiverGroup.Others, CachingOption = EventCaching.DoNotCache }
                    : new RaiseEventOptions { Receivers = ReceiverGroup.Others, CachingOption = EventCaching.DoNotCache };
                PhotonNetwork.RaiseEvent(ZexPrivateMsgEventCode, payload, opts, new SendOptions { Reliability = true });
                int count = targets != null ? targets.Length : 0;
                ShowToast(count > 0
                    ? ("Shared \"" + wpName + "\" → " + count + " client(s)")
                    : ("Shared \"" + wpName + "\" (no other clients tagged)"), "social");
            }
            catch (Exception ex)
            {
                ShowToast("Share WP failed", "system");
                Logger.LogWarning("ShareWaypointToZexClients: " + ex.Message);
            }
        }

        private void ReplyToLastPm()
        {
            if (lastPmFromActor <= 0)
            {
                ShowToast("No PM to reply to", "social");
                return;
            }
            if (!PhotonNetwork.InRoom)
            {
                ShowToast("Not in a room", "social");
                return;
            }
            Player p = PhotonNetwork.CurrentRoom != null
                ? PhotonNetwork.CurrentRoom.GetPlayer(lastPmFromActor) : null;
            if (p == null)
            {
                ShowToast("Sender left the room", "social");
                return;
            }
            OpenPmWithPlayer(p);
        }

        /// <summary>
        /// Payload: ["PING", x, y, z, fromActor]
        /// Only Zex clients listen (event 175) — vanilla players ignore it.
        /// </summary>
        private void HandlePartyPingPayload(object[] arr, int sender)
        {
            try
            {
                if (arr == null || arr.Length < 4) return;
                float px = Convert.ToSingle(arr[1]);
                float py = Convert.ToSingle(arr[2]);
                float pz = Convert.ToSingle(arr[3]);
                int fromActor = sender;
                if (arr.Length >= 5 && arr[4] is int)
                    fromActor = (int)arr[4];

                // Ignore our own echo
                if (PhotonNetwork.LocalPlayer != null && fromActor == PhotonNetwork.LocalPlayer.ActorNumber)
                    return;

                partyPingWorld = new Vector3(px, py, pz);
                partyPingUntil = Time.unscaledTime + PartyPingDuration;
                partyPingActive = true;

                Player from = PhotonNetwork.CurrentRoom != null
                    ? PhotonNetwork.CurrentRoom.GetPlayer(fromActor)
                    : null;
                partyPingFrom = from != null && !string.IsNullOrEmpty(from.NickName)
                    ? from.NickName
                    : ("#" + fromActor);

                ShowToast("˚ʚ♡ɞ˚ Ping from " + partyPingFrom, "social");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("HandlePartyPingPayload: " + ex.Message);
            }
        }

        /// <summary>Broadcast a world marker to other Zex clients in the room.</summary>
        private void SendPartyPing()
        {
            if (!PhotonNetwork.InRoom)
            {
                ShowToast("Not in a room", "system");
                return;
            }

            Vector3 pos;
            GameObject local = GetLocalPlayer();
            if (local != null)
                pos = local.transform.position;
            else if (Camera.main != null)
                pos = Camera.main.transform.position;
            else
            {
                ShowToast("No position for ping", "system");
                return;
            }

            try
            {
                int self = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
                object[] payload =
                {
                    "PING",
                    pos.x,
                    pos.y,
                    pos.z,
                    self
                };

                // Prefer only Zex clients if we can list them; else Others (vanilla ignores code 175)
                int[] targets = CollectZexClientActors(excludeLocal: true);
                RaiseEventOptions opts;
                if (targets != null && targets.Length > 0)
                {
                    opts = new RaiseEventOptions
                    {
                        TargetActors = targets,
                        Receivers = ReceiverGroup.Others,
                        CachingOption = EventCaching.DoNotCache
                    };
                }
                else
                {
                    opts = new RaiseEventOptions
                    {
                        Receivers = ReceiverGroup.Others,
                        CachingOption = EventCaching.DoNotCache
                    };
                }

                PhotonNetwork.RaiseEvent(ZexPrivateMsgEventCode, payload, opts, new SendOptions { Reliability = true });

                // Local marker so sender sees it too
                partyPingWorld = pos;
                partyPingUntil = Time.unscaledTime + PartyPingDuration;
                partyPingActive = true;
                partyPingFrom = "You";

                int n = targets != null ? targets.Length : 0;
                ShowToast(n > 0
                    ? ("˚ʚ♡ɞ˚ Ping sent → " + n + " client(s)")
                    : "˚ʚ♡ɞ˚ Ping sent (no other clients tagged)", "social");
            }
            catch (Exception ex)
            {
                ShowToast("Ping failed: " + ex.Message, "system");
                Logger.LogWarning("SendPartyPing: " + ex.Message);
            }
        }

        private int[] CollectZexClientActors(bool excludeLocal)
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.PlayerList == null)
                return null;
            List<int> ids = new List<int>();
            for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
            {
                Player p = PhotonNetwork.PlayerList[i];
                if (p == null) continue;
                if (excludeLocal && p.IsLocal) continue;
                if (!PlayerHasZexClient(p)) continue;
                ids.Add(p.ActorNumber);
            }
            return ids.Count > 0 ? ids.ToArray() : null;
        }

        private void OpenPmWithPlayer(Player p)
        {
            if (p == null || p.IsLocal)
            {
                ShowToast("Can't message yourself", "social");
                return;
            }
            if (!PlayerHasZexClient(p))
            {
                ShowToast("They need ˚ʚ♡ɞ˚ client for PM", "social");
                // Still open chat tab so user understands
            }
            pmTargetActor = p.ActorNumber;
            pmDraft = "";
            pmDraftFocused = true;
            tab = 13;
            menuVisible = true;
            ShowToast("Chat → " + (string.IsNullOrEmpty(p.NickName) ? ("#" + p.ActorNumber) : p.NickName), "social");
        }

        private void DrawPartyPingMark()
        {
            if (!partyPingActive)
                return;
            if (Time.unscaledTime > partyPingUntil)
            {
                partyPingActive = false;
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 baseW = partyPingWorld;
            Vector3 topW = partyPingWorld + Vector3.up * 2.4f;
            Vector3 baseS = cam.WorldToScreenPoint(baseW);
            Vector3 topS = cam.WorldToScreenPoint(topW);
            if (baseS.z <= 0f && topS.z <= 0f) return;

            Vector2 baseGui = new Vector2(baseS.x, Screen.height - baseS.y);
            Vector2 topGui = new Vector2(topS.x, Screen.height - topS.y);

            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 7f));
            Color col = new Color(0.75f, 0.45f, 1f, pulse); // purple = Zex party

            if (baseS.z > 0f && topS.z > 0f)
                DrawTracerLine(baseGui, topGui, col);

            if (topS.z > 0f)
            {
                float arm = 11f;
                DrawTracerLine(topGui + new Vector2(-arm, 0f), topGui + new Vector2(arm, 0f), col);
                DrawTracerLine(topGui + new Vector2(0f, -arm), topGui + new Vector2(0f, arm), col);

                float left = Mathf.Max(0f, partyPingUntil - Time.unscaledTime);
                string label = "˚ʚ♡ɞ˚ " + (partyPingFrom ?? "?") + "  " + left.ToString("0.0") + "s";
                GUIStyle st = smallStyle != null ? smallStyle : GUI.skin.label;
                Vector2 sz = st.CalcSize(new GUIContent(label));
                GUI.color = col;
                GUI.Label(new Rect(topGui.x - sz.x * 0.5f, topGui.y - sz.y - 4f, sz.x, sz.y), label, st);
                GUI.color = Color.white;
            }
        }

        private void SendPrivateMessage(int targetActor, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (!PhotonNetwork.InRoom)
            {
                ShowToast("Not in a room");
                return;
            }

            try
            {
                var opts = new RaiseEventOptions
                {
                    TargetActors = new[] { targetActor },
                    Receivers = ReceiverGroup.Others,
                    CachingOption = EventCaching.DoNotCache
                };
                var sendOpts = new SendOptions { Reliability = true };
                object[] payload =
                {
                    "PM",
                    message,
                    PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1
                };
                PhotonNetwork.RaiseEvent(ZexPrivateMsgEventCode, payload, opts, sendOpts);

                Player target = PhotonNetwork.CurrentRoom != null
                    ? PhotonNetwork.CurrentRoom.GetPlayer(targetActor)
                    : null;
                string toName = target != null && !string.IsNullOrEmpty(target.NickName)
                    ? target.NickName
                    : ("#" + targetActor);
                AppendPmLog("(Sent to " + toName + ") \"" + message + "\"");
                ShowToast("˚ʚ♡ɞ˚ PM sent to " + toName, "social");
            }
            catch (Exception ex)
            {
                ShowToast("PM failed: " + ex.Message, "social");
                Logger.LogWarning("SendPrivateMessage: " + ex);
            }
        }

        private void AppendPmLog(string line)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            pmLog.Add("[" + stamp + "] " + line);
            while (pmLog.Count > PmLogMax)
                pmLog.RemoveAt(0);
        }

        // ============================================================
        // Clean UI + screenshot
        // ============================================================
        private void ToggleCleanUiAndScreenshot()
        {
            if (screenshotPending) return;
            StartCoroutine(CleanScreenshotRoutine());
        }

        private IEnumerator CleanScreenshotRoutine()
        {
            screenshotPending = true;

            cleanUiRestoreMenu = menuVisible;
            cleanUiRestoreOverlay = showPlayerOverlay;
            cleanUiRestoreRadar = showPlayerRadar;
            cleanUiRestoreNames = showNames;
            cleanUiRestoreTracers = tracersEnabled;

            cleanUiActive = true;
            menuVisible = false;
            showPlayerOverlay = false;
            showPlayerRadar = false;
            showNames = false;
            tracersEnabled = false;

            // Best-effort: hide uGUI canvases via reflection (no UIModule reference required)
            var disabledBehaviours = new List<Behaviour>();
            try
            {
                Type canvasType = Type.GetType("UnityEngine.Canvas, UnityEngine.UIModule")
                                  ?? Type.GetType("UnityEngine.Canvas, UnityEngine");
                if (canvasType != null)
                {
                    var find = typeof(UnityEngine.Object).GetMethod(
                        "FindObjectsOfType", Type.EmptyTypes);
                    // FindObjectsOfType<T> is generic — use Resources.FindObjectsOfTypeAll
                    UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(canvasType);
                    if (all != null)
                    {
                        var modeProp = canvasType.GetProperty("renderMode");
                        for (int i = 0; i < all.Length; i++)
                        {
                            var c = all[i] as Behaviour;
                            if (c == null || !c.enabled) continue;
                            try
                            {
                                if (modeProp != null)
                                {
                                    object mode = modeProp.GetValue(c, null);
                                    // WorldSpace == 2
                                    if (mode != null && Convert.ToInt32(mode) == 2) continue;
                                }
                            }
                            catch { }
                            c.enabled = false;
                            disabledBehaviours.Add(c);
                        }
                    }
                }
            }
            catch { }

            // Wait for UI to disappear, then capture at end of frame
            yield return null;
            yield return new WaitForEndOfFrame();

            // Prefer a writable folder (Steam game dir under Program Files is often locked)
            string dir = GetWritableScreenshotDir();

            string file = "zex_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bmp";
            string full = Path.Combine(dir, file);
            int superSize = Mathf.Clamp(screenshotSuperSize, 1, 4);

            bool captured = false;
            int outW = Screen.width;
            int outH = Screen.height;
            try
            {
                // Reliable path: render main camera into a scaled RenderTexture + EncodeToPNG
                Camera cam = Camera.main;
                if (cam == null)
                {
                    Camera[] cams = UnityEngine.Object.FindObjectsOfType<Camera>();
                    if (cams != null)
                    {
                        for (int i = 0; i < cams.Length; i++)
                        {
                            if (cams[i] != null && cams[i].enabled && cams[i].gameObject.activeInHierarchy)
                            {
                                cam = cams[i];
                                break;
                            }
                        }
                    }
                }

                if (cam != null)
                {
                    outW = Mathf.Max(16, Screen.width * superSize);
                    outH = Mathf.Max(16, Screen.height * superSize);

                    RenderTexture rt = new RenderTexture(outW, outH, 24, RenderTextureFormat.ARGB32);
                    rt.Create();

                    RenderTexture prevTarget = cam.targetTexture;
                    RenderTexture prevActive = RenderTexture.active;

                    cam.targetTexture = rt;
                    cam.Render();

                    RenderTexture.active = rt;
                    Texture2D tex = new Texture2D(outW, outH, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, outW, outH), 0, 0);
                    tex.Apply();

                    cam.targetTexture = prevTarget;
                    RenderTexture.active = prevActive;

                    byte[] png = EncodeTextureBmp(tex);
                    UnityEngine.Object.Destroy(tex);
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);

                    if (png == null || png.Length == 0)
                    {
                        Logger.LogWarning("Screenshot: BMP encode returned empty");
                    }
                    else
                    {
                        try
                        {
                            File.WriteAllBytes(full, png);
                            captured = File.Exists(full) && new FileInfo(full).Length > 0;
                            if (captured) lastScreenshotPath = full;
                            else Logger.LogWarning("Screenshot: write produced empty file at " + full);
                        }
                        catch (Exception wex)
                        {
                            Logger.LogWarning("Screenshot: write failed: " + wex.Message + " path=" + full);
                        }
                    }
                }

                // Fallback: grab the backbuffer at native res
                if (!captured)
                {
                    outW = Screen.width;
                    outH = Screen.height;
                    Texture2D tex = new Texture2D(outW, outH, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, outW, outH), 0, 0);
                    tex.Apply();
                    byte[] png = EncodeTextureBmp(tex);
                    UnityEngine.Object.Destroy(tex);
                    if (png == null || png.Length == 0)
                    {
                        Logger.LogWarning("Screenshot fallback: BMP encode returned empty");
                    }
                    else
                    {
                        try
                        {
                            File.WriteAllBytes(full, png);
                            captured = File.Exists(full) && new FileInfo(full).Length > 0;
                            if (captured) lastScreenshotPath = full;
                        }
                        catch (Exception wex)
                        {
                            Logger.LogWarning("Screenshot fallback write failed: " + wex.Message);
                        }
                    }
                }

                if (!captured)
                    Logger.LogWarning("Screenshot: encode/write failed. dir=" + dir + " exists=" + Directory.Exists(dir));
                else
                    Logger.LogInfo("Screenshot wrote " + full + " (" + outW + "x" + outH + ", " + new FileInfo(full).Length + " bytes)");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Screenshot failed: " + ex);
            }

            yield return null;

            for (int i = 0; i < disabledBehaviours.Count; i++)
            {
                try { if (disabledBehaviours[i] != null) disabledBehaviours[i].enabled = true; }
                catch { }
            }

            cleanUiActive = false;
            menuVisible = cleanUiRestoreMenu;
            showPlayerOverlay = cleanUiRestoreOverlay;
            showPlayerRadar = cleanUiRestoreRadar;
            showNames = cleanUiRestoreNames;
            tracersEnabled = cleanUiRestoreTracers;

            screenshotPending = false;

            if (captured)
            {
                ShowToast("Screenshot saved → " + file);
            }
            else
            {
                ShowToast("Screenshot failed — check BepInEx log");
            }
        }

        private static string GetWritableScreenshotDir()
        {
            var list = new System.Collections.Generic.List<string>();
            try
            {
                list.Add(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BepInEx", "ZexScreenshots")));
            }
            catch { }
            list.Add(Path.Combine(Application.persistentDataPath, "ZexScreenshots"));
            try { list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ZexScreenshots")); } catch { }
            try { list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZexScreenshots")); } catch { }
            list.Add(Path.Combine(Path.GetTempPath(), "ZexScreenshots"));

            for (int i = 0; i < list.Count; i++)
            {
                string d = list[i];
                if (string.IsNullOrEmpty(d)) continue;
                try
                {
                    if (!Directory.Exists(d))
                        Directory.CreateDirectory(d);
                    string probe = Path.Combine(d, ".write_test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return d;
                }
                catch { }
            }

            return Application.persistentDataPath;
        }

        private static byte[] EncodeTextureBmp(Texture2D tex)
        {
            if (tex == null) return null;
            try
            {
                int w = tex.width;
                int h = tex.height;
                Color32[] pixels = tex.GetPixels32();
                if (pixels == null || pixels.Length < w * h)
                    return null;

                int rowStride = ((w * 3 + 3) / 4) * 4; // padded to 4 bytes
                int pixelSize = rowStride * h;
                int fileSize = 14 + 40 + pixelSize;

                byte[] data = new byte[fileSize];
                // BITMAPFILEHEADER
                data[0] = (byte)'B';
                data[1] = (byte)'M';
                WriteInt32LE(data, 2, fileSize);
                WriteInt32LE(data, 10, 14 + 40); // pixel offset
                // BITMAPINFOHEADER
                WriteInt32LE(data, 14, 40); // header size
                WriteInt32LE(data, 18, w);
                WriteInt32LE(data, 22, h);  // positive = bottom-up
                data[26] = 1; // planes
                data[28] = 24; // bpp
                WriteInt32LE(data, 34, pixelSize);

                int dst = 14 + 40;
                for (int y = 0; y < h; y++)
                {
                    int srcRow = y * w; // GetPixels32 is bottom-up already for BMP? 
                    // Unity GetPixels32 is left-to-right, bottom-to-top
                    int rowStart = dst + y * rowStride;
                    for (int x = 0; x < w; x++)
                    {
                        Color32 c = pixels[srcRow + x];
                        int i = rowStart + x * 3;
                        data[i] = c.b;
                        data[i + 1] = c.g;
                        data[i + 2] = c.r;
                    }
                }
                return data;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("EncodeTextureBmp: " + ex.Message);
                return null;
            }
        }

        private static void WriteInt32LE(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }


        // ============================================================
        // Cheat sheet / PM / testing UI draws
        // ============================================================

        private void DrawCheatSheetOverlay()
        {
            if (!cheatSheetVisible || cleanUiActive) return;

            float w = 340f;
            float h = 280f;
            Rect r = new Rect(16f, 16f, w, h);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.Box(r, "");
            GUI.color = prev;

            GUIStyle st = smallStyle != null ? smallStyle : GUI.skin.label;
            float y = r.y + 8f;
            float x = r.x + 10f;
            float lw = r.width - 20f;

            GUI.Label(new Rect(x, y, lw, 20f), "KEYBINDS (hold)", headerStyle != null ? headerStyle : st);
            y += 24f;

            void Line(string a, string b)
            {
                GUI.Label(new Rect(x, y, lw, 18f), a + "  ·  " + b, st);
                y += 18f;
            }

            Line("Menu", menuToggleKey != null ? menuToggleKey.Value.ToString() : "?");
            Line("Noclip", noclipToggleKey != null ? noclipToggleKey.Value.ToString() : "?");
            Line("Waypoint", waypointQuickSaveKey != null ? waypointQuickSaveKey.Value.ToString() : "?");
            Line("Fly +", flySpeedUpKey != null ? flySpeedUpKey.Value.ToString() : "?");
            Line("Fly −", flySpeedDownKey != null ? flySpeedDownKey.Value.ToString() : "?");
            Line("Spec next", spectateNextKey != null ? spectateNextKey.Value.ToString() : "?");
            Line("Spec prev", spectatePrevKey != null ? spectatePrevKey.Value.ToString() : "?");
            Line("Clean shot", cleanScreenshotKey != null ? cleanScreenshotKey.Value.ToString() : "F9");
            Line("Cheat sheet", cheatSheetKey != null ? cheatSheetKey.Value.ToString() : "F10");
        }

        private void DrawClientFeaturesOverlays()
        {
            if (cleanUiActive) return;
            DrawCheatSheetOverlay();
        }

        /// <summary>TESTING tab content for client WIP features.</summary>
        private void DrawClientFeaturesTestingPanel(float x, float y, float width, float maxHeight)
        {
            float leftW = width * 0.48f;
            float gap = 12f;
            float rightX = x + leftW + gap;
            float rightW = width - leftW - gap;
            float startY = y;

            // ---- LEFT: WIP tools ----
            GUI.Label(new Rect(x, y, leftW, 22f), new GUIContent("WIP / CLIENT"), headerStyle);
            y += 26f;

            GUI.Label(new Rect(x, y, leftW, 18f),
                new GUIContent("UI SCALE: " + uiScale.ToString("0.00") + "x"), labelStyle);
            y += 20f;
            float newScale = GUI.HorizontalSlider(new Rect(x, y, leftW, 18f), uiScale, UiScaleMin, UiScaleMax,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            if (!Mathf.Approximately(newScale, uiScale))
            {
                uiScale = newScale;
                if (configUiScale != null) configUiScale.Value = uiScale;
            }
            y += 26f;

            if (GUI.Button(new Rect(x, y, leftW, 26f),
                new GUIContent(showHudWaypoints ? "HUD Waypoints: ON" : "HUD Waypoints: OFF"), buttonStyle))
            {
                showHudWaypoints = !showHudWaypoints;
                if (configShowHudWaypoints != null) configShowHudWaypoints.Value = showHudWaypoints;
            }
            y += 30f;

            string lastRoom = !string.IsNullOrEmpty(lastJoinedRoomName) ? lastJoinedRoomName : "(none)";
            if (lastRoom.Length > 26) lastRoom = lastRoom.Substring(0, 25) + "…";
            if (GUI.Button(new Rect(x, y, leftW, 26f),
                new GUIContent("Rejoin last: " + lastRoom), buttonStyle))
                TryRejoinLastRoom();
            y += 34f;

            // Screenshot
            GUI.Label(new Rect(x, y, leftW, 18f),
                new GUIContent("SHOT SCALE: " + screenshotSuperSize + "x  (~" +
                               (Screen.width * screenshotSuperSize) + "x" +
                               (Screen.height * screenshotSuperSize) + ")"), labelStyle);
            y += 20f;
            float ss = GUI.HorizontalSlider(new Rect(x, y, leftW, 18f), screenshotSuperSize, 1f, 4f,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);
            int ssi = Mathf.RoundToInt(ss);
            if (ssi != screenshotSuperSize)
            {
                screenshotSuperSize = ssi;
                if (configScreenshotSuperSize != null) configScreenshotSuperSize.Value = screenshotSuperSize;
            }
            y += 24f;
            if (GUI.Button(new Rect(x, y, leftW, 28f),
                new GUIContent("CLEAN SCREENSHOT (" +
                               (cleanScreenshotKey != null ? cleanScreenshotKey.Value.ToString() : "F9") + ")"),
                buttonStyle))
                ToggleCleanUiAndScreenshot();
            y += 32f;

            if (!string.IsNullOrEmpty(lastScreenshotPath))
            {
                GUI.Label(new Rect(x, y, leftW, 32f),
                    new GUIContent("Last: " + Path.GetFileName(lastScreenshotPath)), smallStyle);
            }

            GUI.Label(new Rect(x + leftW + 12f, startY, width - leftW - 12f, 60f),
    new GUIContent("Chat moved to the CHAT tab (sidebar → SOON → CHAT)"),
    smallStyle);

        }

        private void DrawChatPanel(float x, float y, float width, float maxHeight)
        {
            float startY = y;
            float gap = 14f;
            float leftW = width * 0.34f;
            float rightX = x + leftW + gap;
            float rightW = width - leftW - gap;

            // ----- Left: people -----
            GUI.Label(new Rect(x, y, leftW, 22f), new GUIContent("PEOPLE"), headerStyle);
            y += 24f;

            string status = !PhotonNetwork.InRoom
                ? "Not in a room"
                : "In room · pick who to message";
            GUI.Label(new Rect(x, y, leftW, 18f), new GUIContent(status), smallStyle);
            y += 22f;

            // Chatting-with banner
            string chatWith = "Nobody selected";
            if (pmTargetActor > 0 && PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            {
                Player tp = PhotonNetwork.CurrentRoom.GetPlayer(pmTargetActor);
                if (tp != null)
                    chatWith = string.IsNullOrEmpty(tp.NickName) ? ("#" + pmTargetActor) : tp.NickName;
                else
                    chatWith = "#" + pmTargetActor;
            }
            GUI.Label(new Rect(x, y, leftW, 36f),
                new GUIContent("Chatting with: " + chatWith), labelStyle);
            y += 40f;

            float listBottom = startY + maxHeight - 8f;
            float listH = Mathf.Max(80f, listBottom - y);
            Rect listRect = new Rect(x, y, leftW, listH);
            GUI.Box(listRect, "");

            float rowY = listRect.y + 4f;
            int zexCount = 0;
            if (PhotonNetwork.InRoom && PhotonNetwork.PlayerList != null)
            {
                for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
                {
                    Player p = PhotonNetwork.PlayerList[i];
                    if (p == null || !PlayerHasZexClient(p)) continue;
                    if (p.IsLocal) continue; // don't PM yourself
                    zexCount++;
                    string name = string.IsNullOrEmpty(p.NickName) ? "#" + p.ActorNumber : p.NickName;
                    bool sel = pmTargetActor == p.ActorNumber;
                    if (rowY + 26f > listRect.yMax - 4f) break;
                    if (GUI.Button(new Rect(listRect.x + 4f, rowY, listRect.width - 8f, 24f),
                        new GUIContent(sel ? "► " + name : name),
                        sel ? selectedButtonStyle : buttonStyle))
                    {
                        pmTargetActor = p.ActorNumber;
                    }
                    rowY += 26f;
                }
            }

            if (zexCount == 0)
            {
                GUI.Label(new Rect(listRect.x + 8f, listRect.y + 10f, listRect.width - 16f, 48f),
                    new GUIContent(PhotonNetwork.InRoom
                        ? "No other ˚ʚ♡ɞ˚ clients in this room."
                        : "Join a room to see ˚ʚ♡ɞ˚ clients."),
                    smallStyle);
            }

            // ----- Right: conversation -----
            float ry = startY;
            GUI.Label(new Rect(rightX, ry, rightW, 22f), new GUIContent("CONVERSATION"), headerStyle);
            ry += 24f;

            GUI.Label(new Rect(rightX, ry, rightW, 18f),
                new GUIContent(pmTargetActor > 0 ? ("Thread with " + chatWith) : "Select someone on the left"),
                smallStyle);
            ry += 22f;

            float inputH = 28f;
            float sendH = 30f;
            float logH = Mathf.Max(100f, (startY + maxHeight) - ry - inputH - sendH - 16f);
            Rect logRect = new Rect(rightX, ry, rightW, logH);
            GUI.Box(logRect, "");

            int maxLines = Mathf.Max(1, Mathf.FloorToInt((logH - 8f) / 18f));
            int start = Mathf.Max(0, pmLog.Count - maxLines);
            for (int i = start; i < pmLog.Count; i++)
            {
                float ly = logRect.y + 4f + (i - start) * 18f;
                string line = pmLog[i];
                // Mild color cue via prefix already in text
                GUI.Label(new Rect(logRect.x + 8f, ly, logRect.width - 16f, 18f), line, smallStyle);
            }
            ry += logH + 8f;

            // Composer
            Rect field = new Rect(rightX, ry, rightW, inputH);
            GUI.Box(field, "");
            string placeholder = pmTargetActor > 0
                ? ("Message " + chatWith + "…")
                : "Select someone first…";
            string shown = string.IsNullOrEmpty(pmDraft)
                ? (pmDraftFocused ? "|" : placeholder)
                : pmDraft + (pmDraftFocused ? "|" : "");
            GUI.Label(new Rect(field.x + 6f, field.y + 5f, field.width - 12f, 18f), shown, labelStyle);

            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && field.Contains(e.mousePosition))
            {
                if (pmTargetActor > 0)
                    pmDraftFocused = true;
                e.Use();
            }
            if (pmDraftFocused && e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    if (pmTargetActor > 0 && !string.IsNullOrEmpty(pmDraft))
                    {
                        SendPrivateMessage(pmTargetActor, pmDraft);
                        pmDraft = "";
                    }
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Backspace && pmDraft.Length > 0)
                {
                    pmDraft = pmDraft.Substring(0, pmDraft.Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    pmDraftFocused = false;
                    e.Use();
                }
                else if (e.character != 0 && !char.IsControl(e.character) && pmDraft.Length < 120)
                {
                    pmDraft += e.character;
                    e.Use();
                }
            }
            ry += inputH + 6f;

            string sendLabel = pmTargetActor > 0 ? ("SEND TO " + chatWith.ToUpperInvariant()) : "SELECT SOMEONE FIRST";
            if (GUI.Button(new Rect(rightX, ry, rightW, sendH), new GUIContent(sendLabel), buttonStyle))
            {
                if (pmTargetActor > 0 && !string.IsNullOrEmpty(pmDraft))
                {
                    SendPrivateMessage(pmTargetActor, pmDraft);
                    pmDraft = "";
                }
            }
        }

    }
}