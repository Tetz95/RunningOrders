using Game.Events;
using Game.State;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Railroader.Modding;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using RunningOrders.Services;
using RunningOrders.Settings;
using RunningOrders.State;
using RunningOrders.UI;
using RunningOrders.Wrappers;

namespace RunningOrders
{
    public class Loader : IModEntryPoint
    {
        public IModContext ModContext { get; private set; }
        public Harmony HarmonyInstance { get; private set; }
        public WaypointQueueController Instance { get; private set; }

        public WaypointWindow WaypointWindow { get; private set; }
        public RouteManagerWindow RouteManagerWindow { get; private set; }
        public ModSettingsWindow ModSettingsWindow { get; private set; }
        internal PendingSwitchRestorer PendingSwitchRestorer { get; private set; }
        internal WaypointPreviewer WaypointPreviewer { get; private set; }

        internal ServiceProvider ServiceProvider { get; private set; }

        // Static: a reload (OnModUnload, then OnModLoad on a new loader) on a map that stays loaded gets no MapDidLoad, and
        // its windows were never registered.
        private static bool MapHasLoaded = false;

        private IDisposable _mapWillUnloadSubscription;

        public GameObject QueueStorageGO { get; private set; }

        public GameObject RouteStorageGO { get; private set; }

        // Disposed on unload: the game reloads mods mid-session (OnModUnload, then OnModLoad on a new loader), and the old
        // loader's handler would still run on map load against windows destroyed with it.
        private IDisposable _mapDidLoadSubscription;

        public static string QueueStorageGOName = "WaypointQueueStateStorage";
        public static string RouteStorageGOName = "WaypointQueueRouteStorage";

        public void OnModLoad(IModContext modContext)
        {
            if (ModContext != null)
            {
                modContext.Log.Warning("Running Orders is already loaded!");
            }
            Assembly assembly = Assembly.GetExecutingAssembly();
            if (assembly == null)
            {
                modContext.Log.Error("Running Orders assembly is null");
            }
            AssemblyName assemblyName = assembly.GetName();
            if (assemblyName == null)
            {
                modContext.Log.Error("Running Orders assembly name is null");
            }
            //Log($"Loading WaypointQueue assembly version {Assembly.GetExecutingAssembly().GetName().Version}");

            ModContext = modContext;
            ModLocator.Loader = this;

            LogVersionFromInfoJson();

            ModLocator.Settings = WaypointQueueSettings.Load();
            ModLocator.Settings.Save();

            ServiceProvider = new ServiceProvider();
            ModLocator.ServiceProvider = ServiceProvider;
            ConfigureServices();

            _mapDidLoadSubscription = EventBus.Subscribe<MapDidLoadEvent>(OnMapDidLoad);
            _mapWillUnloadSubscription = EventBus.Subscribe<MapWillUnloadEvent>(_ => MapHasLoaded = false);

            ModLifecycle.Init(ModContext.ModIdentifier, m => Log(m), (m, ex) => LogError(ex, m));
            HarmonyInstance = ModLifecycle.Harmony;
            //Harmony.DEBUG = true;

            try
            {
                var waypointQueueGO = new GameObject("RunningOrders");
                Instance = waypointQueueGO.AddComponent<WaypointQueueController>();
                ModLocator.QueueController = Instance;
                waypointQueueGO.AddComponent<WaypointCarPicker>();
                waypointQueueGO.AddComponent<ErrorModalController>();
                waypointQueueGO.AddComponent<WaypointPicker>();
                waypointQueueGO.AddComponent<ModStateManager>();
                WaypointWindow = waypointQueueGO.AddComponent<WaypointWindow>();
                RouteManagerWindow = waypointQueueGO.AddComponent<RouteManagerWindow>();
                ModSettingsWindow = waypointQueueGO.AddComponent<ModSettingsWindow>();
                PendingSwitchRestorer = waypointQueueGO.AddComponent<PendingSwitchRestorer>();
                WaypointPreviewer = waypointQueueGO.AddComponent<WaypointPreviewer>();

                QueueStorageGO = new GameObject(QueueStorageGOName);
                RouteStorageGO = new GameObject(RouteStorageGOName);

                UnityEngine.Object.DontDestroyOnLoad(waypointQueueGO);
                UnityEngine.Object.DontDestroyOnLoad(QueueStorageGO);
                UnityEngine.Object.DontDestroyOnLoad(RouteStorageGO);

                if (MapHasLoaded)
                {
                    InitWindows();
                }
            }
            catch (Exception ex)
            {
                modContext.Log.Error(ex, $"Failed to load {modContext.ModIdentifier}:");
            }

            // After the components (a patch calls into them): class by class, so a game update that breaks one patch costs
            // only its feature.
            ModLifecycle.Patch();

            // Loaded again while a railroad is loaded (the mod set changed without a game load): its queues back.
            try
            {
                if (StateManager.Shared != null && StateManager.Shared.HasRestoredProperties && ModStateManager.Shared != null)
                {
                    ModStateManager.Shared.ResumeMidWorld();
                }
            }
            catch (Exception ex)
            {
                modContext.Log.Error(ex, "Restoring the queues after the mod reload:");
            }
        }

