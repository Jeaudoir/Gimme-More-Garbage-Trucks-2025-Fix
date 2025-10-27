namespace GimmeMoreGarbageTrucks2025
{
    // Singleton that holds mod constants and metadata.
    // These are fixed values used throughout the mod, unlike ModConfiguration which holds user settings.
    public sealed class Constants
    {
        private Constants()
        {
            // ===================================================================
            // MOD METADATA
            // ===================================================================
            Tag = "Gimme More Garbage Trucks! (2025 Fix)";
            HarmonyId = "com.gimmemore.garbagetrucks2025";

            // ===================================================================
            // ANGULAR CONSTANTS (in radians)
            // ===================================================================
            PI = 3.141592653589793;
            TWO_PI = 6.283185307179586;
            HALF_PI = 1.5707963267948966;              // π/2 = 90 degrees
            NEGATIVE_HALF_PI = -1.5707963267948966;    // -π/2 = -90 degrees
            THIRD_PI = 1.0471975512;                   // π/3 = 60 degrees
            NEGATIVE_THIRD_PI = -1.0471975512;         // -π/3 = -60 degrees

            // ===================================================================
            // DISTANCE & RANGE CONSTANTS
            // ===================================================================
            ImmediateRange1 = 4000;                    // Close range for immediate target selection
            ImmediateRange2 = 20000;                   // Medium range for immediate target selection
            PathFindDistance = 32f;                    // Search radius for pathfinding positions
            MinDistanceThreshold = 10f;                // Minimum distance to consider alternate path positions
            DistanceComparisonBuffer = 0.9;            // 90% - distance multiplier for target comparison
            CloseProximityDistanceSquared = 2500f;     // Very close range threshold (50 units squared)

            // ===================================================================
            // PATHFINDING CONSTANTS
            // ===================================================================
            MaxPathCost = 20000f;                      // Maximum cost for path creation
            MaxRetryAttempts = 20;                     // Maximum pathfinding retry attempts

            // ===================================================================
            // CAPACITY & CARGO CONSTANTS
            // ===================================================================
            StandardTruckCapacity = 20000;             // Standard garbage truck cargo capacity
            MinSpaceForDispatch = 20000;               // Minimum space (1 truckload) required before facility can dispatch
            CustomBufferMultiplier = 1000;             // Multiplier for custom buffer in garbage calculations

            // ===================================================================
            // TIME CONSTANTS (in seconds or days)
            // ===================================================================
            TargetChangeCleanupSeconds = 10.0;         // How often to clean up old target change records
            SwarmTimeoutSeconds = 5.0;                 // How long to wait for swarm trucks to spawn
            FullScanIntervalSeconds = 10.0;            // How often to run a full city scan
            TargetChangeDelayDays = 0.5;               // Delay before allowing target changes
            MillisecondsToSeconds = 1000.0;            // Conversion factor

            // ===================================================================
            // ITERATION & COUNT LIMITS
            // ===================================================================
            MaxVehicleIterations = 65535;              // Safety limit for vehicle iteration loops
            MaxFailedTargetsPerTruck = 20;             // Maximum failed targets to track per truck
            TargetChangeListLimit = 20;                // Maximum size of target change tracking list

            // ===================================================================
            // USER SETTING LIMITS (for UI validation)
            // ===================================================================
            MinGarbageThreshold = 100;
            MaxGarbageThreshold = 10000;
            DefaultGarbageThreshold = 1500;
            
            MinEmergencyTruckCount = 1;
            MaxEmergencyTruckCount = 50;
            
            MinSwarmRedirectGroupSize = 1;
            MaxSwarmRedirectGroupSize = 10;
            
            MinNormalDispatchTruckCount = 1;
            MaxNormalDispatchTruckCount = 10;
            
            MinLowCargoRecallDays = 1.0;
            MaxLowCargoRecallDays = 30.0;
            DefaultLowCargoRecallDays = 10.0;
            
            MinLowCargoThreshold = 0;
            MaxLowCargoThreshold = 20000;
            DefaultLowCargoThreshold = 2000;
            
            MinScanFrequencyMs = 0;
            MaxScanFrequencyMs = 2000;
            DefaultScanFrequencyMs = 333;
        }

        public static Constants Instance { get; } = new Constants();

        // ===================================================================
        // MOD METADATA
        // ===================================================================
        public readonly string Tag;
        public readonly string HarmonyId;

        // ===================================================================
        // ANGULAR CONSTANTS
        // ===================================================================
        public readonly double PI;
        public readonly double TWO_PI;
        public readonly double HALF_PI;
        public readonly double NEGATIVE_HALF_PI;
        public readonly double THIRD_PI;
        public readonly double NEGATIVE_THIRD_PI;

        // ===================================================================
        // DISTANCE & RANGE CONSTANTS
        // ===================================================================
        public readonly int ImmediateRange1;
        public readonly int ImmediateRange2;
        public readonly float PathFindDistance;
        public readonly float MinDistanceThreshold;
        public readonly double DistanceComparisonBuffer;
        public readonly float CloseProximityDistanceSquared;

        // ===================================================================
        // PATHFINDING CONSTANTS
        // ===================================================================
        public readonly float MaxPathCost;
        public readonly int MaxRetryAttempts;

        // ===================================================================
        // CAPACITY & CARGO CONSTANTS
        // ===================================================================
        public readonly int StandardTruckCapacity;
        public readonly int MinSpaceForDispatch;
        public readonly int CustomBufferMultiplier;

        // ===================================================================
        // TIME CONSTANTS
        // ===================================================================
        public readonly double TargetChangeCleanupSeconds;
        public readonly double SwarmTimeoutSeconds;
        public readonly double FullScanIntervalSeconds;
        public readonly double TargetChangeDelayDays;
        public readonly double MillisecondsToSeconds;

        // ===================================================================
        // ITERATION & COUNT LIMITS
        // ===================================================================
        public readonly int MaxVehicleIterations;
        public readonly int MaxFailedTargetsPerTruck;
        public readonly int TargetChangeListLimit;

        // ===================================================================
        // USER SETTING LIMITS
        // ===================================================================
        public readonly int MinGarbageThreshold;
        public readonly int MaxGarbageThreshold;
        public readonly int DefaultGarbageThreshold;
        
        public readonly int MinEmergencyTruckCount;
        public readonly int MaxEmergencyTruckCount;
        
        public readonly int MinSwarmRedirectGroupSize;
        public readonly int MaxSwarmRedirectGroupSize;
        
        public readonly int MinNormalDispatchTruckCount;
        public readonly int MaxNormalDispatchTruckCount;
        
        public readonly double MinLowCargoRecallDays;
        public readonly double MaxLowCargoRecallDays;
        public readonly double DefaultLowCargoRecallDays;
        
        public readonly int MinLowCargoThreshold;
        public readonly int MaxLowCargoThreshold;
        public readonly int DefaultLowCargoThreshold;
        
        public readonly int MinScanFrequencyMs;
        public readonly int MaxScanFrequencyMs;
        public readonly int DefaultScanFrequencyMs;
    }
}