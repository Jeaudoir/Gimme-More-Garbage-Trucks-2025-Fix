using System;
using System.IO;
using ColossalFramework;
using ICities;
using ColossalFramework.UI;

// Gimme More Garbage Trucks! (2025 Fix)
// Created by Jeaudoir
// https://github.com/Jeaudoir/Gimme-More-Garbage-Trucks-2025-Fix-

namespace GimmeMoreGarbageTrucks2025
{
    // Mod entry point: defines mod info, creates the settings UI with all configuration options
    // (emergency response, normal operations, truck management, debug logging), and manages
    // the XML config file for persistent settings.
	// WARNING: contains an OCD amount of organizing and spacing between elements :P
    public class Identity : IUserMod
    {
        public string Name => Constants.Instance.Tag;

        public string Description => "Oversees trash services to ensure that garbage vehicles are dispatched effectively.";

		public void OnSettingsUI(UIHelperBase helper)
		{
			InitConfigFile();
			UIHelperBase uihelperBase = helper.AddGroup("Gimme More Garbage Trucks! - Settings");
			
			// ===============================================================
			// EMERGENCY RESPONSE
			// ===============================================================
			
			UIHelperBase emergencyGroup = uihelperBase.AddGroup("EMERGENCY RESPONSE");
			
			UITextField emergencyCountField = (UITextField)emergencyGroup.AddTextfield(
				$"RELEASE THE SWARM! \nNumber of emergency trucks to send from each landfill or incinerator \nto each 'piled up!' alert. Best to set above 1 as trucks often fill up \nbefore reaching the target. \n({Constants.Instance.MinEmergencyTruckCount}-{Constants.Instance.MaxEmergencyTruckCount}; default is {ModConf.EmergencyTruckCount})",
				ModConf.EmergencyTruckCount.ToString(), 
				delegate(string text) { }, 
				delegate(string text) { }
			);
			
			emergencyCountField.numericalOnly = true;
			emergencyCountField.maxLength = 2;
			emergencyCountField.eventTextSubmitted += delegate(UIComponent component, string value)
			{
				int parsedValue;
				if (int.TryParse(value, out parsedValue))
				{
					parsedValue = Math.Max(Constants.Instance.MinEmergencyTruckCount, Math.Min(Constants.Instance.MaxEmergencyTruckCount, parsedValue));
					ModConf.EmergencyTruckCount = parsedValue;
					ModConfiguration.Serialize(configPath, ModConf);
					emergencyCountField.text = parsedValue.ToString();
				}
				else
				{
					emergencyCountField.text = ModConf.EmergencyTruckCount.ToString();
				}
			};
			
			emergencyGroup.AddSpace(85);
			
			UITextField swarmRedirectGroupSizeField = (UITextField)emergencyGroup.AddTextfield(
				$"Swarm redirect group size: after emergency target is cleared, swarm \nbreaks into groups. How big should the new groups be?\n({Constants.Instance.MinSwarmRedirectGroupSize}-{Constants.Instance.MaxSwarmRedirectGroupSize}; default is {ModConf.SwarmRedirectGroupSize})",
				ModConf.SwarmRedirectGroupSize.ToString(), 
				delegate(string text) { }, 
				delegate(string text) { }
			);
			
			swarmRedirectGroupSizeField.numericalOnly = true;
			swarmRedirectGroupSizeField.maxLength = 2;
			swarmRedirectGroupSizeField.eventTextSubmitted += delegate(UIComponent component, string value)
			{
				int parsedValue;
				if (int.TryParse(value, out parsedValue))
				{
					parsedValue = Math.Max(Constants.Instance.MinSwarmRedirectGroupSize, Math.Min(Constants.Instance.MaxSwarmRedirectGroupSize, parsedValue));
					ModConf.SwarmRedirectGroupSize = parsedValue;
					ModConfiguration.Serialize(configPath, ModConf);
					swarmRedirectGroupSizeField.text = parsedValue.ToString();
				}
				else
				{
					swarmRedirectGroupSizeField.text = ModConf.SwarmRedirectGroupSize.ToString();
				}
			};
			
			emergencyGroup.AddSpace(55);
			
			emergencyGroup.AddCheckbox("Prioritize higher-garbage buildings: trucks visit potentially-further \nbuildings with garbage warnings over closer buildings with less garbage", ModConf.PrioritizeTargetWithRedSigns, delegate(bool isChecked)
			{
				ModConf.PrioritizeTargetWithRedSigns = isChecked;
				ModConfiguration.Serialize(configPath, ModConf);
			});
			
			emergencyGroup.AddSpace(20);
			
			uihelperBase.AddSpace(10);
			
			// ===============================================================
			// NORMAL OPERATIONS
			// ===============================================================
			UIHelperBase normalGroup = uihelperBase.AddGroup("NORMAL OPERATIONS");
			
			UITextField garbageThresholdField = (UITextField)normalGroup.AddTextfield(
				$"Garbage threshold: amount of garbage in a building before trucks are sent \n({Constants.Instance.MinGarbageThreshold}-{Constants.Instance.MaxGarbageThreshold}; game default is 2500; {Constants.Instance.DefaultGarbageThreshold} reduces warnings)", 
				ModConf.GarbageThreshold.ToString(), 
				delegate(string text) { }, 
				delegate(string text) { }
			);
			
			normalGroup.AddSpace(10);
			
			garbageThresholdField.numericalOnly = true;
			garbageThresholdField.maxLength = 5;
			garbageThresholdField.eventTextSubmitted += delegate(UIComponent component, string value)
			{
				int parsedValue;
				if (int.TryParse(value, out parsedValue))
				{
					parsedValue = Math.Max(Constants.Instance.MinGarbageThreshold, Math.Min(Constants.Instance.MaxGarbageThreshold, parsedValue));
					ModConf.GarbageThreshold = parsedValue;
					ModConfiguration.Serialize(configPath, ModConf);
					garbageThresholdField.text = parsedValue.ToString();
				}
				else
				{
					garbageThresholdField.text = ModConf.GarbageThreshold.ToString();
				}
			};
			
			normalGroup.AddSpace(10);
			
			UITextField normalDispatchCountField = (UITextField)normalGroup.AddTextfield(
				$"Trucks per normal dispatch: how many trucks to send for routine pickups \n({Constants.Instance.MinNormalDispatchTruckCount}-{Constants.Instance.MaxNormalDispatchTruckCount}; default is {ModConf.NormalDispatchTruckCount})", 
				ModConf.NormalDispatchTruckCount.ToString(), 
				delegate(string text) { }, 
				delegate(string text) { }
			);
			
			normalGroup.AddSpace(20);
			
			normalDispatchCountField.numericalOnly = true;
			normalDispatchCountField.maxLength = 2;
			normalDispatchCountField.eventTextSubmitted += delegate(UIComponent component, string value)
			{
				int parsedValue;
				if (int.TryParse(value, out parsedValue))
				{
					parsedValue = Math.Max(Constants.Instance.MinNormalDispatchTruckCount, Math.Min(Constants.Instance.MaxNormalDispatchTruckCount, parsedValue));
					ModConf.NormalDispatchTruckCount = parsedValue;
					ModConfiguration.Serialize(configPath, ModConf);
					normalDispatchCountField.text = parsedValue.ToString();
				}
				else
				{
					normalDispatchCountField.text = ModConf.NormalDispatchTruckCount.ToString();
				}
			};
			
			normalGroup.AddSpace(5);
			
			normalGroup.AddCheckbox("District restriction: trucks only serve their own district \n(Triggers immediate recalls if enabled mid-game)", ModConf.DistrictRestrictedService, delegate(bool isChecked)
			{
				ModConf.DistrictRestrictedService = isChecked;
				ModConfiguration.Serialize(configPath, ModConf);
				
				if (isChecked && Dispatcher.IsInitialized)
				{
					Dispatcher.Instance.RecallOutOfDistrictTrucks();
				}
			});
			
			normalGroup.AddSpace(20);
			
			normalGroup.AddCheckbox("Respect facility capacity (turn OFF for aggressive mode \nwhich sends trucks even if facility is full)", ModConf.RespectFacilityCapacity, delegate(bool isChecked)
				{
					ModConf.RespectFacilityCapacity = isChecked;
					ModConfiguration.Serialize(configPath, ModConf);
				});
			
			normalGroup.AddSpace(20);

			uihelperBase.AddSpace(10);
			
			// ===============================================================
			// CARGO MANAGEMENT
			// ===============================================================
			UIHelperBase cargoMgmtGroup = uihelperBase.AddGroup("CARGO MANAGEMENT");
			
			cargoMgmtGroup.AddCheckbox("Recall trucks with low cargo accumulation after # days", ModConf.EnableLowCargoRecall, delegate(bool isChecked)
			{
				ModConf.EnableLowCargoRecall = isChecked;
				ModConfiguration.Serialize(configPath, ModConf);
			});

			UITextField lowCargoRecallDaysField = (UITextField)cargoMgmtGroup.AddTextfield(
				$"Days before low cargo recall: \n({Constants.Instance.MinLowCargoRecallDays:F1}-{Constants.Instance.MaxLowCargoRecallDays:F1}; default is {Constants.Instance.DefaultLowCargoRecallDays:F1}. Set higher for larger cities)", 
				ModConf.LowCargoRecallDays.ToString("F1"), 
				delegate(string text) { }, 
				delegate(string text) { }
			);
			
			cargoMgmtGroup.AddSpace(20);

			lowCargoRecallDaysField.numericalOnly = false;
			lowCargoRecallDaysField.allowFloats = true;
			lowCargoRecallDaysField.maxLength = 4;
			lowCargoRecallDaysField.eventTextSubmitted += delegate(UIComponent component, string value)
			{
				double parsedValue;
				if (double.TryParse(value, out parsedValue))
				{
					parsedValue = Math.Max(Constants.Instance.MinLowCargoRecallDays, Math.Min(Constants.Instance.MaxLowCargoRecallDays, parsedValue));
					ModConf.LowCargoRecallDays = parsedValue;
					ModConfiguration.Serialize(configPath, ModConf);
					lowCargoRecallDaysField.text = parsedValue.ToString("F1");
				}
				else
				{
					lowCargoRecallDaysField.text = ModConf.LowCargoRecallDays.ToString("F1");
				}
			};
			
			UITextField lowCargoThresholdField = (UITextField)cargoMgmtGroup.AddTextfield(
				$"Low cargo threshold ({Constants.Instance.MinLowCargoThreshold}-{Constants.Instance.MaxLowCargoThreshold}; default is {Constants.Instance.DefaultLowCargoThreshold} or {(Constants.Instance.DefaultLowCargoThreshold / (double)Constants.Instance.StandardTruckCapacity * 100):F0}%)",
				ModConf.LowCargoThreshold.ToString(), 
				delegate(string text) { }, 
				delegate(string text) { }
			);
			
			lowCargoThresholdField.numericalOnly = true;
			lowCargoThresholdField.maxLength = 5;
			lowCargoThresholdField.eventTextSubmitted += delegate(UIComponent component, string value)
			{
				int parsedValue;
				if (int.TryParse(value, out parsedValue))
				{
					parsedValue = Math.Max(Constants.Instance.MinLowCargoThreshold, Math.Min(Constants.Instance.MaxLowCargoThreshold, parsedValue));
					ModConf.LowCargoThreshold = parsedValue;
					ModConfiguration.Serialize(configPath, ModConf);
					lowCargoThresholdField.text = parsedValue.ToString();
				}
				else
				{
					lowCargoThresholdField.text = ModConf.LowCargoThreshold.ToString();
				}
			};
			
			cargoMgmtGroup.AddSpace(5);
			
			uihelperBase.AddSpace(10);
			
			// ===============================================================
			// ADVANCED
			// ===============================================================
			UIHelperBase advancedGroup = uihelperBase.AddGroup("ADVANCED");
			
			UITextField scanFrequencyField = (UITextField)advancedGroup.AddTextfield(
				$"Scan frequency: how often to check known buildings for garbage levels \n({Constants.Instance.MinScanFrequencyMs}-{Constants.Instance.MaxScanFrequencyMs} milliseconds; default is {Constants.Instance.DefaultScanFrequencyMs})\nLower = more responsive but more CPU usage. 0 = every frame\n(Note: A full city scan runs automatically every {Constants.Instance.FullScanIntervalSeconds:F0} seconds or when buildings \nare added/removed)", 
				ModConf.ScanFrequencyMs.ToString(), 
				delegate(string text) { }, 
				delegate(string text) { }
			);
			
			scanFrequencyField.numericalOnly = true;
			scanFrequencyField.maxLength = 4;
			scanFrequencyField.eventTextSubmitted += delegate(UIComponent component, string value)
			{
				int parsedValue;
				if (int.TryParse(value, out parsedValue))
				{
					parsedValue = Math.Max(Constants.Instance.MinScanFrequencyMs, Math.Min(Constants.Instance.MaxScanFrequencyMs, parsedValue));
					ModConf.ScanFrequencyMs = parsedValue;
					ModConfiguration.Serialize(configPath, ModConf);
					scanFrequencyField.text = parsedValue.ToString();
				}
				else
				{
					scanFrequencyField.text = ModConf.ScanFrequencyMs.ToString();
				}
			};
			
			advancedGroup.AddSpace(80);

			uihelperBase.AddSpace(10);

			// ===============================================================
			// DEBUG LOGGING
			// ===============================================================
			UIHelperBase debugGroup = uihelperBase.AddGroup("DEBUG LOGGING (WARNING: creates a massive file!)");

			debugGroup.AddCheckbox("Log normal dispatches & assignments", ModConf.DebugLogDispatch, delegate(bool isChecked)
			{
				ModConf.DebugLogDispatch = isChecked;
				ModConfiguration.Serialize(configPath, ModConf);
			});
			
			debugGroup.AddCheckbox("Log emergency dispatches", ModConf.DebugLogEmergency, delegate(bool isChecked)
			{
				ModConf.DebugLogEmergency = isChecked;
				ModConfiguration.Serialize(configPath, ModConf);
			});
			
			debugGroup.AddCheckbox("Log recalls for low cargo and district enforcement", ModConf.DebugLogRecalls, delegate(bool isChecked)
			{
				ModConf.DebugLogRecalls = isChecked;
				ModConfiguration.Serialize(configPath, ModConf);
			});
		}

