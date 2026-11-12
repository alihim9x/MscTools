# Revit MscTools - Bộ Công Cụ Tọa Độ, Cốt Thép & Theo Dõi Mô Hình (Revit 2025 & Revit 2027)

Dự án tích hợp bộ công cụ chuyên dụng trong cùng một Add-in Revit, được phân nhóm chuyên nghiệp trên thanh Ribbon tại Tab **MscTools**:

- Panel **Coordinates**: Công cụ trích xuất và di chuyển tọa độ đối tượng.
- Panel **Rebar**: Công cụ tự động hóa cốt thép và bảng thống kê (Schedule).
- Panel **Tracker**: Công cụ giám sát và nhật ký đồng bộ mô hình (Worksharing).

---

## 🛠 Danh Sách Công Cụ Chi Tiết

### 📌 Nhóm 1: Panel "Coordinates"

#### 1. Lấy Tọa Độ Family (`Btn_ElementCoordinates`)
- **Mục đích**: Trích xuất tọa độ hàng loạt element theo Family được chọn.
- **Tính năng**:
  - Quét toàn bộ Family có trong dự án kèm số lượng instance thực tế.
  - Hỗ trợ 3 hệ tọa độ: **Project Base Point**, **Survey Point (Shared Coordinates)**, **Internal Origin**.
  - Tùy chọn đơn vị: `mm`, `m`, `ft`.
  - **Parameter Mapping**: Cho phép gán trực tiếp tọa độ $X, Y, Z$ vào 3 parameter bất kỳ của instance (Text, Length, Number).
  - Double-click để zoom vào đối tượng trong Revit.
  - Copy bảng vào Clipboard hoặc xuất file CSV/Excel UTF-8.
  - **Modeless**: Cửa sổ chạy độc lập, vẫn thao tác xoay/zoom/chọn trong Revit bình thường.

#### 2. Di Chuyển Tọa Độ Đối Tượng (`Btn_RelocateElements`)
- **Mục đích**: Xem thông tin tọa độ của một hoặc nhiều đối tượng đang chọn và nhập tọa độ mới để di chuyển (Move) hoặc sao chép (Copy).
- **Tính năng nổi bật**:
  - **Linh hoạt chọn đối tượng**: Chọn trước trong Revit hoặc bấm nút rồi bấm **"🎯 Quét / Chọn lại trong Revit"** bất cứ lúc nào.
  - **Hỗ trợ 1 hoặc nhiều đối tượng**: Hiển thị danh sách và tọa độ trung tâm nhóm; áp dụng dịch chuyển đồng bộ.
  - **2 Cách nhập tọa độ liên kết 2 chiều thông minh**:
    - **Tọa Độ Tuyệt Đối Mới ($X_{new}, Y_{new}, Z_{new}$)**: Vị trí đích cần tới.
    - **Khoảng Dịch Chuyển Tương Đối ($\Delta X, \Delta Y, \Delta Z$)**: Khoảng dời theo từng trục.
  - **Hệ tọa độ chuẩn xác**: Hỗ trợ di chuyển theo **Project Base Point**, **Survey Point** (tự động tính xoay True North) hoặc **Internal Origin**.
  - **Tùy chọn an toàn**: Tự động Unpin và hỗ trợ cả **Di Chuyển (Move)** lẫn **Tạo Bản Sao (Copy)**.
  - **Modeless**: Hoàn toàn không khóa Revit trong khi sử dụng.

---

### 🧱 Nhóm 2: Panel "Rebar"

