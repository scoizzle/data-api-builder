// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// One Oracle source after quoting has been resolved. Schema and package are null when
    /// the source did not include that part. Each part is already the catalog spelling:
    /// unquoted tokens are uppercase, quoted tokens keep the characters between the quotes.
    /// </summary>
    internal readonly record struct OracleParsedSource(string? Schema, string Name, string? Package);

    /// <summary>
    /// Parses an Oracle entity or subprogram source. Quoting is decided here and is not stored.
    /// A token that starts with <c>"</c> is an Oracle quoted identifier (<c>""</c> is one
    /// quote, spelling is exact, and a dot inside the quotes is not a separator). A token that
    /// starts with <c>[</c> is DAB's separator escape: <c>]]</c> is one bracket, the text is an
    /// unquoted identifier, and it is folded to uppercase. The two forms do not nest — the first
    /// character of the token chooses. Any other token is unquoted and folded to uppercase.
    /// One token is a name, two are schema and name, three are schema, package, and subprogram.
    /// </summary>
    internal static class OracleSourceNames
    {
        internal static OracleParsedSource Parse(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                throw InitializationError("Oracle object source is empty.");
            }

            List<string> tokens = new();
            int index = 0;
            while (index < source.Length)
            {
                if (tokens.Count == 3)
                {
                    throw InvalidSource(source);
                }

                (string token, int next) = ReadToken(source, index);
                tokens.Add(token);
                index = next;
                if (index >= source.Length)
                {
                    break;
                }

                if (source[index] != '.')
                {
                    throw InvalidSource(source);
                }

                index++;
                if (index >= source.Length)
                {
                    throw InitializationError($"Oracle object source \"{source}\" cannot end with '.'.");
                }
            }

            return tokens.Count switch
            {
                1 => new OracleParsedSource(null, tokens[0], null),
                2 => new OracleParsedSource(tokens[0], tokens[1], null),
                3 => new OracleParsedSource(tokens[0], tokens[2], tokens[1]),
                _ => throw InvalidSource(source)
            };
        }

        /// <summary>
        /// Builds a source string that <see cref="Parse"/> reads back as the same catalog spelling.
        /// A plain uppercase identifier is left bare. Anything else is quoted.
        /// </summary>
        internal static string Format(string schema, string name)
        {
            return Emit(schema) + "." + Emit(name);
        }

        internal static string Emit(string catalogName)
        {
            if (IsBareCatalogName(catalogName))
            {
                return catalogName;
            }

            return "\"" + catalogName.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        /// <summary>
        /// Connection-string User ID. A double-quoted value keeps its spelling (a quoted Oracle
        /// user). An unquoted value folds to uppercase, which is how Oracle stored it.
        /// </summary>
        internal static string CanonicalizeUserToken(string raw)
        {
            string trimmed = raw.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"')
            {
                (string token, int next) = ReadQuoted(trimmed, 0);
                if (next != trimmed.Length)
                {
                    throw InitializationError($"Oracle User ID \"{raw}\" has text after the closing quote.");
                }

                return token;
            }

            if (trimmed.Length == 0)
            {
                throw InitializationError("Oracle User ID is empty.");
            }

            return trimmed.ToUpperInvariant();
        }

        internal static bool TryReadRawUserId(string connectionString, out string raw)
        {
            raw = string.Empty;
            int index = 0;
            while (index < connectionString.Length)
            {
                while (index < connectionString.Length && char.IsWhiteSpace(connectionString[index]))
                {
                    index++;
                }

                if (index >= connectionString.Length)
                {
                    break;
                }

                int equals = connectionString.IndexOf('=', index);
                int semicolon = connectionString.IndexOf(';', index);
                if (equals < 0 || (semicolon >= 0 && semicolon < equals))
                {
                    index = semicolon < 0 ? connectionString.Length : semicolon + 1;
                    continue;
                }

                string key = connectionString[index..equals].Trim();
                index = equals + 1;
                while (index < connectionString.Length && char.IsWhiteSpace(connectionString[index]))
                {
                    index++;
                }

                if (index >= connectionString.Length)
                {
                    break;
                }

                string value;
                if (connectionString[index] == '"')
                {
                    int end = index + 1;
                    while (end < connectionString.Length)
                    {
                        if (connectionString[end] == '"')
                        {
                            if (end + 1 < connectionString.Length && connectionString[end + 1] == '"')
                            {
                                end += 2;
                                continue;
                            }

                            end++;
                            break;
                        }

                        end++;
                    }

                    value = connectionString[index..end];
                    index = end;
                }
                else
                {
                    int end = connectionString.IndexOf(';', index);
                    if (end < 0)
                    {
                        end = connectionString.Length;
                    }

                    value = connectionString[index..end].Trim();
                    index = end;
                }

                if (key.Equals("User Id", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("User ID", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("UID", StringComparison.OrdinalIgnoreCase))
                {
                    raw = value;
                    return raw.Length > 0;
                }

                if (index < connectionString.Length && connectionString[index] == ';')
                {
                    index++;
                }
            }

            return false;
        }

        private static bool IsBareCatalogName(string catalogName)
        {
            if (string.IsNullOrEmpty(catalogName) || !IsAsciiUpperLetter(catalogName[0]))
            {
                return false;
            }

            foreach (char character in catalogName)
            {
                if (!IsAsciiUpperLetter(character)
                    && !char.IsDigit(character)
                    && character is not ('_' or '$' or '#'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAsciiUpperLetter(char character)
        {
            return character is >= 'A' and <= 'Z';
        }

        private static (string Token, int Next) ReadToken(string source, int start)
        {
            char first = source[start];
            if (first == '"')
            {
                return ReadQuoted(source, start);
            }

            if (first == '[')
            {
                return ReadBracket(source, start);
            }

            int index = start;
            while (index < source.Length && source[index] != '.')
            {
                char character = source[index];
                if (character is '"' or '[' or ']')
                {
                    throw InitializationError(
                        $"Oracle object source \"{source}\" has '{character}' outside quotes or brackets.");
                }

                if (char.IsWhiteSpace(character))
                {
                    throw InitializationError($"Oracle object source \"{source}\" has whitespace outside quotes.");
                }

                index++;
            }

            if (index == start)
            {
                throw InitializationError($"Oracle object source \"{source}\" has an empty name.");
            }

            return (source[start..index].ToUpperInvariant(), index);
        }

        private static (string Token, int Next) ReadQuoted(string source, int start)
        {
            StringBuilder text = new();
            int index = start + 1;
            while (index < source.Length)
            {
                if (source[index] == '"')
                {
                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        text.Append('"');
                        index += 2;
                        continue;
                    }

                    if (text.Length == 0)
                    {
                        throw InitializationError($"Oracle object source \"{source}\" has an empty quoted name.");
                    }

                    return (text.ToString(), index + 1);
                }

                text.Append(source[index]);
                index++;
            }

            throw InitializationError($"Oracle object source \"{source}\" has an unterminated quote.");
        }

        private static (string Token, int Next) ReadBracket(string source, int start)
        {
            StringBuilder text = new();
            int index = start + 1;
            while (index < source.Length)
            {
                if (source[index] == ']' && index + 1 < source.Length && source[index + 1] == ']')
                {
                    text.Append(']');
                    index += 2;
                    continue;
                }

                if (source[index] == ']')
                {
                    if (text.Length == 0)
                    {
                        throw InitializationError($"Oracle object source \"{source}\" has an empty bracketed name.");
                    }

                    return (text.ToString().ToUpperInvariant(), index + 1);
                }

                text.Append(source[index]);
                index++;
            }

            throw InitializationError($"Oracle object source \"{source}\" has an unterminated '['.");
        }

        private static DataApiBuilderException InvalidSource(string source)
        {
            return InitializationError(
                $"Invalid Oracle object source: \"{source}\". Expected \"name\", \"schema.name\", " +
                "or \"schema.package.subprogram\".");
        }

        private static DataApiBuilderException InitializationError(string message)
        {
            return new DataApiBuilderException(
                message: message,
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }
    }
}
