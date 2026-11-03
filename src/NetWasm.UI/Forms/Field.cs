using System.Collections.Generic;
using NetWasm.UI.Reactive;

namespace NetWasm.UI.Forms;

/// <summary>The type-independent part of a <see cref="Field{T}"/>, used by <see cref="Form"/> and <see cref="Html.FieldError"/>.</summary>
public abstract class FormField
{
    private protected FormField()
    {
    }

    /// <summary>The <c>id</c> given to the bound input. Unique per Wasm instance unless set explicitly.</summary>
    public abstract string Id { get; set; }

    /// <summary>The <c>id</c> of the error message element; referenced by <c>aria-describedby</c> while an error shows.</summary>
    public string ErrorId => Id + "-error";

    /// <summary>True once the input lost focus, or after <see cref="MarkTouched"/>.</summary>
    public abstract bool Touched { get; }

    /// <summary>True when the value differs from the initial value.</summary>
    public abstract bool Dirty { get; }

    /// <summary>The first validation error (or the error set with <c>SetError</c>), or <see langword="null"/>. Tracked.</summary>
    public abstract string? Error { get; }

    public bool IsValid => Error is null;

    /// <summary>True when there is an error and the user should see it: the field was touched or the form was submitted.</summary>
    public bool ShowError => Error is not null && (Touched || (Form?.SubmitAttempted ?? false));

    /// <summary>The error while <see cref="ShowError"/> is true, otherwise an empty string. Handy as <c>@field.ShownError</c>.</summary>
    public string ShownError => ShowError ? Error! : string.Empty;

    public abstract void MarkTouched();

    /// <summary>Restores the initial value and clears touched and external errors.</summary>
    public abstract void Reset();

    /// <summary>Shows <paramref name="message"/> (e.g. from the server) until the value changes. <see langword="null"/> clears it.</summary>
    public abstract void SetError(string? message);

    /// <summary>The DOM node of the input this field was last bound to, or 0.</summary>
    internal int ElementId { get; set; }

    internal Form? Form { get; set; }
}

/// <summary>
/// A reactive form value with validation state. Bind it with <c>@bind="field"</c> in a template (or
/// <c>Bind(field)</c> in C#) to get two-way binding, touched tracking and <c>aria-invalid</c>/<c>aria-describedby</c>.
/// </summary>
/// <example>
/// <code>
/// readonly Field&lt;string&gt; email = new("", Validators.Required(), Validators.Email());
/// </code>
/// </example>
public sealed class Field<T> : FormField
{
    private static int nextId;

    private readonly Signal<T> value;
    private readonly Signal<T> initial;
    private readonly Signal<bool> touched = new(false);
    private readonly Signal<string?> external = new(null);
    private readonly Validator<T>[] validators;
    private string id;

    public Field(T initial, params Validator<T>[] validators)
    {
        value = new Signal<T>(initial);
        this.initial = new Signal<T>(initial);
        this.validators = validators;
        nextId++;
        id = "nwf" + nextId.ToString();
    }

    /// <summary>The current value. Setting it clears an error set with <see cref="SetError"/>.</summary>
    public T Value
    {
        get => value.Value;
        set
        {
            Scheduler.BeginBatch();
            try
            {
                this.value.Value = value;
                external.Value = null;
            }
            finally
            {
                Scheduler.EndBatch();
            }
        }
    }

    /// <summary>Reads the value without registering a dependency.</summary>
    public T Peek() => value.Peek();

    /// <inheritdoc/>
    /// <remarks>Set it before the field is rendered, e.g. when several app instances share a page.</remarks>
    public override string Id
    {
        get => id;
        set => id = value;
    }

    public override bool Touched => touched.Value;

    public override bool Dirty => !EqualityComparer<T>.Default.Equals(value.Value, initial.Value);

    public override string? Error
    {
        get
        {
            var error = external.Value;
            if (error is not null)
            {
                return error;
            }

            var current = value.Value;
            for (var i = 0; i < validators.Length; i++)
            {
                error = validators[i](current);
                if (error is not null)
                {
                    return error;
                }
            }

            return null;
        }
    }

    public override void MarkTouched() => touched.Value = true;

    public override void SetError(string? message) => external.Value = message;

    public override void Reset() => Reset(initial.Peek());

    /// <summary>Sets a new initial value (e.g. after saving) and resets the field to it.</summary>
    public void Reset(T newInitial)
    {
        Scheduler.BeginBatch();
        try
        {
            initial.Value = newInitial;
            value.Value = newInitial;
            touched.Value = false;
            external.Value = null;
        }
        finally
        {
            Scheduler.EndBatch();
        }
    }

    public override string ToString() => TemplateRuntime.Str(Value) ?? string.Empty;
}
