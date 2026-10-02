using System;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Reads the store name out of the envelope Unity IAP 5 wraps every receipt in: a JSON object
    /// with three string members, the payload, the store name and the transaction id.
    ///
    /// The store name decides whether an order came from the SDK test store and whether a receipt
    /// may reach the Google Play signature check, so this reader is strict. It accepts one
    /// top-level "Store" member with a plain string value and returns an empty name for anything
    /// else: text that is not a JSON object, a missing or repeated member, a value that is not a
    /// string or that contains an escape, or anything after the closing brace. An empty name is
    /// not the test store and not Google Play, so on Android the order is refused.
    ///
    /// Only the outer object is read. The payload is skipped as an opaque string, so text inside
    /// it that looks like a "Store" member is never taken for one.
    /// </summary>
    public static class ReceiptEnvelope
    {
        private const string StoreMember = "Store";

        public static string ReadStoreName(string receipt)
        {
            if (string.IsNullOrEmpty(receipt))
            {
                return string.Empty;
            }

            int at = SkipWhitespace(receipt, 0);
            if (at >= receipt.Length || receipt[at] != '{')
            {
                return string.Empty;
            }

            at = SkipWhitespace(receipt, at + 1);
            string store = null;
            bool closed = false;

            if (at < receipt.Length && receipt[at] == '}')
            {
                closed = true;
                at++;
            }

            while (!closed)
            {
                // Member name.
                if (!TryReadString(receipt, at, out string name, out bool nameHasEscape, out at))
                {
                    return string.Empty;
                }

                at = SkipWhitespace(receipt, at);
                if (at >= receipt.Length || receipt[at] != ':')
                {
                    return string.Empty;
                }

                at = SkipWhitespace(receipt, at + 1);

                // Member value.
                if (!nameHasEscape && string.Equals(name, StoreMember, StringComparison.Ordinal))
                {
                    if (store != null
                        || !TryReadString(receipt, at, out string value, out bool valueHasEscape, out at)
                        || valueHasEscape)
                    {
                        return string.Empty;
                    }

                    store = value;
                }
                else if (!TrySkipValue(receipt, at, out at))
                {
                    return string.Empty;
                }

                // Separator or end of the object.
                at = SkipWhitespace(receipt, at);
                if (at >= receipt.Length)
                {
                    return string.Empty;
                }

                if (receipt[at] == ',')
                {
                    at = SkipWhitespace(receipt, at + 1);
                }
                else if (receipt[at] == '}')
                {
                    closed = true;
                    at++;
                }
                else
                {
                    return string.Empty;
                }
            }

            if (SkipWhitespace(receipt, at) != receipt.Length)
            {
                return string.Empty;
            }

            return store ?? string.Empty;
        }

        // Reads a JSON string that starts at the opening quote. The text between the quotes is
        // returned as written; hasEscape says whether it contains a backslash.
        private static bool TryReadString(string text, int at, out string value, out bool hasEscape, out int next)
        {
            value = null;
            hasEscape = false;
            next = at;

            if (at >= text.Length || text[at] != '"')
            {
                return false;
            }

            for (int i = at + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\')
                {
                    // The character after a backslash never ends the string.
                    hasEscape = true;
                    i++;
                }
                else if (c == '"')
                {
                    value = text.Substring(at + 1, i - at - 1);
                    next = i + 1;
                    return true;
                }
            }

            return false;
        }

        private static bool TrySkipValue(string text, int at, out int next)
        {
            next = at;
            if (at >= text.Length)
            {
                return false;
            }

            char first = text[at];
            if (first == '"')
            {
                return TryReadString(text, at, out _, out _, out next);
            }

            if (first == '{' || first == '[')
            {
                return TrySkipNested(text, at, out next);
            }

            // A number, true, false or null: it runs to the next separator.
            int i = at;
            while (i < text.Length && text[i] != ',' && text[i] != '}' && text[i] != ']' && !IsWhitespace(text[i]))
            {
                i++;
            }

            next = i;
            return i > at;
        }

        // Skips an object or an array by counting brackets, without mistaking brackets inside
        // strings for structure.
        private static bool TrySkipNested(string text, int at, out int next)
        {
            next = at;
            int depth = 0;
            int i = at;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '"')
                {
                    if (!TryReadString(text, i, out _, out _, out i))
                    {
                        return false;
                    }

                    continue;
                }

                if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        next = i + 1;
                        return true;
                    }
                }

                i++;
            }

            return false;
        }

        private static int SkipWhitespace(string text, int at)
        {
            while (at < text.Length && IsWhitespace(text[at]))
            {
                at++;
            }

            return at;
        }

        private static bool IsWhitespace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r';
        }
    }
}
