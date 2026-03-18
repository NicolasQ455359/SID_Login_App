using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public static class MiniJson
{
    public static class Json
    {
        public static object Deserialize(string json)
        {
            if (json == null)
            {
                return null;
            }

            return Parser.Parse(json);
        }

        public static string Serialize(object obj)
        {
            return Serializer.Serialize(obj);
        }

        private sealed class Parser : IDisposable
        {
            private const string WordBreak = "{}[],:\"";
            private readonly StringReader json;

            private Parser(string jsonString)
            {
                json = new StringReader(jsonString);
            }

            public static object Parse(string jsonString)
            {
                using (Parser instance = new Parser(jsonString))
                {
                    return instance.ParseValue();
                }
            }

            public void Dispose()
            {
                json.Dispose();
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> table = new Dictionary<string, object>();
                json.Read();

                while (true)
                {
                    switch (NextToken)
                    {
                        case Token.None:
                            return null;
                        case Token.CurlyClose:
                            return table;
                        default:
                            string name = ParseString();
                            if (name == null)
                            {
                                return null;
                            }

                            if (NextToken != Token.Colon)
                            {
                                return null;
                            }

                            json.Read();
                            table[name] = ParseValue();
                            break;
                    }
                }
            }

            private List<object> ParseArray()
            {
                List<object> array = new List<object>();
                json.Read();

                bool parsing = true;
                while (parsing)
                {
                    Token nextToken = NextToken;
                    switch (nextToken)
                    {
                        case Token.None:
                            return null;
                        case Token.SquaredClose:
                            parsing = false;
                            break;
                        default:
                            array.Add(ParseValue());
                            break;
                    }
                }

                return array;
            }

            private object ParseValue()
            {
                switch (NextToken)
                {
                    case Token.String:
                        return ParseString();
                    case Token.Number:
                        return ParseNumber();
                    case Token.CurlyOpen:
                        return ParseObject();
                    case Token.SquaredOpen:
                        return ParseArray();
                    case Token.True:
                        return true;
                    case Token.False:
                        return false;
                    case Token.Null:
                        return null;
                    default:
                        return null;
                }
            }

            private string ParseString()
            {
                StringBuilder builder = new StringBuilder();
                json.Read();

                bool parsing = true;
                while (parsing)
                {
                    if (json.Peek() == -1)
                    {
                        break;
                    }

                    char character = NextChar;
                    switch (character)
                    {
                        case '"':
                            parsing = false;
                            break;
                        case '\\':
                            if (json.Peek() == -1)
                            {
                                parsing = false;
                                break;
                            }

                            character = NextChar;
                            switch (character)
                            {
                                case '"':
                                case '\\':
                                case '/':
                                    builder.Append(character);
                                    break;
                                case 'b':
                                    builder.Append('\b');
                                    break;
                                case 'f':
                                    builder.Append('\f');
                                    break;
                                case 'n':
                                    builder.Append('\n');
                                    break;
                                case 'r':
                                    builder.Append('\r');
                                    break;
                                case 't':
                                    builder.Append('\t');
                                    break;
                                case 'u':
                                    char[] hex = new char[4];
                                    for (int i = 0; i < 4; i++)
                                    {
                                        hex[i] = NextChar;
                                    }

                                    builder.Append((char)Convert.ToInt32(new string(hex), 16));
                                    break;
                            }
                            break;
                        default:
                            builder.Append(character);
                            break;
                    }
                }

                return builder.ToString();
            }

            private object ParseNumber()
            {
                string number = NextWord;
                if (number.IndexOf('.') == -1)
                {
                    long.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out long parsedInteger);
                    return parsedInteger;
                }

                double.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedDouble);
                return parsedDouble;
            }

            private void EatWhitespace()
            {
                while (json.Peek() != -1 && char.IsWhiteSpace(PeekChar))
                {
                    json.Read();
                }
            }

            private char PeekChar => Convert.ToChar(json.Peek());
            private char NextChar => Convert.ToChar(json.Read());

            private string NextWord
            {
                get
                {
                    StringBuilder builder = new StringBuilder();
                    while (json.Peek() != -1 && !IsWordBreak(PeekChar))
                    {
                        builder.Append(NextChar);
                    }

                    return builder.ToString();
                }
            }

            private Token NextToken
            {
                get
                {
                    EatWhitespace();

                    if (json.Peek() == -1)
                    {
                        return Token.None;
                    }

                    switch (PeekChar)
                    {
                        case '{':
                            return Token.CurlyOpen;
                        case '}':
                            json.Read();
                            return Token.CurlyClose;
                        case '[':
                            return Token.SquaredOpen;
                        case ']':
                            json.Read();
                            return Token.SquaredClose;
                        case ',':
                            json.Read();
                            return NextToken;
                        case '"':
                            return Token.String;
                        case ':':
                            return Token.Colon;
                        case '0':
                        case '1':
                        case '2':
                        case '3':
                        case '4':
                        case '5':
                        case '6':
                        case '7':
                        case '8':
                        case '9':
                        case '-':
                            return Token.Number;
                    }

                    string word = NextWord;
                    if (word == "false")
                    {
                        return Token.False;
                    }

                    if (word == "true")
                    {
                        return Token.True;
                    }

                    if (word == "null")
                    {
                        return Token.Null;
                    }

                    return Token.None;
                }
            }

            private static bool IsWordBreak(char character)
            {
                return char.IsWhiteSpace(character) || WordBreak.IndexOf(character) != -1;
            }

            private enum Token
            {
                None,
                CurlyOpen,
                CurlyClose,
                SquaredOpen,
                SquaredClose,
                Colon,
                String,
                Number,
                True,
                False,
                Null
            }
        }

        private sealed class Serializer
        {
            private readonly StringBuilder builder = new StringBuilder();

            public static string Serialize(object obj)
            {
                Serializer instance = new Serializer();
                instance.SerializeValue(obj);
                return instance.builder.ToString();
            }

            private void SerializeValue(object value)
            {
                switch (value)
                {
                    case null:
                        builder.Append("null");
                        break;
                    case string stringValue:
                        SerializeString(stringValue);
                        break;
                    case bool boolValue:
                        builder.Append(boolValue ? "true" : "false");
                        break;
                    case IList list:
                        SerializeArray(list);
                        break;
                    case IDictionary dictionary:
                        SerializeObject(dictionary);
                        break;
                    case char charValue:
                        SerializeString(new string(charValue, 1));
                        break;
                    default:
                        if (value is float || value is int || value is uint || value is long || value is double ||
                            value is sbyte || value is byte || value is short || value is ushort ||
                            value is ulong || value is decimal)
                        {
                            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            SerializeString(value.ToString());
                        }
                        break;
                }
            }

            private void SerializeObject(IDictionary obj)
            {
                bool first = true;
                builder.Append('{');

                foreach (object key in obj.Keys)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    SerializeString(key.ToString());
                    builder.Append(':');
                    SerializeValue(obj[key]);
                    first = false;
                }

                builder.Append('}');
            }

            private void SerializeArray(IList array)
            {
                builder.Append('[');

                bool first = true;
                foreach (object value in array)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    SerializeValue(value);
                    first = false;
                }

                builder.Append(']');
            }

            private void SerializeString(string value)
            {
                builder.Append('"');

                foreach (char character in value)
                {
                    switch (character)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            int codepoint = Convert.ToInt32(character);
                            if (codepoint >= 32 && codepoint <= 126)
                            {
                                builder.Append(character);
                            }
                            else
                            {
                                builder.Append("\\u");
                                builder.Append(codepoint.ToString("x4"));
                            }
                            break;
                    }
                }

                builder.Append('"');
            }
        }
    }
}
