using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ElementCoordinateExtractor.Models;

namespace ElementCoordinateExtractor.Services
{
    public class SyncTrackerService
    {
        private static readonly SyncTrackerService _instance = new();
        public static SyncTrackerService Instance => _instance;

        public bool IsSyncing { get; set; }
        public DateTime SyncStartTime { get; set; }
        public bool AutoPopupEnabled { get; set; } = true;
        public bool HasSyncedAtLeastOnce { get; set; } = false;

        public HashSet<ElementId> AddedIds { get; } = new();
        public HashSet<ElementId> ModifiedIds { get; } = new();
        public HashSet<ElementId> DeletedIds { get; } = new();

        public List<SyncChangeItem> LastSyncItems { get; private set; } = new();
        public DateTime LastSyncTime { get; private set; }
        public string LastSyncUser { get; private set; } = string.Empty;
        public string LastSavedLogPath { get; private set; } = string.Empty;

        public void StartSyncSession()
        {
            IsSyncing = true;
            SyncStartTime = DateTime.Now;
            // Không xóa AddedIds, ModifiedIds, DeletedIds ở đây
            // vì chúng chứa toàn bộ thay đổi do user thao tác trước khi bấm Sync
        }

        public void RecordChanges(ICollection<ElementId> added, ICollection<ElementId> modified, ICollection<ElementId> deleted)
        {
            // Tích lũy thay đổi liên tục trong suốt phiên làm việc (cả trước và trong lúc Sync)
            foreach (var id in added)
            {
                DeletedIds.Remove(id);
                AddedIds.Add(id);
            }

            foreach (var id in modified)
            {
                // Nếu đối tượng vừa mới tạo trong phiên này thì vẫn giữ là Added
                if (!AddedIds.Contains(id))
                {
                    ModifiedIds.Add(id);
                }
            }

            foreach (var id in deleted)
            {
                if (AddedIds.Contains(id))
                {
                    // Được tạo rồi xóa ngay trong cùng một phiên -> không cần sync
                    AddedIds.Remove(id);
                }
                else
                {
                    ModifiedIds.Remove(id);
                    DeletedIds.Add(id);
                }
            }
        }

        public List<SyncChangeItem> ProcessSyncCompletion(Document doc)
        {
            IsSyncing = false;
            LastSyncTime = DateTime.Now;
            LastSyncUser = doc.Application.Username;
            HasSyncedAtLeastOnce = true;

            var result = new List<SyncChangeItem>();
            string currentUser = doc.Application.Username;
            bool isWorkshared = doc.IsWorkshared;

            // 1. Xử lý các đối tượng Thêm mới (Added)
            foreach (var id in AddedIds)
            {
                var elem = doc.GetElement(id);
                if (elem == null || !IsValidModelElement(elem)) continue;

                string creator = currentUser;
                string lastChangedBy = currentUser;

                if (isWorkshared)
                {
                    try
                    {
                        var info = WorksharingUtils.GetWorksharingTooltipInfo(doc, id);
                        creator = string.IsNullOrWhiteSpace(info.Creator) ? currentUser : info.Creator;
                        lastChangedBy = string.IsNullOrWhiteSpace(info.LastChangedBy) ? creator : info.LastChangedBy;
                    }
                    catch { }
                }

                bool isColleague = !string.Equals(lastChangedBy, currentUser, StringComparison.OrdinalIgnoreCase);

                result.Add(new SyncChangeItem
                {
                    ElementId = id.Value,
                    ChangeType = SyncChangeType.Added,
                    CategoryName = elem.Category?.Name ?? "General",
                    ElementName = elem.Name,
                    TypeName = GetElementTypeName(elem),
                    Creator = creator,
                    LastChangedBy = lastChangedBy,
                    LevelName = GetElementLevel(doc, elem),
                    IsChangedByColleague = isColleague
                });
            }

            // 2. Xử lý các đối tượng Chỉnh sửa (Modified) - Loại bỏ các đối tượng vừa mới tạo trong chính đợt sync này
            foreach (var id in ModifiedIds)
            {
                if (AddedIds.Contains(id)) continue;

                var elem = doc.GetElement(id);
                if (elem == null || !IsValidModelElement(elem)) continue;

                string creator = "Unknown";
                string lastChangedBy = currentUser;

                if (isWorkshared)
                {
                    try
                    {
                        var info = WorksharingUtils.GetWorksharingTooltipInfo(doc, id);
                        creator = string.IsNullOrWhiteSpace(info.Creator) ? "Unknown" : info.Creator;
                        lastChangedBy = string.IsNullOrWhiteSpace(info.LastChangedBy) ? currentUser : info.LastChangedBy;
                    }
                    catch { }
                }

                bool isColleague = !string.Equals(lastChangedBy, currentUser, StringComparison.OrdinalIgnoreCase);

                result.Add(new SyncChangeItem
                {
                    ElementId = id.Value,
                    ChangeType = SyncChangeType.Modified,
                    CategoryName = elem.Category?.Name ?? "General",
                    ElementName = elem.Name,
                    TypeName = GetElementTypeName(elem),
                    Creator = creator,
                    LastChangedBy = lastChangedBy,
                    LevelName = GetElementLevel(doc, elem),
                    IsChangedByColleague = isColleague
                });
            }

            // 3. Xử lý các đối tượng Đã xóa (Deleted)
            foreach (var id in DeletedIds)
            {
                result.Add(new SyncChangeItem
                {
                    ElementId = id.Value,
                    ChangeType = SyncChangeType.Deleted,
                    CategoryName = "(Đã bị xóa)",
                    ElementName = $"Element {id.Value}",
                    TypeName = "-",
                    Creator = "-",
                    LastChangedBy = "Unknown",
                    LevelName = "-",
                    IsChangedByColleague = false
                });
            }

            LastSyncItems = result;

            // Ghi file log CSV tự động
            LastSavedLogPath = SyncLogService.WriteSyncLog(doc, currentUser, LastSyncTime, result);

            // Xóa danh sách tích lũy để sẵn sàng ghi nhận các thao tác mới cho đợt Sync tiếp theo
            AddedIds.Clear();
            ModifiedIds.Clear();
            DeletedIds.Clear();

            return result;
        }

