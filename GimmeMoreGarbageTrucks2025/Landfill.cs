using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Plugins;
using UnityEngine;

namespace GimmeMoreGarbageTrucks2025
{
    // Represents a single landfill/incinerator facility and manages its service area.
    // Tracks buildings in primary (same district) and secondary (within range) zones,
    // dispatches trucks for emergencies (swarms) vs normal pickups, and finds optimal
    // targets based on distance, priority flags, and pathfinding history.
    public class Landfill
    {
        private readonly ushort _buildingID;
        private Dictionary<ushort, Claimant> _master;
        public HashSet<ushort> _primary;
        public HashSet<ushort> _secondary;
        private Dictionary<ushort, HashSet<ushort>> _oldtargets;
        private Dictionary<ushort, DateTime> _lastchangetimes;
		private Helper _helper;
		
        public Landfill(ushort id, ref Dictionary<ushort, Claimant> master, ref Dictionary<ushort, HashSet<ushort>> oldtargets, ref Dictionary<ushort, DateTime> lastchangetimes)
        {
            _buildingID = id;
            _master = master;
            _primary = new HashSet<ushort>();
            _secondary = new HashSet<ushort>();
            _oldtargets = oldtargets;
            _lastchangetimes = lastchangetimes;
			_helper = Helper.Instance;
        }

        public void AddPickup(ushort id)
        {
            if (_primary.Contains(id) || _secondary.Contains(id))
            {
                return;
            }
            if (WithinPrimaryRange(id))
            {
                _primary.Add(id);
                return;
            }
            if (WithinSecondaryRange(id))
            {
                _secondary.Add(id);
            }
        }

        private bool WithinPrimaryRange(ushort id)
        {
            Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            Building building = buffer[_buildingID];
            Building building2 = buffer[id];
            DistrictManager instance = Singleton<DistrictManager>.instance;
            byte district = instance.GetDistrict(building.m_position);
            return district == instance.GetDistrict(building2.m_position) && (district != 0 || WithinSecondaryRange(id));
        }

        private bool WithinSecondaryRange(ushort id)
        {
            Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            Building building = buffer[_buildingID];
            Building building2 = buffer[id];
            // Get the service range and square it for optimized distance comparison
            // (comparing squared distances avoids expensive square root calculations)
            float serviceRangeSquared = building.Info.m_buildingAI.GetCurrentRange(_buildingID, ref building);
            serviceRangeSquared *= serviceRangeSquared;
            return (building.m_position - building2.m_position).sqrMagnitude <= serviceRangeSquared;
        }

