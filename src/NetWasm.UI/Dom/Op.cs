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
    SetTextInt = 19,      // id, value(i32)     (text is the decimal number)
    RemoveRange = 20,     // firstId, endId     (detaches firstId and its next siblings up to, not including, endId)
    Call = 21,            // id, method, argCount(u8), args(string)…  (node[method](...args))
    AddGlobalListener = 22, // target(u8: 0 document, 1 window), event, handlerId, flags, withinId (0 = none)
    RemoveGlobalListener = 23, // handlerId     (also stops an ObserveResize)
    ObserveResize = 24,   // id, handlerId      (ResizeObserver; dispatches an empty "resize" event)
    Portal = 25,          // id, selector       (append to the target like Mount, but never replaces prerendered content)
    TemplateListener = 26, // templateId, hole, event, slot(u8), flags  (an element of every clone listens without a
                           // listener op per clone: the event is dispatched to handler 0 with firstId, slot and
                           // the event payload; see Template.Listen)
    ReleaseBlock = 27,    // firstId, count     (Release for the ids firstId .. firstId + count - 1: the nodes of
                          // a template clone, or of several clones whose ids follow each other)
    RemoveAnimated = 28,  // id                 (sets data-state="closed", detaches once its animations finish)
    FocusEdge = 29,       // id, edge(u8: 0 first, 1 last)  (focus the first/last tabbable descendant; the element
                          // itself when it has none)
    CreateSvgElement = 30, // id, tag           (an element in the SVG namespace)
    DefineSvgTemplate = 31, // templateId, html (DefineTemplate for SVG content: parsed as if inside an <svg> element)
}

/// <summary>The target of <see cref="Op.AddGlobalListener"/>.</summary>
public enum GlobalTarget : byte
{
    Document = 0,
    Window = 1,
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
    /// <summary>Listens in the capture phase (<c>addEventListener</c>'s <c>capture</c> option). The listener is added on the element itself instead of being delegated.</summary>
    Capture = 8,
    /// <summary>A passive listener (<c>addEventListener</c>'s <c>passive</c> option), e.g. for touch or wheel. It cannot be combined with <see cref="PreventDefault"/>; the listener is added on the element itself instead of being delegated.</summary>
    Passive = 16,
}