        /// <summary>
        /// Tô màu nổi bật các đối tượng thay đổi trên View hiện tại (Xanh lá = Mới, Cam = Sửa).
        /// </summary>
        public static (int count, string error) HighlightElementsInView(UIDocument uidoc, IEnumerable<SyncChangeItem> items)
        {
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            if (view == null || view.ViewType == ViewType.Schedule || view.ViewType == ViewType.DrawingSheet)
            {
                return (0, "Vui lòng mở một View 3D hoặc Mặt bằng để thực hiện tô màu.");
            }

            try
            {
                // Lấy Solid Fill Pattern
                var solidFillPattern = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

                var colorAdded = new Color(40, 167, 69);     // Green
                var colorModified = new Color(217, 119, 6);  // Orange

                var ogsAdded = new OverrideGraphicSettings();
                var ogsModified = new OverrideGraphicSettings();

                if (solidFillPattern != null)
                {
                    ogsAdded.SetSurfaceForegroundPatternId(solidFillPattern.Id);
                    ogsAdded.SetSurfaceForegroundPatternColor(colorAdded);
                    ogsAdded.SetCutForegroundPatternId(solidFillPattern.Id);
                    ogsAdded.SetCutForegroundPatternColor(colorAdded);

                    ogsModified.SetSurfaceForegroundPatternId(solidFillPattern.Id);
                    ogsModified.SetSurfaceForegroundPatternColor(colorModified);
                    ogsModified.SetCutForegroundPatternId(solidFillPattern.Id);
                    ogsModified.SetCutForegroundPatternColor(colorModified);
                }

                int count = 0;
                using (var tx = new Transaction(doc, "Tô màu đối tượng thay đổi sau Sync"))
                {
                    tx.Start();

                    foreach (var item in items)
                    {
                        if (item.ChangeType == SyncChangeType.Deleted) continue;

                        var elemId = new ElementId(item.ElementId);
                        var elem = doc.GetElement(elemId);
                        if (elem == null) continue;

                        if (item.ChangeType == SyncChangeType.Added)
                        {
                            view.SetElementOverrides(elemId, ogsAdded);
                            count++;
                        }
                        else if (item.ChangeType == SyncChangeType.Modified)
                        {
                            view.SetElementOverrides(elemId, ogsModified);
                            count++;
                        }
                    }

                    tx.Commit();
                }

                return (count, string.Empty);
            }
            catch (Exception ex)
            {
                return (0, ex.Message);
            }
        }

        /// <summary>
        /// Khôi phục màu mặc định (Xóa Override Graphics) trên View hiện tại.
        /// </summary>
        public static (int count, string error) ResetHighlightInView(UIDocument uidoc, IEnumerable<SyncChangeItem> items)
        {
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            if (view == null) return (0, "Không tìm thấy View hiện tại.");

            try
            {
                int count = 0;
                var resetOgs = new OverrideGraphicSettings();

                using (var tx = new Transaction(doc, "Khôi phục màu đối tượng"))
                {
                    tx.Start();

                    foreach (var item in items)
                    {
                        if (item.ChangeType == SyncChangeType.Deleted) continue;
                        var elemId = new ElementId(item.ElementId);
                        view.SetElementOverrides(elemId, resetOgs);
                        count++;
                    }

                    tx.Commit();
                }

                return (count, string.Empty);
            }
            catch (Exception ex)
            {
                return (0, ex.Message);
            }
        }

        private static bool IsValidModelElement(Element elem)
        {
            if (elem.Category == null) return false;
            // Bỏ qua các category phụ trợ/nội bộ
            var catType = elem.Category.CategoryType;
            return catType == CategoryType.Model || catType == CategoryType.Annotation;
        }

        private static string GetElementTypeName(Element elem)
        {
            if (elem is FamilyInstance fi && fi.Symbol != null)
                return fi.Symbol.Name;

            var typeId = elem.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                var typeElem = elem.Document.GetElement(typeId);
                if (typeElem != null) return typeElem.Name;
            }

            return "-";
        }

        private static string GetElementLevel(Document doc, Element elem)
        {
            if (elem.LevelId != ElementId.InvalidElementId)
            {
                var lvl = doc.GetElement(elem.LevelId);
                if (lvl != null) return lvl.Name;
            }

            var p = elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                 ?? elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

            if (p != null && p.AsElementId() != ElementId.InvalidElementId)
            {
                var lvl = doc.GetElement(p.AsElementId());
                if (lvl != null) return lvl.Name;
            }

            return "-";
        }
    }
}
