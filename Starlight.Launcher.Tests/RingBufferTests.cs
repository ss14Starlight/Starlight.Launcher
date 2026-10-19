using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.Tests;

public sealed class RingBufferTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCapacity_Throws(int capacity)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(capacity));

    [Fact]
    public void BelowCapacity_KeepsInsertionOrder()
    {
        var buffer = new RingBuffer<int>(4);
        buffer.AddRange([1, 2, 3]);

        Assert.Equal(3, buffer.Count);
        Assert.Equal(4, buffer.Capacity);
        Assert.Equal([1, 2, 3], buffer);
        Assert.Equal(1, buffer[0]);
        Assert.Equal(3, buffer[2]);
    }

    [Fact]
    public void Overflow_DropsOldestItems()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.AddRange([1, 2, 3, 4, 5]);

        Assert.Equal(3, buffer.Count);
        Assert.Equal([3, 4, 5], buffer);
        Assert.Equal(3, buffer[0]);
        Assert.Equal(5, buffer[2]);
    }

    [Fact]
    public void Overflow_ManyTimesCapacity_StaysConsistent()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.AddRange(Enumerable.Range(1, 100));

        Assert.Equal([98, 99, 100], buffer);
    }

    [Fact]
    public void CapacityOne_KeepsOnlyLatest()
    {
        var buffer = new RingBuffer<string>(1) { "a", "b" };

        Assert.Equal(["b"], buffer);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Indexer_OutOfRange_Throws(int index)
    {
        var buffer = new RingBuffer<int>(3);
        buffer.AddRange([1, 2]);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = buffer[index]);
    }

    [Fact]
    public void Clear_ResetsAndAllowsReuse()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.AddRange([1, 2, 3, 4]);

        buffer.Clear();

        Assert.Empty(buffer);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = buffer[0]);

        buffer.AddRange([7, 8]);
        Assert.Equal([7, 8], buffer);
    }
}
