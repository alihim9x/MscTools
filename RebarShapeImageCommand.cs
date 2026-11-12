using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ElementCoordinateExtractor.Models;
using ElementCoordinateExtractor.Services;
using ElementCoordinateExtractor.Views;

namespace ElementCoordinateExtractor
{
    public class RebarSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Rebar || elem is RebarInSystem;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RebarShapeImageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                TaskDialog.Show("Thông báo", "Vui lòng mở một mô hình dự án Revit.");
                return Result.Cancelled;
            }

            var doc = uidoc.Document;

            // 1. Thu thập các thanh thép được chọn
            var rebars = new List<Element>();
            var selectedIds = uidoc.Selection.GetElementIds();

            if (selectedIds != null && selectedIds.Count > 0)
            {
                foreach (var id in selectedIds)
                {
                    var elem = doc.GetElement(id);
                    if (elem is Rebar || elem is RebarInSystem)
                    {
                        rebars.Add(elem);
                    }
                }
            }

            // Nếu người dùng chưa chọn thanh thép nào trước đó -> Cho phép click chọn trên màn hình
            if (rebars.Count == 0)
            {
                try
                {
                    var pickedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new RebarSelectionFilter(),
                        "Chọn một hoặc nhiều thanh thép (Rebar) để tạo Shape Image (nhấn Finish trên thanh công cụ sau khi chọn xong)...");

                    foreach (var r in pickedRefs)
                    {
                        var elem = doc.GetElement(r.ElementId);
                        if (elem != null) rebars.Add(elem);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (rebars.Count == 0)
            {
                TaskDialog.Show("Thông báo", "Không có thanh thép nào được chọn.");
                return Result.Cancelled;
            }

            // 2. Nhóm các thanh thép theo Shape (tránh xử lý trùng lặp một Shape nhiều lần)
            var uniqueShapes = new Dictionary<string, Element>();
            foreach (var r in rebars)
            {
                var (_, shapeName, _) = RebarShapeImageService.CheckIfShapeHasImage(doc, r);
                if (!uniqueShapes.ContainsKey(shapeName))
                {
                    uniqueShapes[shapeName] = r;
                }
            }

            // KIỂM TRA XEM CÁC SHAPE ĐÃ CÓ SHAPE IMAGE HAY CHƯA
            var shapesWithImage = new List<string>();
            var shapesWithoutImage = new List<string>();

            foreach (var kvp in uniqueShapes)
            {
                var (hasImage, shapeName, _) = RebarShapeImageService.CheckIfShapeHasImage(doc, kvp.Value);
                if (hasImage)
                {
                    shapesWithImage.Add(shapeName);
                }
                else
                {
                    shapesWithoutImage.Add(shapeName);
                }
            }

            // Nếu có ít nhất 1 Shape đã có ảnh -> Hiện hộp thoại thông báo hỏi người dùng: Ghi đè hay Bỏ qua
            if (shapesWithImage.Count > 0)
            {
                var td = new TaskDialog("Kiểm Tra Shape Image")
                {
                    MainInstruction = shapesWithImage.Count == uniqueShapes.Count
                        ? $"Tất cả {shapesWithImage.Count} Shape được chọn ĐÃ CÓ hình ảnh Shape Image."
                        : $"Phát hiện {shapesWithImage.Count}/{uniqueShapes.Count} Shape được chọn ĐÃ CÓ hình ảnh Shape Image.",
                    MainContent = $"• Đã có ảnh ({shapesWithImage.Count}): {string.Join(", ", shapesWithImage)}\n" +
                                  (shapesWithoutImage.Count > 0 ? $"• Chưa có ảnh ({shapesWithoutImage.Count}): {string.Join(", ", shapesWithoutImage)}\n\n" : "\n") +
                                  "Bạn muốn GHI ĐÈ hình ảnh mới hay BỎ QUA các Shape đã có ảnh?",
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };

                td.AddCommandLink(
                    TaskDialogCommandLinkId.CommandLink1,
                    "Ghi đè tất cả (Xem trước & Ghi đè ảnh mới)",
                    "Mở cửa sổ xem trước để tinh chỉnh và cập nhật lại hình ảnh mới cho các Shape.");

                if (shapesWithoutImage.Count > 0)
                {
                    td.AddCommandLink(
                        TaskDialogCommandLinkId.CommandLink2,
                        $"Bỏ qua các Shape đã có ảnh (Chỉ tạo cho {shapesWithoutImage.Count} Shape còn lại)",
                        $"Bỏ qua {shapesWithImage.Count} Shape đã có ảnh, chỉ mở xem trước cho các Shape chưa có ảnh.");
                }

                var tdRes = td.Show();

                if (tdRes == TaskDialogResult.Cancel)
                {
                    return Result.Cancelled;
                }
                else if (tdRes == TaskDialogResult.CommandLink2)
                {
                    // Lọc chỉ giữ lại các Shape chưa có ảnh
                    uniqueShapes = uniqueShapes
                        .Where(kvp => shapesWithoutImage.Contains(kvp.Key))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                }
            }

            if (uniqueShapes.Count == 0)
            {
                TaskDialog.Show("Thông báo", "Tất cả các Shape được chọn đều đã có hình ảnh. Không có Shape nào cần tạo mới.");
                return Result.Cancelled;
            }

            // 3. Trích xuất hình học 2D và chuẩn bị danh sách xem trước
            var previewItems = new List<RebarShapePreviewItem>();
            foreach (var kvp in uniqueShapes)
            {
                string shapeName = kvp.Key;
                Element rebarElem = kvp.Value;

                var (hasImage, _, shape) = RebarShapeImageService.CheckIfShapeHasImage(doc, rebarElem);
                var shape2D = RebarGeometryService.Extract2DShape(rebarElem, shapeName, shape);

                if (shape2D != null && shape2D.Legs.Count > 0)
                {
                    var item = new RebarShapePreviewItem(shapeName, rebarElem, shape, shape2D, hasImage);
                    previewItems.Add(item);
                }
            }

            if (previewItems.Count == 0)
            {
                TaskDialog.Show("Thông báo", "Không thể trích xuất hình học cho các thanh thép đã chọn.");
                return Result.Cancelled;
            }

            // 4. Mở cửa sổ Xem Trước & Tinh Chỉnh Shape Image (WPF Window)
            try
            {
                var previewWindow = new RebarShapePreviewWindow(doc, previewItems);
                new WindowInteropHelper(previewWindow).Owner = uiapp.MainWindowHandle;
                previewWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Lỗi hiển thị", $"Không thể mở cửa sổ xem trước: {ex.Message}");
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }
}