		public void DispatchIdleVehicle()
		{
			Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
			Building building = buffer[_buildingID];
			// Don't dispatch trucks from emptying landfills
			if ((building.m_flags & Building.Flags.Downgrading) != 0)
			{
				return;
			}
			
			if ((building.m_flags & Building.Flags.Active) == 0 && building.m_productionRate == 0)
			{
				return;
			}
			if ((building.m_flags & Building.Flags.Evacuating) != 0)
			{
				return;
			}
			if (building.Info.m_buildingAI.IsFull(_buildingID, ref buffer[_buildingID]))
			{
				return;
			}
			
			int max;
			int now;
			CalculateWorkingVehicles(out max, out now);
			
			// Check facility capacity before dispatching
			if (!HasCapacityForDispatch())
			{
				return;
			}
			
			byte landfillDistrict = Helper.GetBuildingDistrict(_buildingID);
			
			// Debug: Log how many flagged buildings are in our tracking lists
			if (Identity.ModConf.DebugLogEmergency)
			{
				int primaryFlagged = 0;
				int secondaryFlagged = 0;
				
				foreach (ushort targetId in _primary)
				{
					if (Helper.GetGarbagePriorityLevel(targetId) >= 1)
						primaryFlagged++;
				}
				
				foreach (ushort targetId in _secondary)
				{
					if (Helper.GetGarbagePriorityLevel(targetId) >= 1)
						secondaryFlagged++;
				}
				
				if (primaryFlagged > 0 || secondaryFlagged > 0)
				{
					_helper.LogWithTimestamp($"EMERGENCIES:: DEBUG (LANDFILL): Landfill {_buildingID}: Found {primaryFlagged} flagged buildings in primary, {secondaryFlagged} in secondary");
				}
			}
			
			// Check if we have any priority 1 (yellow/orange flag) targets
			bool hasCriticalTargets = false;
			foreach (ushort targetId in _primary)
			{
				if (Helper.GetGarbagePriorityLevel(targetId) >= 1)
				{
					hasCriticalTargets = true;
					break;
				}
			}
			
			if (!hasCriticalTargets)  // If we didn't find any such flags in primary range
			{
				// Then check secondary range for red flags
				foreach (ushort targetId in _secondary)
				{
					if (Helper.GetGarbagePriorityLevel(targetId) == 2)
					{
						hasCriticalTargets = true;
						break;
					}
				}
			}

			// Emergency mode: dispatch ALL available trucks if critical targets exist
			if (hasCriticalTargets)
			{
				// Build list of emergency targets (problem buildings with garbage)
				List<ushort> emergencyTargets = new List<ushort>();

				// Always check primary targets
				foreach (ushort targetId in _primary)
				{
					int priority = Helper.GetGarbagePriorityLevel(targetId);
					if (priority >= 1)
					{
						// Check district restriction
						if (!Helper.IsValidDistrictTarget(_buildingID, targetId))
						{
							if (Identity.ModConf.DebugLogEmergency)
							{
								byte targetDistrict = Helper.GetBuildingDistrict(targetId);
								byte sourceLandfillDistrict = Helper.GetBuildingDistrict(_buildingID);
								_helper.LogWithTimestamp($"EMERGENCIES::   Skipping emergency target {targetId}: different district ({targetDistrict} vs {landfillDistrict})");
							}
							continue;
						}
						
						emergencyTargets.Add(targetId);
					}
				}

				// If district restrictions are OFF, also check secondary targets for emergencies
				if (Identity.ModConf.DistrictRestrictedService == false || landfillDistrict == 0)
				{
					foreach (ushort targetId in _secondary)
					{
						int priority = Helper.GetGarbagePriorityLevel(targetId);
						// Only add red flag (priority 2) emergencies from secondary range
						if (priority == 2)
						{
							emergencyTargets.Add(targetId);
						}
					}
				}
				
				// Filter out targets that already have trucks assigned (claimed in _master)
				List<ushort> unclaimedEmergencyTargets = new List<ushort>();
				foreach (ushort targetId in emergencyTargets)
				{
					// During emergency mode, allow multiple trucks per building -- Don't filter out "claimed" targets
					unclaimedEmergencyTargets.Add(targetId);
				}
				
				// If all emergency targets are already claimed, skip emergency dispatch -- silently
				if (unclaimedEmergencyTargets.Count == 0)
				{
					return;
				}

				// Log only the unclaimed emergency targets
				if (Identity.ModConf.DebugLogEmergency)
				{
					foreach (ushort targetId in unclaimedEmergencyTargets)
					{
						byte targetDistrict = Helper.GetBuildingDistrict(targetId);
						int priority = Helper.GetGarbagePriorityLevel(targetId);
						_helper.LogWithTimestamp($"EMERGENCIES::   Unclaimed emergency target: Building {targetId} (District {targetDistrict}, Priority {priority})");
					}
				}

				// Use unclaimed targets for dispatch
				emergencyTargets = unclaimedEmergencyTargets;
				
				// Calculate how many trucks are actually available
				int trucksAvailable = max - now;

				if (Identity.ModConf.DebugLogEmergency)
					_helper.LogWithTimestamp($"EMERGENCIES:: EMERGENCY DISPATCH: Landfill {_buildingID} (District {landfillDistrict}) has {emergencyTargets.Count} emergency targets, {trucksAvailable} trucks available (working: {now}/{max})");

				// Skip if no trucks available
				if (trucksAvailable <= 0)
				{
					if (Identity.ModConf.DebugLogEmergency)
						_helper.LogWithTimestamp($"EMERGENCIES::   Landfill {_buildingID}: All trucks on assignment, skipping emergency dispatch");
					return;
				}
				
				// Check facility capacity before dispatching
				if (!HasCapacityForDispatch())
				{
					return;
				}

				// Dispatch configured number of trucks in round-robin to emergency targets
				int targetIndex = 0;
				int trucksDispatched = 0;
				int maxTrucksToDispatch = Math.Min(Identity.ModConf.EmergencyTruckCount, trucksAvailable);

				while (trucksDispatched < maxTrucksToDispatch && emergencyTargets.Count > 0)
				{
					ushort targetBuilding = emergencyTargets[targetIndex % emergencyTargets.Count];
					
					TransferManager.TransferOffer transferOffer = default(TransferManager.TransferOffer);
					transferOffer.Building = targetBuilding;
					transferOffer.Position = buffer[targetBuilding].m_position;
					
					building.Info.m_buildingAI.StartTransfer(_buildingID, ref buffer[_buildingID], TransferManager.TransferReason.Garbage, transferOffer);
					
					if (Identity.ModConf.DebugLogEmergency)
						_helper.LogWithTimestamp($"EMERGENCIES::   Landfill {_buildingID}: Dispatching truck #{trucksDispatched + 1} to building {targetBuilding}");
					
					now++;
					targetIndex++;
					trucksDispatched++;
				}
				
				if (Identity.ModConf.DebugLogEmergency)
					_helper.LogWithTimestamp($"EMERGENCIES:: EMERGENCY DISPATCH COMPLETE: Landfill {_buildingID} dispatched {trucksDispatched} trucks total");
				
				// Track pending swarm - we'll identify actual truck IDs in the next scan
				if (trucksDispatched > 0 && Dispatcher.Instance._pendingSwarms != null)
				{
					if (emergencyTargets.Count > 0)
					{
						ushort primaryTarget = emergencyTargets[0];
						DateTime currentTime = Singleton<SimulationManager>.instance.m_currentGameTime;
						
						if (!Dispatcher.Instance._pendingSwarms.ContainsKey(primaryTarget))
						{
							Dispatcher.Instance._pendingSwarms[primaryTarget] = new PendingSwarm(trucksDispatched, currentTime);
						}
						else
						{
							// Add to existing pending swarm
							Dispatcher.Instance._pendingSwarms[primaryTarget].ExpectedTruckCount += trucksDispatched;
						}
						
						if (Identity.ModConf.DebugLogEmergency)
							_helper.LogWithTimestamp($"EMERGENCIES:: Pending swarm: {trucksDispatched} trucks to building {primaryTarget}");
					}
				}
			}
			else
			{
				// Normal mode: dispatch configured number of trucks to SAME building
				int trucksToDispatch = Math.Min(Identity.ModConf.NormalDispatchTruckCount, max - now);

				if (trucksToDispatch <= 0)
				{
					return;
				}

				// Find ONE target building
				ushort targetBuilding = 0;

				// Try primary targets first
				foreach (ushort targetId in _primary)
				{
					// Check district restriction
					if (!Helper.IsValidDistrictTarget(_buildingID, targetId))
						continue;
					
					if (Helper.IsBuildingWithGarbage(targetId))
					{
						if (!_master.ContainsKey(targetId) || !_master[targetId].IsValid)
						{
							targetBuilding = targetId;
							break;
						}
					}
				}

				// If no primary target found, try secondary
				if (targetBuilding == 0)
				{
					foreach (ushort targetId in _secondary)
					{
						// Check district restriction
						if (!Helper.IsValidDistrictTarget(_buildingID, targetId))
							continue;
						
						if (Helper.IsBuildingWithGarbage(targetId))
						{
							if (!_master.ContainsKey(targetId) || !_master[targetId].IsValid)
							{
								targetBuilding = targetId;
								break;
							}
						}
					}
				}

				// If no valid target found, don't dispatch
				if (targetBuilding == 0)
				{
					return;
				}

				// Dispatch all X trucks to the SAME building
				for (int i = 0; i < trucksToDispatch; i++)
				{
					TransferManager.TransferOffer transferOffer = default(TransferManager.TransferOffer);
					transferOffer.Building = targetBuilding;
					transferOffer.Position = buffer[targetBuilding].m_position;
					building.Info.m_buildingAI.StartTransfer(_buildingID, ref buffer[_buildingID], TransferManager.TransferReason.Garbage, transferOffer);

					now++;
				}

				// Track this dispatch for logging (after all trucks are dispatched)
				if (Identity.ModConf.DebugLogDispatch && Dispatcher.IsInitialized)
				{
					if (Dispatcher.Instance._pendingNormalDispatchLogs == null)
						Dispatcher.Instance._pendingNormalDispatchLogs = new HashSet<ushort>();
					
					// Add the target once - we'll log all trucks targeting it
					Dispatcher.Instance._pendingNormalDispatchLogs.Add(targetBuilding);
				}
			}
		}
		
