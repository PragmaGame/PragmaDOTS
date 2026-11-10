using System;
using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Mathematics.Editor
{
    /// <summary>
    /// A <see cref="FixedCurve"/> edited with Unity's curve editor. An edit is stored key for key; a curve with weighted
    /// keys is stored as linear samples instead, since fixed-point keys have no weights.
    /// </summary>
    /// <remarks>
    /// Linear and constant segments are shown with Unity's Linear and Constant tangent modes, so they keep their shape
    /// when a key moves, and a segment with Linear modes on both sides is stored as <see cref="FixedCurveInterpolation.Linear"/>.
    /// </remarks>
    [CustomPropertyDrawer(typeof(FixedCurve))]
    internal sealed class FixedCurveDrawer : PropertyDrawer
    {
        private const int BAKE_SAMPLES = 64;
        private const string KEYS = "_keys";
        private const string PRE_WRAP = "_preWrap";
        private const string POST_WRAP = "_postWrap";

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            if (!TryRead(property, out var curve))
            {
                EditorGUI.LabelField(position, label.text, "The keys are not in time order: fix them in the Debug inspector.");
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.BeginChangeCheck();
            var edited = EditorGUI.CurveField(position, label, ToEditorCurve(curve));
            if (EditorGUI.EndChangeCheck())
            {
                Write(property, FromEditorCurve(edited));
            }
            EditorGUI.EndProperty();
        }

        /// <summary>The curve for Unity's curve editor, with the tangent modes of its linear and constant segments.</summary>
        internal static AnimationCurve ToEditorCurve(FixedCurve curve)
        {
            var unity = curve.ToAnimationCurve();
            var keys = curve.Keys;
            for (var i = 0; i < keys.Length - 1; i++)
            {
                switch (keys[i].interpolation)
                {
                    case FixedCurveInterpolation.Linear:
                        SetSegmentMode(unity, i, AnimationUtility.TangentMode.Linear);
                        break;
                    case FixedCurveInterpolation.Constant:
                        SetSegmentMode(unity, i, AnimationUtility.TangentMode.Constant);
                        break;
                }
            }
            return unity;
        }

        /// <summary>
        /// The fixed curve of an edited one: key for key, with segments that have Linear tangent modes on both sides stored
        /// as <see cref="FixedCurveInterpolation.Linear"/>; a curve the conversion refuses, such as a weighted one, is baked.
        /// </summary>
        internal static FixedCurve FromEditorCurve(AnimationCurve edited)
        {
            if (!FixedCurve.TryFromAnimationCurve(edited, out var converted))
            {
                return FixedCurve.Bake(edited, BAKE_SAMPLES);
            }

            var keys = converted.Keys.ToArray();
            for (var i = 0; i < keys.Length - 1; i++)
            {
                if (keys[i].interpolation == FixedCurveInterpolation.Cubic
                    && AnimationUtility.GetKeyRightTangentMode(edited, i) == AnimationUtility.TangentMode.Linear
                    && AnimationUtility.GetKeyLeftTangentMode(edited, i + 1) == AnimationUtility.TangentMode.Linear)
                {
                    // A linear segment ignores the tangents at its ends: store them as zero, like FixedCurveKey(time, value).
                    keys[i].interpolation = FixedCurveInterpolation.Linear;
                    keys[i].outTangent = FixedPoint.Zero;
                    keys[i + 1].inTangent = FixedPoint.Zero;
                }
            }
            return new FixedCurve(converted.PreWrap, converted.PostWrap, keys);
        }

        private static void SetSegmentMode(AnimationCurve curve, int index, AnimationUtility.TangentMode mode)
        {
            AnimationUtility.SetKeyRightTangentMode(curve, index, mode);
            AnimationUtility.SetKeyLeftTangentMode(curve, index + 1, mode);
        }

        private static bool TryRead(SerializedProperty property, out FixedCurve curve)
        {
            var keysProperty = property.FindPropertyRelative(KEYS);
            var keys = new FixedCurveKey[keysProperty.arraySize];
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keysProperty.GetArrayElementAtIndex(i);
                keys[i] = new FixedCurveKey(
                    FixedPointDrawerUtility.GetValue(key.FindPropertyRelative(nameof(FixedCurveKey.time))),
                    FixedPointDrawerUtility.GetValue(key.FindPropertyRelative(nameof(FixedCurveKey.value))),
                    FixedPointDrawerUtility.GetValue(key.FindPropertyRelative(nameof(FixedCurveKey.inTangent))),
                    FixedPointDrawerUtility.GetValue(key.FindPropertyRelative(nameof(FixedCurveKey.outTangent))))
                {
                    interpolation = (FixedCurveInterpolation)key.FindPropertyRelative(nameof(FixedCurveKey.interpolation)).intValue,
                };
            }
            var preWrap = (FixedCurveWrap)property.FindPropertyRelative(PRE_WRAP).intValue;
            var postWrap = (FixedCurveWrap)property.FindPropertyRelative(POST_WRAP).intValue;
            try
            {
                curve = new FixedCurve(preWrap, postWrap, keys);
                return true;
            }
            catch (ArgumentException)
            {
                curve = null;
                return false;
            }
        }

        private static void Write(SerializedProperty property, FixedCurve curve)
        {
            var keys = curve.Keys;
            var keysProperty = property.FindPropertyRelative(KEYS);
            keysProperty.arraySize = keys.Length;
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keysProperty.GetArrayElementAtIndex(i);
                FixedPointDrawerUtility.SetValue(key.FindPropertyRelative(nameof(FixedCurveKey.time)), keys[i].time);
                FixedPointDrawerUtility.SetValue(key.FindPropertyRelative(nameof(FixedCurveKey.value)), keys[i].value);
                FixedPointDrawerUtility.SetValue(key.FindPropertyRelative(nameof(FixedCurveKey.inTangent)), keys[i].inTangent);
                FixedPointDrawerUtility.SetValue(key.FindPropertyRelative(nameof(FixedCurveKey.outTangent)), keys[i].outTangent);
                key.FindPropertyRelative(nameof(FixedCurveKey.interpolation)).intValue = (int)keys[i].interpolation;
            }
            property.FindPropertyRelative(PRE_WRAP).intValue = (int)curve.PreWrap;
            property.FindPropertyRelative(POST_WRAP).intValue = (int)curve.PostWrap;
        }
    }
}
