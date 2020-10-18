using NUnit.Framework;

namespace kcp2k.Tests
{
    public class SegmentTests
    {
        [SetUp]
        public void SetUp()
        {
        }

        [TearDown]
        public void TearDown()
        {
            // always clear the pool
            Segment.Pool.Clear();
        }

        [Test]
        public void TakeFromEmptyPool()
        {
            // taking from an empty pool should still return a new segment with
            // the correct size
            Segment seg = Segment.Take(42);
            Assert.That(seg.data.Capacity, Is.EqualTo(42));
        }

        [Test]
        public void TakeReturnTake()
        {
            // take a new one from an empty pool
            Segment seg = Segment.Take(22);

            // return to pool
            Segment.Return(seg);

            // next take call should give our returned one instead of allocating
            // a new one
            Segment val = Segment.Take(44);
            Assert.That(val, Is.EqualTo(seg));
        }
    }
}