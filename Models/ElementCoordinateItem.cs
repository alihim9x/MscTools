using System;

namespace ElementCoordinateExtractor.Models
{
    public class ElementCoordinateItem
    {
        public long ElementId { get; set; }
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string Mark { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        
        // Tọa độ hiển thị theo đơn vị người dùng chọn (mm, m, ft)
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        
        // Tọa độ gốc nội bộ Revit (feet) dùng để ghi vào tham số kiểu Length
        public double RawX { get; set; }
        public double RawY { get; set; }
        public double RawZ { get; set; }

        public double RotationDegrees { get; set; }
        public string GeometryType { get; set; } = "Point"; // Point or Curve (Line)

        public string FormattedX => $"{X:F3}";
        public string FormattedY => $"{Y:F3}";
        public string FormattedZ => $"{Z:F3}";
        public string FormattedRotation => $"{RotationDegrees:F1}°";
    }
}