        private void ConfigureServices()
        {
            // This is a bit ugly but it provides the benefit of dependency injection
            // without the overhead of requiring an extra DI package


            ServiceProvider.AddSingleton<IOpsControllerWrapper>(new OpsControllerWrapper());

            ServiceProvider.AddSingleton<TrainControllerWrapper>(new TrainControllerWrapper());

            ServiceProvider.AddSingleton<ICarService>(new CarService(
                ServiceProvider.GetService<TrainControllerWrapper>()
                ));

            ServiceProvider.AddSingleton<UncouplingService>(new UncouplingService(
                ServiceProvider.GetService<ICarService>(),
                ServiceProvider.GetService<IOpsControllerWrapper>()
                ));
            ServiceProvider.AddSingleton<AutoEngineerService>(new AutoEngineerService(
                ServiceProvider.GetService<ICarService>()
                ));

            ServiceProvider.AddSingleton<CouplingService>(new CouplingService(
                ServiceProvider.GetService<ICarService>(),
                ServiceProvider.GetService<AutoEngineerService>(),
                ServiceProvider.GetService<TrainControllerWrapper>()
                ));

            ServiceProvider.AddSingleton<RefuelService>(new RefuelService(
                ServiceProvider.GetService<ICarService>(),
                ServiceProvider.GetService<AutoEngineerService>(),
                ServiceProvider.GetService<IOpsControllerWrapper>()
                ));

            ServiceProvider.AddSingleton<CoupleNearestOnApproach>(new CoupleNearestOnApproach(
                ServiceProvider.GetService<CouplingService>(),
                ServiceProvider.GetService<ICarService>()
                ));

            ServiceProvider.AddSingleton<WaypointResolver>(new WaypointResolver(
                ServiceProvider.GetService<UncouplingService>(),
                ServiceProvider.GetService<RefuelService>(),
                ServiceProvider.GetService<CouplingService>(),
                ServiceProvider.GetService<ICarService>(),
                ServiceProvider.GetService<IOpsControllerWrapper>()
                ));
        }

        public void OnModUnload()
        {
            ModLocator.Settings.Save();

            _mapDidLoadSubscription?.Dispose();
            _mapDidLoadSubscription = null;
            _mapWillUnloadSubscription?.Dispose();
            _mapWillUnloadSubscription = null;

            try
            {
                ModStateManager.Shared?.Unload();
            }
            catch (Exception ex)
            {
                LogError(ex, "Unloading the queue storage:");
            }

            ModLifecycle.Unpatch();

            if (Instance != null) UnityEngine.Object.DestroyImmediate(Instance.gameObject);
            Instance = null;

            if (QueueStorageGO != null) UnityEngine.Object.DestroyImmediate(QueueStorageGO.gameObject);
            QueueStorageGO = null;

            if (RouteStorageGO != null) UnityEngine.Object.DestroyImmediate(RouteStorageGO.gameObject);
            RouteStorageGO = null;
        }

        private void OnMapDidLoad(MapDidLoadEvent @event)
        {
            MapHasLoaded = true;

            InitWindows();
        }

        private void InitWindows()
        {
            WaypointWindow.Register();
            RouteManagerWindow.Register();
            ModSettingsWindow.Register();
        }

        public void Log(string str)
        {
            ModContext.Log.Info(str);
        }

        public void LogDebug(string str)
        {
            if (ModLocator.Settings.EnableVerboseLogging)
            {
                ModContext.Log.Info(str);
            }
            else
            {
                ModContext.Log.Debug(str);
            }
        }

        public void LogError(string str)
        {
            ModContext.Log.Error(str);
        }

        public void LogError(Exception ex, string str)
        {
            ModContext.Log.Error(ex, str);
        }

        private void LogVersionFromInfoJson()
        {
            string jsonPath = Path.Combine(ModContext.ModDirectory, "Info.json");

            if (!File.Exists(jsonPath))
            {
                LogError("Info.json not found");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var jObj = JObject.Parse(json);

            if (jObj.TryGetValue("version", out var version))
            {
                Log($"Loaded Tetz's Running Orders version {version}");
            }
            else
            {
                LogError("Version not found in Info.json");
            }
        }
    }
}
