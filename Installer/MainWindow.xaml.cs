using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace MscToolsInstaller
{
    public partial class MainWindow : Window
    {
        private const string AddinGuid2025 = "9E819543-A79F-4418-8A33-911855CD9691";
        private const string AddinGuid2026 = "7B2284C1-3D5F-4E90-B812-421735BC9722";
        private const string AddinGuid2027 = "6D4C8299-FB76-476E-8041-A8165F329433";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DetectRevitInstallations();
        }

        private void DetectRevitInstallations()
        {
            // 1. Kiểm tra Revit 2025 (Chỉ kiểm tra để hiển thị trạng thái, MẶC ĐỊNH KHÔNG TICK)
            bool found2025 = CheckRevitExists(2025);
            ChkRevit2025.IsChecked = false;
            UpdateStatusBadge(TxtStatus2025, Badge2025, found2025);

            // 2. Kiểm tra Revit 2026 (Chỉ kiểm tra để hiển thị trạng thái, MẶC ĐỊNH KHÔNG TICK)
            bool found2026 = CheckRevitExists(2026);
            ChkRevit2026.IsChecked = false;
            UpdateStatusBadge(TxtStatus2026, Badge2026, found2026);

            // 3. Kiểm tra Revit 2027 (Chỉ kiểm tra để hiển thị trạng thái, MẶC ĐỊNH KHÔNG TICK)
            bool found2027 = CheckRevitExists(2027);
            ChkRevit2027.IsChecked = false;
            UpdateStatusBadge(TxtStatus2027, Badge2027, found2027);
        }

        private static bool CheckRevitExists(int year)
        {
            // 1. Kiểm tra sự tồn tại của file thực thi Revit.exe (chính xác nhất)
            string[] possibleExePaths = {
                $@"C:\Program Files\Autodesk\Revit {year}\Revit.exe",
                $@"D:\Program Files\Autodesk\Revit {year}\Revit.exe",
                $@"E:\Program Files\Autodesk\Revit {year}\Revit.exe"
            };

            foreach (var exe in possibleExePaths)
            {
                if (File.Exists(exe)) return true;
            }

            // 2. Kiểm tra Registry xem có thư mục cài đặt thực tế chứa Revit.exe không
            try
            {
                using var hklmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Autodesk\Revit\{year}");
                if (hklmKey != null)
                {
                    var installLoc = hklmKey.GetValue("InstallLocation")?.ToString();
                    if (!string.IsNullOrEmpty(installLoc) && File.Exists(Path.Combine(installLoc, "Revit.exe")))
                        return true;
                }

                using var hkcuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Autodesk\Revit\Autodesk Revit {year}");
                if (hkcuKey != null)
                {
                    var installLoc = hkcuKey.GetValue("InstallLocation")?.ToString();
                    if (!string.IsNullOrEmpty(installLoc) && File.Exists(Path.Combine(installLoc, "Revit.exe")))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static void UpdateStatusBadge(System.Windows.Controls.TextBlock txt, System.Windows.Controls.Border badge, bool found)
        {
            if (found)
            {
                txt.Text = "Đã phát hiện trên máy";
                txt.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
                badge.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
            }
            else
            {
                txt.Text = "Chưa phát hiện";
                txt.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                badge.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            }
        }

        private static bool IsRevitRunning()
        {
            try
            {
                var procs = Process.GetProcessesByName("Revit");
                return procs.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var selectedYears = new List<int>();
            if (ChkRevit2025.IsChecked == true) selectedYears.Add(2025);
            if (ChkRevit2026.IsChecked == true) selectedYears.Add(2026);
            if (ChkRevit2027.IsChecked == true) selectedYears.Add(2027);

            if (selectedYears.Count == 0)
            {
                MessageBox.Show(
                    "Vui lòng chọn ít nhất một phiên bản Revit để cài đặt.",
                    "Chưa chọn phiên bản",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (IsRevitRunning())
            {
                var res = MessageBox.Show(
                    "Phát hiện Revit đang chạy trên máy tính!\n\nVui lòng lưu tài liệu và đóng toàn bộ cửa sổ Revit trước khi bấm OK để tiếp tục cài đặt.",
                    "Cảnh báo: Revit đang chạy",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

                if (res != MessageBoxResult.OK) return;

                if (IsRevitRunning())
                {
                    MessageBox.Show("Tiến trình Revit vẫn chưa được đóng. Quá trình cài đặt đã dừng lại.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            BtnInstall.IsEnabled = false;
            BtnUninstall.IsEnabled = false;
            PrgProgress.Visibility = Visibility.Visible;
            PrgProgress.Value = 10;
            TxtLog.Text = "Đang khởi tạo các tệp tin cài đặt...";

            bool isAllUsers = RbAllUsers.IsChecked == true;

            try
            {
                await Task.Run(() => PerformInstallation(selectedYears, isAllUsers));

                PrgProgress.Value = 100;
                TxtLog.Text = $"✅ Cài đặt thành công cho: Revit {string.Join(", ", selectedYears)}!";

                MessageBox.Show(
                    $"Chúc mừng! Bộ công cụ MscTools đã được cài đặt thành công cho:\n\n" +
                    $"• Revit {string.Join("\n• Revit ", selectedYears)}\n\n" +
                    $"Bây giờ bạn có thể mở Revit lên, thanh Ribbon sẽ xuất hiện Tab 'MscTools' với đầy đủ các tính năng:\n" +
                    $"- Panel Coordinates: Lấy tọa độ Family & Di chuyển tọa độ\n" +
                    $"- Panel Rebar: Tạo Shape Image cho thép Custom\n" +
                    $"- Panel Tracker: Giám sát lịch sử đồng bộ (Sync Tracker)\n",
                    "Cài Đặt Thành Công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                PrgProgress.Value = 0;
                TxtLog.Text = $"❌ Lỗi cài đặt: {ex.Message}";
                MessageBox.Show($"Đã xảy ra lỗi trong quá trình cài đặt:\n\n{ex.Message}", "Lỗi Cài Đặt", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnInstall.IsEnabled = true;
                BtnUninstall.IsEnabled = true;
            }
        }

        private void PerformInstallation(List<int> years, bool isAllUsers)
        {
            string baseAddinFolder = isAllUsers
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "Addins")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins");

            string basePayloadFolder = isAllUsers
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MscTools", "Addins")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MscTools", "Addins");

            if (!Directory.Exists(basePayloadFolder))
            {
                Directory.CreateDirectory(basePayloadFolder);
            }

            foreach (int year in years)
            {
                Dispatcher.Invoke(() => TxtLog.Text = $"Đang cài đặt thư viện cho Revit {year}...");

                // 0. Dọn dẹp tàn dư bản cũ (ElementCoordinateExtractor) để chống xung đột 100%
                var candidateAddinFolders = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", year.ToString()),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "Addins", year.ToString())
                };

                foreach (var folder in candidateAddinFolders)
                {
                    string oldManifest = Path.Combine(folder, "ElementCoordinateExtractor.addin");
                    if (File.Exists(oldManifest))
                    {
                        try { File.Delete(oldManifest); } catch { }
                    }
                }

                var oldPayloadDirs = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIMTools", "Addins", $"Revit{year}"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BIMTools", "Addins", $"Revit{year}")
                };

                foreach (var oldDir in oldPayloadDirs)
                {
                    if (Directory.Exists(oldDir))
                    {
                        try { Directory.Delete(oldDir, true); } catch { }
                    }
                }

                string yearPayloadDir = Path.Combine(basePayloadFolder, $"Revit{year}");
                if (!Directory.Exists(yearPayloadDir))
                {
                    Directory.CreateDirectory(yearPayloadDir);
                }

                string targetDllPath = Path.Combine(yearPayloadDir, "MscTool.dll");

                // 1. Sao chép hoặc trích xuất DLL
                ExtractOrCopyDll(year, targetDllPath);

                // 2. Tạo thư mục Addins của Revit nếu chưa có
                string targetAddinYearDir = Path.Combine(baseAddinFolder, year.ToString());
                if (!Directory.Exists(targetAddinYearDir))
                {
                    Directory.CreateDirectory(targetAddinYearDir);
                }

                // 3. Tạo file manifest .addin
                string targetAddinManifest = Path.Combine(targetAddinYearDir, "MscTool.addin");
                string guid = year switch
                {
                    2025 => AddinGuid2025,
                    2026 => AddinGuid2026,
                    2027 => AddinGuid2027,
                    _ => Guid.NewGuid().ToString()
                };

                string manifestContent = GenerateAddinManifest(targetDllPath, guid);
                File.WriteAllText(targetAddinManifest, manifestContent, Encoding.UTF8);
            }
        }

        private static void ExtractOrCopyDll(int year, string targetPath)
        {
            // Thử trích xuất từ Embedded Resource trước
            string resourceNameSuffix = year switch
            {
                2027 => "Revit2027.MscTool.dll",
                _ => "Revit2025.MscTool.dll"
            };

            var asm = Assembly.GetExecutingAssembly();
            string? match = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(resourceNameSuffix, StringComparison.OrdinalIgnoreCase)
                || r.EndsWith(year == 2027 ? "Revit2027.ElementCoordinateExtractor.dll" : "Revit2025.ElementCoordinateExtractor.dll", StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                using var stream = asm.GetManifestResourceStream(match);
                if (stream != null)
                {
                    using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
                    stream.CopyTo(fs);
                    return;
                }
            }

            // Fallback: Tìm file build sẵn ở các thư mục lân cận
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidatePaths = new List<string>
            {
                Path.Combine(appDir, $"Revit{year}", "MscTool.dll"),
                Path.Combine(appDir, year == 2027 ? @"..\..\bin\2027\Debug\MscTool.dll" : @"..\..\bin\2025\Debug\MscTool.dll"),
                Path.Combine(appDir, year == 2027 ? @"..\bin\2027\Debug\MscTool.dll" : @"..\bin\2025\Debug\MscTool.dll"),
                Path.Combine(appDir, @"..\..\bin\Debug\MscTool.dll"),
                Path.Combine(appDir, "MscTool.dll"),
                // Fallback cũ nếu có:
                Path.Combine(appDir, $"Revit{year}", "ElementCoordinateExtractor.dll"),
                Path.Combine(appDir, year == 2027 ? @"..\..\bin\2027\Debug\ElementCoordinateExtractor.dll" : @"..\..\bin\2025\Debug\ElementCoordinateExtractor.dll"),
                Path.Combine(appDir, @"..\..\bin\Debug\ElementCoordinateExtractor.dll")
            };

            foreach (var candidate in candidatePaths)
            {
                if (File.Exists(candidate))
                {
                    File.Copy(candidate, targetPath, true);
                    return;
                }
            }

            throw new FileNotFoundException($"Không tìm thấy tệp DLL tương thích cho Revit {year}. Vui lòng đảm bảo dự án đã được build trước khi chạy.");
        }

        private static string GenerateAddinManifest(string dllPath, string guid)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<RevitAddIns>
  <AddIn Type=""Application"">
    <Name>MscTool</Name>
    <Assembly>{dllPath}</Assembly>
    <FullClassName>ElementCoordinateExtractor.App</FullClassName>
    <ClientId>{guid}</ClientId>
    <VendorId>HASKONING</VendorId>
    <VendorDescription>Royal HaskoningDHV MscTool</VendorDescription>
  </AddIn>
</RevitAddIns>";
        }

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (IsRevitRunning())
            {
                MessageBox.Show(
                    "Phát hiện Revit đang chạy. Vui lòng đóng tất cả cửa sổ Revit trước khi gỡ cài đặt.",
                    "Cảnh báo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "Bạn có chắc chắn muốn gỡ bỏ hoàn toàn bộ công cụ MscTools khỏi Revit không?",
                "Xác nhận gỡ cài đặt",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            BtnInstall.IsEnabled = false;
            BtnUninstall.IsEnabled = false;
            PrgProgress.Visibility = Visibility.Visible;
            PrgProgress.Value = 30;
            TxtLog.Text = "Đang gỡ bỏ các đăng ký Addin...";

            try
            {
                await Task.Run(() => PerformUninstallation());

                PrgProgress.Value = 100;
                TxtLog.Text = "✅ Đã gỡ cài đặt MscTools thành công khỏi toàn bộ các phiên bản Revit!";

                MessageBox.Show(
                    "Đã gỡ cài đặt hoàn toàn bộ công cụ MscTools khỏi Revit.",
                    "Gỡ Cài Đặt Thành Công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtLog.Text = $"❌ Lỗi khi gỡ cài đặt: {ex.Message}";
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnInstall.IsEnabled = true;
                BtnUninstall.IsEnabled = true;
            }
        }

        private static void PerformUninstallation()
        {
            var years = new[] { 2025, 2026, 2027 };
            var addinBaseFolders = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "Addins")
            };

            // 1. Xóa các file .addin
            foreach (var baseFolder in addinBaseFolders)
            {
                foreach (int year in years)
                {
                    string manifest = Path.Combine(baseFolder, year.ToString(), "MscTool.addin");
                    if (File.Exists(manifest))
                    {
                        try { File.Delete(manifest); } catch { }
                    }

                    string oldManifest = Path.Combine(baseFolder, year.ToString(), "ElementCoordinateExtractor.addin");
                    if (File.Exists(oldManifest))
                    {
                        try { File.Delete(oldManifest); } catch { }
                    }
                }
            }

            // 2. Dọn dẹp thư mục Payload
            var payloadFolders = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MscTools", "Addins"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MscTools", "Addins"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIMTools", "Addins"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BIMTools", "Addins")
            };

            foreach (var folder in payloadFolders)
            {
                if (Directory.Exists(folder))
                {
                    try { Directory.Delete(folder, true); } catch { }
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
