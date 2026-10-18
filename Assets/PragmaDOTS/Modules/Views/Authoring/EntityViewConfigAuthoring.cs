using Unity.Entities;
using UnityEngine;
#if PRAGMA_DOTS_UNITY_NETCODE
using Unity.NetCode.Hybrid;
#endif

namespace Pragma.Dots.Views.Authoring
{
    /// <summary>
    /// Bakes an <see cref="EntityViewConfig"/> into the presentation world: an <see cref="EntityViewRegistry"/> entity
    /// with one <see cref="EntityViewPrefabElement"/> per binder.
    /// </summary>
    /// <remarks>
    /// Put it in a subscene loaded by the client (or local) world. A dedicated server build bakes nothing here, so the
    /// view prefabs stay out of it. Worlds created after the subscene loaded need <see cref="EntityViewConfigProvider"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Pragma/DOTS/Entity View Config")]
    public sealed class EntityViewConfigAuthoring : MonoBehaviour
    {
        [SerializeField] private EntityViewConfig _config;

        private sealed class Baker : Baker<EntityViewConfigAuthoring>
        {
            public override void Bake(EntityViewConfigAuthoring authoring)
            {
#if PRAGMA_DOTS_UNITY_NETCODE
                // The server never presents anything.
                if (this.GetNetcodeTarget(false) == NetcodeConversionTarget.Server)
                {
                    return;
                }
#endif
                if (authoring._config == null)
                {
                    Debug.LogError($"[Lockstep] {nameof(EntityViewConfigAuthoring)} on '{authoring.name}' has no config.", authoring);
                    return;
                }
                DependsOn(authoring._config);

                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent<EntityViewRegistry>(entity);
                var elements = AddBuffer<EntityViewPrefabElement>(entity);
                foreach (var binder in authoring._config.Binders)
                {
                    if (binder.View == null || !EntityViewKey.TryCreate(binder.Key, out var key))
                    {
                        Debug.LogError($"[Lockstep] {authoring._config.name}: the binder '{binder.Key}' needs a view and a key of 1 to 29 bytes.", authoring._config);
                        continue;
                    }
                    elements.Add(new EntityViewPrefabElement { key = key.value, prefab = binder.View.gameObject });
                }
            }
        }
    }
}
