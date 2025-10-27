using System;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework;
using ICities;

namespace GimmeMoreGarbageTrucks2025
{
    // Main orchestrator: scans city for garbage buildings, manages landfill facilities,
    // dispatches trucks intelligently (emergency swarms vs normal pickups), handles recalls
    // for low cargo/district violations, and tracks active swarms for redirection when targets clear.
    public class Dispatcher : ThreadingExtensionBase
    {
        private Constants _constants;
        private Helper _helper;
        public CustomGarbageTruckAI CustomGarbageTruckAI;
        private bool _initialized;
        private bool _baselined;
        private bool _terminated;
		private DateTime _lastScanTime;

		internal Dictionary<ushort, Landfill> _landfills;           // landfillId -> landfill tracker
		internal Dictionary<ushort, Claimant> _master;              // buildingId -> current claimant
		internal Dictionary<ushort, HashSet<ushort>> _oldtargets;   // vehicleId -> set of previous targets
		internal Dictionary<ushort, PendingSwarm> _pendingSwarms;	// buildingId -> pending swarm info
        private Dictionary<ushort, ushort> _lasttargets;
        private Dictionary<ushort, DateTime> _lastchangetimes;
        private Dictionary<ushort, ushort> _pathfindCount;
		private Dictionary<ushort, DateTime> _lastDispatchTime;
		private HashSet<ushort> _goingHome;
		public HashSet<ushort> _pendingNormalDispatchLogs;
		private Dictionary<ushort, List<TargetChange>> _recentTargetChanges;
		private Dictionary<int, Swarm> _activeSwarms;
		private int _nextSwarmId = 0;
		private Dictionary<ushort, int> _lastKnownPriorityLevels;
		

		// Direct game data monitoring
		private HashSet<ushort> _knownLandfills;
        private HashSet<ushort> _knownGarbageBuildings;
		
		// Cache tracking for ScanForChanges optimization
		private int _lastKnownBuildingCount;
		private DateTime _lastFullScan;

        public static Dispatcher Instance { get; private set; }
        public static bool IsInitialized => Instance != null && Instance._initialized;

        public override void OnCreated(IThreading threading)
        {
            Instance = this;
            _constants = Constants.Instance;
            _helper = Helper.Instance;
            CustomGarbageTruckAI = new CustomGarbageTruckAI();
            _initialized = false;
            _baselined = false;
            _terminated = false;
            base.OnCreated(threading);
        }

