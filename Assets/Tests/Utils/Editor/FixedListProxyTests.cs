using NUnit.Framework;
using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Utils.Editor;
using Unity.Collections;
using UnityEngine;

namespace Pragma.Lockstep.Utils.Tests
{
    public class FixedListProxyTests
    {
        [Test]
        public void FixedListProxy_RoundTripsElementsThroughTheArray()
        {
            var list = new FixedList64Bytes<FixedPoint> { (FixedPoint)1, FixedPoint.FromFraction(1, 2) };
            var proxy = FixedListProxy.Create(list.GetType());
            try
            {
                proxy.Read(list);
                var items = proxy.Items;
                Assert.IsNotNull(items, "the element type has serialized properties");
                Assert.AreEqual(2, items.arraySize);

                items.GetArrayElementAtIndex(1).FindPropertyRelative(nameof(FixedPoint.rawValue)).longValue = ((FixedPoint)3).rawValue;
                var written = (FixedList64Bytes<FixedPoint>)proxy.Write();

                Assert.AreEqual(2, written.Length);
                Assert.AreEqual((FixedPoint)1, written[0]);
                Assert.AreEqual((FixedPoint)3, written[1]);
            }
            finally
            {
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void FixedListProxy_CutsTheListAtItsCapacity()
        {
            var list = new FixedList32Bytes<int>();
            var proxy = FixedListProxy.Create(list.GetType());
            try
            {
                proxy.Read(list);
                proxy.Items.arraySize = list.Capacity + 5;
                var written = (FixedList32Bytes<int>)proxy.Write();

                Assert.AreEqual(list.Capacity, written.Length);
            }
            finally
            {
                Object.DestroyImmediate(proxy);
            }
        }
    }
}
