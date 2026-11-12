using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using ElementCoordinateExtractor.Models;

namespace ElementCoordinateExtractor.Services
{
    public class CoordinateService
    {
        private readonly Document _doc;

        public CoordinateService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        /// <summary>
        /// Lấy danh sách các Family có chứa instance trong mô hình, kèm theo số lượng instance tương ứng.
        /// </summary>
        public List<FamilyItem> GetFamiliesWithInstances()
        {
            var familyDict = new Dictionary<ElementId, (string Name, string Category, int Count)>();

            // Quét nhanh qua các FamilyInstance đang tồn tại
            var instances = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>();

            foreach (var inst in instances)
            {
                var symbol = inst.Symbol;
                if (symbol == null) continue;

                var family = symbol.Family;
                if (family == null) continue;

                var famId = family.Id;
                var famName = string.IsNullOrWhiteSpace(family.Name) ? "Unnamed Family" : family.Name;
                var catName = family.FamilyCategory?.Name ?? inst.Category?.Name ?? "General";

                if (familyDict.TryGetValue(famId, out var info))
                {
                    familyDict[famId] = (info.Name, info.Category, info.Count + 1);
                }
                else
                {
                    familyDict[famId] = (famName, catName, 1);
                }
            }

            return familyDict.Select(kv => new FamilyItem(kv.Key, kv.Value.Name, kv.Value.Category, kv.Value.Count))
                             .OrderBy(f => f.CategoryName)
                             .ThenBy(f => f.FamilyName)
                             .ToList();
        }

        /// <summary>
        /// Lấy danh sách các Parameter (có thể ghi được) của instance thuộc Family được chọn.
        /// </summary>
        public List<string> GetAvailableInstanceParameters(ElementId familyId)
        {
            var result = new List<string> { "-- Không chọn --" };
            var family = _doc.GetElement(familyId) as Family;
            if (family == null) return result;

            var symbolIds = new HashSet<ElementId>(family.GetFamilySymbolIds());
            if (symbolIds.Count == 0) return result;

            var sampleInst = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .FirstOrDefault(fi => symbolIds.Contains(fi.GetTypeId()));

            if (sampleInst == null) return result;

            var paramNames = new HashSet<string>();
            foreach (Parameter p in sampleInst.Parameters)
            {
                if (!p.IsReadOnly && p.StorageType != StorageType.ElementId && p.StorageType != StorageType.None)
                {
                    // Lọc các parameter phù hợp để chứa tọa độ: Text, Double, Integer
                    paramNames.Add(p.Definition.Name);
                }
            }

            result.AddRange(paramNames.OrderBy(n => n));
            return result;
        }

        /// <summary>
        /// Lấy toàn bộ tọa độ của các instance thuộc Family được chọn.
        /// </summary>
        public List<ElementCoordinateItem> ExtractCoordinates(
            ElementId familyId, 
            CoordinateSystemType coordSystem, 
            DisplayUnitType unitType)
        {
            var results = new List<ElementCoordinateItem>();
            var family = _doc.GetElement(familyId) as Family;
            if (family == null) return results;

            var symbolIds = new HashSet<ElementId>(family.GetFamilySymbolIds());
            if (symbolIds.Count == 0) return results;

            // Tìm BasePoint dự án phòng khi cần tính tọa độ theo Project Base Point
            XYZ pbpPos = XYZ.Zero;
            if (coordSystem == CoordinateSystemType.ProjectBasePoint)
            {
                var pbp = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_ProjectBasePoint)
                    .Cast<BasePoint>()
                    .FirstOrDefault();

                if (pbp != null)
                {
                    if (pbp.Location is LocationPoint lp)
                        pbpPos = lp.Point;
                    else
                        pbpPos = pbp.Position;
                }
            }

            // Quét các FamilyInstance thuộc các Symbol của Family này
            var instances = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(fi => symbolIds.Contains(fi.GetTypeId()))
                .ToList();

