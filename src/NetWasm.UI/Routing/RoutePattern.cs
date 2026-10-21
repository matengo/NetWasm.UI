using System;
using System.Collections.Generic;
using System.Globalization;

namespace NetWasm.UI.Routing;

/// <summary>Parameters captured by a <see cref="RoutePattern"/> match, plus the raw query string.</summary>
public sealed class RouteParams
{
    public static readonly RouteParams Empty = new(Array.Empty<string>(), Array.Empty<string>(), string.Empty);

    private readonly string[] names;
    private readonly string[] values;

    internal RouteParams(string[] names, string[] values, string query)
    {
        this.names = names;
        this.values = values;
        Query = query;
    }

    public int Count => names.Length;

    /// <summary>Raw query string without the leading <c>?</c>, or empty.</summary>
    public string Query { get; }

    /// <summary>The decoded value for <paramref name="name"/>, or null when the pattern has no such parameter.</summary>
    public string? this[string name]
    {
        get
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (names[i] == name)
                {
                    return values[i];
                }
            }

            return null;
        }
    }

    public string Get(string name) => this[name] ?? throw new KeyNotFoundException($"Route has no parameter '{name}'.");

    public int GetInt(string name, int fallback = 0) =>
        int.TryParse(this[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>True when both captured the same values; used to avoid rebuilding a page for an identical match.</summary>
    public bool SameAs(RouteParams other)
    {
        if (other.Count != Count || other.Query != Query)
        {
            return false;
        }

        for (var i = 0; i < names.Length; i++)
        {
            if (names[i] != other.names[i] || values[i] != other.values[i])
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// A path pattern such as <c>/todos/{id}</c> or <c>/files/{*path}</c>. Segments are literal
/// (case-insensitive), <c>{name}</c> (one segment) or <c>{*name}</c> (the rest of the path, last only).
/// Matching ignores query and hash and a trailing slash.
/// </summary>
public sealed class RoutePattern
{
    private readonly string[] segments;
    private readonly string?[] paramNames;   // null for literal segments
    private readonly bool catchAll;
    private readonly int paramCount;

    public RoutePattern(string pattern)
    {
        Pattern = pattern;
        segments = Split(StripQuery(pattern));
        paramNames = new string?[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            var s = segments[i];
            if (s.Length >= 2 && s[0] == '{' && s[s.Length - 1] == '}')
            {
                var name = s.Substring(1, s.Length - 2);
                if (name.StartsWith("*", StringComparison.Ordinal))
                {
                    if (i != segments.Length - 1)
                    {
                        throw new ArgumentException($"Catch-all must be the last segment: '{pattern}'.", nameof(pattern));
                    }

                    catchAll = true;
                    name = name.Substring(1);
                }

                paramNames[i] = name;
                paramCount++;
            }
        }
    }

    public string Pattern { get; }

    public bool TryMatch(string path, out RouteParams result)
    {
        var query = QueryOf(path);
        var parts = Split(StripQuery(path));

        if (catchAll ? parts.Length < segments.Length - 1 : parts.Length != segments.Length)
        {
            result = RouteParams.Empty;
            return false;
        }

        var names = paramCount == 0 ? Array.Empty<string>() : new string[paramCount];
        var values = paramCount == 0 ? Array.Empty<string>() : new string[paramCount];
        var p = 0;

        for (var i = 0; i < segments.Length; i++)
        {
            var name = paramNames[i];
            if (name is null)
            {
                if (!string.Equals(segments[i], parts[i], StringComparison.OrdinalIgnoreCase))
                {
                    result = RouteParams.Empty;
                    return false;
                }

                continue;
            }

            names[p] = name;
            if (catchAll && i == segments.Length - 1)
            {
                values[p] = JoinFrom(parts, i);
            }
            else
            {
                values[p] = Uri.UnescapeDataString(parts[i]);
            }

            p++;
        }

        result = new RouteParams(names, values, query);
        return true;
    }

    private static string StripQuery(string path)
    {
        var end = path.Length;
        var q = path.IndexOf('?');
        var h = path.IndexOf('#');
        if (q >= 0 && q < end) end = q;
        if (h >= 0 && h < end) end = h;
        return path.Substring(0, end);
    }

    private static string QueryOf(string path)
    {
        var q = path.IndexOf('?');
        if (q < 0)
        {
            return string.Empty;
        }

        var h = path.IndexOf('#', q);
        return h < 0 ? path.Substring(q + 1) : path.Substring(q + 1, h - q - 1);
    }

    private static string[] Split(string path)
    {
        var list = new List<string>();
        var start = 0;
        for (var i = 0; i <= path.Length; i++)
        {
            if (i == path.Length || path[i] == '/')
            {
                if (i > start)
                {
                    list.Add(path.Substring(start, i - start));
                }

                start = i + 1;
            }
        }

        return list.ToArray();
    }

    private static string JoinFrom(string[] parts, int from)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = from; i < parts.Length; i++)
        {
            if (i > from)
            {
                sb.Append('/');
            }

            sb.Append(Uri.UnescapeDataString(parts[i]));
        }

        return sb.ToString();
    }
}
