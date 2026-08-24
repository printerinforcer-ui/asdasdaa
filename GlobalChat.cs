using System;
using BepInEx.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ZexQoLMenu
{
    /// <summary>
    /// Cross-server global chat via a tiny HTTP relay.
    /// API:
    ///   GET  {base}/chat?after={id}  → {"messages":[{"id":1,"user":"a","msg":"hi","ts":0}]}
    ///   POST {base}/chat             → body {"user":"a","msg":"hi","uid":"..."}  → {"ok":true,"id":1}
    /// </summary>
    public partial class Plugin
    {
        private ConfigEntry<bool> configGlobalChatEnabled;
        private ConfigEntry<string> configGlobalChatUrl;
        private ConfigEntry<float> configGlobalChatPollSeconds;
        private ConfigEntry<string> configGlobalChatName;

        private bool globalChatEnabled;
        private string globalChatUrl = "http://127.0.0.1:8765";
        private float globalChatPollSeconds = 2.5f;
        private string globalChatName = "";

        private readonly List<string> globalChatLog = new List<string>();
        private const int GlobalChatLogMax = 60;
        private string globalChatDraft = "";
        private bool globalChatDraftFocused;
        private long globalChatLastId;
        private float globalChatNextPoll;
        private bool globalChatBusy;
        private string globalChatStatus = "off";
        private string globalChatUid;

        private int chatTab; // 0 = room PM, 1 = global

        private void BindGlobalChatConfig()
        {
            configGlobalChatEnabled = Config.Bind(
                "GlobalChat",
                "Enabled",
                false,
                "Opt-in cross-server chat via HTTP relay. Requires a relay URL.");

            configGlobalChatUrl = Config.Bind(
                "GlobalChat",
                "RelayUrl",
                "http://127.0.0.1:8765",
                "Base URL of the Zex chat relay (no trailing slash).");

            configGlobalChatPollSeconds = Config.Bind(
                "GlobalChat",
                "PollSeconds",
                2.5f,
                "How often to fetch new global messages (1–15).");

            configGlobalChatName = Config.Bind(
                "GlobalChat",
                "DisplayName",
                "",
                "Name shown in global chat. Empty = Photon nick / Steam-style fallback.");

            globalChatEnabled = configGlobalChatEnabled.Value;
            globalChatUrl = (configGlobalChatUrl.Value ?? "").Trim().TrimEnd('/');
            globalChatPollSeconds = Mathf.Clamp(configGlobalChatPollSeconds.Value, 1f, 15f);
            globalChatName = configGlobalChatName.Value ?? "";

            if (string.IsNullOrEmpty(globalChatUid))
                globalChatUid = Guid.NewGuid().ToString("N").Substring(0, 12);

            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            BindPhotonChatConfig();
        }

        private void TickGlobalChat()
        {
            // Photon Chat path (cross-room)
            if (string.Equals(chatTransport, "PhotonChat", StringComparison.OrdinalIgnoreCase))
            {
                if (globalChatEnabled)
                    TickPhotonChat();
                return;
            }

            // HTTP relay path
            if (!string.Equals(chatTransport, "Http", StringComparison.OrdinalIgnoreCase))
                return;
            if (!globalChatEnabled) return;
            if (string.IsNullOrEmpty(globalChatUrl)) return;
            if (Time.unscaledTime < globalChatNextPoll) return;
            if (globalChatBusy) return;

            globalChatNextPoll = Time.unscaledTime + globalChatPollSeconds;
            ThreadPool.QueueUserWorkItem(_ => PollGlobalChatWorker());
        }

        private string ResolveGlobalChatName()
        {
            if (!string.IsNullOrEmpty(globalChatName))
                return SanitizeChatToken(globalChatName, 24);

            try
            {
                if (Photon.Pun.PhotonNetwork.LocalPlayer != null &&
                    !string.IsNullOrEmpty(Photon.Pun.PhotonNetwork.LocalPlayer.NickName))
                    return SanitizeChatToken(Photon.Pun.PhotonNetwork.LocalPlayer.NickName, 24);
            }
            catch { }

            return "Zex_" + globalChatUid.Substring(0, 6);
        }

        private static string SanitizeChatToken(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "anon";
            var sb = new StringBuilder(Math.Min(s.Length, maxLen));
            for (int i = 0; i < s.Length && sb.Length < maxLen; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\' || c < 32) continue;
                sb.Append(c);
            }
            string r = sb.ToString().Trim();
            return string.IsNullOrEmpty(r) ? "anon" : r;
        }

        private void PollGlobalChatWorker()
        {
            if (globalChatBusy) return;
            globalChatBusy = true;
            try
            {
                string url = globalChatUrl + "/chat?after=" + globalChatLastId;
                string json = HttpGet(url, 4000);
                if (string.IsNullOrEmpty(json))
                {
                    globalChatStatus = "poll empty/fail";
                    return;
                }

                // Very small parser: find "id","user","msg" objects
                int added = 0;
                int pos = 0;
                while (pos < json.Length)
                {
                    int idIdx = json.IndexOf("\"id\"", pos, StringComparison.Ordinal);
                    if (idIdx < 0) break;
                    long id = ExtractLongAfter(json, idIdx);
                    int userIdx = json.IndexOf("\"user\"", idIdx, StringComparison.Ordinal);
                    int msgIdx = json.IndexOf("\"msg\"", idIdx, StringComparison.Ordinal);
                    if (userIdx < 0 || msgIdx < 0) { pos = idIdx + 4; continue; }

                    string user = ExtractJsonString(json, userIdx);
                    string msg = ExtractJsonString(json, msgIdx);
                    pos = Math.Max(msgIdx, idIdx) + 4;

                    if (id <= globalChatLastId) continue;
                    if (string.IsNullOrEmpty(msg)) continue;

                    globalChatLastId = id;
                    string line = "[G] " + (user ?? "?") + ": " + msg;
                    lock (globalChatLog)
                    {
                        globalChatLog.Add(line);
                        while (globalChatLog.Count > GlobalChatLogMax)
                            globalChatLog.RemoveAt(0);
                    }
                    added++;
                }

                globalChatStatus = added > 0 ? ("+" + added + " · id=" + globalChatLastId) : ("ok · id=" + globalChatLastId);
            }
            catch (Exception ex)
            {
                globalChatStatus = "err: " + Trunc(ex.Message, 40);
            }
            finally
            {
                globalChatBusy = false;
            }
        }

        private void SendGlobalChatMessage(string message)
        {
            if (!globalChatEnabled)
            {
                ShowToast("Global chat is OFF (TESTING / config)");
                return;
            }
            message = (message ?? "").Trim();
            if (message.Length == 0) return;
            if (message.Length > 200) message = message.Substring(0, 200);

            string user = ResolveGlobalChatName();

            if (string.Equals(chatTransport, "PhotonChat", StringComparison.OrdinalIgnoreCase))
            {
                lock (globalChatLog)
                {
                    globalChatLog.Add("[G] " + user + ": " + message);
                    while (globalChatLog.Count > GlobalChatLogMax)
                        globalChatLog.RemoveAt(0);
                }
                if (!SendPhotonChatMessage(message))
                    lock (globalChatLog)
                        globalChatLog.Add("[G] !! PhotonChat send failed (" + photonChatStatus + ")");
                return;
            }

            if (!string.Equals(chatTransport, "Http", StringComparison.OrdinalIgnoreCase))
            {
                ShowToast("Set GlobalChat.Transport to Http or PhotonChat");
                return;
            }
            if (string.IsNullOrEmpty(globalChatUrl))
            {
                ShowToast("Set GlobalChat RelayUrl in config");
                return;
            }
            string body = "{\"user\":\"" + JsonEscape(user) +
                          "\",\"msg\":\"" + JsonEscape(message) +
                          "\",\"uid\":\"" + JsonEscape(globalChatUid) + "\"}";

            // optimistic local echo
            lock (globalChatLog)
            {
                globalChatLog.Add("[G] " + user + ": " + message + "  (sending…)");
                while (globalChatLog.Count > GlobalChatLogMax)
                    globalChatLog.RemoveAt(0);
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string resp = HttpPost(globalChatUrl + "/chat", body, 5000);
                    if (!string.IsNullOrEmpty(resp) && resp.IndexOf("\"ok\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        globalChatStatus = "sent";
                        // Pull soon so we get canonical id / drop duplicate echo noise
                        globalChatNextPoll = 0f;
                    }
                    else
                    {
                        globalChatStatus = "send fail";
                        lock (globalChatLog)
                            globalChatLog.Add("[G] !! send failed");
                    }
                }
                catch (Exception ex)
                {
                    globalChatStatus = "send err";
                    lock (globalChatLog)
                        globalChatLog.Add("[G] !! " + Trunc(ex.Message, 48));
                }
            });
        }

        private void DrawGlobalChatSection(float x, float y, float width, float maxBottom, ref float yOut)
        {
            GUI.Label(new Rect(x, y, width, 20f),
                new GUIContent("GLOBAL  ·  " + chatTransport + "  ·  " + (globalChatEnabled ? "ON" : "OFF") + "  ·  " + (string.Equals(chatTransport, "PhotonChat", StringComparison.OrdinalIgnoreCase) ? photonChatStatus : globalChatStatus)),
                headerStyle);
            y += 22f;

            if (GUI.Button(new Rect(x, y, width * 0.32f, 24f),
                new GUIContent(globalChatEnabled ? "ON" : "OFF"), buttonStyle))
            {
                globalChatEnabled = !globalChatEnabled;
                if (configGlobalChatEnabled != null)
                    configGlobalChatEnabled.Value = globalChatEnabled;
                globalChatStatus = globalChatEnabled ? "enabled" : "off";
                if (globalChatEnabled && string.Equals(chatTransport, "PhotonChat", StringComparison.OrdinalIgnoreCase))
                    TryStartPhotonChat();
                if (!globalChatEnabled && string.Equals(chatTransport, "PhotonChat", StringComparison.OrdinalIgnoreCase))
                    DisconnectPhotonChat();
            }
            if (GUI.Button(new Rect(x + width * 0.34f, y, width * 0.32f, 24f),
                new GUIContent(chatTransport), buttonStyle))
            {
                // cycle Off → Http → PhotonChat
                if (chatTransport == "Off") chatTransport = "Http";
                else if (string.Equals(chatTransport, "Http", StringComparison.OrdinalIgnoreCase)) chatTransport = "PhotonChat";
                else chatTransport = "Off";
                if (configChatTransport != null) configChatTransport.Value = chatTransport;
                if (string.Equals(chatTransport, "PhotonChat", StringComparison.OrdinalIgnoreCase) && globalChatEnabled)
                    TryStartPhotonChat();
                if (!string.Equals(chatTransport, "PhotonChat", StringComparison.OrdinalIgnoreCase))
                    DisconnectPhotonChat();
            }
            if (GUI.Button(new Rect(x + width * 0.68f, y, width * 0.32f, 24f),
                new GUIContent("POLL"), buttonStyle))
            {
                globalChatNextPoll = 0f;
                if (string.Equals(chatTransport, "PhotonChat", StringComparison.OrdinalIgnoreCase))
                    TryStartPhotonChat();
            }
            y += 28f;

            float logH = Mathf.Min(100f, Mathf.Max(60f, maxBottom - y - 60f));
            Rect logRect = new Rect(x, y, width, logH);
            GUI.Box(logRect, "");
            int maxLines = Mathf.Max(1, Mathf.FloorToInt((logH - 4f) / 16f));
            List<string> snap;
            lock (globalChatLog)
                snap = new List<string>(globalChatLog);
            int start = Mathf.Max(0, snap.Count - maxLines);
            for (int i = start; i < snap.Count; i++)
            {
                float ly = logRect.y + 2f + (i - start) * 16f;
                GUI.Label(new Rect(logRect.x + 4f, ly, logRect.width - 8f, 16f), snap[i], smallStyle);
            }
            y += logH + 6f;

            // draft
            Rect field = new Rect(x, y, width, 22f);
            GUI.Box(field, "");
            string shown = string.IsNullOrEmpty(globalChatDraft)
                ? (globalChatDraftFocused ? "|" : "global message…")
                : globalChatDraft + (globalChatDraftFocused ? "|" : "");
            GUI.Label(new Rect(field.x + 4f, field.y + 2f, field.width - 8f, 18f), shown, labelStyle);
            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && field.Contains(e.mousePosition))
            {
                globalChatDraftFocused = true;
                pmDraftFocused = false;
                e.Use();
            }
            if (globalChatDraftFocused && e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    if (!string.IsNullOrEmpty(globalChatDraft))
                    {
                        SendGlobalChatMessage(globalChatDraft);
                        globalChatDraft = "";
                    }
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Backspace && globalChatDraft.Length > 0)
                {
                    globalChatDraft = globalChatDraft.Substring(0, globalChatDraft.Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    globalChatDraftFocused = false;
                    e.Use();
                }
                else if (e.character != 0 && !char.IsControl(e.character) && globalChatDraft.Length < 200)
                {
                    globalChatDraft += e.character;
                    e.Use();
                }
            }
            y += 26f;

            if (GUI.Button(new Rect(x, y, width, 26f), new GUIContent("SEND GLOBAL"), buttonStyle))
            {
                if (!string.IsNullOrEmpty(globalChatDraft))
                {
                    SendGlobalChatMessage(globalChatDraft);
                    globalChatDraft = "";
                }
            }
            y += 30f;
            yOut = y;
        }

        // ---- minimal HTTP (no UnityWebRequest module required) ----

        private static string HttpGet(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "ZexQoL/GlobalChat";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static string HttpPost(string url, string jsonBody, int timeoutMs)
        {
            byte[] data = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.ContentType = "application/json";
            req.UserAgent = "ZexQoL/GlobalChat";
            req.ContentLength = data.Length;
            using (var s = req.GetRequestStream())
                s.Write(data, 0, data.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
        }

        private static string ExtractJsonString(string json, int keyIdx)
        {
            int colon = json.IndexOf(':', keyIdx);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return "";
            int q2 = q1 + 1;
            var sb = new StringBuilder();
            while (q2 < json.Length)
            {
                char c = json[q2];
                if (c == '\\' && q2 + 1 < json.Length)
                {
                    sb.Append(json[q2 + 1]);
                    q2 += 2;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
                q2++;
            }
            return sb.ToString();
        }

        private static long ExtractLongAfter(string json, int keyIdx)
        {
            int colon = json.IndexOf(':', keyIdx);
            if (colon < 0) return 0;
            int i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            long v = 0;
            bool any = false;
            while (i < json.Length && json[i] >= '0' && json[i] <= '9')
            {
                any = true;
                v = v * 10 + (json[i] - '0');
                i++;
            }
            return any ? v : 0;
        }

        private static string Trunc(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n);
        }
    }
}