            foreach (var inst in instances)
            {
                XYZ rawPoint = XYZ.Zero;
                double rotationDeg = 0.0;
                string geomType = "Point";

                if (inst.Location is LocationPoint locPt)
                {
                    rawPoint = locPt.Point;
                    rotationDeg = locPt.Rotation * (180.0 / Math.PI);
                    geomType = "Point";
                }
                else if (inst.Location is LocationCurve locCurve)
                {
                    var curve = locCurve.Curve;
                    var pt0 = curve.GetEndPoint(0);
                    var pt1 = curve.GetEndPoint(1);
                    rawPoint = (pt0 + pt1) * 0.5; // Lấy trung điểm (Midpoint)
                    
                    XYZ direction = (pt1 - pt0).Normalize();
                    rotationDeg = Math.Atan2(direction.Y, direction.X) * (180.0 / Math.PI);
                    if (rotationDeg < 0) rotationDeg += 360.0;
                    
                    geomType = "Curve (Midpoint)";
                }
                else
                {
                    // Fallback sang BoundingBox Center
                    var bb = inst.get_BoundingBox(null);
                    if (bb != null)
                    {
                        rawPoint = (bb.Min + bb.Max) * 0.5;
                        geomType = "BoundingBox Center";
                    }
                }

                // Chuyển đổi tọa độ theo hệ được chọn
                double rawFeetX = 0;
                double rawFeetY = 0;
                double rawFeetZ = 0;

                switch (coordSystem)
                {
                    case CoordinateSystemType.InternalOrigin:
                        rawFeetX = rawPoint.X;
                        rawFeetY = rawPoint.Y;
                        rawFeetZ = rawPoint.Z;
                        break;

                    case CoordinateSystemType.ProjectBasePoint:
                        var relPoint = rawPoint - pbpPos;
                        rawFeetX = relPoint.X;
                        rawFeetY = relPoint.Y;
                        rawFeetZ = relPoint.Z;
                        break;

                    case CoordinateSystemType.SurveyPoint:
                        // Lấy tọa độ theo Survey Point / Shared Coordinates (East/West, North/South, Elevation)
                        var projPos = _doc.ActiveProjectLocation.GetProjectPosition(rawPoint);
                        rawFeetX = projPos.EastWest;
                        rawFeetY = projPos.NorthSouth;
                        rawFeetZ = projPos.Elevation;
                        break;
                }

                double outX = ConvertFeetToUnit(rawFeetX, unitType);
                double outY = ConvertFeetToUnit(rawFeetY, unitType);
                double outZ = ConvertFeetToUnit(rawFeetZ, unitType);

                // Lấy thông tin phụ trợ
                string typeName = inst.Symbol?.Name ?? "Standard";
                string mark = inst.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty;
                string levelName = GetElementLevelName(inst);

                long idValue = inst.Id.Value;

                results.Add(new ElementCoordinateItem
                {
                    ElementId = idValue,
                    FamilyName = family.Name,
                    TypeName = typeName,
                    CategoryName = family.FamilyCategory?.Name ?? "General",
                    Mark = mark,
                    LevelName = levelName,
                    X = outX,
                    Y = outY,
                    Z = outZ,
                    RawX = rawFeetX,
                    RawY = rawFeetY,
                    RawZ = rawFeetZ,
                    RotationDegrees = rotationDeg,
                    GeometryType = geomType
                });
            }

            return results;
        }

        /// <summary>
        /// Ghi tọa độ X, Y, Z vào 3 parameters do người dùng lựa chọn cho tất cả instance của Family.
        /// </summary>
        public (int updated, int failed, string message) WriteCoordinatesToParameters(
            ElementId familyId,
            CoordinateSystemType coordSystem,
            DisplayUnitType unitType,
            string paramXName,
            string paramYName,
            string paramZName)
        {
            var family = _doc.GetElement(familyId) as Family;
            if (family == null) return (0, 0, "Không tìm thấy Family.");

            var symbolIds = new HashSet<ElementId>(family.GetFamilySymbolIds());
            if (symbolIds.Count == 0) return (0, 0, "Family không có Symbol nào.");

            var instances = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(fi => symbolIds.Contains(fi.GetTypeId()))
                .ToList();

            if (instances.Count == 0) return (0, 0, "Không tìm thấy instance nào trong mô hình.");

            var coords = ExtractCoordinates(familyId, coordSystem, unitType);
            var coordDict = coords.ToDictionary(c => c.ElementId, c => c);

            bool writeX = !string.IsNullOrEmpty(paramXName) && paramXName != "-- Không chọn --";
            bool writeY = !string.IsNullOrEmpty(paramYName) && paramYName != "-- Không chọn --";
            bool writeZ = !string.IsNullOrEmpty(paramZName) && paramZName != "-- Không chọn --";

            if (!writeX && !writeY && !writeZ)
            {
                return (0, 0, "Vui lòng chọn ít nhất một parameter (X, Y hoặc Z) để gán giá trị.");
            }

            int updated = 0;
            int failed = 0;

            using (var tx = new Transaction(_doc, "Ghi tọa độ vào Parameters"))
            {
                tx.Start();

                foreach (var inst in instances)
                {
                    if (!coordDict.TryGetValue(inst.Id.Value, out var item)) continue;

                    bool instOk = true;

                    if (writeX)
                    {
                        instOk &= SetParameterValue(inst, paramXName, item.X, item.RawX);
                    }

                    if (writeY)
                    {
                        instOk &= SetParameterValue(inst, paramYName, item.Y, item.RawY);
                    }

                    if (writeZ)
                    {
                        instOk &= SetParameterValue(inst, paramZName, item.Z, item.RawZ);
                    }

                    if (instOk) updated++;
                    else failed++;
                }

                tx.Commit();
            }

            return (updated, failed, $"Đã cập nhật tọa độ thành công cho {updated} instances.");
        }

