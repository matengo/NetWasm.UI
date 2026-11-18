using NetWasm.UI.Dom;

namespace NetWasm.UI.Tests;

public class BatchWriterTests
{
    [Fact]
    public void Encodes_ops_little_endian_with_utf8_strings()
    {
        var w = new BatchWriter(4);
        w.Op(Op.CreateElement);
        w.Int(258);
        w.String("hé");

        var bytes = w.Take();

        Assert.Equal(new byte[] { 1, 2, 1, 0, 0, 3, 0, 0, 0, (byte)'h', 0xC3, 0xA9 }, bytes);
        Assert.True(w.IsEmpty);
    }

    // Writes a batch of about `bytes` bytes, flushes it as the renderer does and returns the buffer it went out in.
    private static byte[] Flush(BatchWriter w, int bytes)
    {
        for (var i = 0; i < bytes / 5; i++)
        {
            w.Op(Op.Release);
            w.Int(i);
        }

        var ops = w.Detach(out _);
        w.Return(ops);
        return ops;
    }

    [Fact]
    public void A_buffer_that_one_large_batch_grew_is_not_kept_for_the_small_batches_after_it()
    {
        var w = new BatchWriter(1024);
        Flush(w, 512 * 1024);
        Assert.True(w.Capacity >= 512 * 1024);

        Flush(w, 100);
        Flush(w, 100);

        Assert.True(w.Capacity <= BatchWriter.KeepAtMost, $"capacity {w.Capacity}");
    }

    [Fact]
    public void Large_batches_one_after_another_keep_using_the_same_two_buffers()
    {
        var w = new BatchWriter(1024);
        var first = Flush(w, 512 * 1024);
        var second = Flush(w, 512 * 1024);

        Assert.Same(first, Flush(w, 512 * 1024));
        Assert.Same(second, Flush(w, 400 * 1024));
    }

    [Fact]
    public void A_block_release_next_to_the_one_before_it_extends_that_op()
    {
        var w = new BatchWriter(4);
        w.ReleaseBlock(20, 5);
        w.ReleaseBlock(25, 5); // follows it
        w.ReleaseBlock(15, 5); // precedes it
        w.ReleaseBlock(40, 5); // touches neither end: a new op, which the next block extends
        w.ReleaseBlock(35, 5);

        Assert.Equal(2, w.OpCount);
        Assert.Equal(new byte[] { 27, 15, 0, 0, 0, 15, 0, 0, 0, 27, 35, 0, 0, 0, 10, 0, 0, 0 }, w.Take());
    }

    [Fact]
    public void A_block_release_extends_only_the_last_op_of_the_same_batch()
    {
        var w = new BatchWriter(4);
        w.ReleaseBlock(20, 5);
        w.Op(Op.Release);
        w.Int(7);
        w.ReleaseBlock(25, 5);
        Assert.Equal(3, w.OpCount);

        w.Clear();
        w.ReleaseBlock(30, 5);
        Assert.Equal(new byte[] { 27, 30, 0, 0, 0, 5, 0, 0, 0 }, w.Take());

        // Take starts a new batch as well.
        w.ReleaseBlock(35, 5);
        Assert.Equal(new byte[] { 27, 35, 0, 0, 0, 5, 0, 0, 0 }, w.Take());
    }

    [Fact]
    public void Fake_host_round_trips_a_tree()
    {
        var host = new FakeDomHost();
        var w = new BatchWriter();
        w.Op(Op.CreateElement); w.Int(1); w.String("div");
        w.Op(Op.CreateText); w.Int(2); w.String("hi");
        w.Op(Op.SetAttribute); w.Int(1); w.String("class"); w.String("x");
        w.Op(Op.AppendChild); w.Int(1); w.Int(2);
        w.Op(Op.Mount); w.Int(1); w.String("#app");

        host.ApplyBatch(w.Take());

        Assert.Equal("<div class=\"x\">hi</div>", host.Html);
    }
}