        public override void OnBeforeSimulationTick()
        {
            if (_terminated)
            {
                return;
            }
            if (!_helper.GameLoaded)
            {
                _initialized = false;
                _baselined = false;
                return;
            }
            base.OnBeforeSimulationTick();
        }

        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            if (_terminated)
            {
                return;
            }
            if (!_helper.GameLoaded)
            {
                return;
            }
            try
            {
                if (!_initialized)
                {
					_landfills = new Dictionary<ushort, Landfill>();
					_master = new Dictionary<ushort, Claimant>();
					_oldtargets = new Dictionary<ushort, HashSet<ushort>>();
					_lasttargets = new Dictionary<ushort, ushort>();
					_lastchangetimes = new Dictionary<ushort, DateTime>();
					_pathfindCount = new Dictionary<ushort, ushort>();
					_lastDispatchTime = new Dictionary<ushort, DateTime>();
					_goingHome = new HashSet<ushort>();
					_knownLandfills = new HashSet<ushort>();
					_knownGarbageBuildings = new HashSet<ushort>();
					_pendingNormalDispatchLogs = new HashSet<ushort>();
					_lastScanTime = DateTime.MinValue;
					_recentTargetChanges = new Dictionary<ushort, List<TargetChange>>();
					_activeSwarms = new Dictionary<int, Swarm>();
					_pendingSwarms = new Dictionary<ushort, PendingSwarm>();
					_lastKnownPriorityLevels = new Dictionary<ushort, int>();
					
					// Initialize cache tracking
					_lastKnownBuildingCount = 0;
					_lastFullScan = DateTime.MinValue;

					_initialized = true;
                    _helper.NotifyPlayer("Initialized");
                }
                else if (!_baselined)
                {
                    CreateBaseline();
                }
                else
                {
					DateTime now = DateTime.Now;

					// Scan at configured frequency (or every frame if set to 0)
					double scanIntervalSeconds = Identity.ModConf.ScanFrequencyMs / 1000.0;
					if (scanIntervalSeconds == 0 || (now - _lastScanTime).TotalSeconds >= scanIntervalSeconds)
					{
						ScanForChanges();
						ProcessNewLandfills();
						ProcessRemovedLandfills();
						ProcessNewPickups();
						
						if (!Singleton<SimulationManager>.instance.SimulationPaused)
						{
							ProcessIdleGarbageTrucks();
						}
						
						_lastScanTime = now;

						// Periodic cleanup of target change tracking (to prevent memory leak)
						if (_recentTargetChanges != null && _recentTargetChanges.Count > 0)
						{
							DateTime currentRealTime = DateTime.Now;
							List<ushort> trucksToClean = new List<ushort>();
							
							foreach (var kvp in _recentTargetChanges)
							{
								// Remove entries older than 10 real-world seconds
								kvp.Value.RemoveAll(tc => (currentRealTime - tc.Time).TotalSeconds > Constants.Instance.TargetChangeCleanupSeconds);
								
								// If no entries left, mark truck for removal
								if (kvp.Value.Count == 0)
								{
									trucksToClean.Add(kvp.Key);
								}
							}
							
							// Remove empty entries
							foreach (ushort truckId in trucksToClean)
							{
								_recentTargetChanges.Remove(truckId);
							}
						}

						// Process pending swarms - identify truck IDs now that they've spawned
						if (_pendingSwarms != null && _pendingSwarms.Count > 0)
						{
							VehicleManager vehicleManager = Singleton<VehicleManager>.instance;
							BuildingManager buildingManager = Singleton<BuildingManager>.instance;
							DateTime currentTime = Singleton<SimulationManager>.instance.m_currentGameTime;
							List<ushort> completedSwarms = new List<ushort>();
							List<ushort> timedOutSwarms = new List<ushort>();
							
							// Check for timed out pending swarms (older than 5 seconds)
							foreach (var pending in _pendingSwarms)
							{
								TimeSpan age = currentTime - pending.Value.CreatedAt;
								if (age.TotalSeconds > Constants.Instance.SwarmTimeoutSeconds)
								{
									timedOutSwarms.Add(pending.Key);
									if (Identity.ModConf.DebugLogEmergency)
										_helper.LogWithTimestamp($"EMERGENCIES:: Pending swarm timeout for building {pending.Key} (age: {age.TotalSeconds:F1}s, expected {pending.Value.ExpectedTruckCount} trucks)");
								}
							}
							
							// Remove any timed out swarms
							foreach (ushort buildingId in timedOutSwarms)
							{
								_pendingSwarms.Remove(buildingId);
								if (Identity.ModConf.DebugLogEmergency)
								_helper.LogWithTimestamp($"EMERGENCIES:: Cleaned up timed-out pending swarm for building {buildingId}");
							}
							
							// Process remaining pending swarms
							foreach (var pending in _pendingSwarms)
							{
								ushort targetBuilding = pending.Key;
								int expectedCount = pending.Value.ExpectedTruckCount;
							
								// Find all trucks targeting this building from any landfill
								List<ushort> foundTrucks = new List<ushort>();
								
								foreach (ushort landfillId in _landfills.Keys)
								{
									Building landfillBuilding = buildingManager.m_buildings.m_buffer[landfillId];
									ushort vehicleId = landfillBuilding.m_ownVehicles;
									int safety = 0;
									
									while (vehicleId != 0 && safety < Constants.Instance.MaxVehicleIterations)
									{
										Vehicle vehicle = vehicleManager.m_vehicles.m_buffer[vehicleId];
										
										if (vehicle.m_targetBuilding == targetBuilding)
										{
											foundTrucks.Add(vehicleId);
										}
										
										vehicleId = vehicle.m_nextOwnVehicle;
										safety++;
									}
								}
								
								// CREATE ZEE SWARM!
								if (foundTrucks.Count >= expectedCount)
								{
									int swarmId = _nextSwarmId++;
									_activeSwarms[swarmId] = new Swarm(targetBuilding, foundTrucks);
									completedSwarms.Add(targetBuilding);
									
									if (Identity.ModConf.DebugLogEmergency)
									{
										string truckList = string.Join(", ", foundTrucks.Select(id => id.ToString()).ToArray());
										Helper.Instance.LogWithTimestamp($"EMERGENCIES:: Swarm {swarmId} created: {foundTrucks.Count} trucks targeting building {targetBuilding}: [{truckList}]");
									}
								}
								else if (Identity.ModConf.DebugLogEmergency)
								{
									Helper.Instance.LogWithTimestamp($"EMERGENCIES:: Pending swarm for building {targetBuilding}: found {foundTrucks.Count}/{expectedCount} trucks, waiting...");
								}
							}
							
						// Remove completed swarms from pending
						foreach (ushort completedBuilding in completedSwarms)
						{
							_pendingSwarms.Remove(completedBuilding);
						}
						}

						// Process active swarms - if target buildings are cleared, redirect trucks
						if (_activeSwarms != null && _activeSwarms.Count > 0)
						{
							VehicleManager vehicleManager = Singleton<VehicleManager>.instance;
							BuildingManager buildingManager = Singleton<BuildingManager>.instance;
							List<int> completedSwarmIds = new List<int>();
							
							foreach (var swarmEntry in _activeSwarms)
							{
								int swarmId = swarmEntry.Key;
								Swarm swarm = swarmEntry.Value;
								
								// Check if target building's garbage has been cleared
								Building targetBuilding = buildingManager.m_buildings.m_buffer[swarm.TargetBuilding];
								int currentGarbage = targetBuilding.Info.m_buildingAI.GetGarbageAmount(swarm.TargetBuilding, ref targetBuilding);
								
								if (currentGarbage <= 100)
								{
									// Target cleared! Find trucks still en route
									List<ushort> trucksToRedirect = new List<ushort>();
									
									foreach (ushort truckId in swarm.Trucks)
									{
										// Check if truck still exists and is targeting the cleared building
										if (Helper.IsGarbageTruck(truckId))
										{
											Vehicle truck = vehicleManager.m_vehicles.m_buffer[truckId];
											if (truck.m_targetBuilding == swarm.TargetBuilding)
											{
												trucksToRedirect.Add(truckId);
											}
										}
									}
									
									if (Identity.ModConf.DebugLogEmergency)
										Helper.Instance.LogWithTimestamp($"EMERGENCIES:: Swarm {swarmId}: Target building {swarm.TargetBuilding} cleared (garbage: {currentGarbage}), redirecting {trucksToRedirect.Count} trucks");
									
									if (trucksToRedirect.Count > 0)
									{
										// Get the landfill that owns these trucks
										ushort landfillId = vehicleManager.m_vehicles.m_buffer[trucksToRedirect[0]].m_sourceBuilding;
										
										if (_landfills.ContainsKey(landfillId))
										{
											// Use landfill's existing tracked targets (respecting service range)
											List<KeyValuePair<ushort, int>> allTargets = new List<KeyValuePair<ushort, int>>();

											// Check primary range first (closer buildings)
											foreach (ushort targetId in _landfills[landfillId]._primary)
											{
												// Check district restriction
												if (!Helper.IsValidDistrictTarget(landfillId, targetId))
													continue;
												
												Building building = buildingManager.m_buildings.m_buffer[targetId];
												if ((building.m_flags & Building.Flags.Created) != 0)
												{
													int garbageAmount = building.Info.m_buildingAI.GetGarbageAmount(targetId, ref building);
													if (garbageAmount > 0)
													{
														allTargets.Add(new KeyValuePair<ushort, int>(targetId, garbageAmount));
													}
												}
											}

											// Add secondary range if needed
											foreach (ushort targetId in _landfills[landfillId]._secondary)
											{
												// Check district restriction
												if (!Helper.IsValidDistrictTarget(landfillId, targetId))
													continue;
												
												Building building = buildingManager.m_buildings.m_buffer[targetId];
												if ((building.m_flags & Building.Flags.Created) != 0)
												{
													int garbageAmount = building.Info.m_buildingAI.GetGarbageAmount(targetId, ref building);
													if (garbageAmount > 0)
													{
														allTargets.Add(new KeyValuePair<ushort, int>(targetId, garbageAmount));
													}
												}
											}
											
											// Sort by garbage amount (descending)
											allTargets.Sort((a, b) => b.Value.CompareTo(a.Value));
											
											// Redirect trucks in groups of X (configurable)
											int currentTargetIndex = 0;
											int trucksAssignedToCurrentTarget = 0;
											int trucksRedirected = 0;

											foreach (ushort truckId in trucksToRedirect)
											{
												// Move to next target if current one has X trucks
												if (trucksAssignedToCurrentTarget >= Identity.ModConf.SwarmRedirectGroupSize)
												{
													currentTargetIndex++;
													trucksAssignedToCurrentTarget = 0;
												}
												
												// Safety check
												if (currentTargetIndex >= allTargets.Count)
												{
													if (Identity.ModConf.DebugLogEmergency)
														Helper.Instance.LogWithTimestamp($"EMERGENCIES:: WARNING: Ran out of targets for swarm truck {truckId}");
													break;
												}
												
												// Reassign this truck
												ushort newTarget = allTargets[currentTargetIndex].Key;
												int newTargetGarbage = allTargets[currentTargetIndex].Value;
												
												CustomGarbageTruckAI.SetTarget(truckId, ref vehicleManager.m_vehicles.m_buffer[truckId], newTarget);
												trucksAssignedToCurrentTarget++;
												trucksRedirected++;
												
											}
											
											if (Identity.ModConf.DebugLogEmergency)
												Helper.Instance.LogWithTimestamp($"EMERGENCIES:: Swarm {swarmId} divert complete: redirected {trucksRedirected} trucks");
										}
									}
									
									// Mark this swarm as complete
									completedSwarmIds.Add(swarmId);
								}
							}
							
							// Remove completed swarms
							foreach (int swarmId in completedSwarmIds)
							{
								_activeSwarms.Remove(swarmId);
							}
						}
					}
						
					UpdateGarbageTrucks();
					RecallDistantTransferTrucks();
                }
            }
            catch (Exception ex)
            {
                string text = string.Format("Failed to {0}\r\n", (!_initialized) ? "initialize" : "update");
                text += string.Format("Error: {0}\r\n", ex.Message);
                text += "\r\n";
                text += "==== STACK TRACE ====\r\n";
                text += ex.StackTrace;
                _helper.LogWithTimestamp(text);
                if (!_initialized)
                {
                    _terminated = true;
                }
            }
            base.OnUpdate(realTimeDelta, simulationTimeDelta);
        }