		private bool HasCapacityForDispatch()
		{
			if (!Identity.ModConf.RespectFacilityCapacity)
				return true; // Aggressive mode: always dispatch regardless of capacity
			
			BuildingManager buildingManager = Singleton<BuildingManager>.instance;
			Building building = buildingManager.m_buildings.m_buffer[_buildingID];
			LandfillSiteAI ai = (LandfillSiteAI)building.Info.m_buildingAI;
			
			int currentGarbage = building.m_garbageBuffer + (building.m_customBuffer1 * Constants.Instance.CustomBufferMultiplier);
			int maxCapacity = ai.m_garbageCapacity;
			int spaceRemaining = maxCapacity - currentGarbage;
			
			// Use vanilla's logic: incinerators need 20k space, landfills need only 1
			int requiredSpace = (ai.m_garbageConsumption != 0) ? Constants.Instance.MinSpaceForDispatch : 1;
			
			if (Identity.ModConf.DebugLogDispatch && spaceRemaining < requiredSpace)
			{
				_helper.LogWithTimestamp($"DISPATCHES:: Facility {_buildingID} too full ({currentGarbage}/{maxCapacity}, only {spaceRemaining} space remaining, need {requiredSpace}), skipping dispatch");
			}
			
			return spaceRemaining >= requiredSpace;
		}

