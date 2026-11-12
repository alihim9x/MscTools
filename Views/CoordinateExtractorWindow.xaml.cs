using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ElementCoordinateExtractor.Models;
using ElementCoordinateExtractor.Services;
using Microsoft.Win32;
using ComboBox = System.Windows.Controls.ComboBox;

namespace ElementCoordinateExtractor.Views
{
    public partial class CoordinateExtractorWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly RevitExternalEventHandler _handler;

        private List<FamilyItem> _allFamilies = new();
        private List<ElementCoordinateItem> _currentItems = new();

        public CoordinateExtractorWindow(UIApplication uiApp, RevitExternalEventHandler handler)
        {
            InitializeComponent();
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));

            LoadFamilies();
        }

        private Document? CurrentDocument => _uiApp.ActiveUIDocument?.Document;

        private void LoadFamilies()
        {
            var doc = CurrentDocument;
            if (doc == null) return;

            try
            {
                var service = new CoordinateService(doc);
                _allFamilies = service.GetFamiliesWithInstances();
                FilterFamilies();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi tải danh sách Family: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FilterFamilies()
        {
            string keyword = TxtSearchFamily.Text?.Trim().ToLower() ?? string.Empty;
            var filtered = string.IsNullOrEmpty(keyword)
                ? _allFamilies
                : _allFamilies.Where(f => f.FamilyName.ToLower().Contains(keyword) || f.CategoryName.ToLower().Contains(keyword)).ToList();

            CboFamilies.ItemsSource = filtered;
            if (filtered.Count > 0)
            {
                CboFamilies.SelectedIndex = 0;
            }
            else
            {
                _currentItems.Clear();
                DgrResults.ItemsSource = null;
                ResetParameterDropdowns();
                TxtStatus.Text = "Không tìm thấy Family phù hợp.";
            }
        }

        private void ResetParameterDropdowns()
        {
            var emptyList = new List<string> { "-- Không chọn --" };
            CboParamX.ItemsSource = emptyList;
            CboParamX.SelectedIndex = 0;
            CboParamY.ItemsSource = emptyList;
            CboParamY.SelectedIndex = 0;
            CboParamZ.ItemsSource = emptyList;
            CboParamZ.SelectedIndex = 0;
        }

        private void LoadFamilyParameters(ElementId familyId)
        {
            var doc = CurrentDocument;
            if (doc == null) return;

            try
            {
                var service = new CoordinateService(doc);
                var paramsList = service.GetAvailableInstanceParameters(familyId);

                CboParamX.ItemsSource = paramsList;
                CboParamY.ItemsSource = paramsList;
                CboParamZ.ItemsSource = paramsList;

                // Tự động tìm thử các parameter có tên tương tự (ví dụ ToaDo_X, X, North, East...)
                AutoSelectMatchingParameter(CboParamX, paramsList, new[] { "x", "toado_x", "east", "eastwest", "toadox" });
                AutoSelectMatchingParameter(CboParamY, paramsList, new[] { "y", "toado_y", "north", "northsouth", "toadoy" });
                AutoSelectMatchingParameter(CboParamZ, paramsList, new[] { "z", "toado_z", "elevation", "caodo", "toadoz" });
            }
            catch
            {
                ResetParameterDropdowns();
            }
        }

        private static void AutoSelectMatchingParameter(ComboBox cbo, List<string> items, string[] candidateKeywords)
        {
            cbo.SelectedIndex = 0;
            foreach (var kw in candidateKeywords)
            {
                for (int i = 1; i < items.Count; i++)
                {
                    if (items[i].Equals(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        cbo.SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        private void TxtSearchFamily_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterFamilies();
        }

        private void CboFamilies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboFamilies.SelectedItem is FamilyItem selectedFamily)
            {
                LoadFamilyParameters(selectedFamily.FamilyId);
                RefreshCoordinates();
            }
            else
            {
                ResetParameterDropdowns();
                _currentItems.Clear();
                DgrResults.ItemsSource = null;
            }
        }

        private void CoordinateSystem_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                RefreshCoordinates();
            }
        }

        private void CboUnits_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                RefreshCoordinates();
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _handler.Run(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var service = new CoordinateService(doc);
                var families = service.GetFamiliesWithInstances();

                Dispatcher.Invoke(() =>
                {
                    _allFamilies = families;
                    FilterFamilies();
                    TxtStatus.Text = $"Đã làm mới dữ liệu lúc {DateTime.Now:HH:mm:ss}.";
                });
            });
        }

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

        private void RefreshCoordinates()
        {
            var doc = CurrentDocument;
            if (doc == null) return;

            if (CboFamilies.SelectedItem is not FamilyItem selectedFamily)
            {
                _currentItems.Clear();
                DgrResults.ItemsSource = null;
                TxtStatus.Text = "Chưa chọn Family.";
                return;
            }

            var coordSystem = GetCurrentCoordinateSystem();
            var unitType = GetCurrentUnitType();
            string unitStr = GetUnitString(unitType);

            // Cập nhật tiêu đề cột để người dùng nắm rõ hệ tọa độ và đơn vị
            if (ColX != null && ColY != null && ColZ != null)
            {
                string prefixX = coordSystem == CoordinateSystemType.SurveyPoint ? "X (East)" : "X";
                string prefixY = coordSystem == CoordinateSystemType.SurveyPoint ? "Y (North)" : "Y";
                string prefixZ = coordSystem == CoordinateSystemType.SurveyPoint ? "Z (Elev)" : "Z";

                ColX.Header = $"{prefixX} [{unitStr}]";
                ColY.Header = $"{prefixY} [{unitStr}]";
                ColZ.Header = $"{prefixZ} [{unitStr}]";
            }

            try
            {
                var service = new CoordinateService(doc);
                _currentItems = service.ExtractCoordinates(selectedFamily.FamilyId, coordSystem, unitType);
                DgrResults.ItemsSource = _currentItems;

                string sysName = coordSystem switch
                {
                    CoordinateSystemType.SurveyPoint => "Survey Point (Shared Coordinates)",
                    CoordinateSystemType.InternalOrigin => "Internal Origin",
                    _ => "Project Base Point"
                };

                TxtStatus.Text = $"Đã tải {_currentItems.Count} instance của family '{selectedFamily.FamilyName}' | Hệ tọa độ: {sysName} [{unitStr}].";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi lấy tọa độ: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnWriteParameters_Click(object sender, RoutedEventArgs e)
        {
            if (CboFamilies.SelectedItem is not FamilyItem selectedFamily)
            {
                MessageBox.Show("Vui lòng chọn một Family trước khi ghi tọa độ.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string paramX = CboParamX.SelectedItem as string ?? string.Empty;
            string paramY = CboParamY.SelectedItem as string ?? string.Empty;
            string paramZ = CboParamZ.SelectedItem as string ?? string.Empty;

            bool hasX = !string.IsNullOrEmpty(paramX) && paramX != "-- Không chọn --";
            bool hasY = !string.IsNullOrEmpty(paramY) && paramY != "-- Không chọn --";
            bool hasZ = !string.IsNullOrEmpty(paramZ) && paramZ != "-- Không chọn --";

            if (!hasX && !hasY && !hasZ)
            {
                MessageBox.Show("Vui lòng chọn ít nhất một parameter (X, Y hoặc Z) để gán giá trị.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var coordSystem = GetCurrentCoordinateSystem();
            var unitType = GetCurrentUnitType();
            string sysName = coordSystem switch
            {
                CoordinateSystemType.SurveyPoint => "Survey Point",
                CoordinateSystemType.InternalOrigin => "Internal Origin",
                _ => "Project Base Point"
            };
            string unitStr = GetUnitString(unitType);

            var confirmResult = MessageBox.Show(
                $"Bạn có chắc chắn muốn ghi tọa độ vào parameters cho {_currentItems.Count} đối tượng của family '{selectedFamily.FamilyName}'?\n\n" +
                $"• Hệ tọa độ: {sysName}\n" +
                $"• Đơn vị: {unitStr}\n" +
                $"• Parameter X: {(hasX ? paramX : "(Không ghi)")}\n" +
                $"• Parameter Y: {(hasY ? paramY : "(Không ghi)")}\n" +
                $"• Parameter Z: {(hasZ ? paramZ : "(Không ghi)")}",
                "Xác nhận ghi tọa độ",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes) return;

            // Thực thi ghi tham số thông qua ExternalEvent để an toàn luồng Revit API
            _handler.Run(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var service = new CoordinateService(doc);
                var (updated, failed, msg) = service.WriteCoordinatesToParameters(
                    selectedFamily.FamilyId,
                    coordSystem,
                    unitType,
                    paramX,
                    paramY,
                    paramZ);

                Dispatcher.Invoke(() =>
                {
                    if (failed > 0)
                    {
                        MessageBox.Show($"{msg}\n(Có {failed} đối tượng thất bại do parameter bị ReadOnly hoặc sai kiểu dữ liệu).", "Kết quả", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show(msg, "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    TxtStatus.Text = $"{msg} | {DateTime.Now:HH:mm:ss}";
                });
            });
        }

        private void DgrResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectAndShowInRevit();
        }

        private void BtnShowSelected_Click(object sender, RoutedEventArgs e)
        {
            SelectAndShowInRevit();
        }

        private void SelectAndShowInRevit()
        {
            var selectedItems = DgrResults.SelectedItems.Cast<ElementCoordinateItem>().ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất một dòng trong bảng.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var elementIds = selectedItems.Select(item => new ElementId(item.ElementId)).ToList();

            // Thực thi chọn và zoom qua ExternalEvent
            _handler.Run(app =>
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;

                try
                {
                    uidoc.Selection.SetElementIds(elementIds);
                    uidoc.ShowElements(elementIds);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Lỗi", $"Không thể zoom vào đối tượng: {ex.Message}");
                }
            });
        }

        private void BtnCopyClipboard_Click(object sender, RoutedEventArgs e)
        {
            var itemsToCopy = DgrResults.SelectedItems.Count > 0
                ? DgrResults.SelectedItems.Cast<ElementCoordinateItem>().ToList()
                : _currentItems;

            if (itemsToCopy.Count == 0)
            {
                MessageBox.Show("Không có dữ liệu để sao chép.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Element ID\tFamily Name\tType Name\tCategory\tMark\tLevel\tX\tY\tZ\tRotation\tGeometry Type");

            foreach (var item in itemsToCopy)
            {
                sb.AppendLine($"{item.ElementId}\t{item.FamilyName}\t{item.TypeName}\t{item.CategoryName}\t{item.Mark}\t{item.LevelName}\t{item.FormattedX}\t{item.FormattedY}\t{item.FormattedZ}\t{item.FormattedRotation}\t{item.GeometryType}");
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show($"Đã sao chép {itemsToCopy.Count} dòng vào Clipboard! Bạn có thể Paste trực tiếp vào Excel.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentItems.Count == 0)
            {
                MessageBox.Show("Không có dữ liệu để xuất file.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var coordSystem = GetCurrentCoordinateSystem();
            var unitType = GetCurrentUnitType();
            string coordSystemName = coordSystem switch
            {
                CoordinateSystemType.SurveyPoint => "SurveyPoint",
                CoordinateSystemType.InternalOrigin => "InternalOrigin",
                _ => "ProjectBasePoint"
            };
            string unitName = GetUnitString(unitType);

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                Title = "Lưu file tọa độ Element",
                FileName = $"Coordinates_{CboFamilies.SelectedItem as FamilyItem}_{coordSystemName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    CoordinateService.ExportToCsv(_currentItems, saveFileDialog.FileName, coordSystemName, unitName);
                    MessageBox.Show($"Đã xuất file thành công:\n{saveFileDialog.FileName}", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi xuất file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DgrResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int selectedCount = DgrResults.SelectedItems.Count;
            if (selectedCount > 0)
            {
                TxtStatus.Text = $"Đang chọn {selectedCount} / {_currentItems.Count} elements.";
            }
            else if (_currentItems.Count > 0)
            {
                TxtStatus.Text = $"Hiển thị {_currentItems.Count} elements.";
            }
        }
    }
}
