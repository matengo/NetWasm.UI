using NetWasm.UI.Dom;

namespace NetWasm.UI.Tests;

// Released node ids are reused, so the host's node table stays dense: a single node's id by the next single node, a
// template clone's block by the next clone of the same size. FakeDomHost rejects an id created while still live.
public class NodeIdReuseTests : RendererTest
{
    private static readonly Template Row = new("<tr><td><!--$--></td><td $>x</td></tr>");

    [Fact]
    public void A_released_node_id_is_reused_after_its_release()
    {
        var first = new Element("div");
        first.Dispose();
        var second = new Element("span");
        Renderer.Flush();

        Assert.Equal(first.Id, second.Id);
        var release = Dom.Log.FindIndex(l => l.Op == "Release" && l.Detail == first.Id.ToString());
        var create = Dom.Log.FindLastIndex(l => l.Op == "CreateElement" && l.Detail == second.Id + " span");
        Assert.True(release >= 0 && release < create);
    }

    [Fact]
    public void A_template_clone_reuses_a_released_block_of_its_size_and_single_nodes_do_not()
    {
        var instance = Row.Clone();
        var block = instance.Root.Id;
        instance.Dispose();
        var single = new Element("div");
        var again = Row.Clone();
        Renderer.Flush();

        Assert.Equal(block, again.Root.Id);
        Assert.NotInRange(single.Id, block, block + Row.HoleCount - 1);
    }

    [Fact]
    public void Churn_keeps_node_ids_bounded()
    {
        var highest = 0;
        for (var round = 0; round < 1000; round++)
        {
            var instance = Row.Clone();
            var element = new Element("p");
            highest = Math.Max(highest, Math.Max(instance.Root.Id + Row.HoleCount - 1, element.Id));
            element.Dispose();
            instance.Dispose();
            Renderer.Flush();
        }

        Assert.True(highest <= Row.HoleCount + 1, $"highest node id {highest}");
    }

    [Fact]
    public void Disposing_twice_releases_an_id_once()
    {
        var node = new Element("div");
        node.Dispose();
        node.Dispose();
        var instance = Row.Clone();
        instance.Root.Dispose();
        instance.Dispose();
        instance.Dispose();
        Renderer.Flush();

        // Each id was freed once, so these creations get distinct ids; FakeDomHost throws on a live id.
        var a = new Element("p");
        var b = new Element("p");
        var c = Row.Clone();
        var d = Row.Clone();
        Renderer.Flush();

        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(c.Root.Id, d.Root.Id);
        Assert.Equal(1, Dom.Log.Count(l => l.Op == "Release" && l.Detail == node.Id.ToString()));
    }

    [Fact]
    public void Reset_forgets_released_ids()
    {
        new Element("div").Dispose();
        Row.Clone().Dispose();
        Renderer.Reset();
        Renderer.Initialize(Dom);

        Assert.Equal(1, new Element("div").Id);
        Assert.Equal(2, Row.Clone().Root.Id);
    }
}