        public void CalculateWorkingVehicles(out int max, out int now)
        {
            BuildingManager buildingManager = Singleton<BuildingManager>.instance;
            Building building = buildingManager.m_buildings.m_buffer[_buildingID];
            
            max = (PlayerBuildingAI.GetProductionRate(100, Singleton<EconomyManager>.instance.GetBudget(building.Info.m_class)) * ((LandfillSiteAI)building.Info.m_buildingAI).m_garbageTruckCount + 99) / 100;
            now = 0;
            VehicleManager instance = Singleton<VehicleManager>.instance;
            for (ushort num = building.m_ownVehicles; num != 0; num = instance.m_vehicles.m_buffer[num].m_nextOwnVehicle)
            {
                if (instance.m_vehicles.m_buffer[num].m_transferType == (byte)TransferManager.TransferReason.Garbage)
                {
                    now++;
                }
            }
        }

		public ushort GetUnclaimedTarget(ushort vehicleID = 0)
		{
			ushort num = GetUnclaimedTarget(_primary, vehicleID);
			if (num == 0)
			{
				num = GetUnclaimedTarget(_secondary, vehicleID);
			}
			return num;
		}

		private ushort GetUnclaimedTarget(ICollection<ushort> targets, ushort vehicleID)
		{
			Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
			List<ushort> list = new List<ushort>();
			ushort num = 0;
			int num2 = 0;
			float num3 = float.PositiveInfinity;
			Building building = buffer[_buildingID];
			
			foreach (ushort num4 in targets)
			{
				if (num != num4 && (!_oldtargets.ContainsKey(vehicleID) || !_oldtargets[vehicleID].Contains(num4)))
				{
					// Skip if district restriction is enabled and target is in different district
					if (!Helper.IsValidDistrictTarget(_buildingID, num4))
						continue;
					
					if (!Helper.IsBuildingWithGarbage(num4))
					{
						list.Add(num4);
					}
					else
					{
						float sqrMagnitude = (buffer[num4].m_position - building.m_position).sqrMagnitude;
						int num5 = Helper.GetGarbagePriorityLevel(num4);
						if ((!_master.ContainsKey(num4) || !_master[num4].IsValid) && num2 <= num5 && (num2 < num5 || sqrMagnitude <= num3))
						{
							num = num4;
							num2 = num5;
							num3 = sqrMagnitude;
						}
					}
				}
			}
			foreach (ushort num6 in list)
			{
				_master.Remove(num6);
				targets.Remove(num6);
			}
			return num;
		}

