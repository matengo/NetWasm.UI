namespace NetWasm.UI.Dom;

/// <summary>
/// Opcodes of the binary render protocol. Each op is: opcode (u8) followed by its operands.
/// Integers are little-endian i32; strings are u32 byte length followed by UTF-8 bytes; booleans are u8.
/// The JavaScript decoder in dom.mjs must stay in sync with this enum.
/// </summary>
public enum Op : byte
{
    CreateElement = 1,    // id, tag
    CreateText = 2,       // id, text
    CreateAnchor = 3,     // id  (comment node used as insertion marker)
    SetText = 4,          // id, text
    SetAttribute = 5,     // id, name, value
    RemoveAttribute = 6,  // id, name
    SetProperty = 7,      // id, name, value(string)
    SetBoolProperty = 8,  // id, name, value(bool)
    AppendChild = 9,      // parent, child      (a comment parent is an insertion point: child goes before it)
    InsertBefore = 10,    // reference, child   (child goes before reference, under reference.parentNode)
    Remove = 11,          // id                 (node.remove(), keeps handle)
    Release = 12,         // id                 (drop handle and its listeners)
    AddListener = 13,     // id, event, handlerId, flags
    RemoveListener = 14,  // id, handlerId
    Mount = 15,           // id, selector       (append to document.querySelector(selector))
    Focus = 16,           // id
    DefineTemplate = 17,  // templateId, html   (parsed once; see Template)
    CloneTemplate = 18,   // templateId, firstId (root gets firstId, holes firstId+1.. in document order)
}

/// <summary>Flags for <see cref="Op.AddListener"/>.</summary>
[System.Flags]
public enum ListenerFlags : byte
{
    None = 0,
    PreventDefault = 1,
    StopPropagation = 2,
    /// <summary>Calls preventDefault only for an unmodified primary-button click (lets ctrl/middle-click open new tabs).</summary>
    PreventDefaultPlainClick = 4,
}
