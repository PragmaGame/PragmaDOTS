using System;
using Unity.Entities;

namespace Pragma.Dots.Utils
{
    /// <summary>
    /// A dynamic buffer used as a dictionary of any size: its elements (<see cref="IDictionaryElement{TKey}"/>) are kept
    /// sorted by key and found by binary search. Burst-compatible.
    /// </summary>
    /// <remarks>
    /// Iterating the buffer visits the entries in key order, whatever order they were added in, so it is deterministic
    /// for any key with a deterministic <c>CompareTo</c> (integers, fixed strings; not <c>Entity</c>, whose index differs
    /// between clients). A lookup costs O(log n), an add or a remove O(n) for the elements it moves. Change the entries
    /// through these methods, or keep the buffer sorted by key yourself, and never change a key in place.
    /// </remarks>
    public static class BufferDictionary
    {
        /// <summary>
        /// The index of the element with <paramref name="key"/>, or, when there is none, the bitwise complement of the index
        /// it would be inserted at (like <c>Array.BinarySearch</c>).
        /// </summary>
        public static int IndexOf<TElement, TKey>(this DynamicBuffer<TElement> buffer, TKey key)
            where TElement : unmanaged, IDictionaryElement<TKey>
            where TKey : unmanaged, IComparable<TKey>
        {
            var low = 0;
            var high = buffer.Length - 1;
            while (low <= high)
            {
                var middle = (low + high) >> 1;
                // The indexer, not ElementAt: lookups work on read-only buffers too.
                var order = buffer[middle].Key.CompareTo(key);
                if (order == 0)
                {
                    return middle;
                }
                if (order < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return ~low;
        }

        public static bool ContainsKey<TElement, TKey>(this DynamicBuffer<TElement> buffer, TKey key)
            where TElement : unmanaged, IDictionaryElement<TKey>
            where TKey : unmanaged, IComparable<TKey>
        {
            return buffer.IndexOf(key) >= 0;
        }

        public static bool TryGet<TElement, TKey>(this DynamicBuffer<TElement> buffer, TKey key, out TElement element)
            where TElement : unmanaged, IDictionaryElement<TKey>
            where TKey : unmanaged, IComparable<TKey>
        {
            var index = buffer.IndexOf(key);
            element = index >= 0 ? buffer[index] : default;
            return index >= 0;
        }

        /// <summary>
        /// A reference to the element with <paramref name="key"/>, inserted with its other fields at their defaults when it
        /// is missing. The reference is valid until the buffer changes.
        /// </summary>
        public static ref TElement GetOrAdd<TElement, TKey>(this DynamicBuffer<TElement> buffer, TKey key)
            where TElement : unmanaged, IDictionaryElement<TKey>
            where TKey : unmanaged, IComparable<TKey>
        {
            return ref buffer.ElementAt(FindOrInsert(buffer, key, out _));
        }

        /// <inheritdoc cref="GetOrAdd{TElement, TKey}(DynamicBuffer{TElement}, TKey)"/>
        public static ref TElement GetOrAdd<TElement, TKey>(this DynamicBuffer<TElement> buffer, TKey key, out bool added)
            where TElement : unmanaged, IDictionaryElement<TKey>
            where TKey : unmanaged, IComparable<TKey>
        {
            return ref buffer.ElementAt(FindOrInsert(buffer, key, out added));
        }

        /// <summary>Removes the element with <paramref name="key"/>; the others keep their order.</summary>
        public static bool Remove<TElement, TKey>(this DynamicBuffer<TElement> buffer, TKey key)
            where TElement : unmanaged, IDictionaryElement<TKey>
            where TKey : unmanaged, IComparable<TKey>
        {
            var index = buffer.IndexOf(key);
            if (index < 0)
            {
                return false;
            }
            buffer.RemoveAt(index);
            return true;
        }

        private static int FindOrInsert<TElement, TKey>(DynamicBuffer<TElement> buffer, TKey key, out bool added)
            where TElement : unmanaged, IDictionaryElement<TKey>
            where TKey : unmanaged, IComparable<TKey>
        {
            var index = buffer.IndexOf(key);
            added = index < 0;
            if (added)
            {
                index = ~index;
                var element = default(TElement);
                element.Key = key;
                buffer.Insert(index, element);
            }
            return index;
        }
    }
}
