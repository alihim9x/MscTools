using Autodesk.Revit.DB;

namespace ElementCoordinateExtractor.Models
{
    public class RelocateElementItem
    {
        public long ElementId { get; set; }
        public string ElementName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public bool IsPinned { get; set; }
        
        // Tọa độ gốc nội bộ (feet)
        public XYZ InternalPoint { get; set; } = XYZ.Zero;

        // Tọa độ hiển thị theo hệ và đơn vị được chọn
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public string FormattedX => $"{X:F3}";
        public string FormattedY => $"{Y:F3}";
        public string FormattedZ => $"{Z:F3}";
        public string PinnedStatus => IsPinned ? "Đang ghim (Pinned)" : "Tự do";
    }
}
