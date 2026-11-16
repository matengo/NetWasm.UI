namespace NetWasm.UI;

/// <summary>
/// The counter behind <see cref="Html.UseId"/> and form field ids. Ids are handed out in build order, so a prerendered
/// page and the app that takes it over produce the same ids.
/// </summary>
internal static class Ids
{
    private static int next;

    internal static int Next() => ++next;
}
