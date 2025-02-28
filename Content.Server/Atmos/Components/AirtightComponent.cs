using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.ViewVariables;

namespace Content.Server.Atmos.Components
{
    [RegisterComponent]
    public class AirtightComponent : Component, IMapInit
    {
        [Dependency] private readonly IMapManager _mapManager = default!;

        private (GridId, Vector2i) _lastPosition;
        private AtmosphereSystem _atmosphereSystem = default!;

        public override string Name => "Airtight";

        [DataField("airBlockedDirection", customTypeSerializer: typeof(FlagSerializer<AtmosDirectionFlags>))]
        [ViewVariables]
        private int _initialAirBlockedDirection = (int) AtmosDirection.All;

        [ViewVariables]
        private int _currentAirBlockedDirection;

        [DataField("airBlocked")]
        private bool _airBlocked = true;

        [DataField("fixVacuum")]
        private bool _fixVacuum = true;

        [ViewVariables]
        [DataField("rotateAirBlocked")]
        private bool _rotateAirBlocked = true;

        [ViewVariables]
        [DataField("fixAirBlockedDirectionInitialize")]
        private bool _fixAirBlockedDirectionInitialize = true;

        [ViewVariables]
        [DataField("noAirWhenFullyAirBlocked")]
        public bool NoAirWhenFullyAirBlocked { get; } = true;

        [ViewVariables(VVAccess.ReadWrite)]
        public bool AirBlocked
        {
            get => _airBlocked;
            set
            {
                _airBlocked = value;

                UpdatePosition();
            }
        }

        public AtmosDirection AirBlockedDirection
        {
            get => (AtmosDirection)_currentAirBlockedDirection;
            set
            {
                _currentAirBlockedDirection = (int) value;
                _initialAirBlockedDirection = (int)Rotate(AirBlockedDirection, -Owner.Transform.LocalRotation);

                UpdatePosition();
            }
        }

        [ViewVariables]
        public bool FixVacuum => _fixVacuum;

        protected override void Initialize()
        {
            base.Initialize();

            _atmosphereSystem = EntitySystem.Get<AtmosphereSystem>();

            if (_fixAirBlockedDirectionInitialize)
                RotateEvent(new RotateEvent(Owner, Angle.Zero, Owner.Transform.WorldRotation));

            // Adding this component will immediately anchor the entity, because the atmos system
            // requires airtight entities to be anchored for performance.
            Owner.Transform.Anchored = true;

            UpdatePosition();
        }

        public void RotateEvent(RotateEvent ev)
        {
            if (!_rotateAirBlocked || ev.Sender != Owner || _initialAirBlockedDirection == (int)AtmosDirection.Invalid)
                return;

            _currentAirBlockedDirection = (int) Rotate((AtmosDirection)_initialAirBlockedDirection, ev.NewRotation);
            UpdatePosition();
        }

        private AtmosDirection Rotate(AtmosDirection myDirection, Angle myAngle)
        {
            var newAirBlockedDirs = AtmosDirection.Invalid;

            if (myAngle == Angle.Zero)
                return myDirection;

            // TODO ATMOS MULTIZ When we make multiZ atmos, special case this.
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                if (!myDirection.IsFlagSet(direction)) continue;
                var angle = direction.ToAngle();
                angle += myAngle;
                newAirBlockedDirs |= angle.ToAtmosDirectionCardinal();
            }

            return newAirBlockedDirs;
        }

        public void MapInit()
        {
            UpdatePosition();
        }

        protected override void Shutdown()
        {
            base.Shutdown();

            _airBlocked = false;

            InvalidatePosition(_lastPosition.Item1, _lastPosition.Item2);

            if (_fixVacuum)
            {
                _atmosphereSystem.GetGridAtmosphere(_lastPosition.Item1)?.FixVacuum(_lastPosition.Item2);
            }
        }

        public void AnchorStateChanged()
        {
            var gridId = Owner.Transform.GridID;
            var coords = Owner.Transform.Coordinates;

            var grid = _mapManager.GetGrid(gridId);
            var tilePos = grid.TileIndicesFor(coords);

            // Update and invalidate new position.
            _lastPosition = (gridId, tilePos);
            InvalidatePosition(gridId, tilePos);
        }

        private void UpdatePosition()
        {
            if (!Owner.Transform.Anchored || !Owner.Transform.GridID.IsValid())
                return;

            var grid = _mapManager.GetGrid(Owner.Transform.GridID);
            _lastPosition = (Owner.Transform.GridID, grid.TileIndicesFor(Owner.Transform.Coordinates));
            InvalidatePosition(_lastPosition.Item1, _lastPosition.Item2);
        }

        private void InvalidatePosition(GridId gridId, Vector2i pos)
        {
            if (!gridId.IsValid())
                return;

            var gridAtmos = _atmosphereSystem.GetGridAtmosphere(gridId);

            gridAtmos?.UpdateAdjacentBits(pos);
            gridAtmos?.Invalidate(pos);
        }
    }
}
