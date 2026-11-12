using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ElementCoordinateExtractor.Models;

namespace ElementCoordinateExtractor.Services
{
    public class RelocateService
    {
        private readonly Document _doc;

        public RelocateService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public static XYZ GetProjectBasePoint(Document doc)
        {
            var pbp = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ProjectBasePoint)
                .Cast<BasePoint>()
                .FirstOrDefault();

            if (pbp != null)
            {
                if (pbp.Location is LocationPoint lp)
                    return lp.Point;
                return pbp.Position;
            }

            return XYZ.Zero;
        }

        public static XYZ GetElementLocation(Element elem)
        {
            if (elem.Location is LocationPoint lp)
                return lp.Point;

            if (elem.Location is LocationCurve lc)
                return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) * 0.5;

            var bb = elem.get_BoundingBox(null);
            if (bb != null)
                return (bb.Min + bb.Max) * 0.5;

            return XYZ.Zero;
        }

        public List<RelocateElementItem> GetElementsInfo(
            ICollection<ElementId> elementIds,
            CoordinateSystemType coordSystem,
            DisplayUnitType unitType)
        {
            var results = new List<RelocateElementItem>();
            if (elementIds == null || elementIds.Count == 0) return results;

            XYZ pbpPos = coordSystem == CoordinateSystemType.ProjectBasePoint 
                ? GetProjectBasePoint(_doc) 
                : XYZ.Zero;

            foreach (var id in elementIds)
            {
                var elem = _doc.GetElement(id);
                if (elem == null) continue;

                XYZ internalPt = GetElementLocation(elem);

                double rawFeetX = internalPt.X;
                double rawFeetY = internalPt.Y;
                double rawFeetZ = internalPt.Z;

                switch (coordSystem)
                {
                    case CoordinateSystemType.ProjectBasePoint:
                        var rel = internalPt - pbpPos;
                        rawFeetX = rel.X;
                        rawFeetY = rel.Y;
                        rawFeetZ = rel.Z;
                        break;

                    case CoordinateSystemType.SurveyPoint:
                        var projPos = _doc.ActiveProjectLocation.GetProjectPosition(internalPt);
                        rawFeetX = projPos.EastWest;
                        rawFeetY = projPos.NorthSouth;
                        rawFeetZ = projPos.Elevation;
                        break;
                }

                results.Add(new RelocateElementItem
                {
                    ElementId = elem.Id.Value,
                    ElementName = elem.Name,
                    CategoryName = elem.Category?.Name ?? "General",
                    IsPinned = elem.Pinned,
                    InternalPoint = internalPt,
                    X = ConvertFeetToUnit(rawFeetX, unitType),
                    Y = ConvertFeetToUnit(rawFeetY, unitType),
                    Z = ConvertFeetToUnit(rawFeetZ, unitType)
                });
            }

            return results;
        }

        /// <summary>
        /// Tính vector dịch chuyển (feet) trong hệ tọa độ nội bộ Revit từ khoảng dời Delta người dùng nhập.
        /// </summary>
        public XYZ CalculateInternalDisplacement(
            double deltaX_display,
            double deltaY_display,
            double deltaZ_display,
            CoordinateSystemType coordSystem,
            DisplayUnitType unitType)
        {
            double dX_feet = ConvertUnitToFeet(deltaX_display, unitType);
            double dY_feet = ConvertUnitToFeet(deltaY_display, unitType);
            double dZ_feet = ConvertUnitToFeet(deltaZ_display, unitType);

            switch (coordSystem)
            {
                case CoordinateSystemType.SurveyPoint:
                    // Hệ Survey Point có góc xoay True North (Angle to True North)
                    var pos = _doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
                    double angle = pos.Angle;

                    double intX = Math.Cos(angle) * dX_feet - Math.Sin(angle) * dY_feet;
                    double intY = Math.Sin(angle) * dX_feet + Math.Cos(angle) * dY_feet;
                    double intZ = dZ_feet;
                    return new XYZ(intX, intY, intZ);

                case CoordinateSystemType.ProjectBasePoint:
                case CoordinateSystemType.InternalOrigin:
                default:
                    return new XYZ(dX_feet, dY_feet, dZ_feet);
            }
        }

        public (int count, string error) MoveElements(
            ICollection<ElementId> elementIds,
            XYZ displacement,
            bool unpinIfPinned)
        {
            if (elementIds == null || elementIds.Count == 0)
                return (0, "Không có đối tượng nào được chọn.");

            if (displacement.IsAlmostEqualTo(XYZ.Zero))
                return (0, "Khoảng dịch chuyển bằng 0, không có thay đổi.");

            try
            {
                using (var tx = new Transaction(_doc, "Di chuyển đối tượng tới tọa độ mới"))
                {
                    tx.Start();

                    foreach (var id in elementIds)
                    {
                        var elem = _doc.GetElement(id);
                        if (elem != null && elem.Pinned)
                        {
                            if (unpinIfPinned)
                            {
                                elem.Pinned = false;
                            }
                            else
                            {
                                tx.RollBack();
                                return (0, $"Đối tượng '{elem.Name}' (ID: {elem.Id}) đang bị ghim (Pinned). Vui lòng bỏ ghim hoặc tích chọn 'Tự động bỏ ghim'.");
                            }
                        }
                    }

                    if (elementIds.Count == 1)
                    {
                        ElementTransformUtils.MoveElement(_doc, elementIds.First(), displacement);
                    }
                    else
                    {
                        ElementTransformUtils.MoveElements(_doc, elementIds, displacement);
                    }

                    tx.Commit();
                }

                return (elementIds.Count, string.Empty);
            }
            catch (Exception ex)
            {
                return (0, ex.Message);
            }
        }

        public (int count, string error) CopyElements(
            ICollection<ElementId> elementIds,
            XYZ displacement)
        {
            if (elementIds == null || elementIds.Count == 0)
                return (0, "Không có đối tượng nào được chọn.");

            if (displacement.IsAlmostEqualTo(XYZ.Zero))
                return (0, "Khoảng dịch chuyển bằng 0, không tạo bản sao.");

            try
            {
                int count = 0;
                using (var tx = new Transaction(_doc, "Tạo bản sao tại tọa độ mới"))
                {
                    tx.Start();

                    var copiedIds = ElementTransformUtils.CopyElements(_doc, elementIds, displacement);
                    count = copiedIds.Count;

                    tx.Commit();
                }

                return (count, string.Empty);
            }
            catch (Exception ex)
            {
                return (0, ex.Message);
            }
        }

        public static double ConvertFeetToUnit(double feetValue, DisplayUnitType unitType)
        {
            return unitType switch
            {
                DisplayUnitType.Millimeters => feetValue * 304.8,
                DisplayUnitType.Meters => feetValue * 0.3048,
                DisplayUnitType.Feet => feetValue,
                _ => feetValue
            };
        }

        public static double ConvertUnitToFeet(double displayValue, DisplayUnitType unitType)
        {
            return unitType switch
            {
                DisplayUnitType.Millimeters => displayValue / 304.8,
                DisplayUnitType.Meters => displayValue / 0.3048,
                DisplayUnitType.Feet => displayValue,
                _ => displayValue
            };
        }
    }
}
