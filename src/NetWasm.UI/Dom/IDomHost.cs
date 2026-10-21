namespace NetWasm.UI.Dom;

/// <summary>The boundary to the real (or fake) DOM. One call per flush.</summary>
public interface IDomHost
{
    /// <summary>Applies an encoded batch of ops. See <see cref="Op"/> for the format.</summary>
    void ApplyBatch(byte[] ops);
}
