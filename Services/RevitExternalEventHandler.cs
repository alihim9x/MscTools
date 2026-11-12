using System;
using Autodesk.Revit.UI;

namespace ElementCoordinateExtractor.Services
{
    /// <summary>
    /// Handler xử lý tương tác bất đồng bộ từ giao diện Modeless (không khóa Revit)
    /// vào luồng chính của Revit API một cách an toàn thông qua ExternalEvent.
    /// </summary>
    public class RevitExternalEventHandler : IExternalEventHandler
    {
        private Action<UIApplication>? _action;
        private ExternalEvent? _externalEvent;

        public static RevitExternalEventHandler Create(out ExternalEvent exEvent)
        {
            var handler = new RevitExternalEventHandler();
            exEvent = ExternalEvent.Create(handler);
            handler._externalEvent = exEvent;
            return handler;
        }

        public void Run(Action<UIApplication> action)
        {
            _action = action;
            _externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            try
            {
                _action?.Invoke(app);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Lỗi Revit API", $"Đã xảy ra lỗi khi thực thi trong Revit:\n{ex.Message}");
            }
            finally
            {
                _action = null;
            }
        }

        public string GetName() => "RevitExternalEventHandler";
    }
}
