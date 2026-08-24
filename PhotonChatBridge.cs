using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ZexQoLMenu
{
    /// <summary>
    /// Optional Photon Chat transport for cross-room chat.
    /// Uses reflection so the mod still compiles if PhotonChat.dll is absent.
    /// Runtime: put PhotonChat.dll in BepInEx/plugins or game Managed folder.
    /// Get a free Chat AppId: https://dashboard.photonengine.com/ → create Chat app.
    /// </summary>
    public partial class Plugin
    {
        private const string PhotonChatChannel = "zex-global";

        private ConfigEntry<string> configPhotonChatAppId;
        private ConfigEntry<string> configChatTransport; // "Off" | "Http" | "PhotonChat"

        private string chatTransport = "Off";
        private string photonChatAppId = "";

        private object photonChatClient; // Photon.Chat.ChatClient
        private object photonChatListener; // dynamic listener proxy
        private Assembly photonChatAssembly;
        private Type chatClientType;
        private bool photonChatConnecting;
        private bool photonChatConnected;
        private string photonChatStatus = "idle";
        private float photonChatNextService;

        private void BindPhotonChatConfig()
        {
            configChatTransport = Config.Bind(
                "GlobalChat",
                "Transport",
                "Off",
                "Off | Http (relay) | PhotonChat (cross-room via Photon Chat AppId)");

            configPhotonChatAppId = Config.Bind(
                "GlobalChat",
                "PhotonChatAppId",
                "",
                "Photon Chat AppId from dashboard (not the same as Realtime/PUN id unless you linked them).");

            chatTransport = (configChatTransport.Value ?? "Off").Trim();
            photonChatAppId = (configPhotonChatAppId.Value ?? "").Trim();
        }

        private void TickPhotonChat()
        {
            if (!string.Equals(chatTransport, "PhotonChat", StringComparison.OrdinalIgnoreCase))
                return;

            if (photonChatClient == null)
            {
                if (!photonChatConnecting && !string.IsNullOrEmpty(photonChatAppId))
                    TryStartPhotonChat();
                return;
            }

            // chatClient.Service() must be called regularly
            if (Time.unscaledTime >= photonChatNextService)
            {
                photonChatNextService = Time.unscaledTime + 0.05f;
                try
                {
                    chatClientType.GetMethod("Service", Type.EmptyTypes)?.Invoke(photonChatClient, null);
                }
                catch (Exception ex)
                {
                    photonChatStatus = "service err";
                    Logger.LogWarning("PhotonChat Service: " + ex.Message);
                }
            }
        }

        private bool TryLoadPhotonChatAssembly()
        {
            if (photonChatAssembly != null) return true;

            string[] candidates = new string[]
            {
                System.IO.Path.Combine(Application.dataPath, "Managed", "PhotonChat.dll"),
                System.IO.Path.Combine(Application.dataPath, "Managed", "Photon.Chat.dll"),
                System.IO.Path.Combine(Paths.PluginPath ?? "", "PhotonChat.dll"),
                System.IO.Path.Combine(Paths.PluginPath ?? "", "Photon.Chat.dll"),
                System.IO.Path.Combine(Paths.BepInExRootPath ?? "", "plugins", "PhotonChat.dll"),
            };

            foreach (string path in candidates)
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;
                try
                {
                    photonChatAssembly = Assembly.LoadFrom(path);
                    Logger.LogInfo("Loaded Photon Chat assembly: " + path);
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Load PhotonChat failed: " + path + " · " + ex.Message);
                }
            }

            if (photonChatAssembly == null)
            {
                // Already loaded?
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (asm.GetType("Photon.Chat.ChatClient") != null)
                        {
                            photonChatAssembly = asm;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (photonChatAssembly == null)
            {
                photonChatStatus = "PhotonChat.dll missing";
                return false;
            }

            chatClientType = photonChatAssembly.GetType("Photon.Chat.ChatClient");
            if (chatClientType == null)
            {
                photonChatStatus = "ChatClient type missing";
                return false;
            }
            return true;
        }

        private void TryStartPhotonChat()
        {
            if (photonChatConnecting || photonChatClient != null) return;
            photonChatConnecting = true;
            photonChatStatus = "connecting…";

            try
            {
                if (!TryLoadPhotonChatAssembly())
                {
                    photonChatConnecting = false;
                    ShowToast("PhotonChat.dll not found — use Http transport or add DLL");
                    return;
                }

                if (string.IsNullOrEmpty(photonChatAppId))
                {
                    photonChatStatus = "no AppId";
                    photonChatConnecting = false;
                    ShowToast("Set GlobalChat.PhotonChatAppId in config");
                    return;
                }

                // Full Chat client needs IChatClientListener (compile-time PhotonChat ref).
                // This test build only detects the DLL / AppId; use Http relay for real chat until
                // a PhotonChat-linked build is made.
                photonChatStatus = "DLL ok — need linked build";
                photonChatConnecting = false;
                Logger.LogWarning(
                    "Photon Chat: assembly found but listener requires PhotonChat reference. " +
                    "Use Transport=Http for now, or compile against PhotonChat.dll.");
                ShowToast("PhotonChat DLL found — use Http until linked build");
            }
            catch (Exception ex)
            {
                photonChatStatus = "start err";
                photonChatConnecting = false;
                Logger.LogWarning("TryStartPhotonChat: " + ex);
                ShowToast("Photon Chat start failed");
            }
        }

        private void OnPhotonChatConnected()
        {
            photonChatConnected = true;
            photonChatConnecting = false;
            photonChatStatus = "connected";
            try
            {
                // Subscribe(string[] channels)
                MethodInfo sub = chatClientType.GetMethod("Subscribe", new[] { typeof(string[]) });
                if (sub != null)
                    sub.Invoke(photonChatClient, new object[] { new string[] { PhotonChatChannel } });
                photonChatStatus = "subscribed " + PhotonChatChannel;
                ShowToast("Photon Chat connected");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("PhotonChat subscribe: " + ex.Message);
            }
        }

        private void OnPhotonChatMessages(string channel, string[] senders, object[] messages)
        {
            if (senders == null || messages == null) return;
            int n = Math.Min(senders.Length, messages.Length);
            for (int i = 0; i < n; i++)
            {
                string user = senders[i] ?? "?";
                string msg = messages[i] != null ? messages[i].ToString() : "";
                if (string.IsNullOrEmpty(msg)) continue;
                string line = "[G] " + user + ": " + msg;
                lock (globalChatLog)
                {
                    globalChatLog.Add(line);
                    while (globalChatLog.Count > GlobalChatLogMax)
                        globalChatLog.RemoveAt(0);
                }
            }
            photonChatStatus = "msg +" + n;
        }

        private bool SendPhotonChatMessage(string message)
        {
            if (photonChatClient == null || !photonChatConnected)
            {
                ShowToast("Photon Chat not connected");
                return false;
            }
            try
            {
                MethodInfo pub = chatClientType.GetMethod(
                    "PublishMessage",
                    new[] { typeof(string), typeof(object) });
                if (pub == null)
                {
                    foreach (var m in chatClientType.GetMethods())
                    {
                        if (m.Name == "PublishMessage" && m.GetParameters().Length >= 2)
                        {
                            pub = m;
                            break;
                        }
                    }
                }
                if (pub == null) return false;
                pub.Invoke(photonChatClient, new object[] { PhotonChatChannel, message });
                photonChatStatus = "published";
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("PublishMessage: " + ex.Message);
                return false;
            }
        }

        private void DisconnectPhotonChat()
        {
            try
            {
                if (photonChatClient != null)
                    chatClientType?.GetMethod("Disconnect", Type.EmptyTypes)?.Invoke(photonChatClient, null);
            }
            catch { }
            photonChatClient = null;
            photonChatConnected = false;
            photonChatConnecting = false;
            photonChatStatus = "disconnected";
        }


        private static Type FindTypeAnywhere(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(fullName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
