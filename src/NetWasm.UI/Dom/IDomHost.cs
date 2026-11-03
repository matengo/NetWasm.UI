using System;

namespace NetWasm.UI.Dom;

/// <summary>The boundary to the real (or fake) DOM. One call per flush.</summary>
public interface IDomHost
{
    /// <summary>
    /// Applies an encoded batch of ops. See <see cref="Op"/> for the format. <paramref name="ops"/> is a view
    /// of the renderer's buffer and is only valid during the call; copy it to keep it.
    /// </summary>
    void ApplyBatch(ReadOnlySpan<byte> ops);
}
