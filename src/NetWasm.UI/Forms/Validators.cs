using System;

namespace NetWasm.UI.Forms;

/// <summary>Delegate that checks a value and returns an error message, or <see langword="null"/> when the value is valid.</summary>
public delegate string? Validator<in T>(T value);

/// <summary>
/// Built-in validators. They use plain ASCII checks (no <c>Regex</c>, no culture data) to keep the Wasm small;
/// use <see cref="Must{T}"/> for anything else.
/// </summary>
public static class Validators
{
    /// <summary>Fails when the text is empty or only whitespace.</summary>
    public static Validator<string> Required(string message = "This field is required.") =>
        value => IsBlank(value) ? message : null;

    /// <summary>Fails unless the value is <see langword="true"/> (e.g. an "I accept" checkbox).</summary>
    public static Validator<bool> MustBeChecked(string message = "This box must be checked.") =>
        value => value ? null : message;

    /// <summary>Fails when non-empty text is shorter than <paramref name="length"/>. Combine with <see cref="Required"/> to reject empty text.</summary>
    public static Validator<string> MinLength(int length, string? message = null) =>
        value => value is { Length: > 0 } && value.Length < length
            ? message ?? "Use at least " + length.ToString() + " characters."
            : null;

    /// <summary>Fails when the text is longer than <paramref name="length"/>.</summary>
    public static Validator<string> MaxLength(int length, string? message = null) =>
        value => value is not null && value.Length > length
            ? message ?? "Use at most " + length.ToString() + " characters."
            : null;

    /// <summary>
    /// Fails when non-empty text does not look like an e-mail address: one <c>@</c>, something before it,
    /// a dot inside the domain and no whitespace. Deliberately loose; the server has the final say.
    /// </summary>
    public static Validator<string> Email(string message = "Enter a valid e-mail address.") =>
        value => string.IsNullOrEmpty(value) || LooksLikeEmail(value) ? null : message;

    /// <summary>Fails when non-empty text contains anything but the ASCII digits 0-9.</summary>
    public static Validator<string> Digits(string message = "Use digits only.") =>
        value =>
        {
            if (value is null)
            {
                return null;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    return message;
                }
            }

            return null;
        };

    /// <summary>
    /// Fails when <paramref name="predicate"/> returns <see langword="false"/>. Signals read inside the predicate are
    /// tracked, so <c>Must(v =&gt; v == password.Value, "…")</c> re-validates when the other field changes.
    /// </summary>
    public static Validator<T> Must<T>(Func<T, bool> predicate, string message) =>
        value => predicate(value) ? null : message;

    internal static bool IsBlank(string? value)
    {
        if (value is null)
        {
            return true;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != ' ' && c != '\t' && c != '\n' && c != '\r' && c != '\u00A0')
            {
                return false;
            }
        }

        return true;
    }

    private static bool LooksLikeEmail(string value)
    {
        var at = -1;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                return false;
            }

            if (c == '@')
            {
                if (at >= 0)
                {
                    return false;
                }

                at = i;
            }
        }

        if (at < 1 || at == value.Length - 1)
        {
            return false;
        }

        // Needs a dot in the domain that is neither its first nor its last character.
        for (var i = at + 2; i < value.Length - 1; i++)
        {
            if (value[i] == '.')
            {
                return true;
            }
        }

        return false;
    }
}