#### 3. Tạo Shape Image Thép (`Btn_RebarShapeImage`)
- **Mục đích**: Tự động trích xuất hình học 3D của thanh thép (kể cả thép Custom không có trong tiêu chuẩn BS 8666 hoặc Freeform Rebar), vẽ hình ảnh sơ đồ uốn minh họa (Schematic Diagram) kèm nhãn các đoạn $A, B, C, D...$ và tự động gán vào tham số `Shape Image` để hiển thị trong Bảng thống kê thép (Rebar Schedule).
- **Tính năng nổi bật**:
  - **Kiểm tra thông minh & Chống trùng lặp**:
    - Nhận diện `RebarShape` của thanh thép được chọn.
    - Nếu shape chưa có ảnh -> Tự động vẽ và gán ngay.
    - Nếu shape đã có ảnh sẵn -> Hiển thị hộp thoại hỏi người dùng: Ghi đè hay Bỏ qua (hỗ trợ "Ghi đè tất cả" / "Bỏ qua tất cả" khi chọn nhiều thanh thép cùng lúc).
  - **Hình vẽ sơ đồ minh họa (Proportional Schematic)**:
    - Co giãn cân đối các đoạn uốn và chân móc để hình vẽ luôn ngay ngắn, dễ đọc, không bị dài ngoằng hay dính tịt như khi vẽ đúng tỉ lệ 1:1.
    - Tự động xoay đoạn dài nhất nằm ngang chuẩn kỹ thuật xây dựng.
    - Đường nét vector 2 nét line rỗng ruột sắc nét, vạch chặn đen ở 2 đầu, góc uốn bo tròn to đẹp.
    - Tự động đo và đánh nhãn kích thước $A, B, C, D, E...$ font Arial Italic đậm to rõ kèm mũi tên tam giác đặc và đường gióng.
  - **Cửa Sổ Live Preview & Tinh Chỉnh Hệ Số Kích Thước Trực Quan**:
    - **Xem trước tức thì (Live Preview)**: Hiển thị hình ảnh kết xuất chuẩn tỉ lệ 2:1 (800 × 400 px) ngay trên giao diện trước khi quyết định gán vào mô hình.
    - **Hệ số chiều dài từng đoạn kích thước**: Cho phép người dùng linh hoạt tăng/giảm hệ số hiển thị từng đoạn (A, B, C...) bằng nút `[-]`, `[+]` hoặc thanh trượt Slider. Ảnh xem trước cập nhật theo thời gian thực (realtime) giúp căn chỉnh sơ đồ uốn vừa mắt và dễ nhìn nhất.
    - **Tự động đồng bộ các đoạn cùng kích thước (Sync Equal Legs)**: Tự động giữ tính đối xứng cho các đoạn có cùng chiều dài thực tế (như 2 móc đai thép, 2 cánh thép chữ U...).
    - **Gán có kiểm soát**: Người dùng chỉ gán vào tham số `Shape Image` khi đã xem và hoàn toàn ưng ý với hình vẽ. Hỗ trợ gán từng Shape hoặc gán hàng loạt tất cả các Shape.
  - **Tự động hóa nạp ảnh vào Revit**:
    - Xuất file `.png` độ phân giải cao vào thư mục dự án và nạp vào Family RebarShape qua `EditFamily` cũng như đồng bộ `ImageType` trong Project, gán trực tiếp vào tham số `Shape Image` (`REBAR_SHAPE_IMAGE`).

---

### 🔄 Nhóm 3: Panel "Tracker"

#### 4. Lịch Sử Đồng Bộ Model (`Btn_SyncHistory`)
- **Mục đích**: Tự động phát hiện, báo cáo và lưu lại toàn bộ thay đổi của mô hình sau mỗi lần **Synchronize with Central**.
- **Tính năng nổi bật**:
  - **Ghi nhận liên tục**: Tích lũy mọi thao tác thêm, sửa, xóa trong suốt phiên làm việc trước khi bấm Sync, kết hợp với các thay đổi tải về từ Central của đồng nghiệp khi Sync.
  - **Phân loại trạng thái**:
    - 🟢 **Added**: Đối tượng mới thêm vào.
    - 🟡 **Modified**: Đối tượng bị chỉnh sửa hình học / thông số.
    - 🔴 **Deleted**: Đối tượng bị xóa.
  - **Nhận diện tác giả**: Biết chính xác ai là người thực hiện từng thay đổi (`LastChangedBy`, `CreatedBy`) và gắn nhãn nếu là đồng nghiệp thực hiện.
  - **Tô màu trực quan 3D**: Bấm **"🎨 Tô Màu 3D"** để override màu trực tiếp trên 3D View hiện tại (Xanh lá: Mới, Vàng cam: Sửa). Bấm **"Khôi phục màu"** để reset đồ họa.
  - **Tương tác 2 chiều**: Click đúp vào dòng để tự động Zoom & Select đối tượng đó trong Revit.
  - **Nhật ký Audit Log (CSV)**: Tự động ghi nhận file log chi tiết theo ngày tại thư mục `RevitSyncLogs/` (hỗ trợ cả mô hình Local và mô hình Đám mây ACC / BIM 360).

