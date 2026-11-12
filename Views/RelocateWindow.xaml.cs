using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ElementCoordinateExtractor.Models;
using ElementCoordinateExtractor.Services;

namespace ElementCoordinateExtractor.Views
{
    public partial class RelocateWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly RevitExternalEventHandler _handler;

        private List<ElementId> _currentIds = new();
        private List<RelocateElementItem> _currentItems = new();

        // Tọa độ mốc cơ sở ban đầu (dùng để tính Delta <-> Tọa độ mới)
        private double _baseX;
        private double _baseY;
        private double _baseZ;

        private bool _isUpdatingInputs;

        public RelocateWindow(UIApplication uiApp, RevitExternalEventHandler handler, ICollection<ElementId> selectedIds)
        {
            InitializeComponent();
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));

            _currentIds = selectedIds?.ToList() ?? new List<ElementId>();
            LoadElements(_currentIds);
        }

        private Document? CurrentDocument => _uiApp.ActiveUIDocument?.Document;

        private CoordinateSystemType GetCurrentCoordinateSystem()
        {
            if (RbSurveyPoint?.IsChecked == true)
                return CoordinateSystemType.SurveyPoint;

            if (RbInternalOrigin?.IsChecked == true)
                return CoordinateSystemType.InternalOrigin;

            return CoordinateSystemType.ProjectBasePoint;
        }

        private DisplayUnitType GetCurrentUnitType()
        {
            return CboUnits.SelectedIndex switch
            {
                1 => DisplayUnitType.Meters,
                2 => DisplayUnitType.Feet,
                _ => DisplayUnitType.Millimeters
            };
        }

        private string GetUnitString(DisplayUnitType unitType)
        {
            return unitType switch
            {
                DisplayUnitType.Meters => "m",
                DisplayUnitType.Feet => "ft",
                _ => "mm"
            };
        }

        public void LoadElements(ICollection<ElementId> ids)
        {
            _currentIds = ids?.ToList() ?? new List<ElementId>();

            if (_currentIds.Count == 0)
            {
                _currentItems.Clear();
                DgrElements.ItemsSource = null;
                TxtSelectionSummary.Text = "Chưa chọn đối tượng nào. Vui lòng bấm 'Quét / Chọn lại trong Revit'.";
                ClearInputs();
                return;
            }

            var doc = CurrentDocument;
            if (doc == null) return;

            var coordSystem = GetCurrentCoordinateSystem();
            var unitType = GetCurrentUnitType();
            string unitStr = GetUnitString(unitType);

            // Cập nhật header cột
            if (ColX != null && ColY != null && ColZ != null)
            {
                string prefixX = coordSystem == CoordinateSystemType.SurveyPoint ? "X (East)" : "X";
                string prefixY = coordSystem == CoordinateSystemType.SurveyPoint ? "Y (North)" : "Y";
                string prefixZ = coordSystem == CoordinateSystemType.SurveyPoint ? "Z (Elev)" : "Z";

                ColX.Header = $"{prefixX} [{unitStr}]";
                ColY.Header = $"{prefixY} [{unitStr}]";
                ColZ.Header = $"{prefixZ} [{unitStr}]";
            }

            var service = new RelocateService(doc);
            _currentItems = service.GetElementsInfo(_currentIds, coordSystem, unitType);
            DgrElements.ItemsSource = _currentItems;

            if (_currentItems.Count == 1)
            {
                var item = _currentItems[0];
                TxtSelectionSummary.Text = $"Đang chọn: 1 đối tượng '{item.ElementName}' (ID: {item.ElementId})";
                _baseX = item.X;
                _baseY = item.Y;
                _baseZ = item.Z;
            }
            else
            {
                TxtSelectionSummary.Text = $"Đang chọn: {_currentItems.Count} đối tượng (Áp dụng dịch chuyển chung cho cả nhóm)";
                // Lấy tọa độ trung bình làm mốc
                _baseX = _currentItems.Average(i => i.X);
                _baseY = _currentItems.Average(i => i.Y);
                _baseZ = _currentItems.Average(i => i.Z);
            }

            ResetInputsToBase();
        }

        private void ResetInputsToBase()
        {
            _isUpdatingInputs = true;
            try
            {
                TxtNewX.Text = _baseX.ToString("F3", CultureInfo.InvariantCulture);
                TxtNewY.Text = _baseY.ToString("F3", CultureInfo.InvariantCulture);
                TxtNewZ.Text = _baseZ.ToString("F3", CultureInfo.InvariantCulture);

                TxtDeltaX.Text = "0.000";
                TxtDeltaY.Text = "0.000";
                TxtDeltaZ.Text = "0.000";
            }
            finally
            {
                _isUpdatingInputs = false;
            }
        }

        private void ClearInputs()
        {
            _isUpdatingInputs = true;
            try
            {
                TxtNewX.Text = string.Empty;
                TxtNewY.Text = string.Empty;
                TxtNewZ.Text = string.Empty;
                TxtDeltaX.Text = "0.000";
                TxtDeltaY.Text = "0.000";
                TxtDeltaZ.Text = "0.000";
            }
            finally
            {
                _isUpdatingInputs = false;
            }
        }

        private void CoordinateSystem_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                LoadElements(_currentIds);
            }
        }

        private void CboUnits_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                LoadElements(_currentIds);
            }
        }

        private void TxtNewTarget_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingInputs) return;

            if (double.TryParse(TxtNewX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double newX) &&
                double.TryParse(TxtNewY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double newY) &&
                double.TryParse(TxtNewZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double newZ))
            {
                _isUpdatingInputs = true;
                try
                {
                    double dX = newX - _baseX;
                    double dY = newY - _baseY;
                    double dZ = newZ - _baseZ;

                    TxtDeltaX.Text = dX.ToString("F3", CultureInfo.InvariantCulture);
                    TxtDeltaY.Text = dY.ToString("F3", CultureInfo.InvariantCulture);
                    TxtDeltaZ.Text = dZ.ToString("F3", CultureInfo.InvariantCulture);
                }
                finally
                {
                    _isUpdatingInputs = false;
                }
            }
        }

        private void TxtDelta_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingInputs) return;

            if (double.TryParse(TxtDeltaX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dX) &&
                double.TryParse(TxtDeltaY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dY) &&
                double.TryParse(TxtDeltaZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dZ))
            {
                _isUpdatingInputs = true;
                try
                {
                    double newX = _baseX + dX;
                    double newY = _baseY + dY;
                    double newZ = _baseZ + dZ;

                    TxtNewX.Text = newX.ToString("F3", CultureInfo.InvariantCulture);
                    TxtNewY.Text = newY.ToString("F3", CultureInfo.InvariantCulture);
                    TxtNewZ.Text = newZ.ToString("F3", CultureInfo.InvariantCulture);
                }
                finally
                {
                    _isUpdatingInputs = false;
                }
            }
        }

        private void BtnPickInRevit_Click(object sender, RoutedEventArgs e)
        {
            // Thực thi quét chọn trong Revit an toàn qua ExternalEvent (không khóa giao diện)
            _handler.Run(app =>
            {
                try
                {
                    var uidoc = app.ActiveUIDocument;
                    if (uidoc == null) return;

                    var refs = uidoc.Selection.PickObjects(
                        ObjectType.Element, 
                        "Quét chọn một hoặc nhiều đối tượng trong mô hình Revit (nhấn Finish trên thanh Options Bar):");

                    if (refs != null && refs.Count > 0)
                    {
                        var pickedIds = refs.Select(r => r.ElementId).ToList();
                        uidoc.Selection.SetElementIds(pickedIds);
                        Dispatcher.Invoke(() => LoadElements(pickedIds));
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // Người dùng nhấn ESC, giữ nguyên trạng thái
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Lỗi", $"Lỗi khi chọn đối tượng: {ex.Message}");
                }
            });
        }

        private bool TryGetDeltaValues(out double dX, out double dY, out double dZ)
        {
            dX = 0; dY = 0; dZ = 0;

            if (!double.TryParse(TxtDeltaX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out dX) ||
                !double.TryParse(TxtDeltaY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out dY) ||
                !double.TryParse(TxtDeltaZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out dZ))
            {
                MessageBox.Show("Giá trị tọa độ hoặc độ dời nhập vào không hợp lệ. Vui lòng kiểm tra lại định dạng số.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void BtnMove_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIds.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất một đối tượng trước khi di chuyển.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryGetDeltaValues(out double dX, out double dY, out double dZ)) return;

            var coordSystem = GetCurrentCoordinateSystem();
            var unitType = GetCurrentUnitType();
            bool unpin = ChkUnpin.IsChecked == true;

            _handler.Run(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var service = new RelocateService(doc);
                XYZ disp = service.CalculateInternalDisplacement(dX, dY, dZ, coordSystem, unitType);

                if (disp.IsAlmostEqualTo(XYZ.Zero))
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Khoảng dịch chuyển bằng 0, không có thay đổi vị trí.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    return;
                }

                var (count, error) = service.MoveElements(_currentIds, disp, unpin);

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        MessageBox.Show($"Không thể di chuyển đối tượng: {error}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        MessageBox.Show($"Đã di chuyển thành công {count} đối tượng tới vị trí mới!", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadElements(_currentIds);
                        TxtStatus.Text = $"Đã di chuyển {count} đối tượng thành công lúc {DateTime.Now:HH:mm:ss}";
                    }
                });
            });
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIds.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất một đối tượng.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryGetDeltaValues(out double dX, out double dY, out double dZ)) return;

            var coordSystem = GetCurrentCoordinateSystem();
            var unitType = GetCurrentUnitType();

            _handler.Run(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var service = new RelocateService(doc);
                XYZ disp = service.CalculateInternalDisplacement(dX, dY, dZ, coordSystem, unitType);

                if (disp.IsAlmostEqualTo(XYZ.Zero))
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Khoảng dịch chuyển bằng 0, không thể sao chép trùng vị trí.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                    return;
                }

                var (count, error) = service.CopyElements(_currentIds, disp);

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        MessageBox.Show($"Không thể sao chép đối tượng: {error}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        MessageBox.Show($"Đã tạo bản sao thành công cho {count} đối tượng tại tọa độ mới!", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
                        TxtStatus.Text = $"Đã sao chép {count} đối tượng thành công lúc {DateTime.Now:HH:mm:ss}";
                    }
                });
            });
        }

        private void DgrElements_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgrElements.SelectedItem is RelocateElementItem item)
            {
                _handler.Run(app =>
                {
                    var uidoc = app.ActiveUIDocument;
                    if (uidoc == null) return;

                    try
                    {
                        uidoc.Selection.SetElementIds(new List<ElementId> { new ElementId(item.ElementId) });
                        uidoc.ShowElements(new ElementId(item.ElementId));
                    }
                    catch
                    {
                        // Ignore zoom error
                    }
                });
            }
        }
    }
}
