namespace ElementCoordinateExtractor.Models
{
    public enum SyncChangeType
    {
        Added,
        Modified,
        Deleted
    }

    public class SyncChangeItem
    {
        public long ElementId { get; set; }
        public SyncChangeType ChangeType { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string ElementName { get; set; } = string.Empty;
        public string LastChangedBy { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public bool IsChangedByColleague { get; set; }

        public string ChangeTypeBadge => ChangeType switch
        {
            SyncChangeType.Added => "[+] Thêm mới",
            SyncChangeType.Modified => "[~] Chỉnh sửa",
            SyncChangeType.Deleted => "[-] Đã xóa",
            _ => "Thay đổi"
        };

        public string ChangeTypeColor => ChangeType switch
        {
            SyncChangeType.Added => "#28A745",     // Green
            SyncChangeType.Modified => "#D97706",  // Amber/Orange
            SyncChangeType.Deleted => "#DC3545",   // Red
            _ => "#6C757D"
        };

        public string AuthorDisplay => IsChangedByColleague 
            ? $"{LastChangedBy} (Đồng nghiệp)" 
            : $"{LastChangedBy} (Bạn)";
    }
}