        public override void OnReleased()
        {
            _initialized = false;
            _baselined = false;
            _terminated = false;
            Instance = null;
            base.OnReleased();
        }

        private void CreateBaseline()
        {
            BuildingManager buildingManager = Singleton<BuildingManager>.instance;
            
            // Scan all buildings to find landfills and buildings with garbage
            for (ushort i = 1; i < buildingManager.m_buildings.m_size; i++)
            {
                if ((buildingManager.m_buildings.m_buffer[i].m_flags & Building.Flags.Created) != 0)
                {
                    if (IsLandfillSite(i))
                    {
                        _landfills.Add(i, new Landfill(i, ref _master, ref _oldtargets, ref _lastchangetimes));
                        _knownLandfills.Add(i);
                    }
                    else if (Helper.IsBuildingWithGarbage(i))
                    {
                        _knownGarbageBuildings.Add(i);
                    }
                }
            }

            // Add all garbage buildings to all landfills
            foreach (ushort garbageBuilding in _knownGarbageBuildings)
            {
                foreach (ushort landfillId in _landfills.Keys)
                {
                    _landfills[landfillId].AddPickup(garbageBuilding);
                }
            }

            _baselined = true;
        }

		// Scans all buildings to detect buildings with garbage as well as any newly-built landfill facilities.
		// Tracks buildings that exceed user-config threshold OR have warning flags for emergency response.
		// Only does full scan when building count changes, otherwise just updates known buildings.
		private void ScanForChanges()
		{
			BuildingManager buildingManager = Singleton<BuildingManager>.instance;
			int currentBuildingCount = buildingManager.m_buildingCount;
			DateTime now = Singleton<SimulationManager>.instance.m_currentGameTime;
			
			// Check if we need a full scan (building count changed or been >10 real-world seconds since last full scan)
			bool needFullScan = (currentBuildingCount != _lastKnownBuildingCount) || 
								((now - _lastFullScan).TotalSeconds > Constants.Instance.FullScanIntervalSeconds);
			
			if (needFullScan)
			{
				// Full scan - check all buildings
				HashSet<ushort> foundLandfills = new HashSet<ushort>();
				HashSet<ushort> foundGarbageBuildings = new HashSet<ushort>();
				
				for (ushort i = 1; i < buildingManager.m_buildings.m_size; i++)
				{
					if ((buildingManager.m_buildings.m_buffer[i].m_flags & Building.Flags.Created) != 0)
					{
						bool isLandfill = IsLandfillSite(i);
						bool hasGarbage = Helper.IsBuildingWithGarbage(i);
						bool hasWarningFlag = Helper.GetGarbagePriorityLevel(i) >= 1;
						
						if (isLandfill)
						{
							foundLandfills.Add(i);
						}
						
						if (hasGarbage || hasWarningFlag)
						{
							foundGarbageBuildings.Add(i);
						}
						
						// Track priority changes
						if (Identity.ModConf.DebugLogEmergency)
						{
							int currentPriority = Helper.GetGarbagePriorityLevel(i);
							int lastPriority = _lastKnownPriorityLevels.ContainsKey(i) ? _lastKnownPriorityLevels[i] : 0;
							
							if (currentPriority > lastPriority)
							{
								string priorityName = currentPriority == 2 ? "RED FLAG" : currentPriority == 1 ? "YELLOW FLAG" : "none";
								int garbageAmount = buildingManager.m_buildings.m_buffer[i].Info.m_buildingAI.GetGarbageAmount(i, ref buildingManager.m_buildings.m_buffer[i]);
								_helper.LogWithTimestamp($"EMERGENCIES (DISPATCHER):: Building {i} warning escalated: {lastPriority} → {currentPriority} ({priorityName}), Garbage: {garbageAmount}, Threshold: {Identity.ModConf.GarbageThreshold}");
							}
							
							_lastKnownPriorityLevels[i] = currentPriority;
						}
					}
					else
					{
						// Building deleted - clean up tracking
						_lastKnownPriorityLevels.Remove(i);
					}
				}
				
				// Update our caches
				_knownLandfills = foundLandfills;
				_knownGarbageBuildings = foundGarbageBuildings;
				_lastKnownBuildingCount = currentBuildingCount;
				_lastFullScan = now;
			}
			else
			{
				// Quick scan - only update known buildings
				List<ushort> toRemove = new List<ushort>();
				
				// Check known garbage buildings
				foreach (ushort buildingId in _knownGarbageBuildings)
				{
					Building building = buildingManager.m_buildings.m_buffer[buildingId];
					
					if ((building.m_flags & Building.Flags.Created) == 0)
					{
						// Building deleted
						toRemove.Add(buildingId);
						_lastKnownPriorityLevels.Remove(buildingId);
						continue;
					}
					
					bool hasGarbage = Helper.IsBuildingWithGarbage(buildingId);
					bool hasWarningFlag = Helper.GetGarbagePriorityLevel(buildingId) >= 1;
					
					if (!hasGarbage && !hasWarningFlag)
					{
						// No longer has garbage
						toRemove.Add(buildingId);
					}
					
					// Track priority changes
					if (Identity.ModConf.DebugLogEmergency && hasWarningFlag)
					{
						int currentPriority = Helper.GetGarbagePriorityLevel(buildingId);
						int lastPriority = _lastKnownPriorityLevels.ContainsKey(buildingId) ? _lastKnownPriorityLevels[buildingId] : 0;
						
						if (currentPriority > lastPriority)
						{
							string priorityName = currentPriority == 2 ? "RED FLAG" : currentPriority == 1 ? "YELLOW FLAG" : "none";
							int garbageAmount = building.Info.m_buildingAI.GetGarbageAmount(buildingId, ref building);
							_helper.LogWithTimestamp($"EMERGENCIES (DISPATCHER):: Building {buildingId} warning escalated: {lastPriority} → {currentPriority} ({priorityName}), Garbage: {garbageAmount}, Threshold: {Identity.ModConf.GarbageThreshold}");
						}
						
						_lastKnownPriorityLevels[buildingId] = currentPriority;
					}
				}
				
				// Remove buildings that no longer have garbage
				foreach (ushort buildingId in toRemove)
				{
					_knownGarbageBuildings.Remove(buildingId);
					_lastKnownPriorityLevels.Remove(buildingId);
				}
			}
		}

