using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;

namespace Pragma.Dots.Utils
{
    /// <summary>
    /// A hash map of unmanaged keys and values stored in the struct itself (4 KB), for components and other blittable
    /// state. Open addressing with linear probing; the table doubles up to <see cref="MaxCapacity"/> as it fills.
    /// </summary>
    /// <remarks>
    /// Enumeration walks the slots, so its order follows <c>TKey.GetHashCode</c> and the history of adds and removes.
    /// Simulation state needs keys whose hash is the same on every machine and runtime: integers, fixed strings, structs
    /// that override <c>GetHashCode</c> over such fields. Not <c>Entity</c> (its index differs between clients) and not
    /// structs left with the default <c>ValueType.GetHashCode</c> (runtime-specific, and not Burst-compatible).
    /// </remarks>
    public struct FixedDictionary4096Bytes<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        private const int DEFAULT_CAPACITY = 16;

        // Slot states as byte constants: with a byte enum nested in this generic struct, Burst 2.0 turned the value 2 into
        // 0 and emptied removed slots that probe chains still ran through.
        private const byte EMPTY = 0;
        private const byte OCCUPIED = 1;
        private const byte DELETED = 2;

        private struct Entry
        {
            public byte state;
            public TKey key;
            public TValue value;
        }

        // The table: its length is the capacity, zero until the first add.
        private FixedList4096Bytes<Entry> _entries;
        private int _count;
        private int _deleted;

        /// <summary>Starts with room for <paramref name="capacity"/> slots (clamped to 1 .. <see cref="MaxCapacity"/>).</summary>
        public FixedDictionary4096Bytes(int capacity)
        {
            _entries = default;
            _count = 0;
            _deleted = 0;
            Allocate(capacity);
        }

        public int Count => _count;

        /// <summary>The slots of the table; it grows on its own up to <see cref="MaxCapacity"/>.</summary>
        public int Capacity => _entries.Length;

        /// <summary>The number of entries that fit in 4 KB; adding past it fails.</summary>
        public int MaxCapacity => _entries.Capacity;

        /// <summary>Gets a value (throws if the key is missing) or adds or replaces one (throws if the dictionary is full).</summary>
        public TValue this[TKey key]
        {
            get
            {
                if (!TryFind(key, out var index))
                {
                    throw new KeyNotFoundException();
                }
                return _entries[index].value;
            }
            set
            {
                var index = FindOrAdd(key, out _);
                if (index < 0)
                {
                    throw new InvalidOperationException("FixedDictionary4096Bytes is full.");
                }
                _entries.ElementAt(index).value = value;
            }
        }

        /// <summary>Adds an entry; throws if the key is already there or the dictionary is full.</summary>
        public void Add(TKey key, TValue value)
        {
            var index = FindOrAdd(key, out var added);
            if (index < 0)
            {
                throw new InvalidOperationException("FixedDictionary4096Bytes is full.");
            }
            if (!added)
            {
                throw new ArgumentException("An item with the same key already exists.");
            }
            _entries.ElementAt(index).value = value;
        }

        /// <summary>Adds an entry unless the key is already there or the dictionary is full.</summary>
        public bool TryAdd(TKey key, TValue value)
        {
            var index = FindOrAdd(key, out var added);
            if (index < 0 || !added)
            {
                return false;
            }
            _entries.ElementAt(index).value = value;
            return true;
        }

        public bool Remove(TKey key)
        {
            if (!TryFind(key, out var index))
            {
                return false;
            }

            // A probe that reaches an empty slot stops there, so a slot followed by an empty one ends every chain through
            // it and can be emptied too; otherwise it stays as a marker that keeps the chain going.
            var next = index + 1 == _entries.Length ? 0 : index + 1;
            var state = _entries[next].state == EMPTY ? EMPTY : DELETED;
            _entries[index] = new Entry { state = state };
            if (state == DELETED)
            {
                _deleted++;
            }
            _count--;
            return true;
        }

        public bool ContainsKey(TKey key)
        {
            return TryFind(key, out _);
        }

