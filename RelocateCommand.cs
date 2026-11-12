using System;
using System.Collections.Generic;
using System.Linq;
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
    public class RelocateCommand : IExternalCommand
    {
        // Quản lý cửa sổ Modeless để không mở trùng lặp
        private static RelocateWindow? _currentWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;

            if (uidoc == null || uidoc.Document == null)
            {
                TaskDialog.Show("Thông báo", "Vui lòng mở một dự án Revit trước khi chạy Add-in.");
                return Result.Cancelled;
            }

            // Nếu cửa sổ đang mở, cập nhật các đối tượng đang chọn và đưa lên phía trước
            if (_currentWindow != null && _currentWindow.IsLoaded)
            {
                var currentSelection = uidoc.Selection.GetElementIds();
                if (currentSelection.Count > 0)
                {
                    _currentWindow.LoadElements(currentSelection);
                }
                _currentWindow.Activate();
                return Result.Succeeded;
            }

            try
            {
                var selectedIds = uidoc.Selection.GetElementIds();
                var handler = RevitExternalEventHandler.Create(out _);

                _currentWindow = new RelocateWindow(uiapp, handler, selectedIds ?? new List<ElementId>());
                
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
