using System;
using ColossalFramework;
using UnityEngine;
using ColossalFramework.Plugins;

namespace GimmeMoreGarbageTrucks2025
{
    // Utility methods for common operations: truck/building validation, district lookups,
    // garbage priority detection (yellow/red warning flags), angle calculations for pathfinding,
    // and timestamped debug logging.
    internal sealed class Helper
    {
        private Helper()
        {
            GameLoaded = false;
        }

        public static Helper Instance { get; } = new Helper();

        public bool GameLoaded;

        public void Log(string message)
        {
            Debug.Log($"{Constants.Instance.Tag}: {message}");
        }

        public void NotifyPlayer(string message)
        {
            DebugOutputPanel.AddMessage(PluginManager.MessageType.Message, $"{Constants.Instance.Tag}: {message}");
            Log(message);
        }

        public static double GetAngleDifference(double a, double b)
        {
            if (a < 0.0)
            {
                a += Constants.Instance.TWO_PI;
            }
            if (b < 0.0)
            {
                b += Constants.Instance.TWO_PI;
            }
            double angleDifference = a - b;
            if (angleDifference > Constants.Instance.PI)
            {
                angleDifference -= Constants.Instance.TWO_PI;
            }
            else if (angleDifference < -Constants.Instance.PI)
            {
                angleDifference += Constants.Instance.TWO_PI;
            }
            return angleDifference;
        }

		public static bool IsBuildingWithGarbage(ushort id)
		{
			if (id == 0) return false;
			
			BuildingManager instance = Singleton<BuildingManager>.instance;
			Building building = instance.m_buildings.m_buffer[id];
			
			if ((building.m_flags & (Building.Flags.Created | Building.Flags.Deleted | Building.Flags.Demolishing)) != Building.Flags.Created)
				return false;
			
			// Exclude service buildings (landfills, incinerators, etc.)
			if ((building.m_flags & (Building.Flags.Untouchable)) != 0)
				return false;
				
			// Also exclude buildings that are landfill sites directly
			if (building.Info.m_buildingAI is LandfillSiteAI)
				return false;
				
			return building.Info.m_buildingAI.GetGarbageAmount(id, ref building) > Identity.ModConf.GarbageThreshold;
		}

        public static bool IsGarbageTruck(ushort vehicleId)
        {
            if (vehicleId == 0) return false;
            
            VehicleManager instance = Singleton<VehicleManager>.instance;
            if ((instance.m_vehicles.m_buffer[vehicleId].m_flags & Vehicle.Flags.Created) == 0)
                return false;
                
            return instance.m_vehicles.m_buffer[vehicleId].Info.m_vehicleAI is GarbageTruckAI;
        }
		
		public static int GetGarbagePriorityLevel(ushort buildingId)
		{
			if (buildingId == 0) return 0;
			
			BuildingManager instance = Singleton<BuildingManager>.instance;
			Building building = instance.m_buildings.m_buffer[buildingId];
			
			// Check if building has garbage problem
			if ((building.m_problems.m_Problems1 & Notification.Problem1.Garbage) != Notification.Problem1.None)
			{
				// Check if it's also a major problem (red sign)
				if ((building.m_problems.m_Problems1 & Notification.Problem1.MajorProblem) != Notification.Problem1.None)
				{
					return 2; // High priority - red sign
				}
				else
				{
					return 1; // Normal priority - has problem
				}
			}
			
			return 0; // No garbage problem notification
		}

		public static bool IsTruckInHomeDistrict(ushort vehicleId)
		{
			VehicleManager vehicleManager = Singleton<VehicleManager>.instance;
			BuildingManager buildingManager = Singleton<BuildingManager>.instance;
			DistrictManager districtManager = Singleton<DistrictManager>.instance;
			
			Vehicle vehicle = vehicleManager.m_vehicles.m_buffer[vehicleId];
			
			if (vehicle.m_sourceBuilding == 0)
				return false;
			
			Building homeBuilding = buildingManager.m_buildings.m_buffer[vehicle.m_sourceBuilding];
			Vector3 truckPosition = vehicle.GetLastFramePosition();
			
			byte homeDistrict = districtManager.GetDistrict(homeBuilding.m_position);
			byte truckDistrict = districtManager.GetDistrict(truckPosition);
			
			return homeDistrict == truckDistrict;
		}

		public static bool IsInSameDistrict(ushort building1, ushort building2)
		{
			if (building1 == 0 || building2 == 0)
				return false;
			
			BuildingManager buildingManager = Singleton<BuildingManager>.instance;
			DistrictManager districtManager = Singleton<DistrictManager>.instance;
			
			Building b1 = buildingManager.m_buildings.m_buffer[building1];
			Building b2 = buildingManager.m_buildings.m_buffer[building2];
			
			byte district1 = districtManager.GetDistrict(b1.m_position);
			byte district2 = districtManager.GetDistrict(b2.m_position);
			
			return district1 == district2;
		}

		public static byte GetBuildingDistrict(ushort buildingId)
		{
			if (buildingId == 0)
				return 0;
			
			BuildingManager buildingManager = Singleton<BuildingManager>.instance;
			DistrictManager districtManager = Singleton<DistrictManager>.instance;
			
			Building building = buildingManager.m_buildings.m_buffer[buildingId];
			return districtManager.GetDistrict(building.m_position);
		}

		public void LogWithTimestamp(string message)
		{
			DateTime gameTime = Singleton<SimulationManager>.instance.m_currentGameTime;
			Debug.Log($"GMGT CT[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] GT[{gameTime:yyyy-MM-dd HH:mm:ss}]: {message}");
		}
		
		public static bool IsValidDistrictTarget(ushort landfillId, ushort targetId)
		{
			// District restrictions disabled - all targets valid
			if (!Identity.ModConf.DistrictRestrictedService)
				return true;
			
			byte landfillDistrict = GetBuildingDistrict(landfillId);
			
			// Landfill in district 0 (outside districts) - can service anywhere
			if (landfillDistrict == 0)
				return true;
			
			byte targetDistrict = GetBuildingDistrict(targetId);
			
			// Target must be in same district
			return targetDistrict == landfillDistrict;
		}

    }
}