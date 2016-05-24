using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace RubberPlant
{
    public enum SVGRenderTechnique
    {
        Lines,
        Path
    }

    public class SVGRenderer : IRenderer
    {
        public SVGRenderTechnique Technique { get; set; }

        private List<Tuple<Vector3, Vector3>> m_linesList;
        private string m_outputDir;
        private string m_lsysName;
        private float m_strokeWidth;

        public void StartRender(string outputDir, string lsysName)
        {
            Technique = SVGRenderTechnique.Path;
            m_linesList= new List<Tuple<Vector3, Vector3>>();
            m_outputDir = outputDir;
            m_lsysName = lsysName;
            m_strokeWidth = 1;
        }

        public void DrawSegment(Vector3 start, Vector3 end)
        {
            start.Y = -start.Y;
            end.Y = -end.Y;
            m_linesList.Add(Tuple.Create(start, end));
        }

        public void EndRender()
        {
            // TODO Handle empty list of lines
            var allPoints = m_linesList.Select(t=>t.Item1).Concat(m_linesList.Select(t=>t.Item2)).ToArray();
            var maxX = allPoints.Max(v => v.X);
            var minX = allPoints.Min(v => v.X);
            var maxY = allPoints.Max(v => v.Y);
            var minY = allPoints.Min(v => v.Y);

            float adjX = 0;
            float adjY = 0;

            if (minX < 0)
            {
                adjX = -minX;
            }

            if (minY < 0)
            {
                adjY = -minY;
            }

            adjX += m_strokeWidth;
            adjY += m_strokeWidth;

            maxX += (adjX + m_strokeWidth);
            maxY += (adjY + m_strokeWidth);

            maxX = (float)Math.Ceiling(maxX);
            maxY = (float) Math.Ceiling(maxY);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
            sb.AppendLine("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\"");
            sb.AppendLine("    \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">");
            sb.AppendFormat("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\">\n", maxX, maxY);
            sb.AppendFormat("  <desc>{0} LSystem</desc>\n", m_lsysName);
            sb.AppendFormat("  <g stroke=\"black\" fill=\"none\" stroke-width=\"{0}\">\n", m_strokeWidth);
            if (Technique == SVGRenderTechnique.Lines)
            {
                RenderLines(sb, adjX, adjY);
            }
            else
            {
                RenderPath(sb, adjX, adjY);
            }
            sb.AppendLine("  </g>");
            sb.AppendLine("</svg>");

            using (var fs = new StreamWriter(Path.Combine(m_outputDir, m_lsysName+".svg")))
            {
                fs.Write(sb.ToString());
            }
        }

        private void RenderLines(StringBuilder sb, float adjX, float adjY)
        {
            foreach (var line in m_linesList)
            {
                sb.AppendFormat("    <line x1=\"{0}\" y1=\"{1}\" x2=\"{2}\" y2=\"{3}\" />\n", line.Item1.X + adjX, line.Item1.Y + adjY, line.Item2.X + adjX, line.Item2.Y + adjY);
            }
        }

        private void RenderPath(StringBuilder sb, float adjX, float adjY)
        {
            Vector3 previous = new Vector3(float.MinValue, float.MinValue, 0);
            sb.Append("    <path d=\"");
            foreach (var line in m_linesList)
            {
                Vector3 p1 = line.Item1;
                Vector3 p2 = line.Item2;

                if (p1 == previous)
                {
                    sb.AppendFormat("L {0} {1} ", p2.X + adjX, p2.Y + adjY);
                }
                else
                {
                    sb.AppendFormat("M {0} {1} L {2} {3} ", p1.X + adjX, p1.Y + adjY, p2.X + adjX, p2.Y + adjY);
                }
                previous = p2;
            }
            sb.AppendLine("\" />");
        }
    }
}
