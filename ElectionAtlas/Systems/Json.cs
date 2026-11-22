using System.Globalization;
using System.Text;

namespace ElectionAtlas.Systems
{
    /// <summary>Tiny JSON writer for the UI bindings (the panel decodes one string per binding).</summary>
    internal sealed class Json
    {
        private readonly StringBuilder m_Sb = new StringBuilder(1024);
        private bool m_NeedComma;

        public Json Begin() { Comma(); m_Sb.Append('{'); m_NeedComma = false; return this; }
        public Json End() { m_Sb.Append('}'); m_NeedComma = true; return this; }
        public Json BeginArray() { Comma(); m_Sb.Append('['); m_NeedComma = false; return this; }
        public Json EndArray() { m_Sb.Append(']'); m_NeedComma = true; return this; }

        public Json Key(string key) { Comma(); Str(key); m_Sb.Append(':'); m_NeedComma = false; return this; }

        public Json Value(string s) { Comma(); if (s == null) m_Sb.Append("null"); else Str(s); m_NeedComma = true; return this; }
        public Json Value(int v) { Comma(); m_Sb.Append(v.ToString(CultureInfo.InvariantCulture)); m_NeedComma = true; return this; }
        public Json Value(long v) { Comma(); m_Sb.Append(v.ToString(CultureInfo.InvariantCulture)); m_NeedComma = true; return this; }
        public Json Value(bool v) { Comma(); m_Sb.Append(v ? "true" : "false"); m_NeedComma = true; return this; }

        public Json Value(float v)
        {
            Comma();
            if (float.IsNaN(v) || float.IsInfinity(v)) m_Sb.Append("null");
            else m_Sb.Append(v.ToString("0.####", CultureInfo.InvariantCulture));
            m_NeedComma = true;
            return this;
        }

        public Json Null() { Comma(); m_Sb.Append("null"); m_NeedComma = true; return this; }

        public Json Prop(string key, string v) => Key(key).Value(v);
        public Json Prop(string key, int v) => Key(key).Value(v);
        public Json Prop(string key, long v) => Key(key).Value(v);
        public Json Prop(string key, bool v) => Key(key).Value(v);
        public Json Prop(string key, float v) => Key(key).Value(v);

        /// <summary>0xRRGGBB as "#rrggbb".</summary>
        public Json Color(string key, uint rgb) => Key(key).Value("#" + (rgb & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture));

        public override string ToString() => m_Sb.ToString();

        private void Comma()
        {
            if (m_NeedComma) m_Sb.Append(',');
        }

        private void Str(string s)
        {
            m_Sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': m_Sb.Append("\\\""); break;
                    case '\\': m_Sb.Append("\\\\"); break;
                    case '\n': m_Sb.Append("\\n"); break;
                    case '\r': m_Sb.Append("\\r"); break;
                    case '\t': m_Sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) m_Sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else m_Sb.Append(c);
                        break;
                }
            }

            m_Sb.Append('"');
        }
    }
}
