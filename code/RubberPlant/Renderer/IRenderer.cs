﻿using System.Numerics;

namespace RubberPlant
{
    public interface IRenderer
    {
        void StartRender(string outputDir, string lsysName);
        void DrawSegment(Vector3 start, Vector3 end);
        void EndRender();
    }
}