        private bool SetParameterValue(FamilyInstance inst, string paramName, double displayVal, double rawFeetVal)
        {
            var p = inst.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;

            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        return p.Set(displayVal.ToString("F3"));

                    case StorageType.Double:
                        // Trong Revit 2022-2025: kiểm tra xem parameter có phải kiểu Length không
                        var dataType = p.Definition.GetDataType();
                        if (dataType == SpecTypeId.Length)
                        {
                            // Parameter kiểu Length trong Revit API bắt buộc nhận giá trị internal đơn vị Feet
                            return p.Set(rawFeetVal);
                        }
                        else
                        {
                            // Parameter kiểu Number hoặc không có đơn vị: gán trực tiếp số hiển thị
                            return p.Set(displayVal);
                        }

                    case StorageType.Integer:
                        return p.Set((int)Math.Round(displayVal));

                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private string GetElementLevelName(Element element)
        {
            if (element.LevelId != ElementId.InvalidElementId)
            {
                var lvl = _doc.GetElement(element.LevelId);
                if (lvl != null) return lvl.Name;
            }

            var levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                          ?? element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                          ?? element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

            if (levelParam != null && levelParam.AsElementId() != ElementId.InvalidElementId)
            {
                var lvl = _doc.GetElement(levelParam.AsElementId());
                if (lvl != null) return lvl.Name;
            }

            return "-";
        }

        private static double ConvertFeetToUnit(double feetValue, DisplayUnitType unitType)
        {
            return unitType switch
            {
                DisplayUnitType.Millimeters => feetValue * 304.8,
                DisplayUnitType.Meters => feetValue * 0.3048,
                DisplayUnitType.Feet => feetValue,
                _ => feetValue
            };
        }

        /// <summary>
        /// Xuất kết quả sang CSV chuẩn UTF-8 có BOM (Excel mở trực tiếp tiếng Việt không lỗi font).
        /// </summary>
        public static void ExportToCsv(IEnumerable<ElementCoordinateItem> items, string filePath, string coordSystemName, string unitName)
        {
            var sb = new StringBuilder();

            // Header metadata
            sb.AppendLine($"# Coordinate Extractor Export");
            sb.AppendLine($"# Coordinate System: {coordSystemName}");
            sb.AppendLine($"# Unit: {unitName}");
            sb.AppendLine($"# Export Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // Column Headers
            sb.AppendLine("Element ID,Family Name,Type Name,Category,Mark,Level,X,Y,Z,Rotation (Deg),Geometry Type");

            foreach (var item in items)
            {
                string row = $"{item.ElementId}," +
                             $"\"{EscapeCsv(item.FamilyName)}\"," +
                             $"\"{EscapeCsv(item.TypeName)}\"," +
                             $"\"{EscapeCsv(item.CategoryName)}\"," +
                             $"\"{EscapeCsv(item.Mark)}\"," +
                             $"\"{EscapeCsv(item.LevelName)}\"," +
                             $"{item.X:F3}," +
                             $"{item.Y:F3}," +
                             $"{item.Z:F3}," +
                             $"{item.RotationDegrees:F1}," +
                             $"\"{EscapeCsv(item.GeometryType)}\"";
                sb.AppendLine(row);
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\"", "\"\"");
        }
    }
}
