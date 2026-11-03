using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetWasm.UI.Dom;
using NetWasm.UI.Reactive;

namespace NetWasm.UI.Forms;

/// <summary>
/// Groups <see cref="Field{T}"/>s and runs submission: on submit every field shows its errors, focus moves
/// to the first invalid input, and the handler only runs when everything is valid.
/// </summary>
/// <example>
/// <code>
/// readonly Form form = new();
/// Field&lt;string&gt; email = null!;
/// partial void OnInit() => email = form.Field("", Validators.Required(), Validators.Email());
/// // &lt;form @onsubmit="() =&gt; form.Submit(SaveAsync)"&gt;
/// </code>
/// </example>
public sealed class Form
{
    private readonly List<FormField> fields = new();
    private readonly Signal<int> version = new(0);
    private readonly Signal<bool> submitAttempted = new(false);
    private readonly Signal<bool> submitting = new(false);
    private readonly Signal<string?> submitError = new(null);

    /// <summary>Creates a field that belongs to this form.</summary>
    public Field<T> Field<T>(T initial, params Validator<T>[] validators)
    {
        var field = new Field<T>(initial, validators);
        Add(field);
        return field;
    }

    /// <summary>Adds an existing field to this form.</summary>
    public Form Add(FormField field)
    {
        if (field is null)
        {
            throw new ArgumentNullException(nameof(field));
        }

        field.Form = this;
        fields.Add(field);
        version.Value++;
        return this;
    }

    public IReadOnlyList<FormField> Fields => fields;

    /// <summary>True when every field is valid. Tracked.</summary>
    public bool IsValid
    {
        get
        {
            _ = version.Value;
            for (var i = 0; i < fields.Count; i++)
            {
                if (!fields[i].IsValid)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>True when any field differs from its initial value. Tracked.</summary>
    public bool Dirty
    {
        get
        {
            _ = version.Value;
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].Dirty)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>True after the first submit; from then on every field shows its errors.</summary>
    public bool SubmitAttempted => submitAttempted.Value;

    /// <summary>True while an asynchronous submit handler runs. Use it to disable the submit button.</summary>
    public bool Submitting => submitting.Value;

    /// <summary>The message of the exception thrown by the last submit handler, or <see langword="null"/>.</summary>
    public string? SubmitError => submitError.Value;

    /// <summary>Validates and, if valid, runs <paramref name="onValid"/>. Ignored while a submit is running.</summary>
    public void Submit(Action onValid)
    {
        if (!BeginSubmit())
        {
            return;
        }

        Exception? failure = null;
        try
        {
            onValid();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        EndSubmit(failure);
    }

    /// <summary>Validates and, if valid, runs <paramref name="onValid"/>; <see cref="Submitting"/> is true until it completes.</summary>
    public void Submit(Func<Task> onValid) => _ = SubmitAsync(onValid);

    /// <summary>Like <see cref="Submit(Func{Task})"/>, but returns the task. Exceptions end up in <see cref="SubmitError"/>, not the task.</summary>
    public async Task SubmitAsync(Func<Task> onValid)
    {
        if (!BeginSubmit())
        {
            return;
        }

        Exception? failure = null;
        try
        {
            await onValid();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        EndSubmit(failure);
    }

    /// <summary>Resets every field and the submit state.</summary>
    public void Reset()
    {
        Scheduler.BeginBatch();
        try
        {
            for (var i = 0; i < fields.Count; i++)
            {
                fields[i].Reset();
            }

            submitAttempted.Value = false;
            submitError.Value = null;
        }
        finally
        {
            Scheduler.EndBatch();
        }
    }

    private bool BeginSubmit()
    {
        if (submitting.Peek())
        {
            return false;
        }

        Scheduler.BeginBatch();
        try
        {
            submitAttempted.Value = true;
            submitError.Value = null;
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (!field.IsValid)
                {
                    if (field.ElementId != 0)
                    {
                        Renderer.Focus(field.ElementId);
                    }

                    return false;
                }
            }

            submitting.Value = true;
            return true;
        }
        finally
        {
            Scheduler.EndBatch();
        }
    }

    private void EndSubmit(Exception? failure)
    {
        Scheduler.BeginBatch();
        try
        {
            submitting.Value = false;
            submitError.Value = failure?.Message;
        }
        finally
        {
            Scheduler.EndBatch();
        }
    }
}
