using System;
using System.Collections.Generic;
using Pragma.Dots.Actors;
using UnityEngine;

namespace Pragma.Dots.Tests
{
    /// <summary>Entries by key and one setting: overrides change single entries and take the setting over.</summary>
    [Serializable, ActorPartMenu("Tests/Mergeable")]
    public sealed class TestMergeablePart : ActorPart, IMergeableActorPart
    {
        [SerializeField] private int _setting;
        [SerializeField] private List<TestMergedEntry> _entries = new List<TestMergedEntry>();

        public TestMergeablePart()
        {
        }

        public TestMergeablePart(int setting, params (int key, int value)[] entries)
        {
            _setting = setting;
            foreach (var (key, value) in entries)
            {
                _entries.Add(new TestMergedEntry { key = key, value = value });
            }
        }

        public IReadOnlyList<TestMergedEntry> Entries => _entries;

        public override void Build(IActorBuilder builder)
        {
            var buffer = builder.AddBuffer<TestMergedEntry>();
            foreach (var entry in _entries)
            {
                buffer.Add(entry);
            }
            builder.AddComponent(new TestActorValue { value = _setting });
        }

        public ActorPart MergeOnto(ActorPart inherited)
        {
            var merged = new TestMergeablePart { _setting = _setting };
            merged._entries.AddRange(((TestMergeablePart)inherited)._entries);
            foreach (var entry in _entries)
            {
                var index = merged._entries.FindIndex(existing => existing.key == entry.key);
                if (index >= 0)
                {
                    merged._entries[index] = entry;
                }
                else
                {
                    merged._entries.Add(entry);
                }
            }
            return merged;
        }

        public ActorPart CreateOverride()
        {
            return new TestMergeablePart { _setting = _setting };
        }
    }
}
