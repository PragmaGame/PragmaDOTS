using System;
using Pragma.Dots.Actors;
using Unity.Collections;
using UnityEngine;

namespace Pragma.Dots.Views
{
    /// <summary>
    /// An <see cref="EntityViewKey"/>: the actor is shown by the GameObject view bound to the key in an
    /// <see cref="EntityViewConfig"/>, the actor's own key unless another one is set. The same view previews the actor in
    /// the editor.
    /// </summary>
    [Serializable]
    [ActorPartMenu("View")]
    public sealed class ViewPart : ActorPart
    {
        [SerializeField, Tooltip("Key of the EntityViewConfig binder whose view shows the actor, up to 29 bytes of UTF-8. Empty: the actor's key.")]
        private string _key = string.Empty;

        /// <summary>The view key, or empty for the actor's key.</summary>
        public string Key
        {
            get => _key;
            set => _key = value ?? string.Empty;
        }

        public override void Build(IActorBuilder builder)
        {
            builder.AddComponent(new EntityViewKey(GetKey(builder.Key)));
        }

        /// <summary>The key the view is bound to for an actor whose key is <paramref name="actorKey"/>.</summary>
        public FixedString32Bytes GetKey(in FixedString32Bytes actorKey)
        {
            if (string.IsNullOrEmpty(_key))
            {
                return actorKey;
            }
            if (!EntityViewKey.TryCreate(_key, out var key))
            {
                Debug.LogError($"[Views] The view key '{_key}' of the actor '{actorKey}' must be 1 to 29 bytes of UTF-8; the actor's key is used.");
                return actorKey;
            }
            return key.value;
        }
    }
}
