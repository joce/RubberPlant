using System;
using System.Numerics;

namespace RubberPlant
{
    public static class VectorExtensions
    {
        public static Vector3 Round(this Vector3 val)
        {
            return new Vector3((float)Math.Round(val.X), (float)Math.Round(val.Y), (float)Math.Round(val.Z));
        }

        public static Vector3 Round(this Vector3 val, int decimals)
        {
            return new Vector3((float)Math.Round(val.X, decimals), (float)Math.Round(val.Y, decimals), (float)Math.Round(val.Z, decimals));
        }

        public static Matrix4x4 Round(this Matrix4x4 val)
        {
            return new Matrix4x4((float)Math.Round(val.M11), (float)Math.Round(val.M12), (float)Math.Round(val.M13), (float)Math.Round(val.M14),
                                 (float)Math.Round(val.M21), (float)Math.Round(val.M22), (float)Math.Round(val.M23), (float)Math.Round(val.M24),
                                 (float)Math.Round(val.M31), (float)Math.Round(val.M32), (float)Math.Round(val.M33), (float)Math.Round(val.M34),
                                 (float)Math.Round(val.M41), (float)Math.Round(val.M42), (float)Math.Round(val.M43), (float)Math.Round(val.M44));
        }
        public static Matrix4x4 Round(this Matrix4x4 val, int decimals)
        {
            return new Matrix4x4((float)Math.Round(val.M11, decimals), (float)Math.Round(val.M12, decimals), (float)Math.Round(val.M13, decimals), (float)Math.Round(val.M14, decimals),
                                 (float)Math.Round(val.M21, decimals), (float)Math.Round(val.M22, decimals), (float)Math.Round(val.M23, decimals), (float)Math.Round(val.M24, decimals),
                                 (float)Math.Round(val.M31, decimals), (float)Math.Round(val.M32, decimals), (float)Math.Round(val.M33, decimals), (float)Math.Round(val.M34, decimals),
                                 (float)Math.Round(val.M41, decimals), (float)Math.Round(val.M42, decimals), (float)Math.Round(val.M43, decimals), (float)Math.Round(val.M44, decimals));
        }
    }
}
