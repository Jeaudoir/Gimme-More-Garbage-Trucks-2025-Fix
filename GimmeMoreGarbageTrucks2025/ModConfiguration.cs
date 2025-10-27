using System;
using System.IO;
using System.Xml.Serialization;

namespace GimmeMoreGarbageTrucks2025
{
    // Defines the mod's configuration data structure and handles XML serialization/deserialization.
    // Stores all user settings (thresholds, truck counts, debug flags) with default values.
    public class ModConfiguration
    {
		public ModConfiguration()
		{
			EmergencyTruckCount = 5;
			SwarmRedirectGroupSize = 3;
			PrioritizeTargetWithRedSigns = false;
			GarbageThreshold = 1500;
			NormalDispatchTruckCount = 2;
			DistrictRestrictedService = false;
			RespectFacilityCapacity = true;
			EnableLowCargoRecall = true;
			LowCargoRecallDays = 10.0;
			LowCargoThreshold = 2000;
			ScanFrequencyMs = 333;
			DebugLogDispatch = false;
			DebugLogEmergency = false;
			DebugLogRecalls = false;
			EnableDebugLogging = false;
		}

        public static bool Serialize(string filename, ModConfiguration config)
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(ModConfiguration));
            try
            {
                using (StreamWriter streamWriter = new StreamWriter(filename))
                {
                    xmlSerializer.Serialize(streamWriter, config);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Helper.Instance.Log($"Failed to save configuration to '{filename}': {ex.Message}");
            }
            return false;
        }

        public static ModConfiguration Deserialize(string filename)
        {
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(ModConfiguration));
            try
            {
                using (StreamReader streamReader = new StreamReader(filename))
                {
                    return (ModConfiguration)xmlSerializer.Deserialize(streamReader);
                }
            }
            catch (Exception ex)
            {
                Helper.Instance.Log($"Failed to load configuration from '{filename}': {ex.Message}");
            }
            return null;
        }

		public int EmergencyTruckCount;
		public int SwarmRedirectGroupSize;
		public bool PrioritizeTargetWithRedSigns;
		public int GarbageThreshold;
		public int NormalDispatchTruckCount;
		public bool DistrictRestrictedService;
		public bool RespectFacilityCapacity;
		public bool EnableLowCargoRecall;
		public double LowCargoRecallDays;
		public int LowCargoThreshold;
		public int ScanFrequencyMs;
		public bool DebugLogDispatch;
		public bool DebugLogEmergency;
		public bool DebugLogRecalls;
		public bool EnableDebugLogging;
    }
}