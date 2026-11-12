using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ElementCoordinateExtractor.Models;
using ElementCoordinateExtractor.Services;

namespace ElementCoordinateExtractor.Views
{
    public partial class RebarShapePreviewWindow : Window
    {
        private readonly Document _doc;
        private readonly ObservableCollection<RebarShapePreviewItem> _items;
        private bool _isUpdatingUi = false;

        public RebarShapePreviewItem? CurrentItem => LstShapes.SelectedItem as RebarShapePreviewItem;

        public RebarShapePreviewWindow(Document doc, List<RebarShapePreviewItem> items)
        {
            InitializeComponent();

            _doc = doc;
            _items = new ObservableCollection<RebarShapePreviewItem>(items);

            TxtTotalShapesCount.Text = $"{_items.Count} Shape";
            LstShapes.ItemsSource = _items;

            // Đăng ký sự kiện làm mới ảnh xem trước khi người dùng kéo thanh trượt
            foreach (var item in _items)
            {
                var capturedItem = item;
                capturedItem.PreviewNeedsRefresh += (s, e) => RefreshPreviewImage(capturedItem);

                // Tạo ảnh xem trước ban đầu
                RefreshPreviewImage(capturedItem);
            }

            if (_items.Count > 0)
            {
                LstShapes.SelectedIndex = 0;
            }
            else
            {
                TxtNoPreviewPlaceholder.Visibility = System.Windows.Visibility.Visible;
                BtnAssignCurrent.IsEnabled = false;
                BtnAssignAll.IsEnabled = false;
            }
        }

        private void RefreshPreviewImage(RebarShapePreviewItem item)
        {
            try
            {
                var rtb = RebarImageRenderer.RenderShapeBitmap(
                    item.Shape2D, 
                    item.GetLegFactors(),
                    globalStretch: 1.0,
                    flipX: item.IsFlippedX,
                    flipY: item.IsFlippedY,
                    dimOffset: item.DimOffset,
                    horizontalLegIndex: item.HorizontalLegIndex);
                item.PreviewImage = rtb;

                if (CurrentItem == item)
                {
                    ImgPreview.Source = rtb;
                    TxtNoPreviewPlaceholder.Visibility = System.Windows.Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                TxtStatusMessage.Text = $"⚠️ Lỗi kết xuất xem trước: {ex.Message}";
            }
        }

        private void LstShapes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = CurrentItem;
            if (item == null)
            {
                ImgPreview.Source = null;
                TxtNoPreviewPlaceholder.Visibility = System.Windows.Visibility.Visible;
                ItemsLegs.ItemsSource = null;
                TxtActiveShapeBadge.Text = "Chưa chọn Shape";
                BtnAssignCurrent.IsEnabled = false;
                return;
            }

            BtnAssignCurrent.IsEnabled = true;
            TxtActiveShapeBadge.Text = $"Shape: {item.ShapeName}";
            ChkSyncEqual.IsChecked = item.SyncEqualLegs;
            ItemsLegs.ItemsSource = item.Legs;

            _isUpdatingUi = true;
            try
            {
                SliderDimOffset.Value = item.DimOffset;
                TxtDimOffsetValue.Text = item.DimOffsetDisplay;
            }
            finally
            {
                _isUpdatingUi = false;
            }

            if (item.PreviewImage != null)
            {
                ImgPreview.Source = item.PreviewImage;
                TxtNoPreviewPlaceholder.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                RefreshPreviewImage(item);
            }

            if (item.IsAssigned)
            {
                TxtStatusMessage.Text = $"✅ Shape '{item.ShapeName}' đã được gán vào Rebar Shape thành công.";
            }
            else if (item.HasExistingImage)
            {
                TxtStatusMessage.Text = $"🟡 Shape '{item.ShapeName}' đã có ảnh cũ trong mô hình (sẽ ghi đè khi bấm Gán).";
            }
            else
            {
                TxtStatusMessage.Text = $"💡 Đang xem trước '{item.ShapeName}'. Bạn có thể lật hình, đổi cạnh ngang, chỉnh offset dim và bấm 'Gán vào Shape Image này'.";
            }
        }

