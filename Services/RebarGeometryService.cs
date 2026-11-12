using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ElementCoordinateExtractor.Services
{
    public class RebarPoint2D
    {
        public double X { get; set; }
        public double Y { get; set; }

        public RebarPoint2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static RebarPoint2D operator +(RebarPoint2D a, RebarPoint2D b) => new(a.X + b.X, a.Y + b.Y);
        public static RebarPoint2D operator -(RebarPoint2D a, RebarPoint2D b) => new(a.X - b.X, a.Y - b.Y);
        public static RebarPoint2D operator *(RebarPoint2D a, double scale) => new(a.X * scale, a.Y * scale);
        public static RebarPoint2D operator /(RebarPoint2D a, double scale) => new(a.X / scale, a.Y / scale);

        public double Length => Math.Sqrt(X * X + Y * Y);

        public RebarPoint2D Normalize()
        {
            double len = Length;
            return len < 1e-9 ? new RebarPoint2D(1, 0) : new RebarPoint2D(X / len, Y / len);
        }
    }

    public class RebarLeg2D
    {
        public int Index { get; set; }
        public string Label { get; set; } = string.Empty;
        public RebarPoint2D Start { get; set; } = new(0, 0);
        public RebarPoint2D End { get; set; } = new(0, 0);
        public double LengthMm { get; set; }
        public double DisplayScaleFactor { get; set; } = 1.0;
    }

    public class RebarShape2D
    {
        public string ShapeName { get; set; } = string.Empty;
        public List<RebarLeg2D> Legs { get; set; } = new();
        public double Width { get; set; }
        public double Height { get; set; }
        public int DefaultHorizontalLegIndex { get; set; } = 0;
    }

    public static class RebarGeometryService
    {
        private static readonly string[] LegLabels = { "A", "B", "C", "D", "E", "F", "G", "H", "J", "K" };
        private static readonly Dictionary<string, List<string>> _shapeSegmentLabelsCache = new();

        public static List<string> GetSegmentLabelsInMemory(Document projectDoc, RebarShape shape, Element elem, int expectedCount)
        {
            if (_shapeSegmentLabelsCache.TryGetValue(shape.Name, out var cached) && cached.Count == expectedCount)
            {
                return cached;
            }

            var result = new List<string>();

            // 1. Đọc trực tiếp từ RebarShapeDefinitionBySegments trong bộ nhớ (cực nhanh, không mở file)
            var segDef = shape.GetRebarShapeDefinition() as RebarShapeDefinitionBySegments;
            if (segDef != null)
            {
                for (int sIdx = 0; sIdx < segDef.NumberOfSegments; sIdx++)
                {
                    var s = segDef.GetSegment(sIdx);
                    string bestLbl = "";
                    foreach (var c in s.GetConstraints())
                    {
                        var pId = c.GetParamId();
                        if (pId != null && pId != ElementId.InvalidElementId)
                        {
                            string candidate = GetParameterNameById(projectDoc, shape, elem, pId);
                            if (!string.IsNullOrEmpty(candidate))
                            {
                                if (LegLabels.Contains(candidate.ToUpperInvariant()))
                                {
                                    bestLbl = candidate.ToUpperInvariant();
                                    break;
                                }
                                else if (string.IsNullOrEmpty(bestLbl))
                                {
                                    bestLbl = candidate;
                                }
                            }
                        }
                    }
                    result.Add(bestLbl);
                }
            }

            // 2. Thu thập danh sách các parameter kích thước thực tế có giá trị > 0 (A=736, C=740...)
            var activeParams = new List<string>();
            foreach (var lbl in LegLabels)
            {
                try
                {
                    var p = elem.LookupParameter(lbl) ?? shape.LookupParameter(lbl);
                    if (p != null && p.StorageType == StorageType.Double && p.HasValue)
                    {
                        double valMm = Math.Round(p.AsDouble() * 304.8);
                        if (valMm > 1.0)
                        {
                            activeParams.Add(lbl);
                        }
                    }
                }
                catch { }
            }

            // 3. Kết hợp nhãn từ Definition và activeParams trong bộ nhớ (0ms latency)
            var finalLabels = new List<string>();
            var usedActive = new HashSet<string>();

            for (int i = 0; i < expectedCount; i++)
            {
                string segLbl = (i < result.Count) ? result[i] : "";
                if (!string.IsNullOrEmpty(segLbl) && activeParams.Contains(segLbl) && !usedActive.Contains(segLbl))
                {
                    finalLabels.Add(segLbl);
                    usedActive.Add(segLbl);
                }
                else
                {
                    var nextActive = activeParams.FirstOrDefault(ap => !usedActive.Contains(ap));
                    if (!string.IsNullOrEmpty(nextActive))
                    {
                        finalLabels.Add(nextActive);
                        usedActive.Add(nextActive);
                    }
                    else if (!string.IsNullOrEmpty(segLbl))
                    {
                        finalLabels.Add(segLbl);
                    }
                    else
                    {
                        finalLabels.Add(i < LegLabels.Length ? LegLabels[i] : $"L{i + 1}");
                    }
                }
            }

            if (finalLabels.Count == expectedCount && finalLabels.Any(x => !string.IsNullOrEmpty(x)))
            {
                _shapeSegmentLabelsCache[shape.Name] = finalLabels;
                return finalLabels;
            }

            return finalLabels;
        }

        public static RebarShape2D? Extract2DShape(Element elem, string shapeName, RebarShape? shape = null)
        {
            // CÁCH 1: Nếu có RebarShape, trích xuất chính xác theo Family RebarShape Definition và GetCurvesForBrowser
            if (shape != null)
            {
                try
                {
                    var shapeFromDef = ExtractFromRebarShapeDefinition(elem, shapeName, shape);
                    if (shapeFromDef != null && shapeFromDef.Legs.Count > 0)
                    {
                        return shapeFromDef;
                    }
                }
                catch { }
            }

            // CÁCH 2: Fallback cho thanh thép không có RebarShape (Freeform, RebarInSystem đặc biệt...)
            return ExtractFromInstance3DCurves(elem, shapeName, shape);
        }

        private static RebarShape2D? ExtractFromRebarShapeDefinition(Element elem, string shapeName, RebarShape shape)
        {
            Document doc = elem.Document;
            var segDef = shape.GetRebarShapeDefinition() as RebarShapeDefinitionBySegments;
            int majorSegmentIndex = segDef?.MajorSegmentIndex ?? 0;

            // 1. Thu thập các tham số kích thước thực tế có giá trị > 0 trên thanh thép / shape (ví dụ A=736, C=740)
            var activeParams = new List<(string Name, double ValueMm)>();
            foreach (var lbl in LegLabels)
            {
                try
                {
                    var p = elem.LookupParameter(lbl) ?? shape.LookupParameter(lbl);
                    if (p != null && p.StorageType == StorageType.Double && p.HasValue)
                    {
                        double valMm = Math.Round(p.AsDouble() * 304.8);
                        if (valMm > 1.0)
                        {
                            activeParams.Add((lbl, valMm));
                        }
                    }
                }
                catch { }
            }

            // 2. Lấy 2D curves chuẩn từ GetCurvesForBrowser() của RebarShape
            IList<Curve>? browserCurves = null;
            try
            {
                browserCurves = shape.GetCurvesForBrowser();
            }
            catch { }

            if (browserCurves == null || browserCurves.Count == 0) return null;

            // Tách các đoạn thẳng chính (Lines)
            var straightLines = new List<Line>();
            foreach (var c in browserCurves)
            {
                if (c is Line l && l.Length > 1e-4)
                {
                    straightLines.Add(l);
                }
            }

            if (straightLines.Count == 0) return null;

            // 3. Lấy tên tham số cho từng đoạn (A, C...) chuẩn xác nhất
            // Đọc trực tiếp từ định nghĩa RebarShape trong bộ nhớ (cực nhanh, không làm chậm add-in)
            var assignedLabels = GetSegmentLabelsInMemory(doc, shape, elem, straightLines.Count);

            if (assignedLabels.Count != straightLines.Count || assignedLabels.All(string.IsNullOrEmpty))
            {
                var segLabels = new List<string>();

                if (segDef != null)
                {
                    majorSegmentIndex = segDef.MajorSegmentIndex;
                    for (int i = 0; i < segDef.NumberOfSegments; i++)
                    {
                        var seg = segDef.GetSegment(i);
                        string label = "";
                        var constraints = seg.GetConstraints();
                        foreach (var c in constraints)
                        {
                            ElementId pId = c.GetParamId();
                            if (pId != null && pId != ElementId.InvalidElementId)
                            {
                                label = GetParameterNameById(doc, shape, elem, pId);
                                if (!string.IsNullOrEmpty(label)) break;
                            }
                        }
                        segLabels.Add(label);
                    }
                }

                // Đo chiều dài thực tế từ tim thép 3D của Rebar instance
                var realLengthsMm = new List<double>();
                if (elem is Rebar r)
                {
                    try
                    {
                        var cList = r.GetCenterlineCurves(false, false, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                        if (cList != null)
                        {
                            foreach (var c in cList)
                            {
                                if (c is Line l && l.Length > 1e-4)
                                {
                                    realLengthsMm.Add(Math.Round(l.Length * 304.8));
                                }
                            }
                        }
                    }
                    catch { }
                }

                assignedLabels = new List<string>();
                var usedActive = new HashSet<string>();

                for (int i = 0; i < straightLines.Count; i++)
                {
                    string candidate = i < segLabels.Count ? segLabels[i] : "";
                    if (!string.IsNullOrEmpty(candidate) && activeParams.Any(ap => ap.Name == candidate) && !usedActive.Contains(candidate))
                    {
                        assignedLabels.Add(candidate);
                        usedActive.Add(candidate);
                        continue;
                    }

                    // Ghép theo chiều dài thực tế của instance (nếu có) hoặc browserCurves
                    double measuredMm = (i < realLengthsMm.Count && realLengthsMm[i] > 10.0) 
                        ? realLengthsMm[i] 
                        : (straightLines[i].Length * 304.8);

                    var bestMatch = activeParams
                        .Where(ap => !usedActive.Contains(ap.Name))
                        .OrderBy(ap => Math.Abs(ap.ValueMm - measuredMm))
                        .FirstOrDefault();

                    if (!string.IsNullOrEmpty(bestMatch.Name))
                    {
                        assignedLabels.Add(bestMatch.Name);
                        usedActive.Add(bestMatch.Name);
                    }
                    else if (i < activeParams.Count && !usedActive.Contains(activeParams[i].Name))
                    {
                        assignedLabels.Add(activeParams[i].Name);
                        usedActive.Add(activeParams[i].Name);
                    }
                    else if (!string.IsNullOrEmpty(candidate))
                    {
                        assignedLabels.Add(candidate);
                    }
                    else
                    {
                        assignedLabels.Add(i < LegLabels.Length ? LegLabels[i] : $"L{i + 1}");
                    }
                }
            }

            // 5. Đảm bảo có ít nhất một cạnh LUÔN LUÔN NẰM NGANG
            // Ưu tiên cạnh đã nằm ngang sẵn trong Revit family (dy = 0), hoặc MajorSegmentIndex
            int chosenHorizontalIdx = -1;
            for (int i = 0; i < straightLines.Count; i++)
            {
                var l = straightLines[i];
                if (Math.Abs(l.GetEndPoint(1).Y - l.GetEndPoint(0).Y) < 1e-3)
                {
                    chosenHorizontalIdx = i;
                    break;
                }
            }

            double rotAngle = 0.0;
            if (chosenHorizontalIdx < 0)
            {
                chosenHorizontalIdx = (majorSegmentIndex >= 0 && majorSegmentIndex < straightLines.Count)
                    ? majorSegmentIndex
                    : 0;

                var refLine = straightLines[chosenHorizontalIdx];
                XYZ v = refLine.GetEndPoint(1) - refLine.GetEndPoint(0);
                rotAngle = -Math.Atan2(v.Y, v.X);
            }

            RebarPoint2D RotatePt(XYZ pt, double angle)
            {
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                double rx = pt.X * cos - pt.Y * sin;
                double ry = pt.X * sin + pt.Y * cos;
                return new RebarPoint2D(rx, ry);
            }

            // 6. Tạo danh sách RebarLeg2D
            var legs = new List<RebarLeg2D>();
            for (int i = 0; i < straightLines.Count; i++)
            {
                var line = straightLines[i];
                var p0 = RotatePt(line.GetEndPoint(0), rotAngle);
                var p1 = RotatePt(line.GetEndPoint(1), rotAngle);

                string label = i < assignedLabels.Count ? assignedLabels[i] : (i < LegLabels.Length ? LegLabels[i] : $"L{i + 1}");

                // Lấy kích thước thực tế mm từ parameter của Rebar element
                double lengthMm = 0;
                try
                {
                    var p = elem.LookupParameter(label) ?? shape.LookupParameter(label);
                    if (p != null && p.StorageType == StorageType.Double && p.HasValue)
                    {
                        double pValMm = Math.Round(p.AsDouble() * 304.8);
                        if (pValMm > 1.0) lengthMm = pValMm;
                    }
                }
                catch { }

                if (lengthMm < 1.0)
                {
                    lengthMm = Math.Round(line.Length * 304.8);
                }

                legs.Add(new RebarLeg2D
                {
                    Index = i + 1,
                    Label = label,
                    Start = p0,
                    End = p1,
                    LengthMm = lengthMm,
                    DisplayScaleFactor = 1.0
                });
            }

            // Đưa toạ độ về gốc (0, 0)
            double minX = legs.Min(l => Math.Min(l.Start.X, l.End.X));
            double minY = legs.Min(l => Math.Min(l.Start.Y, l.End.Y));
            foreach (var leg in legs)
            {
                leg.Start.X -= minX;
                leg.Start.Y -= minY;
                leg.End.X -= minX;
                leg.End.Y -= minY;
            }

            double w = legs.Max(l => Math.Max(l.Start.X, l.End.X));
            double h = legs.Max(l => Math.Max(l.Start.Y, l.End.Y));

            int defaultHIdx = chosenHorizontalIdx >= 0 ? chosenHorizontalIdx : 0;
            for (int i = 0; i < legs.Count; i++)
            {
                if (legs[i].Label == "C")
                {
                    defaultHIdx = i;
                    break;
                }
            }

            return new RebarShape2D
            {
                ShapeName = shapeName,
                Legs = legs,
                Width = Math.Max(w, 0.1),
                Height = Math.Max(h, 0.0),
                DefaultHorizontalLegIndex = defaultHIdx
            };
        }

        private static RebarShape2D? ExtractFromInstance3DCurves(Element elem, string shapeName, RebarShape? shape)
        {
            IList<Curve>? curves = GetCenterlineCurvesWithFallbacks(elem, shape);

            // 1. Thu thập danh sách điểm 3D liên tiếp dọc theo tim thép
            var pts3D = new List<XYZ>();

            if (curves != null && curves.Count > 0)
            {
                foreach (var c in curves)
                {
                    if (c == null) continue;

                    IList<XYZ>? sampled = null;
                    try { sampled = c.Tessellate(); } catch { }

                    if (sampled != null && sampled.Count > 0)
                    {
                        foreach (var pt in sampled)
                        {
                            if (pts3D.Count == 0 || pts3D.Last().DistanceTo(pt) > 1e-4)
                            {
                                pts3D.Add(pt);
                            }
                        }
                    }
                    else
                    {
                        var p0 = c.GetEndPoint(0);
                        var p1 = c.GetEndPoint(1);
                        if (pts3D.Count == 0 || pts3D.Last().DistanceTo(p0) > 1e-4) pts3D.Add(p0);
                        pts3D.Add(p1);
                    }
                }
            }

            if (pts3D.Count < 2)
            {
                pts3D.Clear();
                pts3D.Add(new XYZ(0, 0, 0));
                pts3D.Add(new XYZ(3.0, 0, 0));
            }

            // 2. Tính toán mặt phẳng 2D tối ưu (Best-fit plane)
            var centroid = new XYZ(
                pts3D.Average(p => p.X),
                pts3D.Average(p => p.Y),
                pts3D.Average(p => p.Z));

            XYZ normal = XYZ.BasisZ;
            bool foundNormal = false;

            for (int i = 0; i < pts3D.Count - 2; i++)
            {
                XYZ v1 = pts3D[i + 1] - pts3D[i];
                XYZ v2 = pts3D[i + 2] - pts3D[i + 1];
                if (v1.GetLength() < 1e-4 || v2.GetLength() < 1e-4) continue;

                XYZ cross = v1.Normalize().CrossProduct(v2.Normalize());
                if (cross.GetLength() > 1e-3)
                {
                    normal = cross.Normalize();
                    foundNormal = true;
                    break;
                }
            }

            if (!foundNormal)
            {
                XYZ dir = pts3D.Last() - pts3D.First();
                if (dir.GetLength() < 1e-4) dir = XYZ.BasisX;
                dir = dir.Normalize();
                XYZ temp = Math.Abs(dir.DotProduct(XYZ.BasisZ)) < 0.85 ? XYZ.BasisZ : XYZ.BasisY;
                normal = dir.CrossProduct(temp).Normalize();
            }

            // 3. Tìm trục ngang U = đoạn thẳng dài nhất (cho nằm ngang chuẩn kỹ thuật)
            XYZ axisU = XYZ.BasisX;
            double maxSegLen = -1;

            for (int i = 0; i < pts3D.Count - 1; i++)
            {
                XYZ seg = pts3D[i + 1] - pts3D[i];
                double len = seg.GetLength();
                if (len > maxSegLen && len > 1e-4)
                {
                    maxSegLen = len;
                    axisU = seg.Normalize();
                }
            }

            XYZ axisV = normal.CrossProduct(axisU).Normalize();

            // 4. Chiếu tất cả điểm về toạ độ 2D (u, v)
            var pts2D = new List<RebarPoint2D>();
            foreach (var p in pts3D)
            {
                XYZ diff = p - centroid;
                double u = diff.DotProduct(axisU);
                double v = diff.DotProduct(axisV);
                pts2D.Add(new RebarPoint2D(u, v));
            }

            double minU = pts2D.Min(p => p.X);
            double minV = pts2D.Min(p => p.Y);
            foreach (var p in pts2D)
            {
                p.X -= minU;
                p.Y -= minV;
            }

            // Đảm bảo hình dạng uốn ngửa lên trên
            double avgV = pts2D.Average(p => p.Y);
            double halfH = pts2D.Max(p => p.Y) / 2.0;
            if (avgV < halfH && pts2D.Max(p => p.Y) > 0.05)
            {
                double maxV = pts2D.Max(p => p.Y);
                foreach (var p in pts2D) p.Y = maxV - p.Y;
            }

            // 5. Rút gọn các điểm thẳng hàng liên tiếp thành các đoạn uốn chính (Legs)
            var simplifiedPts2D = new List<RebarPoint2D>();
            simplifiedPts2D.Add(pts2D[0]);

            for (int i = 1; i < pts2D.Count - 1; i++)
            {
                var prev = simplifiedPts2D.Last();
                var curr = pts2D[i];
                var next = pts2D[i + 1];

                var v1 = curr - prev;
                var v2 = next - curr;

                if (v1.Length < 1e-3 || v2.Length < 1e-3) continue;

                var u1 = v1.Normalize();
                var u2 = v2.Normalize();

                double dot = u1.X * u2.X + u1.Y * u2.Y;
                if (dot < 0.96)
                {
                    simplifiedPts2D.Add(curr);
                }
            }
            simplifiedPts2D.Add(pts2D.Last());

            // 6. Tìm các tham số kích thước không bằng 0 trên thanh thép (ví dụ A, C hoặc A, B, C...)
            var activeParamNames = new List<string>();
            foreach (var label in LegLabels)
            {
                try
                {
                    var p = elem.LookupParameter(label) ?? shape?.LookupParameter(label);
                    if (p != null && p.StorageType == StorageType.Double && p.HasValue && p.AsDouble() > 1e-3)
                    {
                        activeParamNames.Add(label);
                    }
                }
                catch { }
            }

            var legs = new List<RebarLeg2D>();
            for (int i = 0; i < simplifiedPts2D.Count - 1; i++)
            {
                var pStart = simplifiedPts2D[i];
                var pEnd = simplifiedPts2D[i + 1];
                double dist = (pEnd - pStart).Length;
                if (dist < 1e-3) continue;

                string label = "";
                if (i < activeParamNames.Count)
                {
                    label = activeParamNames[i];
                }
                else
                {
                    label = i < LegLabels.Length ? LegLabels[i] : $"L{i + 1}";
                }

                double lengthMm = Math.Round(dist * 304.8);
                try
                {
                    var p = elem.LookupParameter(label);
                    if (p != null && p.StorageType == StorageType.Double && p.HasValue)
                    {
                        double pValMm = Math.Round(p.AsDouble() * 304.8);
                        if (pValMm > 1.0) lengthMm = pValMm;
                    }
                }
                catch { }

                legs.Add(new RebarLeg2D
                {
                    Index = i + 1,
                    Label = label,
                    Start = pStart,
                    End = pEnd,
                    LengthMm = lengthMm,
                    DisplayScaleFactor = 1.0
                });
            }

            if (legs.Count == 0)
            {
                legs.Add(new RebarLeg2D
                {
                    Index = 1,
                    Label = "A",
                    Start = new RebarPoint2D(0, 0),
                    End = new RebarPoint2D(3.0, 0),
                    LengthMm = 1000,
                    DisplayScaleFactor = 1.0
                });
            }

            double width2D = Math.Max(pts2D.Max(p => p.X), 0.1);
            double height2D = Math.Max(pts2D.Max(p => p.Y), 0.0);

            return new RebarShape2D
            {
                ShapeName = shapeName,
                Legs = legs,
                Width = width2D,
                Height = height2D,
                DefaultHorizontalLegIndex = 0
            };
        }

        private static string GetParameterNameById(Document doc, RebarShape shape, Element elem, ElementId paramId)
        {
            if (paramId == null || paramId == ElementId.InvalidElementId) return string.Empty;

            // 1. Thử lấy từ ParameterElement trong project document
            try
            {
                if (doc.GetElement(paramId) is ParameterElement pe)
                {
                    var def = pe.GetDefinition();
                    if (def != null && !string.IsNullOrEmpty(def.Name)) return def.Name;
                }
            }
            catch { }

            // 2. Thử tìm trên shape
            try
            {
                foreach (Parameter p in shape.Parameters)
                {
                    if (p.Id == paramId) return p.Definition?.Name ?? string.Empty;
                }
            }
            catch { }

            // 3. Thử tìm trên elem (Rebar instance)
            try
            {
                foreach (Parameter p in elem.Parameters)
                {
                    if (p.Id == paramId) return p.Definition?.Name ?? string.Empty;
                }
            }
            catch { }

            // 4. Thử kiểm tra BuiltInParameter nếu paramId là số âm
            try
            {
                if (paramId.Value < 0)
                {
                    var bip = (BuiltInParameter)paramId.Value;
                    return LabelUtils.GetLabelFor(bip);
                }
            }
            catch { }

            return string.Empty;
        }

        private static IList<Curve>? GetCenterlineCurvesWithFallbacks(Element elem, RebarShape? shape)
        {
            IList<Curve>? curves = null;

            if (elem is Rebar rebar)
            {
                int barPos = 0;
                try
                {
                    if (rebar.NumberOfBarPositions > 0)
                    {
                        for (int i = 0; i < rebar.NumberOfBarPositions; i++)
                        {
                            if (rebar.DoesBarExistAtPosition(i))
                            {
                                barPos = i;
                                break;
                            }
                        }
                    }
                }
                catch { }

                // Thử cách 1: Planar curves với suppressBendRadius = true
                try
                {
                    curves = rebar.GetCenterlineCurves(false, false, true, MultiplanarOption.IncludeOnlyPlanarCurves, barPos);
                }
                catch { }

                // Thử cách 2: Planar curves với suppressBendRadius = false
                if (curves == null || curves.Count == 0)
                {
                    try { curves = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, barPos); } catch { }
                }

                // Thử cách 3: Tất cả curves (hỗ trợ cả thanh thép 3D / multiplanar)
                if (curves == null || curves.Count == 0)
                {
                    try { curves = rebar.GetCenterlineCurves(false, false, true, (MultiplanarOption)1, barPos); } catch { }
                }

                // Thử cách 4: Include All với suppressBendRadius = false
                if (curves == null || curves.Count == 0)
                {
                    try { curves = rebar.GetCenterlineCurves(false, false, false, (MultiplanarOption)1, barPos); } catch { }
                }

                // Thử cách 5: TransformedCenterlineCurves
                if (curves == null || curves.Count == 0)
                {
                    try { curves = rebar.GetTransformedCenterlineCurves(false, false, false, (MultiplanarOption)1, barPos); } catch { }
                }
            }
            else if (elem is RebarInSystem rebarInSystem)
            {
                try { curves = rebarInSystem.GetCenterlineCurves(false, false, true); } catch { }
                if (curves == null || curves.Count == 0)
                {
                    try { curves = rebarInSystem.GetCenterlineCurves(false, false, false); } catch { }
                }
            }

            // Thử cách 6: Lấy trực tiếp từ RebarShape definition (Revit official browser curves)
            if ((curves == null || curves.Count == 0) && shape != null)
            {
                try
                {
                    curves = shape.GetCurvesForBrowser();
                }
                catch { }
            }

            // Thử cách 7: Lấy từ Geometry của Element (hỗ trợ Freeform Rebars)
            if (curves == null || curves.Count == 0)
            {
                try
                {
                    var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
                    var geom = elem.get_Geometry(opt);
                    if (geom != null)
                    {
                        var foundCurves = new List<Curve>();
                        ExtractCurvesFromGeometry(geom, foundCurves);
                        if (foundCurves.Count > 0) curves = foundCurves;
                    }
                }
                catch { }
            }

            return curves;
        }

        private static void ExtractCurvesFromGeometry(GeometryElement geomElem, List<Curve> curves)
        {
            foreach (var obj in geomElem)
            {
                if (obj is Curve c && c.Length > 0.01)
                {
                    curves.Add(c);
                }
                else if (obj is GeometryInstance gi)
                {
                    var inst = gi.GetInstanceGeometry();
                    if (inst != null) ExtractCurvesFromGeometry(inst, curves);
                }
                else if (obj is Solid s && s.Volume > 1e-6)
                {
                    foreach (Edge e in s.Edges)
                    {
                        var edgeCurve = e.AsCurve();
                        if (edgeCurve != null && edgeCurve.Length > 0.05)
                        {
                            curves.Add(edgeCurve);
                        }
                    }
                }
            }
        }
    }
}
