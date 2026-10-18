using System;
using Unity.Entities;

namespace Pragma.Dots.Utils
{
    /// <summary>
    /// Signals: one-off events as entities. A signal is an entity with a component of the game (the event and its data)
    /// and a <see cref="SignalTag"/>; consumers read it with an ordinary query, and every signal is destroyed at the end of
    /// the update, just before the end-of-update command buffer plays back (<see cref="SignalCleanupSystem"/>; in a
    /// lockstep simulation, its own cleanup at the end of the tick).
    /// </summary>
    /// <remarks>
    /// Published through the end-of-update command buffer, the usual way and the one for jobs, a signal is created after the
    /// cleanup: it lives through the whole next update, and every system of that update sees it once. Published at once
    /// (<see cref="Publish{TSignal}(EntityManager, in TSignal)"/>) or through a buffer played back earlier, it is seen by
    /// the systems that follow, up to the end of the same update.
    /// </remarks>
    public static class Signals
    {
        /// <summary>Publishes a signal when the command buffer plays back.</summary>
        public static Entity Publish<TSignal>(this EntityCommandBuffer commandBuffer, in TSignal signal) where TSignal : unmanaged, IComponentData
        {
            var type = ComponentType.ReadWrite<TSignal>();
            var entity = commandBuffer.CreateEntity();
            // Both components in one command: one structural change at playback.
            commandBuffer.AddComponent(entity, new ComponentTypeSet(type, ComponentType.ReadWrite<SignalTag>()));
            if (!type.IsZeroSized)
            {
                commandBuffer.SetComponent(entity, signal);
            }
            return entity;
        }

        /// <summary>Publishes a signal from a job; <paramref name="sortKey"/> orders the commands as for any parallel writer.</summary>
        public static Entity Publish<TSignal>(this EntityCommandBuffer.ParallelWriter commandBuffer, int sortKey, in TSignal signal)
            where TSignal : unmanaged, IComponentData
        {
            var type = ComponentType.ReadWrite<TSignal>();
            var entity = commandBuffer.CreateEntity(sortKey);
            commandBuffer.AddComponent(sortKey, entity, new ComponentTypeSet(type, ComponentType.ReadWrite<SignalTag>()));
            if (!type.IsZeroSized)
            {
                commandBuffer.SetComponent(sortKey, entity, signal);
            }
            return entity;
        }

        /// <summary>Publishes a signal at once: a structural change.</summary>
        public static Entity Publish<TSignal>(this EntityManager entityManager, in TSignal signal) where TSignal : unmanaged, IComponentData
        {
            var type = ComponentType.ReadWrite<TSignal>();
            Span<ComponentType> types = stackalloc ComponentType[] { type, ComponentType.ReadWrite<SignalTag>() };
            var entity = entityManager.CreateEntity(types);
            if (!type.IsZeroSized)
            {
                entityManager.SetComponentData(entity, signal);
            }
            return entity;
        }
    }
}
