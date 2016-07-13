using System.Numerics;

namespace RubberPlant
{
    public interface IGenerator
    {
        void StartGenerate(string outputDir, string lsysName);
        void DrawSegment(Matrix4x4 heading, float length);
        void EndGenerate();
    }
}
