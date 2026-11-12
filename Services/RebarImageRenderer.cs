using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ElementCoordinateExtractor.Services
{
    public static class RebarImageRenderer
    {
        // Độ phân giải cao (800 x 400 px, tỉ lệ 2:1 chuẩn ô bảng thống kê Revit Schedule)
        // Đường nét vector và chữ Arial Italic luôn to, sắc nét, không bị mờ hay vỡ hạt
        public const int CanvasWidth = 800;
        public const int CanvasHeight = 400;

        private const double MarginX = 50.0;
        private const double MarginY = 40.0;
        private const double BarThickness = 24.0; // Bề dày thân thép (2 nét line mảnh song song, ruột rỗng)
        private const double NominalBendRadius = 50.0; // Bán kính uốn cong to rõ, mềm mại

        public static RenderTargetBitmap RenderShapeBitmap(
            RebarShape2D shape, 
            Dictionary<string, double>? legFactors = null, 
            double globalStretch = 1.0,
            bool flipX = false,
            bool flipY = false,
            double dimOffset = 35.0,
            int horizontalLegIndex = 0)
        {
            var visual = new DrawingVisual();

            using (DrawingContext dc = visual.RenderOpen())
            {
                // 1. Nền trắng tinh khiết
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, CanvasWidth, CanvasHeight));

                if (shape.Legs.Count == 0)
                {
                    DrawEmptyPlaceholder(dc, shape.ShapeName);
                }
                else if (shape.Legs.Count == 1 || shape.Height < 1e-4)
                {
                    // Thép thẳng (Shape 00) - 2 nét line liền mảnh, rỗng ruột, vạch chặn đen ở 2 đầu
                    double f = 1.0;
                    if (legFactors != null && legFactors.TryGetValue(shape.Legs[0].Label, out double fVal)) f = fVal;
                    else if (shape.Legs[0].DisplayScaleFactor > 0.01) f = shape.Legs[0].DisplayScaleFactor;
                    DrawStraightHollowBar(dc, shape.Legs[0], f, flipY, dimOffset);
                }
                else
                {
                    // Thép uốn (Shape 14, 15, Rebar Shape custom...)
                    DrawBendedHollowBar(dc, shape, legFactors, globalStretch, flipX, flipY, dimOffset, horizontalLegIndex);
                }
            }

            // Kết xuất ra ảnh Bitmap độ phân giải cao
            var rtb = new RenderTargetBitmap(CanvasWidth, CanvasHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            return rtb;
        }

        public static string RenderShapeImage(
            RebarShape2D shape, 
            string outputFolderPath, 
            Dictionary<string, double>? legFactors = null, 
            double globalStretch = 1.0,
            bool flipX = false,
            bool flipY = false,
            double dimOffset = 35.0,
            int horizontalLegIndex = 0)
        {
            if (!Directory.Exists(outputFolderPath))
            {
                Directory.CreateDirectory(outputFolderPath);
            }

            string safeName = GetSafeFileName(shape.ShapeName);
            string filePath = Path.Combine(outputFolderPath, $"Shape_{safeName}.png");

            var rtb = RenderShapeBitmap(shape, legFactors, globalStretch, flipX, flipY, dimOffset, horizontalLegIndex);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                encoder.Save(fileStream);
            }

            return filePath;
        }

        private static void DrawStraightHollowBar(DrawingContext dc, RebarLeg2D leg, double factor = 1.0, bool flipY = false, double dimOffset = 35.0)
        {
            double yBar = 185.0;
            double barH = 26.0;

            double safeFactor = Math.Max(0.3, Math.Min(1.3, factor));
            double totalW = (CanvasWidth - 140.0) * safeFactor;
            double x1 = (CanvasWidth - totalW) / 2.0;
            double x2 = (CanvasWidth + totalW) / 2.0;

            // 1. Thân thép: 2 nét line liền mảnh, ở giữa là rỗng (nền trắng)
            var outlinePen = new Pen(Brushes.Black, 2.5);
            var barRect = new Rect(x1, yBar - barH / 2.0, x2 - x1, barH);
            dc.DrawRectangle(Brushes.White, outlinePen, barRect);

            // 2. Đầu thanh thép: 2 vạch chặn đen ở 2 đầu
            double capW = 6.0;
            dc.DrawRectangle(Brushes.Black, null, new Rect(x1, yBar - barH / 2.0, capW, barH));
            dc.DrawRectangle(Brushes.Black, null, new Rect(x2 - capW, yBar - barH / 2.0, capW, barH));

            // 3. Đường gióng kích thước
            double actualOffset = Math.Max(15.0, Math.Min(80.0, dimOffset));
            double yDim = flipY ? (yBar - barH / 2.0 - actualOffset) : (yBar + barH / 2.0 + actualOffset);
            var dimPen = new Pen(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 1.5);

            double extStart = flipY ? (yBar - barH / 2.0 - 4.0) : (yBar + barH / 2.0 + 4.0);
            double extEnd = flipY ? (yDim - 8.0) : (yDim + 8.0);

            dc.DrawLine(dimPen, new Point(x1, extStart), new Point(x1, extEnd));
            dc.DrawLine(dimPen, new Point(x2, extStart), new Point(x2, extEnd));
            dc.DrawLine(dimPen, new Point(x1, yDim), new Point(x2, yDim));

            // Mũi tên tam giác đặc ở 2 đầu
            DrawArrowhead(dc, new Point(x1, yDim), new Vector(1, 0), 20.0, 5.5);
            DrawArrowhead(dc, new Point(x2, yDim), new Vector(-1, 0), 20.0, 5.5);

            // 4. Nhãn chữ cái "A" font Arial Italic đậm, to rõ
            string label = string.IsNullOrEmpty(leg.Label) ? "A" : leg.Label;
            DrawTextLabel(dc, label, new Point((x1 + x2) / 2.0, yDim), 34.0);
        }

        private static void DrawBendedHollowBar(
            DrawingContext dc, 
            RebarShape2D shape, 
            Dictionary<string, double>? legFactors = null, 
            double globalStretch = 1.0,
            bool flipX = false,
            bool flipY = false,
            double dimOffset = 35.0,
            int horizontalLegIndex = 0)
        {
            var rawLegs = new List<(Vector dir, double len, string label, double factor)>();
            for (int i = 0; i < shape.Legs.Count; i++)
            {
                var leg = shape.Legs[i];
                double dx = leg.End.X - leg.Start.X;
                // Ánh xạ trục Y hướng lên của Revit khớp với hướng lên trực quan của màn hình WPF
                double dy = -(leg.End.Y - leg.Start.Y);
                double l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-4) continue;

                double factor = leg.DisplayScaleFactor > 0.01 ? leg.DisplayScaleFactor : 1.0;
                if (legFactors != null)
                {
                    if (legFactors.TryGetValue(leg.Label, out double fByLabel)) factor = fByLabel;
                    else if (legFactors.TryGetValue(i.ToString(), out double fByIndex)) factor = fByIndex;
                }
                if (factor <= 0.05) factor = 1.0;

                rawLegs.Add((new Vector(dx / l, dy / l), l, leg.Label, factor));
            }

            if (rawLegs.Count == 0) return;

            // ĐẢM BẢO CÓ 1 CẠNH LUÔN LUÔN NẰM NGANG
            // Chọn cạnh horizontalLegIndex (hoặc cạnh 0 nếu ngoài dải) làm cạnh chuẩn nằm ngang
            int hIdx = Math.Clamp(horizontalLegIndex, 0, rawLegs.Count - 1);
            Vector hDir = rawLegs[hIdx].dir;
            double rotAngle = -Math.Atan2(hDir.Y, hDir.X);

            Vector RotateVec(Vector v, double angle)
            {
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                return new Vector(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
            }

            for (int i = 0; i < rawLegs.Count; i++)
            {
                var rotated = RotateVec(rawLegs[i].dir, rotAngle);
                // Triệt tiêu sai số số thực để cạnh chuẩn có Y = 0 tuyệt đối
                if (i == hIdx || Math.Abs(rotated.Y) < 1e-4)
                {
                    rotated.Y = 0;
                    rotated.X = Math.Sign(rotated.X) != 0 ? Math.Sign(rotated.X) : 1.0;
                }
                rawLegs[i] = (rotated, rawLegs[i].len, rawLegs[i].label, rawLegs[i].factor);
            }

            // Kéo dài các đoạn thẳng minh họa theo tỉ lệ tương quan
            double maxL = rawLegs.Max(l => l.len);
            var schematicLens = new List<double>();
            for (int i = 0; i < rawLegs.Count; i++)
            {
                double r = rawLegs[i].len / maxL;
                double sRatio = 0.65 + 0.35 * Math.Sqrt(r);
                double baseLen = sRatio * 160.0;
                schematicLens.Add(baseLen * rawLegs[i].factor * globalStretch);
            }

            var poly = new List<Point> { new Point(0, 0) };
            for (int i = 0; i < rawLegs.Count; i++)
            {
                poly.Add(poly.Last() + rawLegs[i].dir * schematicLens[i]);
            }

            // Áp dụng Lật Trái / Phải (Flip X) và Lật Lên / Xuống (Flip Y)
            if (flipX)
            {
                for (int i = 0; i < poly.Count; i++)
                {
                    poly[i] = new Point(-poly[i].X, poly[i].Y);
                }
            }
            if (flipY)
            {
                for (int i = 0; i < poly.Count; i++)
                {
                    poly[i] = new Point(poly[i].X, -poly[i].Y);
                }
            }

            // Đưa về gốc (0, 0)
            double minX = poly.Min(p => p.X);
            double minY = poly.Min(p => p.Y);
            for (int i = 0; i < poly.Count; i++)
            {
                poly[i] = new Point(poly[i].X - minX, poly[i].Y - minY);
            }

            Point shapeCenter = new Point(poly.Average(p => p.X), poly.Average(p => p.Y));

            // Tính toán đường kích thước với offset dim người dùng chỉnh
            double actualDimOffset = Math.Max(15.0, Math.Min(80.0, dimOffset));
            var dimLines = new List<(Point d1, Point d2, Vector u, string label)>();
            var allBBoxPts = new List<Point>(poly);

            for (int i = 0; i < poly.Count - 1; i++)
            {
                Point c1 = poly[i];
                Point c2 = poly[i + 1];
                Vector v = c2 - c1;
                double len = v.Length;
                if (len < 1e-3) continue;
                Vector u = v / len;
                Vector n = new Vector(-u.Y, u.X);

                Point mid = new Point((c1.X + c2.X) / 2.0, (c1.Y + c2.Y) / 2.0);
                if (Vector.Multiply(n, mid - shapeCenter) < 0) n = -n;

                Point d1 = c1 + n * actualDimOffset;
                Point d2 = c2 + n * actualDimOffset;

                dimLines.Add((d1, d2, u, rawLegs[i].label));
                allBBoxPts.Add(d1);
                allBBoxPts.Add(d2);
                allBBoxPts.Add(d1 + n * 18.0);
                allBBoxPts.Add(d2 + n * 18.0);
            }

            // Ánh xạ lên Canvas 800 x 400
            double bMinX = allBBoxPts.Min(p => p.X);
            double bMaxX = allBBoxPts.Max(p => p.X);
            double bMinY = allBBoxPts.Min(p => p.Y);
            double bMaxY = allBBoxPts.Max(p => p.Y);

            double bw = Math.Max(bMaxX - bMinX, 1.0);
            double bh = Math.Max(bMaxY - bMinY, 1.0);

            double availW = CanvasWidth - 2 * MarginX;
            double availH = CanvasHeight - 2 * MarginY;

            double scale = Math.Min(availW / bw, availH / bh);
            double actualW = bw * scale;
            double actualH = bh * scale;

            double offX = MarginX + (availW - actualW) / 2.0 - bMinX * scale;
            double offY = MarginY + (availH - actualH) / 2.0 - bMinY * scale;

            Point ToCanvas(Point p)
            {
                double cx = offX + p.X * scale;
                double cy = CanvasHeight - (offY + p.Y * scale);
                return new Point(cx, cy);
            }

            var cPts = poly.Select(ToCanvas).ToList();

            // Vẽ đường đo kích thước
            var dimPen = new Pen(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 1.5);

            for (int i = 0; i < dimLines.Count; i++)
            {
                var dl = dimLines[i];
                Point p1 = cPts[i];
                Point p2 = cPts[i + 1];
                Point d1 = ToCanvas(dl.d1);
                Point d2 = ToCanvas(dl.d2);

                Vector dir = d2 - d1;
                double l = dir.Length;
                if (l < 5.0) continue;
                Vector u = dir / l;
                Vector n = new Vector(-u.Y, u.X);

                Point midBar = new Point((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);
                Point midDim = new Point((d1.X + d2.X) / 2.0, (d1.Y + d2.Y) / 2.0);
                if (Vector.Multiply(n, midDim - midBar) < 0) n = -n;

                // Đường gióng
                dc.DrawLine(dimPen, p1 + n * (BarThickness / 2.0 + 3.0), d1 + n * 8.0);
                dc.DrawLine(dimPen, p2 + n * (BarThickness / 2.0 + 3.0), d2 + n * 8.0);

                // Đường kích thước
                dc.DrawLine(dimPen, d1, d2);

                // Mũi tên tam giác đặc ở 2 đầu
                double arrowLen = Math.Min(18.0, l / 3.0);
                DrawArrowhead(dc, d1, u, arrowLen, arrowLen * 0.28);
                DrawArrowhead(dc, d2, -u, arrowLen, arrowLen * 0.28);

                // Nhãn chữ cái to, đậm, Arial Italic
                DrawTextLabel(dc, dl.label, midDim, 32.0);
            }

            // Vẽ thân thép uốn với góc cua tròn to đẹp
            DrawHollowBarWithFillet(dc, cPts, BarThickness, NominalBendRadius);
        }

        private static void DrawHollowBarWithFillet(DrawingContext dc, List<Point> pts, double thickness, double nominalR)
        {
            double halfT = thickness / 2.0;
            int n = pts.Count;
            if (n < 2) return;

            var leftContour = new List<Point>();
            var rightContour = new List<Point>();

            for (int i = 0; i < n; i++)
            {
                if (i == 0)
                {
                    Vector d = pts[1] - pts[0];
                    d.Normalize();
                    Vector norm = new Vector(-d.Y, d.X);
                    leftContour.Add(pts[0] + norm * halfT);
                    rightContour.Add(pts[0] - norm * halfT);
                }
                else if (i == n - 1)
                {
                    Vector d = pts[n - 1] - pts[n - 2];
                    d.Normalize();
                    Vector norm = new Vector(-d.Y, d.X);
                    leftContour.Add(pts[n - 1] + norm * halfT);
                    rightContour.Add(pts[n - 1] - norm * halfT);
                }
                else
                {
                    Point prev = pts[i - 1];
                    Point curr = pts[i];
                    Point next = pts[i + 1];

                    Vector v1 = curr - prev;
                    Vector v2 = next - curr;
                    double len1 = v1.Length;
                    double len2 = v2.Length;
                    Vector u1 = v1 / len1;
                    Vector u2 = v2 / len2;

                    double cross = u1.X * u2.Y - u1.Y * u2.X;
                    double dot = Math.Max(-1.0, Math.Min(1.0, u1.X * u2.X + u1.Y * u2.Y));
                    double turnAngle = Math.Acos(dot);

                    if (Math.Abs(cross) < 1e-4 || turnAngle < 0.05)
                    {
                        Vector norm = new Vector(-u1.Y, u1.X);
                        leftContour.Add(curr + norm * halfT);
                        rightContour.Add(curr - norm * halfT);
                        continue;
                    }

                    // Tính toán góc lượn tròn (Fillet)
                    double halfTurn = turnAngle / 2.0;
                    double maxT = 0.45 * Math.Min(len1, len2);
                    double T = nominalR * Math.Tan(halfTurn);
                    double R = nominalR;
                    if (T > maxT)
                    {
                        T = maxT;
                        R = T / Math.Tan(halfTurn);
                    }

                    Point t1 = curr - u1 * T;
                    Point t2 = curr + u2 * T;

                    Vector norm1 = cross > 0 ? new Vector(-u1.Y, u1.X) : new Vector(u1.Y, -u1.X);
                    Point center = t1 + norm1 * R;

                    double rOuter = R + halfT;
                    double rInner = Math.Max(R - halfT, 4.0);

                    int steps = 12;
                    double startAng = Math.Atan2(t1.Y - center.Y, t1.X - center.X);
                    double endAng = Math.Atan2(t2.Y - center.Y, t2.X - center.X);

                    double diff = endAng - startAng;
                    while (diff > Math.PI) diff -= 2 * Math.PI;
                    while (diff < -Math.PI) diff += 2 * Math.PI;

                    if (cross > 0)
                    {
                        for (int s = 0; s <= steps; s++)
                        {
                            double ang = startAng + diff * (s / (double)steps);
                            leftContour.Add(new Point(center.X + rInner * Math.Cos(ang), center.Y + rInner * Math.Sin(ang)));
                            rightContour.Add(new Point(center.X + rOuter * Math.Cos(ang), center.Y + rOuter * Math.Sin(ang)));
                        }
                    }
                    else
                    {
                        for (int s = 0; s <= steps; s++)
                        {
                            double ang = startAng + diff * (s / (double)steps);
                            leftContour.Add(new Point(center.X + rOuter * Math.Cos(ang), center.Y + rOuter * Math.Sin(ang)));
                            rightContour.Add(new Point(center.X + rInner * Math.Cos(ang), center.Y + rInner * Math.Sin(ang)));
                        }
                    }
                }
            }

            // Ghép đa giác khép kín cho thanh thép rỗng
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(leftContour[0], true, true);
                for (int i = 1; i < leftContour.Count; i++) ctx.LineTo(leftContour[i], true, true);
                for (int i = rightContour.Count - 1; i >= 0; i--) ctx.LineTo(rightContour[i], true, true);
            }

            var outlinePen = new Pen(Brushes.Black, 2.5) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(Brushes.White, outlinePen, geo);

            // Hai vạch chặn đen đặc ở 2 đầu thanh thép
            double capW = 6.0;
            var capPen = new Pen(Brushes.Black, capW);
            dc.DrawLine(capPen, leftContour[0], rightContour[0]);
            dc.DrawLine(capPen, leftContour.Last(), rightContour.Last());
        }

        private static void DrawArrowhead(DrawingContext dc, Point tip, Vector dir, double len = 20.0, double halfW = 5.5)
        {
            dir.Normalize();
            Vector normal = new Vector(-dir.Y, dir.X);

            Point pBase = tip + dir * len;
            Point p1 = pBase + normal * halfW;
            Point p2 = pBase - normal * halfW;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(p1, false, false);
                ctx.LineTo(p2, false, false);
            }
            dc.DrawGeometry(Brushes.Black, null, geo);
        }

        private static void DrawTextLabel(DrawingContext dc, string text, Point center, double fontSize = 32.0)
        {
            var typeface = new Typeface(new FontFamily("Arial"), FontStyles.Italic, FontWeights.Bold, FontStretches.Normal);
            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                1.0);

            double textW = formattedText.Width;
            double textH = formattedText.Height;

            Point origin = new Point(center.X - textW / 2.0, center.Y - textH / 2.0);

            // Nền trắng che đường kích thước bên dưới chữ
            var maskRect = new Rect(origin.X - 6.0, origin.Y - 2.0, textW + 12.0, textH + 4.0);
            dc.DrawRectangle(Brushes.White, null, maskRect);

            dc.DrawText(formattedText, origin);
        }

        private static void DrawEmptyPlaceholder(DrawingContext dc, string shapeName)
        {
            var typeface = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var text = new FormattedText(
                $"Shape {shapeName}",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                24.0,
                Brushes.Gray,
                1.0);

            dc.DrawText(text, new Point((CanvasWidth - text.Width) / 2.0, (CanvasHeight - text.Height) / 2.0));
        }

        private static string GetSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}
