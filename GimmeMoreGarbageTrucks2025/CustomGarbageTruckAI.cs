using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace GimmeMoreGarbageTrucks2025
{
    // The core override: custom truck assignment logic that replaces vanilla GarbageTruckAI.SetTarget().
    // Handles district validation, retry logic for failed pathfinding, and coordinates
    // with Dispatcher to ensure trucks get valid, reachable targets.
    public class CustomGarbageTruckAI
    {
		public void SetTarget(ushort vehicleID, ref Vehicle data, ushort targetBuilding)
		{
			// District restriction validation - secretly reassign out-of-district targets
			if (Identity.ModConf.DistrictRestrictedService && targetBuilding != 0 && data.m_sourceBuilding != 0)
			{
				byte sourceDistrict = Helper.GetBuildingDistrict(data.m_sourceBuilding);
				if (sourceDistrict != 0)
				{
					byte targetDistrict = Helper.GetBuildingDistrict(targetBuilding);
					if (targetDistrict != sourceDistrict)
					{
						// Out of district! Find a valid replacement target
						if (Dispatcher.Instance._landfills != null && Dispatcher.Instance._landfills.ContainsKey(data.m_sourceBuilding))
						{
							ushort validTarget = Dispatcher.Instance._landfills[data.m_sourceBuilding].GetUnclaimedTarget(vehicleID);
							
							if (validTarget != 0)
							{
								// Secretly replace the target with a valid one
								if (Identity.ModConf.DebugLogDispatch)
								{
									Helper.Instance.LogWithTimestamp($"DISPATCHES:: SetTarget override: vanilla job for Truck {vehicleID} redirected by mod from District {targetDistrict} building {targetBuilding} to District {sourceDistrict} building {validTarget}");
								}
								targetBuilding = validTarget;
							}
							else
							{
								// No valid targets available, send truck home -- if user is observing number of trucks on assignment, this may result in a brief flicker in number of trucks being used
								if (Identity.ModConf.DebugLogRecalls)
								{
									Helper.Instance.LogWithTimestamp($"RECALLS:: Rejecting vanilla assignment before truck starts journey: no valid targets in district; Truck {vehicleID} unspawned");
								}
								data.Unspawn(vehicleID);
								return;

							}
						}
					}
				}
			}

			if (targetBuilding == data.m_targetBuilding)
            {
                if (data.m_path != 0U)
                {
                    data.Info.m_vehicleAI.TrySpawn(vehicleID, ref data);
                    return;
                }
				if (!StartPathFind(vehicleID, ref data))
				{
					// Pathfinding failed - truck can't reach any valid position
					// Safe to unspawn even though pathfinding left the vehicle in an incomplete state
					// (Unspawn handles cleanup of any partial data)
					if (Identity.ModConf.DebugLogRecalls)
						Helper.Instance.LogWithTimestamp($"RECALLS:: Truck {vehicleID} unspawned - pathfinding failed on spawn attempt");
					data.Unspawn(vehicleID);
					return;
				}
            }
            else
            {
                if ((data.m_flags & Vehicle.Flags.Spawned) == 0)
                {
                    if (Dispatcher.Instance._landfills != null && Dispatcher.Instance._landfills.ContainsKey(data.m_sourceBuilding))
                    {
                        int maxVehicleCapacity;
                        int currentWorkingVehicles;
                        Dispatcher.Instance._landfills[data.m_sourceBuilding].CalculateWorkingVehicles(out maxVehicleCapacity, out currentWorkingVehicles);
						if (currentWorkingVehicles > maxVehicleCapacity)
						{
							if (Identity.ModConf.DebugLogRecalls)
								Helper.Instance.LogWithTimestamp($"RECALLS:: Truck {vehicleID} unspawned - excess trucks (working: {currentWorkingVehicles} > max: {maxVehicleCapacity})");
							data.Unspawn(vehicleID);
							return;
						}
                    }
                }
                ushort targetBuilding2 = data.m_targetBuilding;
                uint path = data.m_path;
                byte pathPositionIndex = data.m_pathPositionIndex;
                byte lastPathOffset = data.m_lastPathOffset;
                ushort candidateTarget = targetBuilding;
                int garbageTruckStatus = Dispatcher.GetGarbageTruckStatus(ref data);
                int retryAttempts = 1;
                if (garbageTruckStatus == 4)
                {
                    if (Dispatcher.Instance._oldtargets != null)
                    {
                        Dispatcher.Instance._oldtargets.Remove(vehicleID);
                    }
                    retryAttempts = Constants.Instance.MaxRetryAttempts;
                }
                for (int i = 0; i < retryAttempts; i++)
                {
                    if (i > 0)
                    {
                        if (Dispatcher.Instance._landfills == null || !Dispatcher.Instance._landfills.ContainsKey(data.m_sourceBuilding))
                        {
                            break;
                        }
                        candidateTarget = Dispatcher.Instance._landfills[data.m_sourceBuilding].GetUnclaimedTarget(vehicleID);
                        if (candidateTarget == 0)
                        {
                            break;
                        }
                        if (Dispatcher.Instance._oldtargets != null)
                        {
                            if (!Dispatcher.Instance._oldtargets.ContainsKey(vehicleID))
                            {
                                Dispatcher.Instance._oldtargets.Add(vehicleID, new HashSet<ushort>());
                            }
                            Dispatcher.Instance._oldtargets[vehicleID].Add(candidateTarget);
                        }
                    }
                    RemoveTarget(vehicleID, ref data);
                    data.m_targetBuilding = targetBuilding;
                    data.m_flags &= ~Vehicle.Flags.WaitingTarget;
                    data.m_waitCounter = 0;
                    if (targetBuilding != 0)
                    {
                        Singleton<BuildingManager>.instance.m_buildings.m_buffer[targetBuilding].AddGuestVehicle(vehicleID, ref data);
                        if ((Singleton<BuildingManager>.instance.m_buildings.m_buffer[targetBuilding].m_flags & (Building.Flags.IncomingOutgoing)) != 0)
                        {
                            if ((data.m_flags & Vehicle.Flags.TransferToTarget) != 0)
                            {
                                data.m_flags |= Vehicle.Flags.Exporting;
                            }
                            else if ((data.m_flags & Vehicle.Flags.TransferToSource) != 0)
                            {
                                data.m_flags |= Vehicle.Flags.Importing;
                            }
                        }
                    }
                    else
                    {
                        if ((data.m_flags & Vehicle.Flags.TransferToTarget) != 0)
                        {
                            if (data.m_transferSize > 0)
                            {
                                TransferManager.TransferOffer transferOffer = default(TransferManager.TransferOffer);
                                transferOffer.Priority = 7;
                                transferOffer.Vehicle = vehicleID;
                                if (data.m_sourceBuilding != 0)
                                {
                                    transferOffer.Position = (data.GetLastFramePosition() + Singleton<BuildingManager>.instance.m_buildings.m_buffer[data.m_sourceBuilding].m_position) * 0.5f;
                                }
                                else
                                {
                                    transferOffer.Position = data.GetLastFramePosition();
                                }
                                transferOffer.Amount = 1;
                                transferOffer.Active = true;
                                Singleton<TransferManager>.instance.AddOutgoingOffer((TransferManager.TransferReason)data.m_transferType, transferOffer);
                                data.m_flags |= Vehicle.Flags.WaitingTarget;
                            }
                            else
                            {
                                data.m_flags |= Vehicle.Flags.GoingBack;
                            }
                        }
                        if ((data.m_flags & Vehicle.Flags.TransferToSource) != 0)
                        {
                            VehicleInfo info = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].Info;
                            if ((int)data.m_transferSize < ((GarbageTruckAI)info.m_vehicleAI).m_cargoCapacity && !ShouldReturnToSource(vehicleID, ref data))
                            {
                                TransferManager.TransferOffer transferOffer2 = default(TransferManager.TransferOffer);
                                transferOffer2.Priority = 7;
                                transferOffer2.Vehicle = vehicleID;
                                if (data.m_sourceBuilding != 0)
                                {
                                    transferOffer2.Position = (data.GetLastFramePosition() + Singleton<BuildingManager>.instance.m_buildings.m_buffer[data.m_sourceBuilding].m_position) * 0.5f;
                                }
                                else
                                {
                                    transferOffer2.Position = data.GetLastFramePosition();
                                }
                                transferOffer2.Amount = 1;
                                transferOffer2.Active = true;
                                Singleton<TransferManager>.instance.AddIncomingOffer((TransferManager.TransferReason)data.m_transferType, transferOffer2);
                                data.m_flags |= Vehicle.Flags.WaitingTarget;
                            }
                            else
                            {
                                data.m_flags |= Vehicle.Flags.GoingBack;
                            }
                        }
                    }
                    if (targetBuilding == 0 || (garbageTruckStatus != 5 && garbageTruckStatus != 4))
                    {
						if (!StartPathFind(vehicleID, ref data))
						{
							if (Identity.ModConf.DebugLogRecalls)
								Helper.Instance.LogWithTimestamp($"RECALLS:: Truck {vehicleID} unspawned - pathfinding failed after target assignment (target: {targetBuilding})");
							data.Unspawn(vehicleID);
						}
                        return;
                    }
                    if (StartPathFind(vehicleID, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID]))
                    {
                        if (Dispatcher.Instance._oldtargets != null)
                        {
                            if (!Dispatcher.Instance._oldtargets.ContainsKey(vehicleID))
                            {
                                Dispatcher.Instance._oldtargets.Add(vehicleID, new HashSet<ushort>());
                            }
                            Dispatcher.Instance._oldtargets[vehicleID].Add(candidateTarget);
                        }
                        if (Dispatcher.Instance._master != null)
                        {
                            if (Dispatcher.Instance._master.ContainsKey(candidateTarget))
                            {
                                if (Dispatcher.Instance._master[candidateTarget].Vehicle != vehicleID)
                                {
                                    Dispatcher.Instance._master[candidateTarget] = new Claimant(vehicleID, candidateTarget);
                                    return;
                                }
                            }
                            else if (candidateTarget != 0)
                            {
                                Dispatcher.Instance._master.Add(candidateTarget, new Claimant(vehicleID, candidateTarget));
                            }
                        }
                        return;
                    }
                }
                if (garbageTruckStatus == 5)
                {
                    candidateTarget = targetBuilding2;
                    RemoveTarget(vehicleID, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID]);
                    Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_targetBuilding = candidateTarget;
                    Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_path = path;
                    Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_pathPositionIndex = pathPositionIndex;
                    Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].m_lastPathOffset = lastPathOffset;
                    Singleton<BuildingManager>.instance.m_buildings.m_buffer[targetBuilding2].AddGuestVehicle(vehicleID, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID]);
                    if (Dispatcher.Instance._master != null)
                    {
                        if (Dispatcher.Instance._master.ContainsKey(candidateTarget))
                        {
                            if (Dispatcher.Instance._master[candidateTarget].Vehicle != vehicleID)
                            {
                                Dispatcher.Instance._master[candidateTarget] = new Claimant(vehicleID, candidateTarget);
                                return;
                            }
                        }
                        else if (candidateTarget != 0)
                        {
                            Dispatcher.Instance._master.Add(candidateTarget, new Claimant(vehicleID, candidateTarget));
                            return;
                        }
                    }
                }
				else
				{
					if (Identity.ModConf.DebugLogRecalls)
						Helper.Instance.LogWithTimestamp($"RECALLS:: Truck {vehicleID} unspawned - invalid truck status (status: {garbageTruckStatus})");
					Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].Unspawn(vehicleID);
				}
            }
        }

        private static bool ShouldReturnToSource(ushort vehicleID, ref Vehicle data)
        {
            if (data.m_sourceBuilding != 0)
            {
                BuildingManager instance = Singleton<BuildingManager>.instance;
                if ((instance.m_buildings.m_buffer[data.m_sourceBuilding].m_productionRate == 0 || (instance.m_buildings.m_buffer[data.m_sourceBuilding].m_flags & (Building.Flags.Evacuating | Building.Flags.Downgrading | Building.Flags.Collapsed)) != 0) && instance.m_buildings.m_buffer[data.m_sourceBuilding].m_fireIntensity == 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void RemoveTarget(ushort vehicleID, ref Vehicle data)
        {
            if (data.m_targetBuilding != 0)
            {
                Singleton<BuildingManager>.instance.m_buildings.m_buffer[data.m_targetBuilding].RemoveGuestVehicle(vehicleID, ref data);
                data.m_targetBuilding = 0;
            }
        }

        private static bool StartPathFind(ushort vehicleID, ref Vehicle vehicleData)
        {
            VehicleInfo info = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].Info;
            if ((vehicleData.m_flags & Vehicle.Flags.WaitingTarget) != 0)
            {
                return true;
            }
            if ((vehicleData.m_flags & Vehicle.Flags.GoingBack) != 0)
            {
                if (vehicleData.m_sourceBuilding != 0)
                {
                    BuildingManager instance = Singleton<BuildingManager>.instance;
                    BuildingInfo info2 = instance.m_buildings.m_buffer[vehicleData.m_sourceBuilding].Info;
                    Randomizer randomizer = new Randomizer((int)vehicleID);
                    Vector3 vector;
                    Vector3 endPos;
                    info2.m_buildingAI.CalculateUnspawnPosition(vehicleData.m_sourceBuilding, ref instance.m_buildings.m_buffer[vehicleData.m_sourceBuilding], ref randomizer, info, out vector, out endPos);
                    return StartPathFind(vehicleID, ref vehicleData, vehicleData.m_targetPos3, endPos);
                }
            }
            else if (vehicleData.m_targetBuilding != 0)
            {
                BuildingManager instance2 = Singleton<BuildingManager>.instance;
                BuildingInfo info3 = instance2.m_buildings.m_buffer[vehicleData.m_targetBuilding].Info;
                Randomizer randomizer2 = new Randomizer((int)vehicleID);
                Vector3 vector2;
                Vector3 endPos2;
                info3.m_buildingAI.CalculateUnspawnPosition(vehicleData.m_targetBuilding, ref instance2.m_buildings.m_buffer[vehicleData.m_targetBuilding], ref randomizer2, info, out vector2, out endPos2);
                return StartPathFind(vehicleID, ref vehicleData, vehicleData.m_targetPos3, endPos2);
            }
            return false;
        }

        protected static bool StartPathFind(ushort vehicleID, ref Vehicle vehicleData, Vector3 startPos, Vector3 endPos)
        {
            return StartPathFind(vehicleID, ref vehicleData, startPos, endPos, true, true);
        }

        protected static bool StartPathFind(ushort vehicleID, ref Vehicle vehicleData, Vector3 startPos, Vector3 endPos, bool startBothWays, bool endBothWays)
        {
            VehicleInfo info = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleID].Info;
            bool allowUnderground = (vehicleData.m_flags & (Vehicle.Flags.Underground | Vehicle.Flags.Transition)) != 0;
            PathUnit.Position startPosA;
            PathUnit.Position startPosB;
            float startPosADistance;
            float startPosBDistance;
            PathUnit.Position endPosA;
            PathUnit.Position endPosB;
            float endPosADistance;
            float endPosBDistance;
            
            bool foundStart = PathManager.FindPathPosition(startPos, ItemClass.Service.Road, NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle, VehicleInfo.VehicleType.Car, info.vehicleCategory, allowUnderground, false, Constants.Instance.PathFindDistance, false, false, out startPosA, out startPosB, out startPosADistance, out startPosBDistance);
            bool foundEnd = PathManager.FindPathPosition(endPos, ItemClass.Service.Road, NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle, VehicleInfo.VehicleType.Car, info.vehicleCategory, false, false, Constants.Instance.PathFindDistance, false, false, out endPosA, out endPosB, out endPosADistance, out endPosBDistance);
            
            if (foundStart && foundEnd)
            {
                if (!startBothWays || startPosADistance < Constants.Instance.MinDistanceThreshold)
                {
                    startPosB = default(PathUnit.Position);
                }
                if (!endBothWays || endPosADistance < Constants.Instance.MinDistanceThreshold)
                {
                    endPosB = default(PathUnit.Position);
                }
                uint path;
                if (Singleton<PathManager>.instance.CreatePath(out path, ref Singleton<SimulationManager>.instance.m_randomizer, Singleton<SimulationManager>.instance.m_currentBuildIndex, startPosA, startPosB, endPosA, endPosB, default(PathUnit.Position), NetInfo.LaneType.Vehicle | NetInfo.LaneType.CargoVehicle, VehicleInfo.VehicleType.Car, info.vehicleCategory, Constants.Instance.MaxPathCost, IsHeavyVehicle(), IgnoreBlocked(vehicleID, ref vehicleData), false, false, false, false, false))
                {
                    if (vehicleData.m_path != 0U)
                    {
                        Singleton<PathManager>.instance.ReleasePath(vehicleData.m_path);
                    }
                    vehicleData.m_path = path;
                    vehicleData.m_flags |= Vehicle.Flags.WaitingPath;
                    return true;
                }
            }
            return false;
        }

		// Required by pathfinding API - indicates whether this vehicle should be restricted from "no heavy traffic" roads.
		// Garbage trucks return false (not heavy) so they can access residential streets.
		protected static bool IsHeavyVehicle()
		{
			return false;
		}

		// Required by pathfinding API - indicates whether this vehicle can route through blocked roads.
		// Garbage trucks return false (obey blocks) - only emergency vehicles typically ignore blocks.
		protected static bool IgnoreBlocked(ushort vehicleID, ref Vehicle vehicleData)
		{
			return false;
		}
    }
}