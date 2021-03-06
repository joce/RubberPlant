﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace RubberPlant
{
    public enum SVGGeneratorTechnique
    {
        Lines,
        Path
    }

    public class SVGGenerator : IGenerator
    {
        public SVGGeneratorTechnique Technique { get; set; }

        private List<Tuple<Vector3, Vector3>> m_linesList;
        private string m_outputDir;
        private string m_lsysName;
        private float m_strokeWidth;

        public void StartGenerate(string outputDir, string lsysName)
        {
            Technique = SVGGeneratorTechnique.Path;
            m_linesList= new List<Tuple<Vector3, Vector3>>();
            m_outputDir = outputDir;
            m_lsysName = lsysName;
            m_strokeWidth = 1;
        }

        public void DrawSegment(Matrix4x4 heading, float length)
        {
            Vector3 start = heading.Translation;
            Matrix4x4 tr = Matrix4x4.Identity;
            tr.Translation = new Vector3(length, 0, 0);
            Vector3 end = (tr * heading).Translation;
            start.Y = -start.Y;
            end.Y = -end.Y;
            m_linesList.Add(Tuple.Create(start.Round(5), end.Round(5)));
        }

        public void EndGenerate()
        {
            StringBuilder sb = new StringBuilder();

            if (!m_linesList.Any())
            {
                OutputHeader(sb, 400, 400);
                sb.AppendFormat("    <text x=\"25\" y=\"25\" fill=\"black\">Huh ho... Empty LSystem!!</text>\n");
                OutputFooter(sb);
            }
            else
            {
                var allPoints = m_linesList.Select(t => t.Item1).Concat(m_linesList.Select(t => t.Item2)).ToArray();

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
                maxY = (float)Math.Ceiling(maxY);

                OutputHeader(sb, maxX, maxY);
                sb.AppendFormat("  <g stroke=\"black\" fill=\"none\" stroke-width=\"{0}\">\n", m_strokeWidth);
                if (Technique == SVGGeneratorTechnique.Lines)
                {
                    GenerateLines(sb, adjX, adjY);
                }
                else
                {
                    GeneratePath(sb, adjX, adjY);
                }
                sb.AppendFormat("  </g>\n");
                OutputFooter(sb);
            }

            using (var fs = new StreamWriter(Path.Combine(m_outputDir, m_lsysName+".svg")))
            {
                fs.Write(sb.ToString());
            }
        }

        private void GenerateLines(StringBuilder sb, float adjX, float adjY)
        {
            foreach (var line in m_linesList)
            {
                sb.AppendFormat("    <line x1=\"{0}\" y1=\"{1}\" x2=\"{2}\" y2=\"{3}\" />\n", line.Item1.X + adjX, line.Item1.Y + adjY, line.Item2.X + adjX, line.Item2.Y + adjY);
            }
        }

        private void GeneratePath(StringBuilder sb, float adjX, float adjY)
        {
            Vector3 previous = new Vector3(float.MinValue, float.MinValue, 0);
            Vector3 firstOfPath = new Vector3(float.MaxValue, float.MaxValue, 0);
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
                    if (previous == firstOfPath)
                    {
                        sb.Append("Z ");
                    }
                    firstOfPath = p1;
                    sb.AppendFormat("M {0} {1} L {2} {3} ", p1.X + adjX, p1.Y + adjY, p2.X + adjX, p2.Y + adjY);
                }
                previous = p2;
            }

            if (previous == firstOfPath)
            {
                sb.Append("Z ");
            }
            sb.AppendFormat("\" />\n");
        }

        private void OutputHeader(StringBuilder sb, float maxX, float maxY)
        {
            sb.AppendFormat("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n");
            sb.AppendFormat("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\"\n");
            sb.AppendFormat("    \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n");
            sb.AppendFormat("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\">\n", maxX, maxY);
            sb.AppendFormat("  <desc>{0} LSystem</desc>\n", m_lsysName);
            sb.AppendFormat("  <rect width=\"100%\" height=\"100%\" style=\"fill: white\"/>\n");
        }

        private void OutputFooter(StringBuilder sb)
        {
            sb.AppendFormat("</svg>\n");
        }
    }
}
