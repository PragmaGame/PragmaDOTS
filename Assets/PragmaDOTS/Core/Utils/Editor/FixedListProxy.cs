using System;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Utils.Editor
{
    /// <summary>
    /// The elements of a fixed list copied into a serialized array. Unity serializes a fixed list as raw bytes, so its
    /// elements have no serialized properties of their own; the array gives them some, and with them the property
    /// drawers of their types.
    /// </summary>
    internal sealed class FixedListProxy : ScriptableObject
    {
        private interface IBox
        {
            void Read(object list);
            object Write(Type listType);
        }

        [Serializable]
        private sealed class Box<T> : IBox where T : unmanaged
        {
            public T[] items = Array.Empty<T>();

            public void Read(object list)
            {
                var source = (INativeList<T>)list;
                items = new T[source.Length];
                for (var i = 0; i < items.Length; i++)
                {
                    items[i] = source[i];
                }
            }

            public object Write(Type listType)
            {
                // Through the interface the writes go to the box, which is what gets returned.
                var list = (INativeList<T>)Activator.CreateInstance(listType);
                list.Length = Math.Min(items.Length, list.Capacity);
                for (var i = 0; i < list.Length; i++)
                {
                    list[i] = items[i];
                }
                return list;
            }
        }

        [SerializeReference] private IBox _box;
        private SerializedObject _serializedObject;

        public Type ListType { get; private set; }

        public Type ElementType => ListType.GetGenericArguments()[0];

        /// <summary>The array of elements; null when Unity cannot serialize the element type.</summary>
        public SerializedProperty Items => _serializedObject.FindProperty(nameof(_box) + "." + nameof(Box<int>.items));

        public static FixedListProxy Create(Type listType)
        {
            var proxy = CreateInstance<FixedListProxy>();
            proxy.hideFlags = HideFlags.HideAndDontSave;
            proxy.ListType = listType;
            proxy._box = (IBox)Activator.CreateInstance(typeof(Box<>).MakeGenericType(proxy.ElementType));
            proxy._serializedObject = new SerializedObject(proxy);
            return proxy;
        }

        /// <summary>Copies the elements of <paramref name="list"/> (a boxed list of <see cref="ListType"/>) into the array.</summary>
        public void Read(object list)
        {
            _box.Read(list);
            _serializedObject.Update();
        }

        /// <summary>Applies the edits of the array and returns them as a boxed list, cut at its capacity.</summary>
        public object Write()
        {
            _serializedObject.ApplyModifiedPropertiesWithoutUndo();
            return _box.Write(ListType);
        }
    }
}