        private void BtnFlipX_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentItem != null)
            {
                CurrentItem.ToggleFlipX();
                TxtStatusMessage.Text = $"↔ Đã lật ngang Shape '{CurrentItem.ShapeName}'.";
            }
        }

        private void BtnFlipY_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentItem != null)
            {
                CurrentItem.ToggleFlipY();
                TxtStatusMessage.Text = $"↕ Đã lật đứng Shape '{CurrentItem.ShapeName}'.";
            }
        }

        private void BtnCycleHorizontal_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentItem != null)
            {
                CurrentItem.CycleHorizontalLeg();
                int nextIdx = CurrentItem.HorizontalLegIndex;
                string legLabel = nextIdx < CurrentItem.Legs.Count ? CurrentItem.Legs[nextIdx].Label : $"#{nextIdx + 1}";
                TxtStatusMessage.Text = $"🔄 Đã đổi cạnh nằm ngang thành đoạn '{legLabel}'.";
            }
        }

        private void BtnResetOrientation_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentItem != null)
            {
                CurrentItem.ResetOrientation();
                _isUpdatingUi = true;
                try
                {
                    SliderDimOffset.Value = CurrentItem.DimOffset;
                    TxtDimOffsetValue.Text = CurrentItem.DimOffsetDisplay;
                }
                finally
                {
                    _isUpdatingUi = false;
                }
                TxtStatusMessage.Text = $"↺ Đã khôi phục hướng và offset của '{CurrentItem.ShapeName}'.";
            }
        }

        private void SliderDimOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi || CurrentItem == null) return;
            CurrentItem.DimOffset = e.NewValue;
            TxtDimOffsetValue.Text = CurrentItem.DimOffsetDisplay;
        }

        private void BtnDecreaseDimOffset_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentItem != null)
            {
                CurrentItem.DimOffset = Math.Max(15.0, CurrentItem.DimOffset - 5.0);
                _isUpdatingUi = true;
                try
                {
                    SliderDimOffset.Value = CurrentItem.DimOffset;
                    TxtDimOffsetValue.Text = CurrentItem.DimOffsetDisplay;
                }
                finally
                {
                    _isUpdatingUi = false;
                }
            }
        }

        private void BtnIncreaseDimOffset_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentItem != null)
            {
                CurrentItem.DimOffset = Math.Min(80.0, CurrentItem.DimOffset + 5.0);
                _isUpdatingUi = true;
                try
                {
                    SliderDimOffset.Value = CurrentItem.DimOffset;
                    TxtDimOffsetValue.Text = CurrentItem.DimOffsetDisplay;
                }
                finally
                {
                    _isUpdatingUi = false;
                }
            }
        }

        private void BtnDecreaseFactor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RebarLegItem leg)
            {
                leg.Factor = Math.Round(leg.Factor - 0.1, 2);
            }
        }

        private void BtnIncreaseFactor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RebarLegItem leg)
            {
                leg.Factor = Math.Round(leg.Factor + 0.1, 2);
            }
        }

        private void ChkSyncEqual_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentItem != null)
            {
                CurrentItem.SyncEqualLegs = ChkSyncEqual.IsChecked == true;
            }
        }

        private void BtnResetFactors_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentItem != null)
            {
                CurrentItem.ResetFactors();
                TxtStatusMessage.Text = $"🔄 Đã đặt lại toàn bộ hệ số của '{CurrentItem.ShapeName}' về 1.0.";
            }
        }

        private void BtnAssignCurrent_Click(object sender, RoutedEventArgs e)
        {
            var item = CurrentItem;
            if (item == null) return;

            try
            {
                TxtStatusMessage.Text = $"⏳ Đang gán ảnh cho '{item.ShapeName}'...";

                var (status, msg, imgPath) = RebarShapeImageService.GenerateAndAssignImage(
                    _doc,
                    item.RebarElement,
                    item.Shape2D,
                    item.GetLegFactors(),
                    overwriteExisting: true,
                    flipX: item.IsFlippedX,
                    flipY: item.IsFlippedY,
                    dimOffset: item.DimOffset,
                    horizontalLegIndex: item.HorizontalLegIndex);

                if (status == ShapeImageAssignResult.SuccessCreated || status == ShapeImageAssignResult.SuccessOverwritten)
                {
                    item.MarkAssigned(imgPath);

                    // Kiểm tra nếu tất cả shapes đã được gán hoặc danh sách chỉ có 1 shape -> Tắt luôn cửa sổ addin
                    var unassigned = _items.Where(x => !x.IsAssigned).ToList();
                    if (unassigned.Count == 0 || _items.Count == 1)
                    {
                        TaskDialog.Show("Thành công", $"✅ Đã gán Shape Image cho '{item.ShapeName}' thành công!");
                        Close();
                        return;
                    }

                    // Nếu còn shape khác trong danh sách, thông báo và tự động chuyển đến shape tiếp theo
                    TxtStatusMessage.Text = $"✅ Đã gán '{item.ShapeName}'. Đang chuyển đến Shape tiếp theo...";
                    LstShapes.SelectedItem = unassigned[0];
                }
                else
                {
                    TaskDialog.Show("Lỗi gán Shape Image", $"❌ Lỗi khi gán Shape Image cho '{item.ShapeName}':\n\n{msg}");
                    TxtStatusMessage.Text = $"❌ Lỗi khi gán Shape Image cho '{item.ShapeName}': {msg}";
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Lỗi ngoại lệ", $"❌ Lỗi ngoại lệ: {ex.Message}");
                TxtStatusMessage.Text = $"❌ Lỗi ngoại lệ: {ex.Message}";
            }
        }

        private void BtnAssignAll_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) return;

            int countSuccess = 0;
            int countFailed = 0;

            foreach (var item in _items)
            {
                try
                {
                    var (status, _, imgPath) = RebarShapeImageService.GenerateAndAssignImage(
                        _doc,
                        item.RebarElement,
                        item.Shape2D,
                        item.GetLegFactors(),
                        overwriteExisting: true,
                        flipX: item.IsFlippedX,
                        flipY: item.IsFlippedY,
                        dimOffset: item.DimOffset,
                        horizontalLegIndex: item.HorizontalLegIndex);

                    if (status == ShapeImageAssignResult.SuccessCreated || status == ShapeImageAssignResult.SuccessOverwritten)
                    {
                        item.MarkAssigned(imgPath);
                        countSuccess++;
                    }
                    else
                    {
                        countFailed++;
                    }
                }
                catch
                {
                    countFailed++;
                }
            }

            if (countFailed == 0)
            {
                TaskDialog.Show("Thành công", $"🎉 Đã gán thành công Shape Image cho tất cả {countSuccess} loại Shape!");
                Close();
            }
            else
            {
                TaskDialog.Show("Thông báo", $"⚠️ Đã gán thành công {countSuccess}/{_items.Count} Shape. Thất bại: {countFailed}.");
                Close();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
