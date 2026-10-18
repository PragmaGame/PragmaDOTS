using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Pragma.Lockstep.Utils.Editor
{
    /// <summary>
    /// Shows fixed lists like arrays: elements are drawn by the property drawers of their types, the size is capped at
    /// the capacity of the list.
    /// </summary>
    [CustomPropertyDrawer(typeof(FixedList32Bytes<>), true)]
    [CustomPropertyDrawer(typeof(FixedList64Bytes<>), true)]
    [CustomPropertyDrawer(typeof(FixedList128Bytes<>), true)]
    [CustomPropertyDrawer(typeof(FixedList512Bytes<>), true)]
    [CustomPropertyDrawer(typeof(FixedList4096Bytes<>), true)]
    internal sealed class FixedListBytesDrawer : PropertyDrawer
    {
        // One proxy per drawn list, so that its foldout and list state survive repaints.
        private static readonly Dictionary<(EntityId, string), FixedListProxy> Proxies = new Dictionary<(EntityId, string), FixedListProxy>();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var items = GetProxy(property).Items;
            return items != null ? EditorGUI.GetPropertyHeight(items, label, true) : EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var proxy = GetProxy(property);
            var items = proxy.Items;
            if (items == null)
            {
                EditorGUI.LabelField(position, label, new GUIContent($"{proxy.ElementType.Name} is not serializable"));
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(position, items, label, true);
            if (EditorGUI.EndChangeCheck())
            {
                property.boxedValue = proxy.Write();
            }
            EditorGUI.EndProperty();
        }

        private static FixedListProxy GetProxy(SerializedProperty property)
        {
            var list = property.boxedValue;
            var key = (property.serializedObject.targetObject.GetEntityId(), property.propertyPath);
            if (!Proxies.TryGetValue(key, out var proxy) || proxy == null || proxy.ListType != list.GetType())
            {
                proxy = FixedListProxy.Create(list.GetType());
                Proxies[key] = proxy;
            }
            proxy.Read(list);
            return proxy;
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            // Proxies are hidden objects that outlive the domain; nothing would find them after a reload.
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                foreach (var proxy in Proxies.Values)
                {
                    if (proxy != null)
                    {
                        Object.DestroyImmediate(proxy);
                    }
                }
                Proxies.Clear();
            };
        }
    }
}
