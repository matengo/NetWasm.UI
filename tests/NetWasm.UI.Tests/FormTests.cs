using System.Threading.Tasks;
using NetWasm.UI.Forms;
using NetWasm.UI.Reactive;
using static NetWasm.UI.Html;

namespace NetWasm.UI.Tests;

public class FormTests : RendererTest
{
    [Theory]
    [InlineData("", "req")]
    [InlineData(" \t", "req")]
    [InlineData("ab", "min")]
    [InlineData("abc", null)]
    [InlineData("abcdef", "max")]
    public void Validators_run_in_order(string value, string? expected)
    {
        var field = new Field<string>(value, Validators.Required("req"), Validators.MinLength(3, "min"), Validators.MaxLength(5, "max"));
        Assert.Equal(expected, field.Error);
        Assert.Equal(expected is null, field.IsValid);
    }

    [Theory]
    [InlineData("a@b.se", true)]
    [InlineData("first.last@sub.example.com", true)]
    [InlineData("", true)]
    [InlineData("a@b", false)]
    [InlineData("@b.se", false)]
    [InlineData("a@.se", false)]
    [InlineData("a@b.", false)]
    [InlineData("a@@b.se", false)]
    [InlineData("a b@c.se", false)]
    public void Email_is_a_loose_ascii_check(string value, bool valid) =>
        Assert.Equal(valid, Validators.Email()(value) is null);

    [Fact]
    public void Digits_and_must()
    {
        Assert.Null(Validators.Digits()("0123"));
        Assert.NotNull(Validators.Digits()("12a"));
        Assert.Null(Validators.Must<int>(v => v > 0, "pos")(1));
        Assert.Equal("pos", Validators.Must<int>(v => v > 0, "pos")(0));
        Assert.Null(Validators.MustBeChecked()(true));
    }

    [Fact]
    public void Error_is_tracked_including_signals_read_by_validators()
    {
        var password = new Field<string>("secret");
        var confirm = new Field<string>("", Validators.Must<string>(v => v == password.Value, "mismatch"));
        var seen = new List<string?>();
        _ = new Effect(() => seen.Add(confirm.Error));

        confirm.Value = "secret";
        password.Value = "other";

        Assert.Equal(new[] { "mismatch", null, "mismatch" }, seen);
    }

    [Fact]
    public void Dirty_touched_external_error_and_reset()
    {
        var field = new Field<string>("a");
        Assert.False(field.Dirty);

        field.Value = "b";
        field.MarkTouched();
        field.SetError("taken");
        Assert.True(field.Dirty);
        Assert.True(field.Touched);
        Assert.Equal("taken", field.Error);

        field.Value = "c";
        Assert.Null(field.Error);

        field.Reset("c");
        Assert.False(field.Dirty);
        Assert.False(field.Touched);
        Assert.Equal("c", field.Value);
    }

    [Fact]
    public void Errors_show_after_touch_or_submit()
    {
        var form = new Form();
        var name = form.Field("", Validators.Required("req"));
        var other = form.Field("", Validators.Required("req"));

        Assert.False(name.ShowError);
        name.MarkTouched();
        Assert.True(name.ShowError);
        Assert.Equal("req", name.ShownError);
        Assert.False(other.ShowError);

        var ran = false;
        form.Submit(() => ran = true);

        Assert.False(ran);
        Assert.True(form.SubmitAttempted);
        Assert.True(other.ShowError);
    }

    [Fact]
    public void Submit_runs_when_valid_and_reports_exceptions()
    {
        var form = new Form();
        var name = form.Field("Ada", Validators.Required());
        var ran = 0;

        form.Submit(() => ran++);
        Assert.Equal(1, ran);
        Assert.Null(form.SubmitError);

        form.Submit(() => throw new InvalidOperationException("server said no"));
        Assert.Equal("server said no", form.SubmitError);
        Assert.False(form.Submitting);

        name.Value = "";
        Assert.False(form.IsValid);
        form.Reset();
        Assert.True(form.IsValid);
        Assert.False(form.SubmitAttempted);
        Assert.Null(form.SubmitError);
    }

    [Fact]
    public async Task Async_submit_sets_submitting_and_ignores_double_submits()
    {
        var form = new Form();
        form.Field("x");
        var tcs = new TaskCompletionSource();
        var calls = 0;

        var first = form.SubmitAsync(() => { calls++; return tcs.Task; });
        Assert.True(form.Submitting);
        await form.SubmitAsync(() => { calls++; return Task.CompletedTask; });
        Assert.Equal(1, calls);

        tcs.SetResult();
        await first;
        Assert.False(form.Submitting);
    }

    [Fact]
    public void Bind_wires_value_touch_and_aria()
    {
        var form = new Form();
        var email = form.Field("", Validators.Required("Required"), Validators.Email("Bad"));
        email.Id = "email";

        App.Mount(new Lambda(() => El("form", Input(Bind(email)), FieldError(email))), "#app");
        var input = Dom.Query("input")!;
        var error = Dom.Query("span")!;

        Assert.Equal("email", input.Attributes["id"]);
        Assert.Equal("email-error", error.Attributes["id"]);
        Assert.False(input.Attributes.ContainsKey("aria-invalid"));
        Assert.EndsWith("></span>", error.ToHtml());

        Fire(input, "input", "nope");
        Assert.Equal("nope", email.Value);
        Assert.False(input.Attributes.ContainsKey("aria-invalid"));

        Fire(input, "blur");
        Assert.Equal("true", input.Attributes["aria-invalid"]);
        Assert.Equal("email-error", input.Attributes["aria-describedby"]);
        Assert.Contains(">Bad</span>", error.ToHtml());

        Fire(input, "input", "a@b.se");
        Assert.False(input.Attributes.ContainsKey("aria-invalid"));
        Assert.False(input.Attributes.ContainsKey("aria-describedby"));
        Assert.EndsWith("></span>", error.ToHtml());
    }

    [Fact]
    public void Invalid_submit_focuses_the_first_invalid_input()
    {
        var form = new Form();
        var a = form.Field("ok");
        var b = form.Field("", Validators.Required());

        App.Mount(new Lambda(() => El("form", Input(Bind(a)), Input(Bind(b)))), "#app");
        var second = Dom.QueryAll("input")[1];
        Dom.Log.Clear();

        form.Submit(() => { });

        Assert.Contains(("Focus", second.Id.ToString()), Dom.Log);
    }

    [Fact]
    public void Bind_checkbox_field()
    {
        var accept = new Field<bool>(false, Validators.MustBeChecked("Accept"));
        App.Mount(new Lambda(() => Input(Bind(accept))), "#app");
        var box = Dom.Query("input")!;

        Assert.Equal("checkbox", box.Attributes["type"]);
        Fire(box, new DomEvent("change", isChecked: true));

        Assert.True(accept.Value);
        Assert.True(accept.Touched);
        Assert.True(accept.IsValid);
    }
}
