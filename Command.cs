using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ElementCoordinateExtractor.Services;
using ElementCoordinateExtractor.Views;

namespace ElementCoordinateExtractor
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        // Quản lý cửa sổ Modeless để không mở trùng lặp
        private static CoordinateExtractorWindow? _currentWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;

            if (uidoc == null || uidoc.Document == null)
            {
                TaskDialog.Show("Thông báo", "Vui lòng mở một dự án Revit trước khi chạy Add-in.");
                return Result.Cancelled;
            }

            // Nếu cửa sổ đang mở, chỉ cần đưa lên phía trước (Activate)
            if (_currentWindow != null && _currentWindow.IsLoaded)
            {
                _currentWindow.Activate();
                return Result.Succeeded;
            }

            try
            {
                var handler = RevitExternalEventHandler.Create(out _);
                _currentWindow = new CoordinateExtractorWindow(uiapp, handler);
                
                // Gắn Owner vào cửa sổ Revit chính để cửa sổ luôn nổi trên Revit mà KHÔNG khóa Revit
                var helper = new WindowInteropHelper(_currentWindow)
                {
                    Owner = uiapp.MainWindowHandle
                };

                _currentWindow.Closed += (s, e) => _currentWindow = null;
                
                // Mở dạng Modeless (Show thay vì ShowDialog)
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
