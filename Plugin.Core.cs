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
    [BepInPlugin("Zex_QOL_Menu", "˚ʚ♡ɞ˚ Zex's QOL Menu", "1.0.0")]
    public partial class Plugin : BaseUnityPlugin, IConnectionCallbacks, ILobbyCallbacks, IMatchmakingCallbacks, IInRoomCallbacks, IOnEventCallback
    {
        // ============================================================
        // MASTER UI
        // ============================================================
        private bool menuVisible = true;
        private int tab = 0; // 0 ESP … 12 MODS, 13 CHAT, 14 NOTIFICATIONS
        private Rect menuRect = new Rect(90f, 45f, 1000f, 780f);

        // ============================================================
        // GENES (local kobold character stats — reflection-based)
        // ============================================================
        private class GeneFieldDef
        {
            public string Label;
            public string[] FieldNames; // tried in order
            public float DefaultValue;
            public float Min;
            public float Max;

            public GeneFieldDef(string label, string[] names, float def, float min, float max)
            {
                Label = label;
                FieldNames = names;
                DefaultValue = def;
                Min = min;
                Max = max;
            }
        }

        private readonly GeneFieldDef[] geneFieldDefs =
        {
            // Official KoboldGenes field names (github.com/naelstrof/KoboldKare)
            new GeneFieldDef("Energy",    new[] { "maxEnergy" }, 5f, 0.1f, 100f),
            new GeneFieldDef("Belly",    new[] { "bellySize" }, 20f, 0f, 200f),
            new GeneFieldDef("Meta",     new[] { "metabolizeCapacitySize" }, 20f, 0f, 200f),
            new GeneFieldDef("Grab",     new[] { "grabCount" }, 1f, 0f, 20f),
            new GeneFieldDef("Size",     new[] { "baseSize" }, 20f, 0.1f, 200f),
            new GeneFieldDef("Tits",     new[] { "breastSize" }, 0f, 0f, 200f),
            new GeneFieldDef("Fat",      new[] { "fatSize" }, 0f, 0f, 200f),
            new GeneFieldDef("Psize",    new[] { "dickSize" }, 10f, 0f, 200f),
            new GeneFieldDef("Balls",    new[] { "ballSize" }, 10f, 0f, 200f),
            new GeneFieldDef("Hue",      new[] { "hue" }, 0f, 0f, 255f),
            new GeneFieldDef("Bright",   new[] { "brightness" }, 128f, 0f, 255f),
            new GeneFieldDef("Satur",    new[] { "saturation" }, 128f, 0f, 255f),
            new GeneFieldDef("Cloth Hue", new[] { "clothingHue" }, 0f, 0f, 255f),
            new GeneFieldDef("Thick",    new[] { "dickThickness" }, 0.5f, 0f, 3f),
        };

        private readonly float[] geneCurrent = new float[14];
        private readonly float[] geneToSet = new float[14];
        private readonly string[] geneToSetText = new string[14];
        private int geneEditIndex = -1;
        private string geneStatus = "Not loaded";
        private float geneStatusUntil;
        private Type koboldType;
        private Type koboldGenesType;
        private MethodInfo getGenesMethod;
        private MethodInfo setGenesMethod;
        private bool geneTypesResolved;
        private Vector2 genesScroll = Vector2.zero;

        // Dick / species / thickness
        private readonly List<string> dickOptions = new List<string>();
        private int selectedDickIndex;
        private Vector2 dickScroll = Vector2.zero;
        private float cockThickness = 0.7f;
        private int speciesId;
        private string speciesName = "";
        private string speciesEditText = "0";
        private bool speciesEditing;

        // Modded character / avatar list (PlayerDatabase / PrefabDatabase)
        private readonly List<string> characterOptions = new List<string>();
        private int selectedCharacterIndex;
        private Vector2 characterScroll = Vector2.zero;
        private string characterFilter = "";
        private bool characterFilterEditing;

        // Equipment / clothing (EquipmentDatabase + KoboldInventory)
        private readonly List<string> equipNames = new List<string>(); // currently worn
        private readonly List<string> equipCatalog = new List<string>(); // all from EquipmentDatabase
        private int selectedCatalogEquip = -1;
        private int selectedWornEquip = -1;
        private Vector2 equipScroll = Vector2.zero;
        private Vector2 equipCatalogScroll = Vector2.zero;
        private string equipStatus = "";
        private string equipFilter = "";
        private bool equipFilterEditing;

        // Presets — full (char+genes+clothes) is primary; old stats/equip kept for compatibility
        private ConfigEntry<string> configStatsPresets;
        private ConfigEntry<string> configEquipPresets;
        private ConfigEntry<string> configFullPresets;
        private readonly List<string> statsPresetNames = new List<string>();
        private readonly Dictionary<string, string> statsPresetData = new Dictionary<string, string>();
        private readonly List<string> equipPresetNames = new List<string>();
        private readonly Dictionary<string, string> equipPresetData = new Dictionary<string, string>();
        private readonly List<string> fullPresetNames = new List<string>();
        private readonly Dictionary<string, string> fullPresetData = new Dictionary<string, string>();
        private string newPresetName = "MyPreset";
        private bool presetNameEditing;
        private Vector2 presetScroll = Vector2.zero;
        private int selectedStatsPreset = -1;
        private int selectedEquipPreset = -1;
        private int selectedFullPreset = -1;
        private Coroutine applyFullPresetCoroutine;

        // Presets pop-out window (avoids crushing the genes panel layout)
        private bool presetsPopupVisible;
        private Rect presetsPopupRect = new Rect(420f, 120f, 360f, 480f);
        private float equipStatusUntil;

        // Waypoints pop-out (opened from Teleport tab)
        private bool waypointsPopupVisible;
        private Rect waypointsPopupRect = new Rect(420f, 160f, 340f, 420f);

        // Modern menu shell inspired by the supplied Neverlose-style reference.
        private GUIStyle windowStyle;
        private GUIStyle sidebarStyle;
        private GUIStyle sidebarSelectedStyle;
        private GUIStyle topBarStyle;
        private GUIStyle cardStyle;
        private GUIStyle sectionStyle;
        private GUIStyle valueStyle;
        private GUIStyle accentLabelStyle;
        private GUIStyle modernButtonStyle;
        private GUIStyle modernSelectedButtonStyle;
        private GUIStyle modernSmallStyle;
        private Texture2D uiWindowTexture;
        private Texture2D uiSidebarTexture;
        private Texture2D uiCardTexture;
        private Texture2D uiButtonTexture;
        private Texture2D uiButtonHoverTexture;
        private Texture2D uiButtonActiveTexture;
        private Texture2D uiAccentTexture;
        private Texture2D uiMutedTexture;
        private bool sillysNameEditing;
        private string sillysName = "Someone";

        private GUIStyle labelStyle;
        private GUIStyle buttonStyle;
        private GUIStyle selectedButtonStyle;
        private GUIStyle headerStyle;
        private GUIStyle smallStyle;
        private GUIStyle overlayHeaderStyle;
        private GUIStyle overlayPlayerStyle;
        private GUIStyle overlayInfoStyle;
        private GUIStyle overlayRoleStyle;
        private GUIStyle overlayServerStyle;
        private bool stylesCreated;

        // Background from the ESP mod.
        private Texture2D menuBackground;
        private Material backgroundMaterial;



        private const float MaxMoneyValue = 999999f;
        private const int MaxStarsValue = 999999;
        private string rewardStatus = "";
        private float rewardStatusUntil;

        // Animated sprite-sheet background
        private const int BackgroundColumns = 5;
        private const int BackgroundRows = 18;
        private const int BackgroundFrameCount = BackgroundColumns * BackgroundRows;
        private float BackgroundFramesPerSecond = 24f;

        // Menu chrome (used by UI.cs Settings / background tint)
        private float backgroundHue = 0f;
        private float backgroundOpacity = 1f;
        private bool menuColorGreyscale = false;
        private bool menuHueCycling = true;
        private float menuHueCycleSeconds = 12f;
        private float uiScale = 1f;
        private const float UiScaleMin = 0.55f;
        private const float UiScaleMax = 1.50f;
        private float uiScaleSmoothed = 1f;
        private bool showHudWaypoints = true;
        private string lastJoinedRoomName = "";
        private const float MenuHueCycleSecondsMin = 3f;
        private const float MenuHueCycleSecondsMax = 60f;

        // ===========================================================
        // ESP
        // ============================================================
        private bool showNames = true;
        private bool showDistance = true;
        private bool showActorID = false;
        private bool hideSelf = true;
        private bool tracersEnabled = false;
        private bool visibilityCheck = false;
        private bool offscreenArrows = true;
        private float maxDistance = 240f;
        private float tracerThickness = 1.0f;
        private int tracerOrigin = 0;
        private bool tracerDistanceFade = true;
        private bool scaleNames = true;
        private const float NameHeight = 1.7f;
        private const float MinNameScale = 0.70f;
        private const float MaxNameScale = 1.15f;
        private const float OffscreenArrowSize = 18f;
        private const float OffscreenArrowMargin = 35f;
        private readonly Color playerColor = new Color(1f, .2f, 0f);
        private readonly Color hiddenColor = new Color(.55f, .55f, .60f);
        private GUIStyle espStyle;
        private Material tracerMaterial;
        // Track current esp font size because GUIStyle.fontSize has no getter.
        private int espFontSize = 14;

        // ============================================================
        // QOL / STATUS / SPECTATE / PLAYER COLORS
        // ============================================================
        private float fpsValue;
        private float objectCountTimer;
        private int sceneObjectCount;
        private readonly HashSet<int> friendActorIds = new HashSet<int>();

        private bool spectating;
        private Transform spectateTarget;
        private Vector3 savedCameraPosition;
        private static Plugin Instance;
        private GameObject cachedLocalPlayer;
        private float nextPlayerCacheRefresh;
        private const float PlayerCacheRefreshInterval = 1f;
        private Harmony spectateHarmony;
        private Quaternion savedCameraRotation;
        private bool cameraStateSaved;
        private float spectateCameraHeight = 0.85f; // Adjustable camera height (0.0 - 5.0 m)
        private float spectateCameraDistance = 3.25f; // Adjustable camera distance (1.0 - 10.0 m)
        private int spectateActorId = -1; // Actor ID of the player being spectated
        private float spectateCameraRotation; // Adjustable rotation around player (0 - 360 degrees)

        private const float SpectateMouseSensitivity = 3f;
        private const float SpectateMinPitch = -75f;
        private const float SpectateMaxPitch = 75f;

        private int normalColorIndex;
        private int selectedColorIndex = 1;
        private int friendColorIndex = 2;

        private readonly Color[] espColorOptions =
        {
            new Color(1f, .2f, 0f),
            new Color(.75f, .25f, 1f),
            new Color(.2f, 1f, .35f),
            new Color(1f, .85f, .15f),
            new Color(.2f, .75f, 1f),
            new Color(1f, .35f, 0.65f),
            Color.white
        };

        private readonly string[] espColorNames =
        {
            "RED", "PURPLE", "GREEN", "YELLOW", "CYAN", "PINK", "WHITE"
        };

        // ============================================================
        // SPAWNER
        // ============================================================
        private class PrefabEntry
        {
            public string Name;
            public GameObject Prefab;
            public PrefabEntry(string name, GameObject prefab) { Name = name; Prefab = prefab; }
        }

        private readonly List<PrefabEntry> prefabList = new List<PrefabEntry>();
        private readonly List<PrefabEntry> filteredPrefabList = new List<PrefabEntry>();
        private int selectedPrefabIndex = -1;
        private int prefabListOffset;
        private string prefabStatus = "NOT SCANNED";
        private string searchText = "";
        private bool searchFocused;
        private int amount = 1;
        private float spawnDistance = 2f;
        private string spawnStatus = "WAITING";
        private readonly List<GameObject> spawnedObjects = new List<GameObject>();
        private Type preparePoolType;
        private FieldInfo preparePoolInstanceField;
        private FieldInfo dynamicPrefabsField;
        private readonly HashSet<string> favoritePrefabNames = new HashSet<string>();

        // ============================================================
        // TELEPORT
        // ============================================================
        private Vector2 playerScroll = Vector2.zero;
        private int selectedActorId = -1;
        private Player selectedPlayer;
        private float behindDistance = 3f;
        private float frontDistance = 3f;
        private float aboveDistance = 4f;
        private bool originCaptured;
        private Vector3 originPosition = Vector3.zero;

        private readonly Dictionary<string, Vector3> savedWaypoints =
            new Dictionary<string, Vector3>();
        private string newWaypointName = "";
        private bool waypointNameFocused;
        private Vector2 waypointListScroll = Vector2.zero;
        private int waypointAutoIndex = 1;

        // Soft teleport (lerp)
        private bool softTeleportEnabled = true;
        private float softTeleportDuration = 0.35f;
        private Coroutine softTeleportCoroutine;

        // Flying noclip — CharCon-style: Kobold.body.velocity + OrbitCamera look
        private bool flyingNoclipActive;
        private float flySpeed = 25f;
        private Vector3 flySavedPosition;
        private FieldInfo koboldBodyField;
        private int lastHotkeyFrame = -1;
        private Rigidbody flyCachedBody;
        private Behaviour flyCachedKoboldController;
        private bool flyKoboldControllerWasEnabled = true;
        private string flyDebugStatus = "";
        private Vector3 flyPendingVelocity;
        private bool flyHasPendingVelocity;

        // TESTING: ownership + world ping marker
        private string ownershipStatus = "";
        private float ownershipStatusUntil;
        private bool pingMarkActive;
        private Vector3 pingMarkWorld;
        private float pingMarkUntil;
        private const float PingMarkDuration = 8f;

        // Party ping (Zex clients only — Photon event 175)
        private bool partyPingActive;
        private Vector3 partyPingWorld;
        private float partyPingUntil;
        private string partyPingFrom = "";
        private const float PartyPingDuration = 10f;

        // Host freeze (best-effort: keep shoving target back to locked pos)
        private readonly Dictionary<int, Vector3> frozenPlayerPositions = new Dictionary<int, Vector3>();
        private readonly HashSet<int> frozenActorIds = new HashSet<int>();

        private readonly Dictionary<string, string> playerNotes = new Dictionary<string, string>();
        private string notesInput = "";
        private bool notesInputFocused;

        // Global search + collapsible sections
        private readonly Dictionary<string, bool> sectionCollapsed = new Dictionary<string, bool>();

        // Keybinds — CharCon-style: UnityInput.Current for KeyCode / held strings;
        // KeyboardShortcut only for multi-key (menu), evaluated via UnityInput too.
        private ConfigEntry<KeyCode> noclipToggleKey;
        private ConfigEntry<KeyCode> waypointQuickSaveKey;
        private ConfigEntry<KeyCode> flySpeedUpKey;
        private ConfigEntry<KeyCode> flySpeedDownKey;
        private ConfigEntry<KeyCode> spectateNextKey;
        private ConfigEntry<KeyCode> spectatePrevKey;
        private ConfigEntry<KeyCode> cleanScreenshotKey;
        private ConfigEntry<KeyCode> cheatSheetKey;
        private ConfigEntry<int> configScreenshotSuperSize;
        private ConfigEntry<float> configNameplateScale;
        private ConfigEntry<float> configNameplateOpacity;
        private bool waitingForKeyRebind;
        private string rebindTarget; // menu|noclip|waypoint|flyUp|flyDown|specNext|specPrev|stopSpec|freeMouse

        // ============================================================
        // PLAYER OVERLAY
        // ============================================================
        private Rect playerOverlayRect = new Rect(10f, 10f, 300f, 220f);
        private Vector2 playerOverlayScroll = Vector2.zero;
        private bool showPlayerOverlay = true;

        // ============================================================
        // PLAYER CONTEXT MENU / RADAR
        // ============================================================
        private Player contextPlayer;
        private bool playerContextMenuVisible;
        private Vector2 playerContextMenuPosition;
        private bool targetLocked;

        private int followPlayerActorId = -1;
        private float followDistance = 3f;
        private float followHeight = 0.5f;

        private Rect playerRadarRect = new Rect(20f, 250f, 230f, 230f);
        private bool showPlayerRadar = true;
        private bool radarRotateWithCamera = true;
        private bool radarShowNames = true;
        private bool radarShowDistance = true;

        private float radarRange = 30f;
        private float radarOpacity = 0.82f;
        private bool friendsOnlyMode = false;
        private int playerListSortMode = 0; // 0 name, 1 distance, 2 actor, 3 host-first
        private int serverModFilter = 0; // 0 all, 1 modded only, 2 vanilla only
        private bool sidebarCollapsed = false;
        private float sidebarAnimW = 190f;
        private int lastPmFromActor = -1;
        private KeyCode pmReplyKey = KeyCode.R;
        private string selectedShareWaypointName = "";
        private float favRoomNotifyCooldown;

        private bool showQuickActionBar = true;
        private float spectateSmooth = 12f; // higher = snappier, 0 = instant
        private ConfigEntry<KeyCode> stopSpectateKey;
        private ConfigEntry<KeyCode> freeMouseHoldKey;
        private bool freeMouseHeld;
        private CursorLockMode savedCursorLock = CursorLockMode.None;
        private bool savedCursorVisible = true;
        private bool freeMouseOverrideActive;

        // Session stats
        private float sessionStartTime = -1f;
        private int sessionTeleportCount;
        private int sessionKickCount;
        private int sessionSpectateCount;
        private string sessionRoomTimeLabel = "0m";

        private const float RadarMinSize = 170f;
        private const float RadarMaxSize = 360f;

        // ============================================================
        // CONFIGURATION
        // ============================================================
        private ConfigEntry<KeyboardShortcut> menuToggleKey;

        private ConfigEntry<float> configCameraHeight;
        private ConfigEntry<float> configCameraDistance;
        private ConfigEntry<float> configCameraRotation;

        private ConfigEntry<float> configBackgroundHue;
        private ConfigEntry<float> configBackgroundOpacity;
        private ConfigEntry<float> configBackgroundFPS;
        private ConfigEntry<bool> configMenuGreyscale;

        private ConfigEntry<string> configBannedUserIds;
        private ConfigEntry<string> configFavoritePrefabNames;
        private ConfigEntry<string> configFavoriteRoomNames;
        private ConfigEntry<bool> configSoftTeleport;
        private ConfigEntry<float> configFlySpeed;

        // ============================================================
        // HOST TOOLS
        // ============================================================
        private bool kickConfirmationVisible;
        private Player pendingKickPlayer;

        private bool kickAllConfirmationVisible;
        private float kickAllCooldownUntil;
        private const float KickAllCooldownSeconds = 3f;

        private bool banConfirmationVisible;
        private Player pendingBanPlayer;

        // Ban list: kicked players are tracked by Photon UserId (falls back to nickname)
        // so a rejoin attempt from the same account can be rejected by the host.
        private readonly HashSet<string> bannedUserIds = new HashSet<string>();
        private string roomLabelInput = "";
        private bool roomLabelFocused;
        private const string RoomLabelPropertyKey = "ZexQoLRoomLabel";
        private const string RoomPlayersPropertyKey = "ZexQoLPlayers";
        private Vector2 recentEventsScroll = Vector2.zero;
        private Vector2 bannedListScroll = Vector2.zero;

        // QoL: destroy local body before leaving a room (cuts leftover corpses)
        private bool destroyBodyOnLeave = true;
        private ConfigEntry<bool> configDestroyBodyOnLeave;

        // Host publishes player names into room props so browser can hover-preview
        private bool publishRoomPlayers = true;
        private ConfigEntry<bool> configPublishRoomPlayers;
        private float nextRoomPlayersPublishTime;
        private string lastPublishedRoomPlayers = "";

        // Server browser hover tip (player list from room props)
        private string serverHoverRoomName = "";
        private string serverHoverPlayersText = "";
        private Vector2 serverHoverGuiPos;

        // Room player scanner — single coroutine owns leave/join/read/leave/rejoin
        private readonly Dictionary<string, string> peekedRoomPlayers = new Dictionary<string, string>();
        private readonly Queue<string> scanQueue = new Queue<string>();
        private bool scanRunning;
        private bool scanAbort;
        private string scanCurrentRoom = "";
        private string peekStatus = "";
        private float peekStatusUntil;
        private string scanHomeRoom = "";
        private bool scanShouldRejoinHome;
        private Coroutine scanCoroutine;
        private string scanLastError = "";
        // Kept for any leftover references
        private bool peekInProgress;
        private string peekTargetRoom = "";
        private bool peekNeedLobbyAfterLeave;
        private bool peekAwaitingTargetJoin;
        private bool peekRejoinAfter;
        private Coroutine peekCoroutine;

        private readonly Dictionary<int, string> knownRoomPlayers =
            new Dictionary<int, string>();

        private readonly List<string> recentPlayerEvents =
            new List<string>();

        // ============================================================
        // SERVER BROWSER
        // ============================================================
        private readonly Dictionary<string, RoomInfo> cachedRooms = new Dictionary<string, RoomInfo>();
        private Vector2 serverListScroll = Vector2.zero;
        private string serverListStatus = "IDLE";
        private bool isBrowsingServers;
        private bool pendingRejoinPrevious;
        private string selectedRoomName = "";
        private string previousRoomName = "";
        private string pendingJoinRoomName = "";
        private bool joinPendingInProgress;
        private float lastRoomListUpdateTime;
        private Coroutine rejoinCoroutine;

        // Server filters / favorites
        private string serverNameFilter = "";
        private bool serverNameFilterFocused;
        private bool serverFilterOpenOnly = true;
        private int serverFilterMinPlayers; // 0 = any
        private int serverFilterMaxPlayers = 255; // 255 = any
        private readonly HashSet<string> favoriteRoomNames = new HashSet<string>();
        private bool serverShowFavoritesOnly;

        // Remember where you were before REFRESH left the room, restore on auto-rejoin
        private bool browseHasSavedTransform;
        private Vector3 browseSavedPosition;
        private Quaternion browseSavedRotation;
        private string browseSavedPrefabName = "";
        private int browseSavedSpeciesIndex = -1;
        private object browseSavedGenes;
        private Coroutine restoreBrowsePositionCoroutine;
        // Continuous re-apply window (game spawn snap often wins a one-shot teleport)
        private bool browseRestoreActive;
        private float browseRestoreUntil;
        private int browseRestoreHits;
        private bool browseDidRespawnRestore;
        private bool browseTriedRespawnRestore;
        // Toggle: after server-browser rejoin, restore saved position (Teleport tab)
        private bool browsePositionRestoreEnabled = true;
        private ConfigEntry<bool> configBrowsePositionRestore;

        // Auto water-splash on join (fixes common visual/physics desync)
        private bool autoSplashOnJoin = true;
        private ConfigEntry<bool> configAutoSplashOnJoin;
        private ConfigEntry<float> configUiScale;
        private ConfigEntry<float> configMenuPosX;
        private ConfigEntry<float> configMenuPosY;
        private ConfigEntry<float> configRadarPosX;
        private ConfigEntry<float> configRadarPosY;
        private ConfigEntry<float> configRadarSize;
        private float nextUiLayoutSaveTime;

        private ConfigEntry<bool> configShowHudWaypoints;
        private ConfigEntry<string> configLastJoinedRoom;
        private string autoSplashStatus = "";
        private float autoSplashStatusUntil;
        private float nextAutoSplashAllowed;

        // Quick create lobby (Host Tools / Servers)
        // Mods tab (local/Workshop selection + apply)
        private readonly List<string> quickLobbyModTitles = new List<string>();
        private readonly List<string> quickLobbyModIds = new List<string>();
        private readonly List<string> quickLobbyModFolders = new List<string>();
        private readonly List<bool> quickLobbyModEnabled = new List<bool>();
        private Vector2 quickLobbyModScroll;
        private Vector2 quickLobbyModSelectedScroll;
        private float nextModListRefresh;
        private string quickLobbyModFilter = "";
        private bool quickLobbyModFilterFocused;
        private bool applyModsRunning;
        private string applyModsStatus = "";
        private float applyModsStatusUntil;
        private string modPresetName = "default";
        private bool modPresetNameFocused = false;
        private readonly List<string> modPresetNames = new List<string>();
        private int selectedModPreset = -1;
        private ConfigEntry<string> configModPresets;

        // Global status toast queue (one visible at a time)
        private string toastMessage = "";
        private string toastCategory = "system";
        private float toastUntil;
        private float toastShownAt;
        private readonly System.Collections.Generic.Queue<string> toastQueue =
            new System.Collections.Generic.Queue<string>();
        private readonly System.Collections.Generic.Queue<string> toastCategoryQueue =
            new System.Collections.Generic.Queue<string>();

        // Toast customization (Notifications tab = 14)
        // Position: 0=TopCenter 1=TopLeft 2=TopRight 3=BottomCenter 4=BottomLeft 5=BottomRight
        private int toastPosition = 0;
        private float toastDurationSec = 2.8f;
        private float toastWidthScale = 1f;
        private float toastHeightScale = 1f;
        private float toastFontScale = 1f;
        private float toastBgOpacity = 0.92f;
        private float toastBgHue = 0.62f;
        private float toastTextBrightness = 1f;
        private float toastMargin = 18f;
        // Animation: 0=None 1=Fade 2=Slide 3=Fade+Slide
        private int toastAnimMode = 1;
        private float toastAnimSeconds = 0.22f;
        private bool toastEnabled = true;
        private bool toastNotifySystem = true;
        private bool toastNotifySocial = true;
        private bool toastNotifyGameplay = true;
        private bool toastNotifyHost = true;
        private bool toastNotifyScan = true;
        private bool toastNotifyScreenshot = true;

        private ConfigEntry<bool> configToastEnabled;
        private ConfigEntry<int> configToastPosition;
        private ConfigEntry<float> configToastDuration;
        private ConfigEntry<float> configToastWidthScale;
        private ConfigEntry<float> configToastHeightScale;
        private ConfigEntry<float> configToastFontScale;
        private ConfigEntry<float> configToastBgOpacity;
        private ConfigEntry<float> configToastBgHue;
        private ConfigEntry<float> configToastTextBrightness;
        private ConfigEntry<float> configToastMargin;
        private ConfigEntry<int> configToastAnimMode;
        private ConfigEntry<float> configToastAnimSeconds;
        private ConfigEntry<bool> configToastNotifySystem;
        private ConfigEntry<bool> configToastNotifySocial;
        private ConfigEntry<bool> configToastNotifyGameplay;
        private ConfigEntry<bool> configToastNotifyHost;
        private ConfigEntry<bool> configToastNotifyScan;
        private ConfigEntry<bool> configToastNotifyScreenshot;

        // Welcome join message (separate from water auto-splash)
        private bool welcomeMessageOnJoin = true;
        private ConfigEntry<bool> configWelcomeMessageOnJoin;

        // ============================================================
        // STARTUP / UPDATE
        // ============================================================
        private void Awake()
        {
            // Absolute minimum — even BindConfig is deferred (it was killing the process).
            Instance = this;
            try { Logger.LogInfo("[ZexQoL] Awake"); } catch { }
            try
            {
                StartCoroutine(ZexDeferredInit());
                try { Logger.LogInfo("[ZexQoL] init coroutine scheduled"); } catch { }
            }
            catch (System.Exception ex)
            {
                try { Logger.LogError("[ZexQoL] schedule FAILED: " + ex); } catch { }
            }
        }


        /// <summary>
        /// Keep HarmonyX noise down — only real problems (Error). Warn optional via BepInEx.cfg.
        /// </summary>
        private static void QuietHarmonyLogs()
        {
            try
            {
                // HarmonyX: prefer HarmonyFileLog over obsolete Harmony.DEBUG
                var fileLog = AccessTools.TypeByName("HarmonyLib.Tools.HarmonyFileLog");
                if (fileLog != null)
                {
                    var en = fileLog.GetProperty("Enabled",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (en != null && en.CanWrite)
                        en.SetValue(null, false, null);
                }
            }
            catch { }
            try
            {
                // HarmonyX (BepInEx): Info/IL/Debug spam about every patch
                var channelType = AccessTools.Inner(typeof(Harmony), "Logger")
                    ?? AccessTools.TypeByName("HarmonyLib.Tools.Logger");
                if (channelType == null)
                    channelType = typeof(Harmony).Assembly.GetType("HarmonyLib.Tools.Logger");
                if (channelType == null) return;

                var filterProp = channelType.GetProperty("ChannelFilter",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (filterProp == null) return;

                var logChannelType = filterProp.PropertyType;
                // Prefer Error only; fall back to Warn|Error if enum has those names
                object value = null;
                try
                {
                    value = System.Enum.Parse(logChannelType, "Error");
                }
                catch
                {
                    try
                    {
                        object warn = System.Enum.Parse(logChannelType, "Warn");
                        object err = System.Enum.Parse(logChannelType, "Error");
                        value = (int)warn | (int)err;
                        value = System.Enum.ToObject(logChannelType, value);
                    }
                    catch { return; }
                }
                filterProp.SetValue(null, value, null);
            }
            catch { }
        }

        private System.Collections.IEnumerator ZexDeferredInit()
        {
            yield return null;
            yield return null;

            try
            {
                Logger.LogInfo("[ZexQoL] BindConfig starting...");
                BindConfig();
                Logger.LogInfo("[ZexQoL] BindConfig OK");
            }
            catch (System.Exception ex)
            {
                Logger.LogError("[ZexQoL] BindConfig FAILED: " + ex);
            }

            yield return null;

            try
            {
                PhotonNetwork.AddCallbackTarget(this);
                Logger.LogInfo("[ZexQoL] Photon callbacks OK");
            }
            catch (System.Exception ex)
            {
                Logger.LogError("[ZexQoL] Photon callbacks FAILED: " + ex);
            }

            yield return null;

            try
            {
                FindPreparePool();
                Logger.LogInfo("[ZexQoL] PreparePool OK");
            }
            catch (System.Exception ex)
            {
                Logger.LogError("[ZexQoL] PreparePool FAILED: " + ex);
            }

            // Background animation removed in optimized build — do not start loader.
            // try
            // {
            //     StartCoroutine(LoadBackgroundAfterStartup());
            // }
            // catch (System.Exception ex)
            // {
            //     Logger.LogError("[ZexQoL] Background schedule FAILED: " + ex);
            // }

            yield return null;

            try
            {
                QuietHarmonyLogs();
                if (spectateHarmony == null)
                    spectateHarmony = new Harmony("com.zex.qolmenu.spectate");
                Logger.LogInfo("[ZexQoL] Harmony OK");
            }
            catch (System.Exception ex)
            {
                Logger.LogError("[ZexQoL] Harmony FAILED: " + ex);
                yield break;
            }

            try
            {
                PatchOrbitCamera();
                Logger.LogInfo("[ZexQoL] PatchOrbitCamera OK");
            }
            catch (System.Exception ex)
            {
                Logger.LogError("[ZexQoL] PatchOrbitCamera FAILED: " + ex);
            }

            try
            {
                PatchPhotonRoomCreation();
                PatchPhotonRoomListSpawner();
                Logger.LogInfo("[ZexQoL] PatchPhotonRoomCreation OK");
            }
            catch (System.Exception ex)
            {
                Logger.LogError("[ZexQoL] PatchPhotonRoomCreation FAILED: " + ex);
            }

            try
            {
                StartCoroutine(DelayedPatchScanModSkip());
            }
            catch (System.Exception ex)
            {
                Logger.LogError("[ZexQoL] DelayedPatchScanModSkip FAILED: " + ex);
            }

            try
            {
                StartCoroutine(InitialPrefabScan());
            }
            catch (System.Exception ex)
            {
                Logger.LogError("[ZexQoL] InitialPrefabScan FAILED: " + ex);
            }

            try
            {
                TryAutoImportCharConConfig();
                Logger.LogInfo("[ZexQoL] CharCon import step done");
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning("[ZexQoL] CharCon import: " + ex.Message);
            }

            peekRejoinAfter = false; // reserved for peek-rejoin; silence CS0649
            Logger.LogInfo("[ZexQoL] Init complete");
        }

        private void Update()
        {
            TryCaptureOrigin();

            float dt = Time.unscaledDeltaTime;
            if (dt > 0.0001f)
                fpsValue = Mathf.Lerp(fpsValue, 1f / dt, 0.12f);

            // RGB CYCLE: advance hue through the full spectrum (LOCK freezes; GREY disables)
            TickMenuHueCycle(dt);

            // BACKGROUND TESTING
            TickBackgroundAnimation(Time.unscaledDeltaTime);

            // Server-browser position restore after rejoin (Teleport toggle)
            if (browseRestoreActive)
            {
                if (!browsePositionRestoreEnabled || !PhotonNetwork.InRoom || Time.unscaledTime > browseRestoreUntil)
                {
                    browseRestoreActive = false;
                    if (!browsePositionRestoreEnabled)
                        browseHasSavedTransform = false;
                    else if (Time.unscaledTime > browseRestoreUntil)
                        browseHasSavedTransform = false;
                    if (PhotonNetwork.InRoom && browsePositionRestoreEnabled)
                        serverListStatus = "CACHED • " + cachedRooms.Count + " rooms • pos restore done (" + browseRestoreHits + " hits)";
                    Logger.LogInfo("Server browse: restore window ended, hits=" + browseRestoreHits + " respawned=" + browseDidRespawnRestore + " enabled=" + browsePositionRestoreEnabled);
                }
                else
                {
                    try
                    {
                        // Once a body exists, destroy+respawn at saved coords (like character swap)
                        if (!browseTriedRespawnRestore && ResolveLocalPlayerBody() != null)
                        {
                            browseTriedRespawnRestore = true;
                            if (TryRespawnAtBrowsePosition())
                            {
                                browseDidRespawnRestore = true;
                                browseRestoreHits++;
                                serverListStatus = "CACHED • respawned at saved pos";
                            }
                        }

                        // Keep shoving in case spawn/network still fights us
                        ForceTeleportLocalPlayer(browseSavedPosition, browseSavedRotation);
                        browseRestoreHits++;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Browse restore tick failed: " + ex.Message);
                    }
                }
            }

            objectCountTimer -= dt;
            if (objectCountTimer <= 0f)
            {
                objectCountTimer = 1f;
                try { sceneObjectCount = UnityEngine.Object.FindObjectsOfType<GameObject>().Length; }
                catch { sceneObjectCount = 0; }
            }

            if (sessionStartTime < 0f)
                sessionStartTime = Time.unscaledTime;
            float sessMin = (Time.unscaledTime - sessionStartTime) / 60f;
            sessionRoomTimeLabel = sessMin < 60f
                ? (sessMin.ToString("0.0") + "m")
                : ((sessMin / 60f).ToString("0.0") + "h");

            TrackRecentPlayers();
            UpdateRoomPlayersPublish();
            CheckFavoriteRoomOpenSlots();
            UpdateFollowPlayer();
            ProcessHotkeys();
            UpdateFlyingNoclipInput(); // input + compute velocity
            UpdateFrozenPlayers();

            if (spectating)
            {
                Player p = GetPlayerByActorId(spectateActorId);
                GameObject target = p == null ? null : FindPlayerObject(p);
                if (p == null || p.IsLocal || target == null)
                    StopSpectating();
                else
                    spectateTarget = target.transform;
            }
        }

        /// <summary>
        /// CharCon-style hotkeys via UnityInput.Current (see UserSettings.Dothekeybinds).
        /// Runs once per frame in Update only.
        /// </summary>
        private void ProcessHotkeys()
        {
            if (waitingForKeyRebind)
                return;

            int frame = Time.frameCount;
            if (frame == lastHotkeyFrame)
                return;
            lastHotkeyFrame = frame;

            try
            {
                // Menu: KeyboardShortcut (CharCon ToggleMenuVisibility.Value.IsDown equivalent)
                if (menuToggleKey != null && ZexInput.ShortcutDown(menuToggleKey.Value))
                {
                    menuVisible = !menuVisible;
                    SaveUiLayoutToConfig(force: true);
                }

                // Noclip / waypoint: single KeyCode + UnityInput.GetKeyDown (CharCon)
                if (noclipToggleKey != null && ZexInput.GetKeyDown(noclipToggleKey.Value))
                    ToggleFlyingNoclip();

                if (waypointQuickSaveKey != null && ZexInput.GetKeyDown(waypointQuickSaveKey.Value))
                    QuickSaveWaypoint();

                if (flySpeedUpKey != null && ZexInput.GetKeyDown(flySpeedUpKey.Value))
                    AdjustFlySpeed(10f);

                if (flySpeedDownKey != null && ZexInput.GetKeyDown(flySpeedDownKey.Value))
                    AdjustFlySpeed(-10f);

                if (spectateNextKey != null && ZexInput.GetKeyDown(spectateNextKey.Value))
                    SpectateCycleFromHotkey(1);
                if (spectatePrevKey != null && ZexInput.GetKeyDown(spectatePrevKey.Value))
                    SpectateCycleFromHotkey(-1);

                // Stop spectate (rebindable; default middle mouse)
                if (spectating && stopSpectateKey != null && ZexInput.GetKeyDown(stopSpectateKey.Value))
                {
                    StopSpectating();
                    ShowToast("Spectate stopped", "gameplay");
                }

                // Hold to unlock mouse (free cursor)
                UpdateFreeMouseHold();

                // Reply to last PM
                if (ZexInput.GetKeyDown(pmReplyKey) && !menuVisible)
                    ReplyToLastPm();

                if (cleanScreenshotKey != null && ZexInput.GetKeyDown(cleanScreenshotKey.Value))
                    ToggleCleanUiAndScreenshot();

                cheatSheetVisible = cheatSheetKey != null && ZexInput.GetKey(cheatSheetKey.Value);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("ProcessHotkeys: " + ex.Message);
            }
        }

        /// <summary>
        /// Hotkey: cycle spectate target without opening the menu.
        /// Starts spectating if not already, then moves next/prev.
        /// </summary>


        private void UpdateFreeMouseHold()
        {
            bool wantFree = freeMouseHoldKey != null && ZexInput.GetKey(freeMouseHoldKey.Value);
            // Also free while menu is open so UI stays usable
            if (menuVisible)
                wantFree = true;

            if (wantFree)
            {
                if (!freeMouseOverrideActive)
                {
                    // Some Unity builds only allow setting Cursor.visible (no getter)
                    try { savedCursorLock = Cursor.lockState; }
                    catch { savedCursorLock = CursorLockMode.Locked; }
                    freeMouseOverrideActive = true;
                }
                Cursor.lockState = CursorLockMode.None;
                try { Cursor.visible = true; } catch { }
                freeMouseHeld = true;
            }
            else if (freeMouseOverrideActive)
            {
                Cursor.lockState = savedCursorLock == CursorLockMode.None
                    ? CursorLockMode.Locked
                    : savedCursorLock;
                // Hide cursor again when returning to gameplay lock
                try { Cursor.visible = Cursor.lockState == CursorLockMode.None; } catch { }
                freeMouseOverrideActive = false;
                freeMouseHeld = false;
            }
        }

        private void LateUpdate()
        {
            if (!spectating)
                return;

            if (spectateTarget == null)
            {
                StopSpectating();
                return;
            }

            Camera cam = Camera.main;

            if (cam == null)
                return;

            Vector3 target =
                spectateTarget.position +
                Vector3.up * 1.25f;

            Vector3 behindOffset =
                -spectateTarget.forward * spectateCameraDistance;

            Vector3 rotatedOffset =
                Quaternion.AngleAxis(
                    spectateCameraRotation,
                    Vector3.up
                ) * behindOffset;

            Vector3 desired =
                target +
                rotatedOffset +
                Vector3.up * spectateCameraHeight;

            // Smoothing: 0 = snap, higher = faster follow
            float sm = Mathf.Clamp(spectateSmooth, 0f, 40f);
            if (sm <= 0.01f)
                cam.transform.position = desired;
            else
                cam.transform.position = Vector3.Lerp(
                    cam.transform.position,
                    desired,
                    1f - Mathf.Exp(-sm * Time.unscaledDeltaTime));

            Vector3 direction =
                target -
                cam.transform.position;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
                if (sm <= 0.01f)
                    cam.transform.rotation = look;
                else
                    cam.transform.rotation = Quaternion.Slerp(
                        cam.transform.rotation,
                        look,
                        1f - Mathf.Exp(-sm * Time.unscaledDeltaTime));
            }
        }

        private void OnDestroy()
        {
            DestroyUITexture(ref uiWindowTexture);
            DestroyUITexture(ref uiSidebarTexture);
            DestroyUITexture(ref uiCardTexture);
            DestroyUITexture(ref uiButtonTexture);
            DestroyUITexture(ref uiButtonHoverTexture);
            DestroyUITexture(ref uiButtonActiveTexture);
            DestroyUITexture(ref uiAccentTexture);
            DestroyUITexture(ref uiMutedTexture);

            PhotonNetwork.RemoveCallbackTarget(this);

            SaveConfig();

            if (spectateHarmony != null)
            {
                spectateHarmony.UnpatchSelf();
                spectateHarmony = null;
            }

            Instance = null;

            cachedLocalPlayer = null;
            playerObjectCache.Clear();
            StopSpectating();
            if (tracerMaterial != null) Destroy(tracerMaterial);
            if (backgroundMaterial != null) Destroy(backgroundMaterial);
            if (menuBackground != null) Destroy(menuBackground);
            tracerMaterial = null; backgroundMaterial = null; menuBackground = null;
        }
    }
}