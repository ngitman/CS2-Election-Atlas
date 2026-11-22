#if DEV_TOOLS
using System;
using System.Collections.Generic;
using Colossal.UI.Binding;

namespace ElectionAtlas.Dev
{
    /// <summary>
    /// Dev builds only. Replays a UI write against a managed recorder before the real (native) writer sees it,
    /// and refuses writes that would crash the UI engine: a type name whose properties differ from the first time
    /// it was written, or an array with a different number of elements than it declared. See the rule in UiWriters.
    /// </summary>
    internal static class ShapeGuard
    {
        private static readonly Dictionary<string, string> s_Shapes = new Dictionary<string, string>();

        /// <summary>Wraps a binding writer: checks it, then writes for real, or writes null if the check failed.</summary>
        public static Action<IJsonWriter> Wrap(string binding, Action<IJsonWriter> write) => w =>
        {
            if (Check(binding, write)) write(w);
            else w.WriteNull();
        };

        /// <summary>True when <paramref name="write"/> keeps every shape; logs the problem otherwise.</summary>
        public static bool Check(string what, Action<IJsonWriter> write)
        {
            try
            {
                write(new Recorder());
                return true;
            }
            catch (Exception e)
            {
                Mod.Log.Error($"[dev] UI write '{what}' refused before it reached the UI engine: {e.Message}");
                return false;
            }
        }

        private sealed class Recorder : IJsonWriter
        {
            private sealed class Frame
            {
                public string Type;              // null for arrays
                public List<string> Props;
                public uint Declared;            // arrays: declared element count
                public uint Written;             // arrays: elements written
            }

            private readonly List<Frame> m_Stack = new List<Frame>(); // used as a stack (Stack<T> is ambiguous in the game's runtime)
            private bool m_AfterName;            // a property name is waiting for its value

            public string debugName => "ElectionAtlas shape recorder";

            public void TypeBegin(string name)
            {
                Value();
                Push(new Frame { Type = name, Props = new List<string>() });
            }

            public void TypeEnd()
            {
                var f = Pop(isArray: false);
                string shape = string.Join(",", f.Props);
                if (s_Shapes.TryGetValue(f.Type, out string known))
                {
                    if (known != shape)
                        throw new InvalidOperationException($"type '{f.Type}' changed shape: first [{known}], now [{shape}]");
                }
                else
                {
                    s_Shapes[f.Type] = shape;
                }
            }

            public void PropertyName(string name)
            {
                if (m_Stack.Count == 0 || Top.Type == null)
                    throw new InvalidOperationException($"property '{name}' outside an object");
                Top.Props.Add(name);
                m_AfterName = true;
            }

            public void ArrayBegin(uint size)
            {
                Value();
                Push(new Frame { Declared = size });
            }

            public void ArrayEnd()
            {
                var f = Pop(isArray: true);
                if (f.Written != f.Declared)
                    throw new InvalidOperationException($"array declared {f.Declared} elements but wrote {f.Written}");
            }

            public void MapBegin(uint size) => ArrayBegin(size * 2);
            public void MapEnd() => ArrayEnd();

            public void Write(bool value) => Value();
            public void Write(double value) => Value();
            public void Write(int value) => Value();
            public void Write(long value) => Value();
            public void Write(float value) => Value();
            public void Write(string value) => Value();
            public void Write(uint value) => Value();
            public void Write(ulong value) => Value();
            public void WriteNull() => Value();

            /// <summary>Counts a value: an array element, or the value of the pending property.</summary>
            private void Value()
            {
                if (m_Stack.Count > 0 && Top.Type == null)
                {
                    Top.Written++;
                }
                else if (m_Stack.Count > 0)
                {
                    if (!m_AfterName) throw new InvalidOperationException($"value without a property name in '{Top.Type}'");
                }
                m_AfterName = false;
            }

            private Frame Top => m_Stack[m_Stack.Count - 1];
            private void Push(Frame f) => m_Stack.Add(f);

            private Frame Pop(bool isArray)
            {
                if (m_Stack.Count == 0 || (Top.Type == null) != isArray)
                    throw new InvalidOperationException(isArray ? "ArrayEnd without ArrayBegin" : "TypeEnd without TypeBegin");
                var top = Top;
                m_Stack.RemoveAt(m_Stack.Count - 1);
                return top;
            }
        }
    }
}
#endif
