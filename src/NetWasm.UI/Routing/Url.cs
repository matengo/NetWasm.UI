using System.Text;

namespace NetWasm.UI.Routing;

/// <summary>
/// Percent-encoding for path segments and query values (UTF-8, RFC 3986). A small replacement for
/// <c>Uri.EscapeDataString</c> / <c>UnescapeDataString</c>, which pull a lot of code into the Wasm module.
/// </summary>
public static class Url
{
    private const string Hex = "0123456789ABCDEF";

    /// <summary>Encodes everything except the unreserved characters <c>A-Z a-z 0-9 - . _ ~</c>.</summary>
    public static string Encode(string value)
    {
        var i = 0;
        while (i < value.Length && IsUnreserved(value[i]))
        {
            i++;
        }

        if (i == value.Length)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length + 16);
        sb.Append(value, 0, i);
        foreach (var b in Encoding.UTF8.GetBytes(value.Substring(i)))
        {
            if (b < 0x80 && IsUnreserved((char)b))
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('%').Append(Hex[b >> 4]).Append(Hex[b & 0xF]);
            }
        }

        return sb.ToString();
    }

    /// <summary>Decodes <c>%XX</c> sequences as UTF-8. Malformed escapes are kept as they are.</summary>
    public static string Decode(string value)
    {
        if (value.IndexOf('%') < 0)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        var bytes = new byte[value.Length / 3];
        var i = 0;
        while (i < value.Length)
        {
            var n = 0;
            while (i + 2 < value.Length && value[i] == '%' && HexValue(value[i + 1]) >= 0 && HexValue(value[i + 2]) >= 0)
            {
                bytes[n++] = (byte)(HexValue(value[i + 1]) << 4 | HexValue(value[i + 2]));
                i += 3;
            }

            if (n > 0)
            {
                sb.Append(Encoding.UTF8.GetString(bytes, 0, n));
            }
            else
            {
                sb.Append(value[i++]);
            }
        }

        return sb.ToString();
    }

    private static bool IsUnreserved(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_' or '~';

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };
}
