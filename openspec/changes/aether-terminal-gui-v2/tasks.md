## 1. Environment & Dependency Setup

- [x] 1.1 Thêm dependency `Avalonia.Markdown` vào `src/Aether.Terminal/Aether.Terminal.csproj`.
- [x] 1.2 Đảm bảo project build thành công sau khi thêm dependency.

## 2. Forge Theme & Aesthetic

- [x] 2.1 Tạo `src/Aether.Terminal/Themes/ForgeTheme.axaml` với các Brush Amber/Black và hiệu ứng Scanline.
- [x] 2.2 Đăng ký ForgeTheme trong `App.axaml`.
- [x] 2.3 Cập nhật `MainWindow.axaml` để sử dụng `ForgeBackground`.

## 3. Soul & Avatar Engine
- [x] 3.1 Implement `SilhouetteView` UserControl cho Avatar mờ ảo.
- [x] 3.2 Thêm các hiệu ứng Animation (Pulse, Flicker, Heartbeat) cho Silhouette.
- [x] 3.3 Tích hợp `SilhouetteView` vào `MainWindow`.

## 4. Maria's Sovereignty Panels

- [ ] 4.1 Tạo `GoalDashboard.axaml` hiển thị danh sách Goals từ `GoalStore`.
- [ ] 4.2 Tạo `ContinuityView.axaml` hiển thị trạng thái từ `CONTINUITY.md`.
- [ ] 4.3 Cập nhật `TerminalViewModel` để crawl dữ liệu từ GoalStore và Memory files.

## 5. Lore Indicators

- [ ] 5.1 Implement `2B Tension Ring` (Vòng tròn trạng thái màng).
- [ ] 5.2 Implement `Agora / Hive Indicator`.
- [ ] 5.3 Tích hợp các chỉ số này vào UI góc màn hình.

## 6. Forge Chat & Tool Components

- [ ] 6.1 Refactor `ChatView.axaml` để sử dụng `Avalonia.Markdown` cho nội dung tin nhắn.
- [ ] 6.2 Tạo `ForgeToolBlock.axaml` để hiển thị Tool Calls theo phong cách "thỏi thép".
- [ ] 6.3 Mapping các Message Role sang các View tương ứng trong Forge style.

## 7. Integration & Heartbeat

- [ ] 7.1 Implement logic tính toán `SystemHeat` trong `TerminalViewModel`.
- [ ] 7.2 Kết nối `SystemHeat` với tốc độ Animation của Avatar (Heartbeat).
- [ ] 7.3 Kiểm thử cuối cùng: Luồng gửi tin nhắn → Tool call → Goal update → Avatar reaction.