        private void ProcessNewLandfills()
        {
            foreach (ushort landfillId in _knownLandfills)
            {
                if (!_landfills.ContainsKey(landfillId))
                {
                    _landfills.Add(landfillId, new Landfill(landfillId, ref _master, ref _oldtargets, ref _lastchangetimes));
                    foreach (ushort garbageBuilding in _knownGarbageBuildings)
                    {
                        _landfills[landfillId].AddPickup(garbageBuilding);
                    }
                }
            }
        }

        private void ProcessRemovedLandfills()
        {
            List<ushort> toRemove = new List<ushort>();
            foreach (ushort landfillId in _landfills.Keys)
            {
                if (!_knownLandfills.Contains(landfillId))
                {
                    toRemove.Add(landfillId);
                }
            }
            
            foreach (ushort landfillId in toRemove)
            {
                _landfills.Remove(landfillId);
            }
        }

		// Adds buildings with garbage to landfill tracking lists.
		// ScanForChanges() identifies buildings needing service (including those with warning flags),
		// this method simply registers them with all landfills for potential dispatch.
		private void ProcessNewPickups()
		{
			foreach (ushort buildingId in _knownGarbageBuildings)
			{
				foreach (ushort landfillId in _landfills.Keys)
				{
					_landfills[landfillId].AddPickup(buildingId);
				}
			}
		}

