using Unity.Entities;

namespace Pragma.Dots.Utils
{
    /// <summary>
    /// Reads of a component or buffer that may be missing. Entities covers the rest: <c>AddComponent</c> and
    /// <c>RemoveComponent</c> do nothing (and return false) when the component is already there or already gone, and
    /// <c>AddComponentData</c> sets the value of a component the entity already has.
    /// </summary>
    public static class EntityManagerExtensions
    {
        /// <summary>The component of <paramref name="entity"/>, if it exists and has one (a tag gives its default).</summary>
        public static bool TryGetComponent<T>(this EntityManager entityManager, Entity entity, out T component) where T : unmanaged, IComponentData
        {
            if (!entityManager.Exists(entity) || !entityManager.HasComponent<T>(entity))
            {
                component = default;
                return false;
            }
            component = ComponentType.ReadOnly<T>().IsZeroSized ? default : entityManager.GetComponentData<T>(entity);
            return true;
        }

        /// <summary>The buffer of <paramref name="entity"/>, if it exists and has one.</summary>
        public static bool TryGetBuffer<T>(this EntityManager entityManager, Entity entity, bool isReadOnly, out DynamicBuffer<T> buffer)
            where T : unmanaged, IBufferElementData
        {
            if (!entityManager.Exists(entity) || !entityManager.HasBuffer<T>(entity))
            {
                buffer = default;
                return false;
            }
            buffer = entityManager.GetBuffer<T>(entity, isReadOnly);
            return true;
        }
    }
}
