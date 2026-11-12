using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ElementCoordinateExtractor.Services
{
    public enum ShapeImageAssignResult
    {
        SuccessCreated,
        SuccessOverwritten,
        SkippedAlreadyExists,
        FailedNoGeometry,
        FailedNoShape
    }

    public class FamilyLoadOptionsOverride : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }

    public static class RebarShapeImageService
    {
        public static string GetImageOutputDirectory(Document? doc = null)
        {
            // Sử dụng thư mục Temp chuẩn của hệ điều hành Windows, hoạt động 100% trên tất cả máy tính
            string tempDir = Path.Combine(Path.GetTempPath(), "MscTools_RebarImages");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }
            return tempDir;
        }

        public static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }

        public static (bool hasImage, string shapeName, RebarShape? shape) CheckIfShapeHasImage(Document doc, Element elem)
        {
            RebarShape? shape = null;
            string shapeName = string.Empty;

            ElementId shapeId = ElementId.InvalidElementId;
            if (elem is Rebar rebar)
            {
                try { shapeId = rebar.GetShapeId(); } catch { }
            }
            else if (elem is RebarInSystem ris)
            {
                try { shapeId = ris.RebarShapeId; } catch { }
            }
            else if (elem is RebarShape rs)
            {
                shape = rs;
                shapeId = rs.Id;
                shapeName = rs.Name;
            }

            if (shapeId != ElementId.InvalidElementId && shape == null)
            {
                shape = doc.GetElement(shapeId) as RebarShape;
                if (shape != null)
                {
                    shapeName = shape.Name;
                }
            }

            if (string.IsNullOrEmpty(shapeName))
            {
                shapeName = shape != null ? shape.Name : $"Custom_{elem.Id.Value}";
            }

            // 1. Kiểm tra trên RebarShape (Type Parameter)
            if (shape != null)
            {
                try
                {
                    var p = shape.GetParameter(ParameterTypeId.RebarShapeImage);
                    if (CheckParamHasValidImage(doc, p)) return (true, shapeName, shape);
                }
                catch { }

                try
                {
                    var p = shape.get_Parameter(BuiltInParameter.REBAR_SHAPE_IMAGE);
                    if (CheckParamHasValidImage(doc, p)) return (true, shapeName, shape);
                }
                catch { }

                try
                {
                    var p = shape.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_IMAGE);
                    if (CheckParamHasValidImage(doc, p)) return (true, shapeName, shape);
                }
                catch { }

                string[] shapeNamesToTry = { "Shape Image", "Image" };
                foreach (var name in shapeNamesToTry)
                {
                    try
                    {
                        var p = shape.LookupParameter(name);
                        if (CheckParamHasValidImage(doc, p)) return (true, shapeName, shape);
                    }
                    catch { }
                }

                try
                {
                    foreach (Parameter p in shape.Parameters)
                    {
                        if (p == null) continue;
                        string pName = p.Definition?.Name?.ToLowerInvariant() ?? "";
                        if (pName.Contains("shape image"))
                        {
                            if (CheckParamHasValidImage(doc, p)) return (true, shapeName, shape);
                        }
                    }
                }
                catch { }
            }

            // LƯU Ý: Tuyệt đối KHÔNG kiểm tra trên Instance Parameter "Image" của Rebar,
            // vì quy trình chuẩn của bảng thống kê Rebar Schedule chỉ dùng Type Parameter "Shape Image".
            return (false, shapeName, shape);
        }

        private static bool CheckParamHasValidImage(Document? doc, Parameter? p)
        {
            if (p == null) return false;
            try
            {
                if (p.HasValue)
                {
                    var id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        if (doc != null)
                        {
                            var elem = doc.GetElement(id);
                            return elem != null;
                        }
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static ImageType? GetExistingShapeImageType(Document doc, RebarShape? shape)
        {
            if (shape == null || doc == null) return null;
            try
            {
                Parameter? p = shape.get_Parameter(BuiltInParameter.REBAR_SHAPE_IMAGE);
                if (p == null) p = shape.GetParameter(ParameterTypeId.RebarShapeImage);
                if (p == null) p = shape.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_IMAGE);
                if (p == null) p = shape.LookupParameter("Shape Image");

                if (p != null && p.HasValue)
                {
                    var id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        var elem = doc.GetElement(id);
                        if (elem is ImageType it) return it;
                    }
                }

                foreach (Parameter param in shape.Parameters)
                {
                    if (param == null || !param.HasValue) continue;
                    string pName = param.Definition?.Name?.ToLowerInvariant() ?? "";
                    if (pName.Contains("shape image") || pName == "image")
                    {
                        var id = param.AsElementId();
                        if (id != null && id != ElementId.InvalidElementId)
                        {
                            var elem = doc.GetElement(id);
                            if (elem is ImageType it) return it;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static (ShapeImageAssignResult status, string message, string imagePath) GenerateAndAssignImage(
            Document doc, 
            Element elem, 
            bool overwriteExisting)
        {
            return GenerateAndAssignImage(doc, elem, null, null, overwriteExisting);
        }

        public static (ShapeImageAssignResult status, string message, string imagePath) GenerateAndAssignImage(
            Document doc, 
            Element elem, 
            RebarShape2D? preExtractedShape2D,
            Dictionary<string, double>? legFactors,
            bool overwriteExisting,
            bool flipX = false,
            bool flipY = false,
            double dimOffset = 35.0,
            int horizontalLegIndex = 0)
        {
            var (hasImage, shapeName, shape) = CheckIfShapeHasImage(doc, elem);

            if (hasImage && !overwriteExisting)
            {
                return (ShapeImageAssignResult.SkippedAlreadyExists, $"Shape '{shapeName}' đã có Shape Image sẵn. Đã bỏ qua.", "");
            }

            // BƯỚC 1: Trích xuất hình học 2D (hoặc dùng hình đã trích xuất sẵn từ UI)
            RebarShape2D? shape2D = preExtractedShape2D;
            if (shape2D == null)
            {
                try
                {
                    shape2D = RebarGeometryService.Extract2DShape(elem, shapeName, shape);
                }
                catch (Exception ex)
                {
                    return (ShapeImageAssignResult.FailedNoGeometry, $"Lỗi khi trích xuất tim thép cho '{shapeName}': {ex.Message}", "");
                }
            }

            if (shape2D == null || shape2D.Legs.Count == 0)
            {
                return (ShapeImageAssignResult.FailedNoGeometry, $"Không thể trích xuất tim thép cho '{shapeName}'.", "");
            }

            // BƯỚC 2: Render ảnh ra file PNG trên đĩa kèm hệ số điều chỉnh kích thước, hướng lật và offset dim
            string outputDir = GetImageOutputDirectory(doc);
            string pngPath = "";
            try
            {
                pngPath = RebarImageRenderer.RenderShapeImage(
                    shape2D, 
                    outputDir, 
                    legFactors, 
                    globalStretch: 1.0, 
                    flipX: flipX, 
                    flipY: flipY, 
                    dimOffset: dimOffset, 
                    horizontalLegIndex: horizontalLegIndex);
            }
            catch (Exception ex)
            {
                return (ShapeImageAssignResult.FailedNoGeometry, $"Lỗi kết xuất ảnh PNG cho '{shapeName}': {ex.Message}", "");
            }

            if (string.IsNullOrEmpty(pngPath) || !File.Exists(pngPath))
            {
                return (ShapeImageAssignResult.FailedNoGeometry, $"Không thể tạo file ảnh PNG cho '{shapeName}'.", "");
            }

            // BƯỚC 3: Gán vào biến Shape Image (Type Parameter) bằng cách mở Family RebarShape, gán ảnh và Load lại vào Project
            // Đúng chuẩn 100% theo quy trình thao tác bằng tay trong Revit
            bool familyEditSuccess = false;
            string familyEditDetail = "";

            try
            {
                if (shape != null)
                {
                    try
                    {
                        familyEditSuccess = AssignShapeImageViaFamilyEdit(doc, shape, pngPath, out familyEditDetail);
                    }
                    catch (Exception ex)
                    {
                        familyEditDetail = $"Lỗi EditFamily: {ex.Message}";
                    }
                }

                if (familyEditSuccess)
                {
                    // HẬU KIỂM (Post-Verification) trên Project Document:
                    // Đọc lại trực tiếp RebarShape trong doc để xác nhận chắc chắn 100% tham số Shape Image đã nhận ảnh
                    bool verifiedInProject = false;
                    try
                    {
                        var (hasImg, _, _) = CheckIfShapeHasImage(doc, elem);
                        verifiedInProject = hasImg;

                        if (!verifiedInProject && shape != null)
                        {
                            var s2 = doc.GetElement(shape.Id) as RebarShape;
                            if (s2 == null)
                            {
                                s2 = new FilteredElementCollector(doc)
                                    .OfClass(typeof(RebarShape))
                                    .Cast<RebarShape>()
                                    .FirstOrDefault(x => x.Name.Equals(shapeName, StringComparison.OrdinalIgnoreCase));
                            }

                            if (s2 != null)
                            {
                                var (hasImg2, _, _) = CheckIfShapeHasImage(doc, s2);
                                verifiedInProject = hasImg2;
                            }
                        }
                    }
                    catch { }

                    if (verifiedInProject)
                    {
                        var resultType = hasImage ? ShapeImageAssignResult.SuccessOverwritten : ShapeImageAssignResult.SuccessCreated;
                        return (resultType, 
                            $"Đã gán thành công Shape Image cho '{shapeName}' và cập nhật vào dự án.", pngPath);
                    }
                    else
                    {
                        return (ShapeImageAssignResult.FailedNoShape, 
                            $"Family của '{shapeName}' đã nạp thành công nhưng tham số Type 'Shape Image' trong dự án chưa nhận được hình ảnh. " +
                            $"Vui lòng kiểm tra lại thiết lập tham số của Rebar Shape Family này.", "");
                    }
                }
                else
                {
                    return (ShapeImageAssignResult.FailedNoShape, 
                        $"Không thể gán Shape Image cho '{shapeName}': {familyEditDetail}\n\nTrong Revit, tham số Shape Image (Type Parameter) chỉ có thể gán thông qua Family RebarShape (.rfa) có thể chỉnh sửa.", "");
                }
            }
            finally
            {
                // BƯỚC 4: Xóa luôn file ảnh tạm thời sau khi đã nạp vào family/project, không lưu lại trong máy
                TryDeleteFile(pngPath);
            }
        }

        public static string NormalizeShapeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            string s = name.Trim().ToLowerInvariant();

            // Loại bỏ tiền tố "m_" hoặc "m" đứng trước số (chuẩn Revit metric)
            if (s.StartsWith("m_")) s = s.Substring(2);
            else if (s.StartsWith("m") && s.Length > 1 && (char.IsDigit(s[1]) || s[1] == '_')) s = s.TrimStart('m', '_');

            // Loại bỏ các từ khoá phổ biến
            s = s.Replace("rebar", "")
                 .Replace("shape", "")
                 .Replace("family", "")
                 .Replace("rs", "")
                 .Replace("-", "")
                 .Replace("_", "")
                 .Replace(" ", "")
                 .Trim();

            // Nếu toàn bộ là chữ số (ví dụ "03", "00", "14") -> Chuẩn hoá bỏ số 0 ở đầu ("03" -> "3", "00" -> "0")
            if (int.TryParse(s, out int num))
            {
                return num.ToString();
            }

            return s;
        }

        public static Family? FindFamilyForShape(Document projectDoc, RebarShape shape)
        {
            if (shape == null || projectDoc == null) return null;

            // 1. ƯU TIÊN SỐ 1 (CHUẨN XÁC 1000%): Tìm trực tiếp Family nào đang sở hữu Symbol Id == shape.Id
            // Cách này dựa vào ID duy nhất của RebarShape trong Revit, hoàn toàn độc lập với tên gọi,
            // đảm bảo không bao giờ bị bắt nhầm sang Family khác (như Rebar Shape 4 cũ).
            try
            {
                var allFamilies = new FilteredElementCollector(projectDoc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(f => f != null && f.IsValidObject)
                    .ToList();

                foreach (var f in allFamilies)
                {
                    try
                    {
                        var symIds = f.GetFamilySymbolIds();
                        if (symIds != null && symIds.Contains(shape.Id))
                        {
                            return f;
                        }
                    }
                    catch { }
                }

                // 2. Ưu tiên số 2: So khớp chính xác 100% theo Tên Family == Tên Shape hiện tại (ví dụ: 'Rebar Shape 7')
                var exactMatched = allFamilies.FirstOrDefault(f => f.Name.Equals(shape.Name, StringComparison.OrdinalIgnoreCase));
                if (exactMatched != null) return exactMatched;

                // 3. Ưu tiên số 3: Lấy Family qua Parameter ELEM_FAMILY_PARAM trên RebarShape
                try
                {
                    var famParam = shape.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
                    if (famParam != null && famParam.HasValue)
                    {
                        var famId = famParam.AsElementId();
                        if (famId != null && famId != ElementId.InvalidElementId)
                        {
                            var elem = projectDoc.GetElement(famId);
                            if (elem is Family f) return f;
                            if (elem is FamilySymbol fs && fs.Family != null) return fs.Family;
                        }
                    }
                }
                catch { }

                // 4. Ưu tiên số 4: Thử lấy qua ShapeFamilyId
                try
                {
                    if (shape.ShapeFamilyId != ElementId.InvalidElementId)
                    {
                        var elemFromId = projectDoc.GetElement(shape.ShapeFamilyId);
                        if (elemFromId is Family f) return f;
                        if (elemFromId is FamilySymbol fs && fs.Family != null) return fs.Family;
                        if (elemFromId != null)
                        {
                            var fp = elemFromId.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
                            if (fp != null && fp.HasValue)
                            {
                                var parentFam = projectDoc.GetElement(fp.AsElementId()) as Family;
                                if (parentFam != null) return parentFam;
                            }
                        }
                    }
                }
                catch { }

                // 5. Ưu tiên số 5: So khớp theo FamilyName của RebarShape
                if (!string.IsNullOrEmpty(shape.FamilyName))
                {
                    var famNameMatched = allFamilies.FirstOrDefault(f => f.Name.Equals(shape.FamilyName, StringComparison.OrdinalIgnoreCase));
                    if (famNameMatched != null) return famNameMatched;
                }
            }
            catch { }

            return null;
        }

        private static bool AssignShapeImageViaFamilyEdit(Document projectDoc, RebarShape shape, string pngPath, out string detail)
        {
            detail = string.Empty;

            // 1. Tìm đối tượng Family của RebarShape
            Family? family = FindFamilyForShape(projectDoc, shape);

            if (family == null)
            {
                detail = $"Không tìm thấy Family của Shape '{shape.Name}' trong dự án";
                return false;
            }

            if (!family.IsEditable)
            {
                detail = $"Family '{family.Name}' bị khóa không thể EditFamily";
                return false;
            }

            // Ghi nhận lại tên Shape và Family mong muốn cũng như ID để duy trì tên sau khi LoadFamily
            string desiredShapeName = shape.Name;
            ElementId shapeId = shape.Id;
            ElementId familyId = family.Id;

            // 2. Mở tài liệu Family trong bộ nhớ (EditFamily)
            Document? famDoc = null;
            try
            {
                famDoc = projectDoc.EditFamily(family);
            }
            catch (Exception ex)
            {
                detail = $"EditFamily thất bại: {ex.Message}";
                return false;
            }

            if (famDoc == null)
            {
                detail = "Không thể mở tài liệu Family";
                return false;
            }

            bool assignOk = false;
            try
            {
                using (var famTx = new Transaction(famDoc, "Gán Shape Image trong Family"))
                {
                    famTx.Start();

                    // 1. Nạp ImageType vào trong Family (kiểm tra tồn tại để Reload, tránh crash do trùng tên)
                    string imageTypeName = Path.GetFileNameWithoutExtension(pngPath);
                    var opt = new ImageTypeOptions(pngPath, false, ImageTypeSource.Import);

                    ImageType? famImgType = new FilteredElementCollector(famDoc)
                        .OfClass(typeof(ImageType))
                        .Cast<ImageType>()
                        .FirstOrDefault(x => string.Equals(x.Name, imageTypeName, StringComparison.OrdinalIgnoreCase));

                    if (famImgType != null)
                    {
                        famImgType.ReloadFrom(opt);
                    }
                    else
                    {
                        famImgType = ImageType.Create(famDoc, opt);
                    }

                    if (famImgType != null)
                    {
                        var famMgr = famDoc.FamilyManager;

                        // 1. Tìm tham số "Shape Image" trong FamilyManager
                        FamilyParameter? shapeImgParam = null;
                        try { shapeImgParam = famMgr.get_Parameter(BuiltInParameter.REBAR_SHAPE_IMAGE); } catch { }
                        if (shapeImgParam == null)
                        {
                            try { shapeImgParam = famMgr.GetParameter(ParameterTypeId.RebarShapeImage); } catch { }
                        }
                        if (shapeImgParam == null)
                        {
                            try { shapeImgParam = famMgr.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_IMAGE); } catch { }
                        }
                        if (shapeImgParam == null)
                        {
                            try { shapeImgParam = famMgr.GetParameter(ParameterTypeId.AllModelTypeImage); } catch { }
                        }
                        if (shapeImgParam == null)
                        {
                            foreach (FamilyParameter fp in famMgr.Parameters)
                            {
                                string fpName = fp.Definition.Name.ToLowerInvariant();
                                if (fpName.Contains("shape image") || fpName == "image")
                                {
                                    shapeImgParam = fp;
                                    break;
                                }
                            }
                        }

                        // Đồng bộ tên Type trong Family cho khớp với tên Type trong Project (ví dụ: 'Rebar Shape 7')
                        // Nhờ đó khi LoadFamily vào Project, Revit sẽ ghi đè chính xác vào Type này mà không sinh Type rác hay đổi ngược tên!
                        if (!string.IsNullOrEmpty(desiredShapeName))
                        {
                            if (famMgr.CurrentType != null)
                            {
                                if (!famMgr.CurrentType.Name.Equals(desiredShapeName, StringComparison.OrdinalIgnoreCase))
                                {
                                    try { famMgr.RenameCurrentType(desiredShapeName); } catch { }
                                }
                            }
                            else if (famMgr.Types.Size > 0)
                            {
                                var firstType = famMgr.Types.Cast<FamilyType>().First();
                                famMgr.CurrentType = firstType;
                                if (!firstType.Name.Equals(desiredShapeName, StringComparison.OrdinalIgnoreCase))
                                {
                                    try { famMgr.RenameCurrentType(desiredShapeName); } catch { }
                                }
                            }
                        }

                        // Gán giá trị vào tham số Shape Image trong Family Types (qua FamilyManager)
                        if (shapeImgParam != null)
                        {
                            if (famMgr.Types.Size > 0)
                            {
                                foreach (FamilyType ft in famMgr.Types)
                                {
                                    try
                                    {
                                        famMgr.CurrentType = ft;
                                        famMgr.Set(shapeImgParam, famImgType.Id);
                                        assignOk = true;
                                    }
                                    catch { }
                                }
                            }
                            else if (famMgr.CurrentType != null)
                            {
                                try
                                {
                                    famMgr.Set(shapeImgParam, famImgType.Id);
                                    assignOk = true;
                                }
                                catch { }
                            }
                        }

                        // 2. Gán trực tiếp trên đối tượng RebarShape element bên trong FamilyDoc
                        // (Đối tượng RebarShape element là nơi cốt lõi lưu định nghĩa Shape trong Family Revit)
                        // TUYỆT ĐỐI KHÔNG SỬA s.Name ĐỂ TRÁNH LỖI CORRUPT FAMILY
                        var shapesInFam = new FilteredElementCollector(famDoc)
                            .OfClass(typeof(RebarShape))
                            .Cast<RebarShape>()
                            .ToList();

                        foreach (var s in shapesInFam)
                        {
                            try
                            {
                                var p = s.get_Parameter(BuiltInParameter.REBAR_SHAPE_IMAGE);
                                if (p != null && !p.IsReadOnly && p.Set(famImgType.Id)) assignOk = true;
                            }
                            catch { }

                            try
                            {
                                var p = s.GetParameter(ParameterTypeId.RebarShapeImage);
                                if (p != null && !p.IsReadOnly && p.Set(famImgType.Id)) assignOk = true;
                            }
                            catch { }

                            try
                            {
                                var p = s.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_IMAGE);
                                if (p != null && !p.IsReadOnly && p.Set(famImgType.Id)) assignOk = true;
                            }
                            catch { }

                            try
                            {
                                var p = s.LookupParameter("Shape Image");
                                if (p != null && !p.IsReadOnly && p.Set(famImgType.Id)) assignOk = true;
                            }
                            catch { }

                            // Quét tất cả parameter của s nếu chưa gán được
                            if (!assignOk)
                            {
                                try
                                {
                                    foreach (Parameter param in s.Parameters)
                                    {
                                        if (param == null || param.IsReadOnly) continue;
                                        string pName = param.Definition?.Name?.ToLowerInvariant() ?? "";
                                        if (pName.Contains("shape image") || pName == "image")
                                        {
                                            try
                                            {
                                                if (param.Set(famImgType.Id))
                                                {
                                                    assignOk = true;
                                                    break;
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }

                        // 3. Gán trên OwnerFamily nếu có
                        try
                        {
                            var ownerFam = famDoc.OwnerFamily;
                            if (ownerFam != null)
                            {
                                var p = ownerFam.get_Parameter(BuiltInParameter.REBAR_SHAPE_IMAGE)
                                     ?? ownerFam.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_IMAGE)
                                     ?? ownerFam.LookupParameter("Shape Image");
                                if (p != null && !p.IsReadOnly && p.Set(famImgType.Id)) assignOk = true;
                            }
                        }
                        catch { }
                    }

                    famTx.Commit();
                }

                if (!assignOk)
                {
                    var famParams = new List<string>();
                    try
                    {
                        foreach (FamilyParameter fp in famDoc.FamilyManager.Parameters)
                            famParams.Add(fp.Definition.Name);
                    }
                    catch { }

                    var shapeParams = new List<string>();
                    try
                    {
                        var shapes = new FilteredElementCollector(famDoc).OfClass(typeof(RebarShape)).Cast<RebarShape>().ToList();
                        foreach (var s in shapes)
                            foreach (Parameter p in s.Parameters)
                                if (!shapeParams.Contains(p.Definition.Name)) shapeParams.Add(p.Definition.Name);
                    }
                    catch { }

                    detail = $"Không tìm thấy tham số Shape Image trong Family để gán.\n(Family Types params: {string.Join(", ", famParams.Take(8))}; RebarShape params: {string.Join(", ", shapeParams.Take(8))})";
                    return false;
                }

                // 3. Load Family trở lại Project với tuỳ chọn ghi đè giá trị tham số
                famDoc.LoadFamily(projectDoc, new FamilyLoadOptionsOverride());
                detail = "Family đã được cập nhật Shape Image và Load thành công vào Project";
                return true;
            }
            catch (Exception ex)
            {
                detail = $"Lỗi xử lý trong FamilyDoc: {ex.Message}";
                return false;
            }
            finally
            {
                try
                {
                    famDoc.Close(false);
                }
                catch { }

                // 4. DUY TRÌ TÊN SHAPE TRONG PROJECT DOCUMENT:
                // Nếu sau khi LoadFamily, Revit tự động đổi tên Type về tên cũ của Family (ví dụ Rebar Shape 7 về Rebar Shape 4),
                // ta phục hồi lại đúng tên người dùng mong muốn trực tiếp trong Project Document (hoàn toàn an toàn và hợp lệ 100%).
                if (!string.IsNullOrEmpty(desiredShapeName))
                {
                    try
                    {
                        using (var tx = new Transaction(projectDoc, "Duy trì tên Rebar Shape"))
                        {
                            tx.Start();
                            var curShape = projectDoc.GetElement(shapeId) as RebarShape;
                            if (curShape != null && !curShape.Name.Equals(desiredShapeName, StringComparison.OrdinalIgnoreCase))
                            {
                                curShape.Name = desiredShapeName;
                            }
                            // TUYỆT ĐỐI KHÔNG can thiệp curFam.Name vì Revit không cho đổi tên Rebar Family
                            tx.Commit();
                        }
                    }
                    catch { }
                }

                // 5. LÀM MỚI (RELOAD) DỮ LIỆU BITMAP CHO IMAGETYPE TRONG PROJECT:
                // Ép Revit xoá cache cũ và nạp lại chính xác các pixel mới vào ImageType trong Project
                try
                {
                    if (File.Exists(pngPath))
                    {
                        var curShape = projectDoc.GetElement(shapeId) as RebarShape;
                        ImageType? projImgType = GetExistingShapeImageType(projectDoc, curShape);

                        if (projImgType == null)
                        {
                            string imageTypeName = Path.GetFileNameWithoutExtension(pngPath);
                            projImgType = new FilteredElementCollector(projectDoc)
                                .OfClass(typeof(ImageType))
                                .Cast<ImageType>()
                                .FirstOrDefault(x => string.Equals(x.Name, imageTypeName, StringComparison.OrdinalIgnoreCase));
                        }

                        if (projImgType != null)
                        {
                            using (var tx = new Transaction(projectDoc, "Làm mới Shape Image"))
                            {
                                tx.Start();
                                var opt = new ImageTypeOptions(pngPath, false, ImageTypeSource.Import);
                                projImgType.ReloadFrom(opt);
                                tx.Commit();
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public static string GetSafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}