        private void ProcessIdleGarbageTrucks()
        {
            foreach (ushort landfillId in _knownLandfills)
            {
                if (_landfills.ContainsKey(landfillId))
                {
                    _landfills[landfillId].DispatchIdleVehicle();
                }
            }
        }

        private void UpdateGarbageTrucks()
        {
            VehicleManager vehicleManager = Singleton<VehicleManager>.instance;
            
            // Process vehicle removal
            for (ushort vehicleId = 1; vehicleId < vehicleManager.m_vehicles.m_size; vehicleId++)
            {
                if ((vehicleManager.m_vehicles.m_buffer[vehicleId].m_flags & Vehicle.Flags.Created) == 0 && 
                    Helper.IsGarbageTruck(vehicleId))
                {
                    if (_lasttargets.ContainsKey(vehicleId) && Helper.IsBuildingWithGarbage(_lasttargets[vehicleId]))
                    {
                        foreach (ushort key in _landfills.Keys)
                        {
                            _landfills[key].AddPickup(_lasttargets[vehicleId]);
                        }
                    }
                    _oldtargets.Remove(vehicleId);
                    if (_lasttargets.ContainsKey(vehicleId))
                    {
                        _master.Remove(_lasttargets[vehicleId]);
                    }
					_lasttargets.Remove(vehicleId);
					_lastchangetimes.Remove(vehicleId);
					_pathfindCount.Remove(vehicleId);
					_lastDispatchTime.Remove(vehicleId);
					_goingHome.Remove(vehicleId);
					_recentTargetChanges.Remove(vehicleId);
                }
            }

            if (!Singleton<SimulationManager>.instance.SimulationPaused)
            {
				for (ushort vehicleId = 1; vehicleId < vehicleManager.m_vehicles.m_size; vehicleId++)
				{
					if (Helper.IsGarbageTruck(vehicleId))
					{
						Vehicle vehicle = vehicleManager.m_vehicles.m_buffer[vehicleId];
						
						if (Identity.ModConf.DebugLogDispatch && _pendingNormalDispatchLogs != null && 
							_pendingNormalDispatchLogs.Contains(vehicle.m_targetBuilding) && 
							!_lastDispatchTime.ContainsKey(vehicleId))  // New truck
						{
							byte landfillDistrict = Helper.GetBuildingDistrict(vehicle.m_sourceBuilding);
							byte targetDistrict = Helper.GetBuildingDistrict(vehicle.m_targetBuilding);
							_helper.LogWithTimestamp($"DISPATCHES:: Truck {vehicleId} normal dispatch: Landfill {vehicle.m_sourceBuilding} (District {landfillDistrict}) → Building {vehicle.m_targetBuilding} (District {targetDistrict})");
						}
						
						if (_landfills.ContainsKey(vehicle.m_sourceBuilding) && 
							(vehicle.m_flags & (Vehicle.Flags.Created | Vehicle.Flags.Deleted)) == Vehicle.Flags.Created &&
							(vehicle.m_flags & Vehicle.Flags.Spawned) != 0 && vehicle.m_path != 0U)
						{
							// Skip trucks from emptying landfills - let vanilla handle them
							BuildingManager buildingManager = Singleton<BuildingManager>.instance;
							if (vehicle.m_sourceBuilding != 0)
							{
								Building sourceBuilding = buildingManager.m_buildings.m_buffer[vehicle.m_sourceBuilding];
								if ((sourceBuilding.m_flags & Building.Flags.Downgrading) != 0)
								{
									continue;
								}
							}
							
							// Skip all processing for trucks that are going home
							if (_goingHome.Contains(vehicleId))
							{
								continue;
							}
							
							// Track dispatch time for this truck
							if (!_lastDispatchTime.ContainsKey(vehicleId))
							{
								_lastDispatchTime[vehicleId] = Singleton<SimulationManager>.instance.m_currentGameTime;
							}

							// Calculate time on route for recall checks
							TimeSpan timeOnRoute = TimeSpan.Zero;
							if (_lastDispatchTime.ContainsKey(vehicleId))
							{
								timeOnRoute = Singleton<SimulationManager>.instance.m_currentGameTime - _lastDispatchTime[vehicleId];
							}

							// LOW CARGO RECALL
							if (Identity.ModConf.EnableLowCargoRecall && timeOnRoute.TotalDays > Identity.ModConf.LowCargoRecallDays && vehicle.m_transferSize < Identity.ModConf.LowCargoThreshold)
							{
								if (Identity.ModConf.DebugLogRecalls)
									_helper.LogWithTimestamp($"RECALLS:: Truck {vehicleId}: Low cargo recall after {timeOnRoute.TotalDays:F2} days with only {vehicle.m_transferSize} new cargo (threshold: {Identity.ModConf.LowCargoThreshold})");
								
								SendTruckHome(vehicleId, vehicleManager);
								
								continue;
							}
							
							// SAFETY NET: DISTRICT VOLATION CHECK
							// Sends out-of-district trucks home when restriction is enabled
							if (Identity.ModConf.DistrictRestrictedService)
							{
								byte landfillDistrict = Helper.GetBuildingDistrict(vehicle.m_sourceBuilding);
								if (landfillDistrict != 0 && vehicle.m_targetBuilding != 0)
								{
									byte targetDistrict = Helper.GetBuildingDistrict(vehicle.m_targetBuilding);
									if (targetDistrict != landfillDistrict)
									{
										if (Identity.ModConf.DebugLogRecalls)
											_helper.LogWithTimestamp($"RECALLS:: District violation: Recalling truck {vehicleId} targeting District {targetDistrict} from District {landfillDistrict} facility");
										
										SendTruckHome(vehicleId, vehicleManager);
										
										continue;
									}
								}
							}
							
                            _pathfindCount.Remove(vehicleId);
							int garbageTruckStatus = GetGarbageTruckStatus(ref vehicle);
							if (garbageTruckStatus == 0)
							{
								if (_lasttargets.ContainsKey(vehicleId))
								{
									if (Helper.IsBuildingWithGarbage(_lasttargets[vehicleId]))
									{
										foreach (ushort key2 in _landfills.Keys)
										{
											_landfills[key2].AddPickup(_lasttargets[vehicleId]);
										}
									}
									_lasttargets.Remove(vehicleId);
								}
								_oldtargets.Remove(vehicleId);  // Clear any failed targets when truck returns home
							}
							else if (garbageTruckStatus == 4)
							{
								// Don't assign new targets to trucks that are going home
								if (_goingHome.Contains(vehicleId))
								{
									continue;
								}
								
								ushort assignedTarget = _landfills[vehicle.m_sourceBuilding].AssignTarget(vehicleId);

                                if (assignedTarget != 0 && assignedTarget != vehicle.m_targetBuilding)
                                {
                                    if (Helper.IsBuildingWithGarbage(vehicle.m_targetBuilding))
                                    {
                                        foreach (ushort key3 in _landfills.Keys)
                                        {
                                            _landfills[key3].AddPickup(vehicle.m_targetBuilding);
                                        }
                                    }
                                    _master.Remove(vehicle.m_targetBuilding);
                                    if (garbageTruckStatus == 5)
                                    {
                                        _lasttargets[vehicleId] = vehicle.m_targetBuilding;
                                        if (_lastchangetimes.ContainsKey(vehicleId))
                                        {
                                            _lastchangetimes[vehicleId] = Singleton<SimulationManager>.instance.m_currentGameTime;
                                        }
                                        else
                                        {
                                            _lastchangetimes.Add(vehicleId, Singleton<SimulationManager>.instance.m_currentGameTime);
                                        }
                                    }
                                    CustomGarbageTruckAI.SetTarget(vehicleId, ref vehicleManager.m_vehicles.m_buffer[vehicleId], assignedTarget);
									
									// Track rapid target changes and clean up old entries to prevent memory leaks
									DateTime currentRealTime = DateTime.Now;
									if (!_recentTargetChanges.ContainsKey(vehicleId))
									{
										_recentTargetChanges[vehicleId] = new List<TargetChange>();
									}
									_recentTargetChanges[vehicleId].Add(new TargetChange(assignedTarget, currentRealTime));

									// Remove old entries (older than 10 real seconds)
									_recentTargetChanges[vehicleId].RemoveAll(tc => (currentRealTime - tc.Time).TotalSeconds > Constants.Instance.TargetChangeCleanupSeconds);

									// Safety: Cap list size at 20 entries per truck
									if (_recentTargetChanges[vehicleId].Count > Constants.Instance.TargetChangeListLimit)
									{
										_recentTargetChanges[vehicleId] = _recentTargetChanges[vehicleId]
											.OrderByDescending(tc => tc.Time)
											.Take(Constants.Instance.TargetChangeListLimit)
											.ToList();
										
										if (Identity.ModConf.DebugLogRecalls)
											_helper.LogWithTimestamp($"RECALLS:: WARNING: Truck {vehicleId} target change list capped at {Constants.Instance.TargetChangeListLimit} entries (excessive target changes)");
									}
                                }
                                else if (_master.ContainsKey(vehicle.m_targetBuilding))
                                {
                                    if (_master[vehicle.m_targetBuilding].Vehicle != vehicleId)
                                    {
                                        _master[vehicle.m_targetBuilding] = new Claimant(vehicleId, vehicle.m_targetBuilding);
                                    }
                                }
                                else
                                {
                                    _master.Add(vehicle.m_targetBuilding, new Claimant(vehicleId, vehicle.m_targetBuilding));
                                }
                            }
                        }
                    }
                }
            }

            // Handle pathfinding failures
            for (ushort vehicleId = 1; vehicleId < vehicleManager.m_vehicles.m_size; vehicleId++)
            {
                if (Helper.IsGarbageTruck(vehicleId))
                {
                    Vehicle vehicle = vehicleManager.m_vehicles.m_buffer[vehicleId];
                    if ((vehicle.m_flags & Vehicle.Flags.WaitingPath) != 0 && vehicle.m_path != 0U)
                    {
                        PathManager pathManager = Singleton<PathManager>.instance;
                        byte pathFindFlags = pathManager.m_pathUnits.m_buffer[vehicle.m_path].m_pathFindFlags;
                        if ((pathFindFlags & PathUnit.FLAG_READY) != 0)
                        {
                            _pathfindCount.Remove(vehicleId);
                        }
                        else if ((pathFindFlags & PathUnit.FLAG_FAILED) != 0)
                        {
                            int garbageTruckStatus = GetGarbageTruckStatus(ref vehicle);
                            if (_lasttargets.ContainsKey(vehicleId))
                            {
                                vehicleManager.m_vehicles.m_buffer[vehicleId].m_flags &= ~Vehicle.Flags.WaitingPath;
                                pathManager.ReleasePath(vehicle.m_path);
                                vehicleManager.m_vehicles.m_buffer[vehicleId].m_path = 0U;
                                CustomGarbageTruckAI.SetTarget(vehicleId, ref vehicleManager.m_vehicles.m_buffer[vehicleId], _lasttargets[vehicleId]);
                                _lasttargets.Remove(vehicleId);
                            }
                            else if ((garbageTruckStatus == 4 || garbageTruckStatus == 5) && 
                                    (vehicle.m_flags & Vehicle.Flags.Spawned) != 0 && 
                                    _landfills.ContainsKey(vehicle.m_sourceBuilding) && 
                                    (!_pathfindCount.ContainsKey(vehicleId) || _pathfindCount[vehicleId] < Constants.Instance.MaxRetryAttempts))
                            {
                                if (!_pathfindCount.ContainsKey(vehicleId))
                                {
                                    _pathfindCount[vehicleId] = 0;
                                }
                                _pathfindCount[vehicleId]++;
                                ushort unclaimedTarget = _landfills[vehicle.m_sourceBuilding].GetUnclaimedTarget(vehicleId);
                                if (unclaimedTarget == 0)
                                {
                                    _pathfindCount[vehicleId] = ushort.MaxValue;
                                }
                                else
                                {
									if (_oldtargets != null)
									{
										if (!_oldtargets.ContainsKey(vehicleId))
										{
											_oldtargets.Add(vehicleId, new HashSet<ushort>());
										}
										
										// Safety: Cap failed targets at 20 per truck
										if (_oldtargets[vehicleId].Count >= Constants.Instance.MaxFailedTargetsPerTruck)
										{
											if (Identity.ModConf.DebugLogRecalls)
												_helper.LogWithTimestamp($"RECALLS:: WARNING: Truck {vehicleId} has failed pathfinding to {Constants.Instance.MaxFailedTargetsPerTruck}+ buildings, sending home");
											
											// Truck is stuck in pathfinding loop - send it home
											SendTruckHome(vehicleId, vehicleManager);
											_oldtargets.Remove(vehicleId);
											continue;
										}
										
										_oldtargets[vehicleId].Add(unclaimedTarget);
									}
                                    vehicleManager.m_vehicles.m_buffer[vehicleId].m_flags &= ~Vehicle.Flags.WaitingPath;
                                    pathManager.ReleasePath(vehicle.m_path);
                                    vehicleManager.m_vehicles.m_buffer[vehicleId].m_path = 0U;
                                    CustomGarbageTruckAI.SetTarget(vehicleId, ref vehicleManager.m_vehicles.m_buffer[vehicleId], unclaimedTarget);
                                }
                            }
                        }
                    }
                }
            }
			
			// Clean up old pending dispatch logs (after each scan, all trucks should have spawned)
			if (_pendingNormalDispatchLogs != null && _pendingNormalDispatchLogs.Count > 0)
			{
				// Simple time-based cleanup - just clear the whole set after logging
				// This runs every scan, so pending logs only last 1-2 scans max
				_pendingNormalDispatchLogs.Clear();
			}
			
        }