        /// <summary>A reference to the value of <paramref name="key"/>; throws if the key is missing.</summary>
        public ref TValue GetValueRef(TKey key)
        {
            if (!TryFind(key, out var index))
            {
                throw new KeyNotFoundException();
            }
            return ref _entries.ElementAt(index).value;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (!TryFind(key, out var index))
            {
                value = default;
                return false;
            }
            value = _entries[index].value;
            return true;
        }

        /// <summary>Removes every entry and keeps the capacity.</summary>
        public void Clear()
        {
            if (_entries.Length > 0)
            {
                Allocate(_entries.Length);
            }
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(ref this);
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private bool TryFind(TKey key, out int index)
        {
            index = -1;
            var capacity = _entries.Length;
            if (_count == 0)
            {
                return false;
            }

            var slot = GetHash(key) % capacity;
            for (var probed = 0; probed < capacity; probed++)
            {
                ref var entry = ref _entries.ElementAt(slot);
                if (entry.state == EMPTY)
                {
                    return false;
                }
                if (entry.state == OCCUPIED && entry.key.Equals(key))
                {
                    index = slot;
                    return true;
                }
                slot = slot + 1 == capacity ? 0 : slot + 1;
            }
            return false;
        }

        // The slot of the key, added when missing; -1 when it is missing and the dictionary is full.
        private int FindOrAdd(TKey key, out bool added)
        {
            added = false;
            if (TryFind(key, out var index))
            {
                return index;
            }
            if (_count >= MaxCapacity)
            {
                return -1;
            }

            Reserve();
            index = FindFreeSlot(key);
            if (_entries[index].state == DELETED)
            {
                _deleted--;
            }
            _entries[index] = new Entry { state = OCCUPIED, key = key };
            _count++;
            added = true;
            return index;
        }

        // Keeps a quarter of the slots empty so that probes stay short: doubles the table while it can and drops the
        // markers of removed entries when they are what fills it.
        private void Reserve()
        {
            var capacity = _entries.Length;
            if (capacity == 0)
            {
                Allocate(DEFAULT_CAPACITY);
                return;
            }
            if ((_count + _deleted + 1) * 4 <= capacity * 3)
            {
                return;
            }

            var target = (_count + 1) * 4 > capacity * 3 ? Math.Min(capacity * 2, MaxCapacity) : capacity;
            if (target > capacity || _deleted > 0)
            {
                Rehash(target);
            }
        }

        private void Rehash(int capacity)
        {
            var entries = _entries;
            Allocate(capacity);
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.state == OCCUPIED)
                {
                    _entries[FindFreeSlot(entry.key)] = entry;
                    _count++;
                }
            }
        }

        // The first slot of the key's probe that holds no entry; the caller made sure one exists.
        private int FindFreeSlot(TKey key)
        {
            var capacity = _entries.Length;
            var slot = GetHash(key) % capacity;
            while (_entries[slot].state == OCCUPIED)
            {
                slot = slot + 1 == capacity ? 0 : slot + 1;
            }
            return slot;
        }

        private void Allocate(int capacity)
        {
            _entries = default;
            _entries.Length = Math.Max(1, Math.Min(capacity, _entries.Capacity));
            _count = 0;
            _deleted = 0;
        }

        private static int GetHash(TKey key)
        {
            return key.GetHashCode() & 0x7FFFFFFF;
        }

        /// <summary>Walks a copy of the dictionary taken when the enumeration starts.</summary>
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            // Not readonly: every read of a readonly struct field would copy the 4 KB.
            private FixedDictionary4096Bytes<TKey, TValue> _dictionary;
            private int _index;
            private KeyValuePair<TKey, TValue> _current;

            internal Enumerator(ref FixedDictionary4096Bytes<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
                _index = -1;
                _current = default;
            }

            public KeyValuePair<TKey, TValue> Current => _current;

            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                while (++_index < _dictionary._entries.Length)
                {
                    var entry = _dictionary._entries[_index];
                    if (entry.state == OCCUPIED)
                    {
                        _current = new KeyValuePair<TKey, TValue>(entry.key, entry.value);
                        return true;
                    }
                }
                return false;
            }

            public void Reset()
            {
                _index = -1;
                _current = default;
            }

            public void Dispose()
            {
            }
        }
    }
}
