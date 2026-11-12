using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ElementCoordinateExtractor.Models;
using ElementCoordinateExtractor.Services;
using ElementCoordinateExtractor.Views;

namespace ElementCoordinateExtractor
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SyncHistoryCommand : IExternalCommand
    {
        private static SyncTrackerWindow? _currentWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;

            if (uidoc == null || uidoc.Document == null)
            {
                TaskDialog.Show("Thông báo", "Vui lòng mở một dự án Revit trước khi xem báo cáo đồng bộ.");
                return Result.Cancelled;
            }

            var tracker = SyncTrackerService.Instance;

            // Nếu trong phiên này chưa từng diễn ra sự kiện Synchronize nào
            if (!tracker.HasSyncedAtLeastOnce && (tracker.LastSyncItems == null || tracker.LastSyncItems.Count == 0))
            {
                var td = new TaskDialog("Chưa có lượt Đồng bộ nào")
                {
                    MainInstruction = "Chưa có lượt Đồng bộ (Synchronize with Central) nào được hoàn thành trong phiên làm việc này.",
                    MainContent = "Add-in sẽ tự động theo dõi và ghi nhận chi tiết ngay khi bạn nhấn Synchronize with Central.\n\nBạn có muốn mở thư mục chứa các file log CSV đã lưu trước đó không?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                };

                if (td.Show() == TaskDialogResult.Yes)
                {
                    SyncLogService.OpenLogFolder(uidoc.Document);
                }

                return Result.Succeeded;
            }

            var itemsToShow = tracker.LastSyncItems ?? new List<SyncChangeItem>();

            if (_currentWindow != null && _currentWindow.IsLoaded)
            {
                _currentWindow.LoadData(itemsToShow);
                _currentWindow.Activate();
                return Result.Succeeded;
            }

            try
            {
                var handler = RevitExternalEventHandler.Create(out _);
                _currentWindow = new SyncTrackerWindow(
                    uiapp, 
                    handler, 
                    itemsToShow, 
                    uidoc.Document.Title, 
                    tracker.LastSyncTime == DateTime.MinValue ? DateTime.Now : tracker.LastSyncTime);

                var helper = new WindowInteropHelper(_currentWindow)
                {
                    Owner = uiapp.MainWindowHandle
                };

                _currentWindow.Closed += (s, e) => _currentWindow = null;
                _currentWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