		public ushort AssignTarget(ushort vehicleID)
        {
            Vehicle vehicle = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID];
            ushort num = 0;
            if (vehicle.m_sourceBuilding != _buildingID)
            {
                return num;
            }
            if (Singleton<PathManager>.instance.m_pathUnits.m_buffer[vehicle.m_path].m_nextPathUnit == 0U)
            {
                byte b = vehicle.m_pathPositionIndex;
                if (b == 255)
                {
                    b = 0;
                }
                if ((b & 1) == 0)
                {
                    b += 1;
                }
                if ((b >> 1) + 1 >= (int)Singleton<PathManager>.instance.m_pathUnits.m_buffer[vehicle.m_path].m_positionCount)
                {
                    return num;
                }
            }
			ushort num2 = vehicle.m_targetBuilding;

			// Validate district restriction
			if (Identity.ModConf.DistrictRestrictedService && num2 != 0)
			{
				byte landfillDistrict = Helper.GetBuildingDistrict(_buildingID);
				if (landfillDistrict != 0)
				{
					byte targetDistrict = Helper.GetBuildingDistrict(num2);
					if (targetDistrict != landfillDistrict)
					{
						if (Identity.ModConf.DebugLogDispatch)
							_helper.LogWithTimestamp($"DISPATCHES:: AssignTarget: Invalidating out-of-district target {num2} for truck {vehicleID}");
						num2 = 0; // Invalidate out-of-district target
					}
				}
			}

			if (!Helper.IsBuildingWithGarbage(num2))
			{
				_oldtargets.Remove(vehicleID);
				_master.Remove(num2);
				_primary.Remove(num2);
				_secondary.Remove(num2);
				num2 = 0;
			}
            else if (_master.ContainsKey(num2) && _master[num2].IsValid && _master[num2].Vehicle != vehicleID)
            {
                num2 = 0;
            }
            int garbageTruckStatus = Dispatcher.GetGarbageTruckStatus(ref vehicle);
            if (num2 != 0 && garbageTruckStatus == 5 && _lastchangetimes.ContainsKey(vehicleID) && (Singleton<SimulationManager>.instance.m_currentGameTime - _lastchangetimes[vehicleID]).TotalDays < Constants.Instance.TargetChangeDelayDays)
            {
                return num;
            }
            bool flag = _primary.Contains(num2) || _secondary.Contains(num2);
            SearchDirection immediateSearchDirection = GetImmediateSearchDirection(vehicleID);
            if (flag && immediateSearchDirection == SearchDirection.None)
            {
                num = num2;
            }
            else
            {
                num = GetClosestTarget(vehicleID, ref _primary, flag, immediateSearchDirection);
                if (num == 0)
                {
                    num = GetClosestTarget(vehicleID, ref _secondary, flag, immediateSearchDirection);
                }
            }
			if (num == 0)
			{
				_oldtargets.Remove(vehicleID);
				num = vehicle.m_targetBuilding;
			}
            return num;
        }

