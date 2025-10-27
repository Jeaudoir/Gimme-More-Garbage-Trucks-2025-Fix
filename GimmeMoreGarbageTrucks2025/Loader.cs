using ICities;
using HarmonyLib;
using System.Reflection;

namespace GimmeMoreGarbageTrucks2025
{
    // Handles mod lifecycle: initializes when a save loads, applies Harmony patches to intercept
    // vanilla game code, and cleans up patches when the level unloads.
    public class Loader : LoadingExtensionBase
    {
        private Helper _helper;
        private Constants _constants;
        private Harmony _harmony;

        public override void OnCreated(ILoading loading)
        {
            _helper = Helper.Instance;
            _constants = Constants.Instance;
            _helper.GameLoaded = loading.loadingComplete;
        }

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);
            if (mode == LoadMode.LoadGame || mode == LoadMode.NewGame)
            {
                _helper.GameLoaded = true;
                
                // Apply Harmony patches
                _harmony = new Harmony(_constants.HarmonyId);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                
                _helper.NotifyPlayer("Initialized with Harmony patches");
            }
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();
            _helper.GameLoaded = false;
            
            // Remove Harmony patches
            if (_harmony != null)
            {
                _harmony.UnpatchAll(_constants.HarmonyId);
                _harmony = null;
            }
        }
    }
}