        private void InitConfigFile()
        {
            try
            {
                string pathName = GameSettings.FindSettingsFileByName("gameSettings").pathName;
                string str = "";
                if (pathName != "")
                {
                    str = Path.GetDirectoryName(pathName) + Path.DirectorySeparatorChar.ToString();
                }
                configPath = str + "GimmeMoreGarbageTrucks2025.xml";
                ModConf = ModConfiguration.Deserialize(configPath);
                if (ModConf == null)
                {
                    ModConf = ModConfiguration.Deserialize("GimmeMoreGarbageTrucks2025.xml");
                    if (ModConf != null && ModConfiguration.Serialize(str + "GimmeMoreGarbageTrucks2025.xml", ModConf))
                    {
                        try
                        {
                            File.Delete("GimmeMoreGarbageTrucks2025.xml");
                        }
                        catch (Exception ex)
                        {
                            Helper.Instance.Log($"GMGT: Failed to delete old config file 'GimmeMoreGarbageTrucks2025.xml': {ex.Message}");
                        }
                    }
                }
                if (ModConf == null)
                {
                    Helper.Instance.Log("GMGT: Failed to load settings from both locations - creating new defaults");
                    
                    ModConf = new ModConfiguration();
                    if (!ModConfiguration.Serialize(configPath, ModConf))
                    {
                        configPath = "GimmeMoreGarbageTrucks2025.xml";
                        ModConfiguration.Serialize(configPath, ModConf);
                    }
                    
                    Helper.Instance.Log($"GMGT: Created default settings file at: {configPath}");
                }
            }
            catch (Exception ex)
            {
                Helper.Instance.Log($"GMGT: Critical error initializing config file: {ex.Message}");
                // Ensure ModConf exists even if everything failed
                if (ModConf == null)
                {
                    ModConf = new ModConfiguration();
                }
            }
        }

        public const string SETTINGFILENAME = "GimmeMoreGarbageTrucks2025.xml";
        public static string configPath;
        public static ModConfiguration ModConf;
    }
}