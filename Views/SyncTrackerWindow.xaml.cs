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

namespace ElementCoordinateExtractor.Views
{
    public partial class SyncTrackerWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly RevitExternalEventHandler _handler;

        private List<SyncChangeItem> _allItems = new();
        private List<SyncChangeItem> _filteredItems = new();

        public SyncTrackerWindow(
            UIApplication uiApp, 
            RevitExternalEventHandler handler, 
            List<SyncChangeItem> items, 
            string modelTitle, 
            DateTime syncTime)
        {
            InitializeComponent();
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));

            TxtModelTitle.Text = modelTitle;
            TxtSyncTimestamp.Text = $"Đồng bộ lúc: {syncTime:yyyy-MM-dd HH:mm:ss}";
            ChkAutoPopup.IsChecked = SyncTrackerService.Instance.AutoPopupEnabled;

            LoadData(items);
        }

        private Document? CurrentDocument => _uiApp.ActiveUIDocument?.Document;

        public void LoadData(List<SyncChangeItem> items)
        {
            _allItems = items ?? new List<SyncChangeItem>();

            int countAdded = _allItems.Count(i => i.ChangeType == SyncChangeType.Added);
            int countModified = _allItems.Count(i => i.ChangeType == SyncChangeType.Modified);
            int countDeleted = _allItems.Count(i => i.ChangeType == SyncChangeType.Deleted);
            int countColleagues = _allItems.Count(i => i.IsChangedByColleague);

            TxtCountAdded.Text = $"{countAdded} đối tượng";
            TxtCountModified.Text = $"{countModified} đối tượng";
            TxtCountDeleted.Text = $"{countDeleted} đối tượng";
            TxtCountColleagues.Text = $"{countColleagues} đối tượng";

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string keyword = TxtSearch.Text?.Trim().ToLower() ?? string.Empty;

            var query = _allItems.AsEnumerable();

            // 1. Lọc theo trạng thái tác giả / loại thay đổi
            if (RbFilterColleagues?.IsChecked == true)
            {
                query = query.Where(i => i.IsChangedByColleague);
            }
            else if (RbFilterMine?.IsChecked == true)
            {
                query = query.Where(i => !i.IsChangedByColleague && i.ChangeType != SyncChangeType.Deleted);
            }
            else if (RbFilterAdded?.IsChecked == true)
            {
                query = query.Where(i => i.ChangeType == SyncChangeType.Added);
            }
            else if (RbFilterModified?.IsChecked == true)
            {
                query = query.Where(i => i.ChangeType == SyncChangeType.Modified);
            }
            else if (RbFilterDeleted?.IsChecked == true)
            {
                query = query.Where(i => i.ChangeType == SyncChangeType.Deleted);
            }

            // 2. Lọc theo từ khóa tìm kiếm
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(i =>
                    i.ElementId.ToString().Contains(keyword) ||
                    i.CategoryName.ToLower().Contains(keyword) ||
                    i.ElementName.ToLower().Contains(keyword) ||
                    i.TypeName.ToLower().Contains(keyword) ||
                    i.LastChangedBy.ToLower().Contains(keyword) ||
                    i.Creator.ToLower().Contains(keyword) ||
                    i.LevelName.ToLower().Contains(keyword));
            }

            _filteredItems = query.ToList();
            DgrChanges.ItemsSource = _filteredItems;

            TxtStatus.Text = $"Hiển thị {_filteredItems.Count} / {_allItems.Count} thay đổi.";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void Filter_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                ApplyFilter();
            }
        }

        private void ChkAutoPopup_Changed(object sender, RoutedEventArgs e)
        {
            SyncTrackerService.Instance.AutoPopupEnabled = ChkAutoPopup.IsChecked == true;
        }

        private void DgrChanges_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectAndShowInRevit();
        }

        private void BtnShowSelected_Click(object sender, RoutedEventArgs e)
        {
            SelectAndShowInRevit();
        }

        private void SelectAndShowInRevit()
        {
            var selected = DgrChanges.SelectedItems.Cast<SyncChangeItem>()
                .Where(i => i.ChangeType != SyncChangeType.Deleted)
                .ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất một đối tượng còn tồn tại (không bị xóa).", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var elementIds = selected.Select(i => new ElementId(i.ElementId)).ToList();

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
                    TaskDialog.Show("Lỗi Zoom", ex.Message);
                }
            });
        }

        private void BtnHighlight_Click(object sender, RoutedEventArgs e)
        {
            var itemsToHighlight = _filteredItems
                .Where(i => i.ChangeType != SyncChangeType.Deleted)
                .ToList();

            if (itemsToHighlight.Count == 0)
            {
                MessageBox.Show("Không có đối tượng nào để tô màu trong danh sách lọc hiện tại.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _handler.Run(app =>
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;

                var (count, error) = SyncTrackerService.HighlightElementsInView(uidoc, itemsToHighlight);

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        MessageBox.Show($"Lỗi tô màu: {error}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show($"Đã tô màu nổi bật {count} đối tượng trên View hiện tại!\n\n• Màu Xanh Lá: Đối tượng mới thêm\n• Màu Cam: Đối tượng được chỉnh sửa", "Tô Màu Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                        TxtStatus.Text = $"Đã tô màu {count} đối tượng lúc {DateTime.Now:HH:mm:ss}";
                    }
                });
            });
        }

        private void BtnResetHighlight_Click(object sender, RoutedEventArgs e)
        {
            var itemsToReset = _allItems
                .Where(i => i.ChangeType != SyncChangeType.Deleted)
                .ToList();

            _handler.Run(app =>
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;

                var (count, error) = SyncTrackerService.ResetHighlightInView(uidoc, itemsToReset);

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        MessageBox.Show($"Lỗi xóa màu: {error}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show($"Đã khôi phục màu hiển thị mặc định cho {count} đối tượng trên View.", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
                        TxtStatus.Text = $"Đã khôi phục màu sắc lúc {DateTime.Now:HH:mm:ss}";
                    }
                });
            });
        }

        private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            var doc = CurrentDocument;
            if (doc != null)
            {
                SyncLogService.OpenLogFolder(doc);
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            var items = DgrChanges.SelectedItems.Count > 0
                ? DgrChanges.SelectedItems.Cast<SyncChangeItem>().ToList()
                : _filteredItems;

            if (items.Count == 0)
            {
                MessageBox.Show("Không có dữ liệu để copy.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Element ID\tLoại Thay Đổi\tCategory\tTên Đối Tượng\tType Name\tNgười Sửa Gần Nhất\tNgười Tạo\tLevel");

            foreach (var item in items)
            {
                sb.AppendLine($"{item.ElementId}\t{item.ChangeTypeBadge}\t{item.CategoryName}\t{item.ElementName}\t{item.TypeName}\t{item.AuthorDisplay}\t{item.Creator}\t{item.LevelName}");
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show($"Đã copy {items.Count} dòng vào Clipboard! Bạn có thể dán trực tiếp vào Excel.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DgrChanges_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int selected = DgrChanges.SelectedItems.Count;
            if (selected > 0)
            {
                TxtStatus.Text = $"Đang chọn {selected} / {_filteredItems.Count} đối tượng.";
            }
            else
            {
                TxtStatus.Text = $"Hiển thị {_filteredItems.Count} / {_allItems.Count} thay đổi.";
            }
        }
    }
}
