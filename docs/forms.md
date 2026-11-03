# Forms and validation

`NetWasm.UI.Forms` has fields with validation, accessible error messages and submit handling.
The `/signup` page in `samples/Counter` is the complete example.

```razor
<form novalidate @onsubmit="() => form.Submit(SaveAsync)">
    <label for="@email.Id">E-mail</label>
    <input @bind="email" type="email" autocomplete="email" />
    @render(FieldError(email))

    <label><input @bind="accept" /> I accept the terms</label>
    @render(FieldError(accept))

    <button type="submit" disabled="@form.Submitting">Sign up</button>
    <p role="status">@form.SubmitError</p>
</form>

@code {
    private readonly Form form = new();
    private Field<string> email = null!;
    private Field<bool> accept = null!;

    partial void OnInit()
    {
        email = form.Field("", Validators.Required(), Validators.Email());
        accept = form.Field(false, Validators.MustBeChecked());
    }

    private async Task SaveAsync()
    {
        if (!await api.SignUpAsync(email.Value))
        {
            email.SetError("That address is already registered.");   // shown until the value changes
            return;
        }

        form.Reset();
    }
}
```

## Fields

`Field<T>` is a signal with validation state. `@bind="field"` (in C#, `Bind(field)`) on an
`<input>` does the following:

- binds the value both ways (text for `Field<string>`, `checked` for `Field<bool>`);
- sets the input's `id` to `field.Id`, so `<label for="@field.Id">` works;
- marks the field touched on blur;
- sets `aria-invalid` and `aria-describedby` while an error shows.

`FieldError(field)` renders `<span id="{Id}-error" class="field-error" aria-live="polite">`
with the error. The error shows once the field is touched or the form is submitted
(`ShowError`, `ShownError`).

| Member | Meaning |
|---|---|
| `Value` / `Peek()` | Tracked and untracked read |
| `Error`, `IsValid` | The first failing validator, or `SetError`'s message |
| `Touched`, `Dirty` | Lost focus once; differs from the initial value |
| `SetError(message)` | A server-side error, shown until the value changes |
| `Reset()` / `Reset(newInitial)` | Back to the initial value, or to a new one after saving |
| `Id` | `nwf1`, `nwf2`, … unless you set it |

Generated ids are unique within one Wasm instance. When [several apps share a
page](embedding.md), set `Id` explicitly in `OnInit`, before the field is rendered, so the
instances do not collide.

## Validators

`Required`, `MinLength`, `MaxLength`, `Email`, `Digits`, `MustBeChecked` and `Must(predicate,
message)`. All of them take an optional message.

- `MinLength`, `Email` and `Digits` accept empty text. Combine them with `Required`.
- `Email` is deliberately loose; the server decides.
- Signals read inside `Must` are tracked. For example,
  `Must<string>(v => v == password.Value, "The passwords differ.")` re-validates when the other
  field changes.
- The validators use plain ASCII checks, not `Regex` or culture data, to keep the Wasm small.

## Form

`Form.Field(...)` creates a field that belongs to the form; `Add(field)` adds an existing one.

`Submit(handler)` does the following:

1. Marks the form as submitted, so every error shows.
2. If a field is invalid, moves focus to the first invalid input and stops.
3. Otherwise runs the handler. For an async handler, `Submitting` is true until the handler
   completes, and an exception ends up in `SubmitError`.
4. Ignores further submits while one is running.

`IsValid`, `Dirty`, `SubmitAttempted`, `Submitting` and `SubmitError` are tracked. `Reset()`
resets every field and the submit state.

Add `novalidate` to `<form>` so the browser's own validation bubbles do not compete with these
messages.