		private void RecallDistantTransferTrucks()
		{
			if (Singleton<SimulationManager>.instance.SimulationPaused)
				return;

			VehicleManager vehicleManager = Singleton<VehicleManager>.instance;
			BuildingManager buildingManager = Singleton<BuildingManager>.instance;

			foreach (ushort landfillId in _landfills.Keys)
			{
				byte landfillDistrict = Helper.GetBuildingDistrict(landfillId);
				
				// Check if this landfill has red flag emergencies in its district
				bool hasRedFlagEmergency = false;
				foreach (ushort targetId in _landfills[landfillId]._primary)
				{
					if (Helper.GetGarbagePriorityLevel(targetId) == 2)
					{
						hasRedFlagEmergency = true;
						break;
					}
				}

				if (!hasRedFlagEmergency)
				{
					foreach (ushort targetId in _landfills[landfillId]._secondary)
					{
						if (Helper.GetGarbagePriorityLevel(targetId) == 2)
						{
							hasRedFlagEmergency = true;
							break;
						}
					}
				}

				if (!hasRedFlagEmergency)
					continue;

				// Check all trucks owned by this landfill
				Building landfillBuilding = buildingManager.m_buildings.m_buffer[landfillId];
				ushort vehicleId = landfillBuilding.m_ownVehicles;
				int safety = 0;

				while (vehicleId != 0 && safety < Constants.Instance.MaxVehicleIterations)
				{
					Vehicle vehicle = vehicleManager.m_vehicles.m_buffer[vehicleId];
					ushort nextVehicle = vehicle.m_nextOwnVehicle;

					// Check if truck is doing transfer work (status 2) and is outside home district
					int status = GetGarbageTruckStatus(ref vehicle);
					if (status == 2 && !Helper.IsTruckInHomeDistrict(vehicleId))
					{
						if (Identity.ModConf.DebugLogEmergency)
							_helper.LogWithTimestamp($"EMERGENCIES:: Emergency recall: Truck {vehicleId} from landfill {landfillId} doing out-of-district transfer during red flag emergency");

						// Recall truck - send it home (target = 0)
						CustomGarbageTruckAI.SetTarget(vehicleId, ref vehicleManager.m_vehicles.m_buffer[vehicleId], 0);
					}

					vehicleId = nextVehicle;
					safety++;
				}
			}
		}

