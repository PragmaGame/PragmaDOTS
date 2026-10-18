using Pragma.Dots.Lockstep;
using Unity.Transforms;
using UnityEngine;

namespace Pragma.Dots.Views
{
    /// <summary>
    /// Moves its GameObject to the simulated transform, interpolated between ticks. <see cref="TransformViewUpdateSystem"/>
    /// feeds it from <see cref="LockstepTransform"/>; a view without this part does not move.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Pragma/DOTS/Transform Component View")]
    public class TransformComponentView : EntityComponentView<LocalTransform>
    {
        private Transform _transform;

        /// <inheritdoc />
        public override void UpdateData(LocalTransform data)
        {
            if (_transform == null)
            {
                _transform = transform;
            }
            Vector3 position = data.Position;
            Quaternion rotation = data.Rotation;
            var scale = new Vector3(data.Scale, data.Scale, data.Scale);

            // Entities at rest keep their transform untouched instead of being marked changed every frame.
            if (position != _transform.position)
            {
                _transform.position = position;
            }
            if (rotation != _transform.rotation)
            {
                _transform.rotation = rotation;
            }
            if (scale != _transform.localScale)
            {
                _transform.localScale = scale;
            }
        }
    }
}
