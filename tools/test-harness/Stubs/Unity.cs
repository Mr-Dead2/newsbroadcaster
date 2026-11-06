// Minimal UnityEngine surface used by Rust plugins. Signatures mirror the real API.
using System;

namespace UnityEngine
{
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum VerticalWrapMode { Truncate, Overflow }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public override string ToString() => $"({x}, {y}, {z})";
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new Quaternion { w = 1 };
    }

    public class Object
    {
        public string name { get; set; }
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null);
    }

    public class Component : Object
    {
        public Transform transform { get; set; } = new Transform();
        public GameObject gameObject { get; set; }
    }

    public class Behaviour : Component { public bool enabled { get; set; } = true; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object { }

    public class Transform
    {
        public Vector3 position { get; set; }
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static int RoundToInt(float f) => (int)Math.Round(f, MidpointRounding.ToEven);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Abs(float f) => Math.Abs(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static int Abs(int f) => Math.Abs(f);
    }

    public static class Time
    {
        // Driven by the harness clock.
        public static float realtimeSinceStartup => (float)Oxide.Core.Interface.Clock;
        public static float time => (float)Oxide.Core.Interface.Clock;
    }

    public static class Debug
    {
        public static void Log(object o) => Console.WriteLine(o);
        public static void LogWarning(object o) => Console.WriteLine("WARN " + o);
        public static void LogError(object o) => Console.WriteLine("ERR " + o);
    }
}

namespace UnityEngine.UI
{
    public class ScrollRect { public enum MovementType { Unrestricted, Elastic, Clamped } }
    public class InputField { public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline } }
    public class Image { public enum Type { Simple, Sliced, Tiled, Filled } }
}