		public void RecallOutOfDistrictTrucks()
		{
			if (!Identity.ModConf.DistrictRestrictedService)
				return;

			VehicleManager vehicleManager = Singleton<VehicleManager>.instance;
			BuildingManager buildingManager = Singleton<BuildingManager>.instance;

			foreach (ushort landfillId in _landfills.Keys)
			{
				byte landfillDistrict = Helper.GetBuildingDistrict(landfillId);
				
				if (landfillDistrict == 0)
					continue;

				Building landfillBuilding = buildingManager.m_buildings.m_buffer[landfillId];
				ushort vehicleId = landfillBuilding.m_ownVehicles;
				int safety = 0;

				while (vehicleId != 0 && safety < Constants.Instance.MaxVehicleIterations)
				{
					Vehicle vehicle = vehicleManager.m_vehicles.m_buffer[vehicleId];
					ushort nextVehicle = vehicle.m_nextOwnVehicle;

					if (vehicle.m_targetBuilding != 0)
					{
						byte targetDistrict = Helper.GetBuildingDistrict(vehicle.m_targetBuilding);
						
						if (targetDistrict != landfillDistrict)
						{
							if (Identity.ModConf.DebugLogRecalls)
								_helper.LogWithTimestamp($"RECALLS:: Bulk recall: Truck {vehicleId} from district {targetDistrict} back to district {landfillDistrict}");

							SendTruckHomeInternal(vehicleId, vehicleManager, _goingHome);
						}
					}

					vehicleId = nextVehicle;
					safety++;
				}
			}
		}

