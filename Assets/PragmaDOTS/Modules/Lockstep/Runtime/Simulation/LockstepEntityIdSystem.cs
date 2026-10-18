using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    /// <summary>Assigns <see cref="LockstepEntityId"/>s to new entities and rebuilds <see cref="LockstepEntityIdMap"/>.</summary>
    /// <remarks>
    /// <c>Instantiate</c> copies the id of the entity it copies: such a copy gets an id of its own, and the id stays with
    /// the entity that had it at the end of the previous tick.
    /// </remarks>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(LockstepEndSimulationEntityCommandBufferSystem))]
    [BurstCompile]
    public partial struct LockstepEntityIdSystem : ISystem
    {
        private NativeHashMap<uint, Entity> _map;
        private NativeHashMap<uint, Entity> _previous;
        private NativeList<int> _assigned;
        private EntityQuery _query;
        private int _orderVersion;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _map = new NativeHashMap<uint, Entity>(256, Allocator.Persistent);
            _previous = new NativeHashMap<uint, Entity>(256, Allocator.Persistent);
            _assigned = new NativeList<int>(64, Allocator.Persistent);
            _query = SystemAPI.QueryBuilder().WithAll<LockstepEntityId>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
            _orderVersion = -1;
            state.EntityManager.AddComponentData(state.SystemHandle, new LockstepEntityIdMap { map = _map });
            state.RequireForUpdate<LockstepEntityIdCounter>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _map.Dispose();
            _previous.Dispose();
            _assigned.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Without a structural change no entity got, lost or copied an id.
            var orderVersion = state.EntityManager.GetComponentOrderVersion<LockstepEntityId>();
            if (orderVersion == _orderVersion)
            {
                return;
            }
            _orderVersion = orderVersion;

            // Jobs of the gameplay systems may still read the map.
            ref var published = ref SystemAPI.GetComponentRW<LockstepEntityIdMap>(state.SystemHandle).ValueRW;
            ref var counter = ref SystemAPI.GetSingletonRW<LockstepEntityIdCounter>().ValueRW;
            var previous = _map;
            _map = _previous;
            _previous = previous;
            _map.Clear();
            _assigned.Clear();

            var entities = _query.ToEntityArray(Allocator.Temp);
            var ids = _query.ToComponentDataArray<LockstepEntityId>(Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                var id = ids[i].value;
                var isCopy = id != 0 && (_previous.TryGetValue(id, out var owner) ? owner != entities[i] : _map.ContainsKey(id));
                if (id == 0 || isCopy)
                {
                    id = ++counter.last;
                    ids[i] = new LockstepEntityId { value = id };
                    _assigned.Add(i);
                }
                _map.Add(id, entities[i]);
            }
            // Written after the walk and only where an id changed: rewriting every id would mark every chunk changed.
            for (var i = 0; i < _assigned.Length; i++)
            {
                var index = _assigned[i];
                SystemAPI.SetComponent(entities[index], ids[index]);
            }
            published.map = _map;
        }
    }
}
