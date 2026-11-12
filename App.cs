using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Interop;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using ElementCoordinateExtractor.Services;
using ElementCoordinateExtractor.Views;

namespace ElementCoordinateExtractor
{
    public class App : IExternalApplication
    {
        private static UIControlledApplication? _controlledApp;

        public Result OnStartup(UIControlledApplication application)
        {
            _controlledApp = application;

            try
            {
                // 1. Đăng ký sự kiện theo dõi quá trình Synchronize With Central
                application.ControlledApplication.DocumentSynchronizingWithCentral += OnDocumentSynchronizingWithCentral;
                application.ControlledApplication.DocumentChanged += OnDocumentChanged;
                application.ControlledApplication.DocumentSynchronizedWithCentral += OnDocumentSynchronizedWithCentral;

                // 2. Thiết lập thanh Ribbon
                string tabName = "MscTools";

                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch
                {
                    // Tab đã tồn tại
                }

                string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;

                // ===== PANEL 1: COORDINATES =====
                var coordPanel = GetOrCreateRibbonPanel(application, tabName, "Coordinates");

                // Nút 1: Lấy Tọa Độ Family
                var button1Data = new PushButtonData(
                    "Btn_ElementCoordinates",
                    "Lấy Tọa Độ\nFamily",
                    thisAssemblyPath,
                    "ElementCoordinateExtractor.Command")
                {
                    ToolTip = "Trích xuất tọa độ hàng loạt element theo Family và gán vào parameters.",
                    LongDescription = "Công cụ cho phép chọn một Family trong mô hình, tự động lấy tọa độ (X, Y, Z, Rotation) của toàn bộ các Instance thuộc Family đó theo hệ tọa độ Project Base Point, Survey Point hoặc Internal Origin, và cho phép gán trực tiếp vào 3 parameters do người dùng chọn."
                };

                // Nút 2: Di Chuyển Tọa Độ Đối Tượng
                var button2Data = new PushButtonData(
                    "Btn_RelocateElements",
                    "Di Chuyển\nTọa Độ",
                    thisAssemblyPath,
                    "ElementCoordinateExtractor.RelocateCommand")
                {
                    ToolTip = "Xem thông tin tọa độ đối tượng đang chọn và di chuyển tới tọa độ mới.",
                    LongDescription = "Cho phép chọn 1 hoặc nhiều đối tượng trong Revit, xem bảng thông tin tọa độ hiện tại và nhập tọa độ tuyệt đối mới hoặc khoảng dời Delta (X, Y, Z) để di chuyển hoặc tạo bản sao của đối tượng."
                };

                coordPanel.AddItem(button1Data);
                coordPanel.AddItem(button2Data);

                // ===== PANEL 2: REBAR =====
                var rebarPanel = GetOrCreateRibbonPanel(application, tabName, "Rebar");

                // Nút 3: Tạo Shape Image Cho Thép
                var buttonRebarData = new PushButtonData(
                    "Btn_RebarShapeImage",
                    "Tạo Shape\nImage",
                    thisAssemblyPath,
                    "ElementCoordinateExtractor.RebarShapeImageCommand")
                {
                    ToolTip = "Tự động tạo hình ảnh sơ đồ uốn thanh thép (Shape Image) cho bảng thống kê Schedule.",
                    LongDescription = "Tự động trích xuất hình học 3D của thanh thép được chọn (kể cả thép Custom không có trong BS 8666 hoặc Freeform Rebar), vẽ sơ đồ uốn minh họa với các đoạn A, B, C, D... theo tỉ lệ chuẩn và gán trực tiếp vào tham số Shape Image của RebarShape."
                };

                rebarPanel.AddItem(buttonRebarData);

                // ===== PANEL 3: TRACKER =====
                var trackerPanel = GetOrCreateRibbonPanel(application, tabName, "Tracker");

                // Nút 4: Giám Sát Thay Đổi Khi Đồng Bộ (Sync Tracker)
                var button3Data = new PushButtonData(
                    "Btn_SyncHistory",
                    "Lịch Sử\nĐồng Bộ",
                    thisAssemblyPath,
                    "ElementCoordinateExtractor.SyncHistoryCommand")
                {
                    ToolTip = "Xem báo cáo chi tiết các đối tượng Thêm mới, Chỉnh sửa, Đã xóa sau mỗi lần Synchronize with Central.",
                    LongDescription = "Tự động thống kê các thay đổi của mô hình sau mỗi lần đồng bộ, phân loại thay đổi do bạn hay đồng nghiệp thực hiện, tô màu trực quan trên 3D và tự động lưu file nhật ký CSV."
                };

                trackerPanel.AddItem(button3Data);

                return Result.Succeeded;
            }
            catch (Exception)
            {
                return Result.Failed;
            }
        }

        private static RibbonPanel GetOrCreateRibbonPanel(UIControlledApplication app, string tabName, string panelName)
        {
            var panels = app.GetRibbonPanels(tabName);
            foreach (var p in panels)
            {
                if (string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
            return app.CreateRibbonPanel(tabName, panelName);
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                application.ControlledApplication.DocumentSynchronizingWithCentral -= OnDocumentSynchronizingWithCentral;
                application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
                application.ControlledApplication.DocumentSynchronizedWithCentral -= OnDocumentSynchronizedWithCentral;
            }
            catch { }

            return Result.Succeeded;
        }

        private void OnDocumentSynchronizingWithCentral(object? sender, DocumentSynchronizingWithCentralEventArgs e)
        {
            // Bắt đầu đợt Sync -> Khởi tạo bộ đếm
            SyncTrackerService.Instance.StartSyncSession();
        }

        private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
        {
            // Ghi nhận các element ID thay đổi liên tục trong suốt phiên làm việc (cả trước và trong lúc Sync)
            SyncTrackerService.Instance.RecordChanges(
                e.GetAddedElementIds(),
                e.GetModifiedElementIds(),
                e.GetDeletedElementIds());
        }

        private void OnDocumentSynchronizedWithCentral(object? sender, DocumentSynchronizedWithCentralEventArgs e)
        {
            var doc = e.Document;
            if (doc == null) return;

            try
            {
                // Tổng hợp dữ liệu và tự động ghi log file CSV
                var items = SyncTrackerService.Instance.ProcessSyncCompletion(doc);

                // Nếu có thay đổi và người dùng bật tính năng tự động hiện popup
                if (items.Count > 0 && SyncTrackerService.Instance.AutoPopupEnabled)
                {
                    var uiapp = new UIApplication(doc.Application);
                    var handler = RevitExternalEventHandler.Create(out _);

                    var window = new SyncTrackerWindow(
                        uiapp,
                        handler,
                        items,
                        doc.Title,
                        SyncTrackerService.Instance.LastSyncTime);

                    var helper = new WindowInteropHelper(window)
                    {
                        Owner = Process.GetCurrentProcess().MainWindowHandle
                    };

                    window.Show();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Lỗi sau khi Sync: {ex.Message}");
            }
        }
    }
}