        public static int GetGarbageTruckStatus(ref Vehicle data)
        {
            if ((data.m_flags & Vehicle.Flags.TransferToSource) == 0)
            {
                if ((data.m_flags & Vehicle.Flags.TransferToTarget) != 0)
                {
                    if ((data.m_flags & Vehicle.Flags.GoingBack) != 0)
                    {
                        return 0;
                    }
                    if ((data.m_flags & Vehicle.Flags.WaitingTarget) != 0)
                    {
                        return 1;
                    }
                    if (data.m_targetBuilding != 0)
                    {
                        return 2;
                    }
                }
                return 3;
            }
            if ((data.m_flags & Vehicle.Flags.GoingBack) != 0)
            {
                return 0;
            }
            if ((data.m_flags & Vehicle.Flags.WaitingTarget) != 0)
            {
                return 4;
            }
            return 5;
        }
		
		private void SendTruckHome(ushort vehicleId, VehicleManager vehicleManager)
		{
			SendTruckHomeInternal(vehicleId, vehicleManager, _goingHome);
		}

		private static void SendTruckHomeInternal(ushort vehicleId, VehicleManager vehicleManager, HashSet<ushort> goingHomeSet)
		// Yes, this method is a little weird. Here's why:
		// Trying to directly send a truck home results in repeated vanilla overrides:
		// The game will only allow trucks to go home if their holds are full ... or if the base facility is on fire.
		// So, temporarily, we're going to fake the truck's cargo value and then reset it after vanilla sets the GoingBack flag.
		// Hey, it works! Don't judge me!
		{
			// Save original cargo
			ushort originalCargo = vehicleManager.m_vehicles.m_buffer[vehicleId].m_transferSize;
			
			// Get cargo capacity
			Vehicle currentVehicle = vehicleManager.m_vehicles.m_buffer[vehicleId];
			VehicleInfo info = currentVehicle.Info;
			int capacity = ((GarbageTruckAI)info.m_vehicleAI).m_cargoCapacity;
			
			// Temporarily set truck to full to trick vanilla AI into sending it home
			vehicleManager.m_vehicles.m_buffer[vehicleId].m_transferSize = (ushort)capacity;
			
			// Send truck home
			if (Dispatcher.IsInitialized)
			{
				Dispatcher.Instance.CustomGarbageTruckAI.SetTarget(vehicleId, ref vehicleManager.m_vehicles.m_buffer[vehicleId], 0);
			}
			
			// Immediately restore original cargo
			vehicleManager.m_vehicles.m_buffer[vehicleId].m_transferSize = originalCargo;
			
			goingHomeSet.Add(vehicleId);
		}
		
        private bool IsLandfillSite(ushort buildingId)
        {
            if (buildingId == 0) return false;
            
            BuildingManager instance = Singleton<BuildingManager>.instance;
            if ((instance.m_buildings.m_buffer[buildingId].m_flags & Building.Flags.Created) == 0)
                return false;
                
            return instance.m_buildings.m_buffer[buildingId].Info.m_buildingAI is LandfillSiteAI;
        }
    }
	
	class TargetChange
	{
		public ushort Target;
		public DateTime Time;
		
		public TargetChange(ushort target, DateTime time)
		{
			Target = target;
			Time = time;
		}
	}

	class Swarm
	{
		public ushort TargetBuilding;
		public List<ushort> Trucks;
		
		public Swarm(ushort targetBuilding, List<ushort> trucks)
		{
			TargetBuilding = targetBuilding;
			Trucks = trucks;
		}
	}
	
	public class PendingSwarm
	{
		public int ExpectedTruckCount;
		public DateTime CreatedAt;
		
		public PendingSwarm(int count, DateTime createdTime)
		{
			ExpectedTruckCount = count;
			CreatedAt = createdTime;
		}
	}
	
}