---

## 📦 Bộ Cài Đặt Tự Động (Installer)

Bộ cài đặt chuyên dụng đã được đóng gói sẵn trong thư mục [`release/`](file:///c:/Users/924002/OneDrive%20-%20Haskoning/Desktop/Addin%20Revit/release/):

- **Chương trình cài đặt giao diện đồ họa**: [`release/BIM_Tools_Setup.exe`](file:///c:/Users/924002/OneDrive%20-%20Haskoning/Desktop/Addin%20Revit/release/BIM_Tools_Setup.exe) (hoặc chạy [`release/Install_Quick.bat`](file:///c:/Users/924002/OneDrive%20-%20Haskoning/Desktop/Addin%20Revit/release/Install_Quick.bat))
- **Script cài đặt dòng lệnh**: [`release/Deploy_Script.ps1`](file:///c:/Users/924002/OneDrive%20-%20Haskoning/Desktop/Addin%20Revit/release/Deploy_Script.ps1)

### Tính năng của Bộ Cài Đặt:
1. **Tự động quét và nhận diện**: Tự động phát hiện các phiên bản **Revit 2025, Revit 2026, Revit 2027** đã cài trên máy tính và đánh dấu sẵn.
2. **Tùy chọn linh hoạt**: Người dùng có thể tích chọn hoặc bỏ chọn bất kỳ phiên bản nào theo nhu cầu.
3. **Phạm vi cài đặt**:
   - *Cài đặt cho người dùng hiện tại (%APPDATA%)*: Không yêu cầu quyền Administrator, an toàn 100% trên máy tính công ty bị khóa quyền Admin.
   - *Cài đặt cho tất cả người dùng (%PROGRAMDATA%)*: Áp dụng cho mọi tài khoản trên máy.
4. **Tích hợp tính năng Gỡ Cài Đặt (Uninstall)**: Hỗ trợ gỡ sạch hoàn toàn add-in chỉ với 1 click.
5. **Cảnh báo tiến trình đang chạy**: Tự động nhắc nhở lưu và đóng Revit nếu phát hiện `Revit.exe` đang mở để tránh lỗi khóa tệp tin.

---

## ⚡ Hỗ Trợ Đa Phiên Bản: Revit 2025, 2026 & Revit 2027

Dự án được cấu hình để tự động biên dịch và triển khai đồng thời cho cả **Revit 2025** và **Revit 2027**:

| Phiên bản Revit | .NET Runtime | Đường dẫn Output DLL | File Cài Đặt .addin |
| :--- | :--- | :--- | :--- |
| **Revit 2025** | .NET 10 | `bin/2025/Debug/ElementCoordinateExtractor.dll` | `%APPDATA%\Autodesk\Revit\Addins\2025\` |
| **Revit 2027** | .NET 10 | `bin/2027/Debug/ElementCoordinateExtractor.dll` | `%APPDATA%\Autodesk\Revit\Addins\2027\` |

Khi bạn nhấn **Build Solution** (`Ctrl + Shift + B`) hoặc chạy lệnh `dotnet build`:
- Cả hai phiên bản **2025** và **2027** đều được biên dịch tự động.
- Cả hai file `.addin` đều được tự động copy vào thư mục Addins tương ứng của Revit.
- Bạn có thể mở Revit 2025 hoặc Revit 2027 bất kỳ lúc nào để sử dụng ngay lập tức mà không cần bất kỳ thao tác cài đặt thủ công nào.
