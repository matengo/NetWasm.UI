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
