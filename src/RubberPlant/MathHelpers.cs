using System;

namespace RubberPlant
{
    public static class MathHelpers
    {
        public static float DegToRad(float deg)
        {
            return (float)(deg * Math.PI/180.0);
        }

        public static float RadToDeg(float rad)
        {
            return (float)(rad * 180.0/Math.PI);
        }
    }
}
