using System.Numerics;

namespace RubberPlant
{
    public interface IGenerator
    {
        void StartGenerate(string outputDir, string lsysName);
        void DrawSegment(Vector3 start, Vector3 end);
        void EndGenerate();
    }
}