        private SearchDirection GetImmediateSearchDirection(ushort vehicleID)
        {
            Vehicle vehicle = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID];
            PathUnit pathUnit = Singleton<PathManager>.instance.m_pathUnits.m_buffer[vehicle.m_path];
            byte b = vehicle.m_pathPositionIndex;
            if (b == 255)
            {
                b = 0;
            }
            PathUnit.Position position = pathUnit.GetPosition(b >> 1);
            NetSegment netSegment = Singleton<NetManager>.instance.m_segments.m_buffer[position.m_segment];
            int vehicleLaneCount = 0;
            int leftmostLaneIndex = -1;
            float leftmostLanePosition = float.PositiveInfinity;
            int rightmostLaneIndex = -1;
            float rightmostLanePosition = float.NegativeInfinity;
            for (int i = 0; i < netSegment.Info.m_lanes.Length; i++)
            {
                NetInfo.Lane lane = netSegment.Info.m_lanes[i];
                if (lane.m_laneType == NetInfo.LaneType.Vehicle && lane.m_vehicleType == VehicleInfo.VehicleType.Car)
                {
                    vehicleLaneCount++;
                    if (lane.m_position < leftmostLanePosition)
                    {
                        leftmostLaneIndex = i;
                        leftmostLanePosition = lane.m_position;
                    }
                    if (lane.m_position > rightmostLanePosition)
                    {
                        rightmostLaneIndex = i;
                        rightmostLanePosition = lane.m_position;
                    }
                }
            }
            SearchDirection result = SearchDirection.None;
            if (vehicleLaneCount != 0)
            {
                if ((int)position.m_lane != leftmostLaneIndex && (int)position.m_lane != rightmostLaneIndex)
                {
                    result = SearchDirection.Ahead;
                }
                else if (leftmostLaneIndex == rightmostLaneIndex)
                {
                    result = (SearchDirection.Ahead | SearchDirection.Left | SearchDirection.Right);
                }
                else if (vehicleLaneCount == 2 && netSegment.Info.m_lanes[leftmostLaneIndex].m_direction != netSegment.Info.m_lanes[rightmostLaneIndex].m_direction)
                {
                    result = (SearchDirection.Ahead | SearchDirection.Left | SearchDirection.Right);
                }
                else if ((int)position.m_lane == leftmostLaneIndex)
                {
                    result = (SearchDirection.Ahead | SearchDirection.Left);
                }
                else
                {
                    result = (SearchDirection.Ahead | SearchDirection.Right);
                }
            }
            return result;
        }

		// TODO: Consider refactoring this method for better readability
		// - Extract pathfinding check logic
		// - Simplify "monster conditional" on/around line 717
		private ushort GetClosestTarget(ushort vehicleID, ref HashSet<ushort> targets, bool immediateOnly, SearchDirection immediateDirection)
		{
			Vehicle vehicle = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID];
			Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
			List<ushort> list = new List<ushort>();
			ushort currentTarget = vehicle.m_targetBuilding;
			if (_master.ContainsKey(currentTarget) && _master[currentTarget].IsValid && _master[currentTarget].Vehicle != vehicleID)
			{
				currentTarget = 0;
			}
			int currentTargetPriority = 0;
			float currentTargetDistance = float.PositiveInfinity;
			float bestCandidateDistance = float.PositiveInfinity;
			Vector3 lastFrameVelocity = vehicle.GetLastFrameVelocity();
			Vector3 lastFramePosition = vehicle.GetLastFramePosition();
			double currentTargetAngle = double.PositiveInfinity;
			double a = Math.Atan2((double)lastFrameVelocity.z, (double)lastFrameVelocity.x);
			
			if (targets.Contains(currentTarget))
			{
				if (!Helper.IsBuildingWithGarbage(currentTarget))
				{
					list.Add(currentTarget);
					currentTarget = 0;
				}
				else
				{
					currentTargetPriority = 0;
					
					Vector3 position = buffer[currentTarget].m_position;
					bestCandidateDistance = (currentTargetDistance = (position - lastFramePosition).sqrMagnitude);
					currentTargetAngle = Math.Atan2((double)(position.z - lastFramePosition.z), (double)(position.x - lastFramePosition.x));
				}
			}
			else if (!immediateOnly)
			{
				currentTarget = 0;
			}
			
			foreach (ushort candidateBuilding in targets)
			{
				if (currentTarget != candidateBuilding)
				{
					// Skip if district restriction is enabled and target is in different district
					if (!Helper.IsValidDistrictTarget(_buildingID, candidateBuilding))
						continue;
					
					if (!Helper.IsBuildingWithGarbage(candidateBuilding))
					{
						list.Add(candidateBuilding);
					}
					else if (!_master.ContainsKey(candidateBuilding) || !_master[candidateBuilding].IsValid || _master[candidateBuilding].IsChallengable)
					{
						if (_master.ContainsKey(candidateBuilding) && _master[candidateBuilding].IsValid && _master[candidateBuilding].Vehicle != vehicleID)
						{
							Vehicle vehicle2 = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[_master[candidateBuilding].Vehicle];
							if ((vehicle2.m_flags & Vehicle.Flags.Spawned) != 0 && vehicle2.m_path != 0U && Singleton<PathManager>.instance.m_pathUnits.m_buffer[vehicle2.m_path].m_nextPathUnit == 0U)
							{
								byte b = vehicle2.m_pathPositionIndex;
								if (b == 255)
								{
									b = 0;
								}
								if ((b & 1) == 0)
								{
									b += 1;
								}
								if ((b >> 1) + 1 >= (int)Singleton<PathManager>.instance.m_pathUnits.m_buffer[vehicle2.m_path].m_positionCount)
								{
									continue;
								}
							}
						}
						Vector3 position2 = buffer[candidateBuilding].m_position;
						float sqrMagnitude = (position2 - lastFramePosition).sqrMagnitude;
						int candidatePriority = Helper.GetGarbagePriorityLevel(candidateBuilding);

						if (_oldtargets.ContainsKey(vehicleID) && _oldtargets[vehicleID].Count > 5 && currentTargetPriority >= candidatePriority)
						{
							return currentTarget;
						}
						if (_master.ContainsKey(candidateBuilding) && _master[candidateBuilding].IsValid && _master[candidateBuilding].IsChallengable)
						{
							if (currentTargetPriority > candidatePriority || (double)sqrMagnitude > (double)currentTargetDistance * Constants.Instance.DistanceComparisonBuffer || sqrMagnitude > bestCandidateDistance || (double)sqrMagnitude > (double)_master[candidateBuilding].Distance * Constants.Instance.DistanceComparisonBuffer)
							{
								continue;
							}
							double angleDifference = Helper.GetAngleDifference(a, Math.Atan2((double)(position2.z - lastFramePosition.z), (double)(position2.x - lastFramePosition.x)));
							if (GetImmediateLevel(sqrMagnitude, angleDifference, immediateDirection) == 0)
							{
								continue;
							}
							if (_oldtargets.ContainsKey(vehicleID) && _oldtargets[vehicleID].Contains(candidateBuilding))
							{
								continue;
							}
						}
						else
						{
							double angleDifference2 = Helper.GetAngleDifference(a, Math.Atan2((double)(position2.z - lastFramePosition.z), (double)(position2.x - lastFramePosition.x)));
							int immediateLevel = GetImmediateLevel(sqrMagnitude, angleDifference2, immediateDirection);
							// Yep! This next line is a monster. I know. It works... and the computer doesn't care.
							// If you're a human trying to understand it: good luck! Maybe grab a coffee first.
							// (Seriously though: it's checking immediate-only requirements, old targets, priority levels,
							// distance thresholds, and angular alignment all in one shot. It's dense but correct.)
							if ((immediateOnly && immediateLevel == 0) || (_oldtargets.ContainsKey(vehicleID) && _oldtargets[vehicleID].Contains(candidateBuilding)) || currentTargetPriority > candidatePriority || (currentTargetPriority >= candidatePriority && ((double)sqrMagnitude > (double)currentTargetDistance * Constants.Instance.DistanceComparisonBuffer || sqrMagnitude > bestCandidateDistance || (immediateLevel <= 0 && !IsAlongTheWay(sqrMagnitude, angleDifference2) && !double.IsPositiveInfinity(currentTargetAngle) && !IsAlongTheWay(sqrMagnitude, Helper.GetAngleDifference(currentTargetAngle, Math.Atan2((double)(position2.z - lastFramePosition.z), (double)(position2.x - lastFramePosition.x))))))))
							{
								continue;
							}
						}
						currentTarget = candidateBuilding;
						currentTargetPriority = candidatePriority;
						bestCandidateDistance = sqrMagnitude;
					}
				}
			}
			foreach (ushort buildingToRemove in list)
			{
				_master.Remove(buildingToRemove);
				targets.Remove(buildingToRemove);
			}
			
			return currentTarget;
		}


        private int GetImmediateLevel(float distance, double angle, SearchDirection immediateDirection)
        {
            // Define the angular field of view for the truck (in radians)
            // Default: -90° to +90° (180° arc in front of the truck)
            // These bounds adjust based on which directions the truck can turn
            double leftAngleBound = Constants.Instance.NEGATIVE_HALF_PI;   // -π/2 radians = -90 degrees (left side)
            double rightAngleBound = Constants.Instance.HALF_PI;           // +π/2 radians = +90 degrees (right side)
            if (distance < (float)Constants.Instance.ImmediateRange1)
            {
                if ((immediateDirection & SearchDirection.Left) == SearchDirection.None)
                {
                    leftAngleBound = 0.0;  // Can't turn left, narrow to center-right only
                }
                if ((immediateDirection & SearchDirection.Right) == SearchDirection.None)
                {
                    rightAngleBound = 0.0;  // Can't turn right, narrow to center-left only
                }
                if (leftAngleBound <= angle && angle <= rightAngleBound)
                {
                    return 2;  // High immediacy: very close and within viewing angle
                }
            }
            else if (distance < (float)Constants.Instance.ImmediateRange2 && (immediateDirection & SearchDirection.Ahead) != SearchDirection.None)
            {
                if ((immediateDirection & SearchDirection.Left) == SearchDirection.None)
                {
                    leftAngleBound = Constants.Instance.NEGATIVE_THIRD_PI;  // -60 degrees (narrower arc)
                }
                if ((immediateDirection & SearchDirection.Right) == SearchDirection.None)
                {
                    rightAngleBound = Constants.Instance.THIRD_PI;  // +60 degrees (narrower arc)
                }
                if (leftAngleBound <= angle && angle <= rightAngleBound)
                {
                    return 1;  // Medium immediacy: moderate distance, narrower viewing angle
                }
            }
            return 0;  // Not immediate: too far or outside viewing angle
        }

        private bool IsAlongTheWay(float distance, double angle)
        {
            return distance >= (float)Constants.Instance.ImmediateRange2 && Constants.Instance.NEGATIVE_HALF_PI <= angle && angle <= Constants.Instance.HALF_PI;
        }

        [Flags]
        private enum SearchDirection : byte
        {
			None = 0,
            Ahead = 1,
            Left = 2,
            Right = 4
        }
    }
}