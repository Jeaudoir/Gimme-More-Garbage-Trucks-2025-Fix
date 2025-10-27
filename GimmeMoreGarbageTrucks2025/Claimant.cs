using System;
using ColossalFramework;

namespace GimmeMoreGarbageTrucks2025
{
    // Tracks which truck has "claimed" a garbage building as its target.
    // Uses distance to determine if another truck is close enough to challenge the claim.
    // Distance values: PositiveInfinity = invalid/no claim, NegativeInfinity = too close to challenge, normal float = actual distance squared
    public class Claimant
    {
        private readonly ushort _id;
        private readonly ushort _target;
        private float _distance;
        private DateTime _lastUpdated;

        public ushort Vehicle => _id;

        public float Distance
        {
            get
            {
                UpdateDistance();
                return _distance;
            }
        }

        public bool IsValid => !float.IsPositiveInfinity(Distance);

        public bool IsChallengable => !float.IsNegativeInfinity(Distance);

        public Claimant(ushort id, ushort target)
        {
            _id = id;
            _target = target;
            _distance = float.PositiveInfinity;
            _lastUpdated = default(DateTime);
        }

        private void UpdateDistance()
        {
            if (_lastUpdated == Singleton<SimulationManager>.instance.m_currentGameTime)
            {
                return;
            }
            _lastUpdated = Singleton<SimulationManager>.instance.m_currentGameTime;
            
            if (!Helper.IsGarbageTruck(_id) || !Helper.IsBuildingWithGarbage(_target))
            {
                _distance = float.PositiveInfinity;
                return;
            }
            
            Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            Vehicle vehicle = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[_id];
            _distance = (buffer[_target].m_position - vehicle.GetLastFramePosition()).sqrMagnitude;
            
            if (_distance <= (float)Constants.Instance.ImmediateRange1)
            {
                _distance = float.NegativeInfinity;
                return;
            }
            
            if (vehicle.m_targetBuilding != _target)
            {
                _distance = float.PositiveInfinity;
                return;
            }
        }
    }
}