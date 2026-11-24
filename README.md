# SysMonitor-Widget
# [SYSMONITOR-WIDGET]

A modern **floating hardware monitoring & gaming overlay widget for Windows 10/11**. Designed with a sleek Windows 11 aesthetic, it replaces cluttered legacy configuration menus with intuitive **drag-and-drop** customization and complete visual freedom.
Ứng dụng theo dõi thông số phần cứng và hiệu năng chơi game dạng **Widget nổi hiện đại cho Windows 10/11**. Thiết kế tối giản chuẩn Windows 11, thay thế cách cấu hình rườm rà truyền thống bằng thao tác **kéo thả trực quan** và tùy biến tự do.

> <img width="277" height="200" alt="Screenshot 2026-11-24 184520" src="https://github.com/user-attachments/assets/508bfc50-5576-4dd0-9308-36cfe4635b15" />
> <img width="1919" height="1026" alt="Screenshot 2026-11-24 184544" src="https://github.com/user-attachments/assets/ca1f3134-6e2d-47d8-896a-86dc58607e78" />
> <img width="1919" height="1030" alt="Screenshot 2026-11-24 184601" src="https://github.com/user-attachments/assets/2d42317f-23ca-4fe6-bf9a-8f8205ee3f13" />
> <img width="1919" height="1032" alt="Screenshot 2026-11-24 184612" src="https://github.com/user-attachments/assets/a84acba3-af13-4315-872d-6d2e635e924f" />

---

## Key Features

### 1. Comprehensive Real-Time Hardware Monitoring
* **CPU:** Usage (%), temperature (°C), clock speed (GHz), and power draw (W).
* **GPU (Dual iGPU & dGPU support):** Usage, temperature, VRAM utilization, and power consumption tracked separately.
* **RAM & Network:** Memory usage and real-time download / upload speeds.
* **Storage:** Read / write speeds (MB/s) per physical drive or system-wide total.

### 2. Advanced FPS Metrics & Anti-Cheat Safe
* Powered by **Intel PresentMon** to read system events directly without injecting DLLs into game processes.
* Tracks **Base FPS**, **FrameGen FPS**, **Average FPS**, **1% Low**, **0.1% Low**, and a smooth real-time **FPS Graph**.

### 3. Automatic GPU Technology Detection
* Automatically identifies installed GPUs and hides or dims unsupported vendor features:
  * **NVIDIA:** DLSS, DLAA, Smooth Motion, Ray Tracing, Path Tracing, NVIDIA Reflex.
  * **AMD:** AMD FSR, FSR Frame Generation, AFMF (Fluid Motion Frames), FSR Latency Reduction.
  * **Intel:** XeSS Super Resolution, XeSS Frame Generation / Multi-Frame Generation, Xe Low Latency (XeLL), Ray Tracing.
* Includes quick manual overrides with automatic per-game (`.exe`) profile saving.

### 4. Modern Windows 11 UI Customization
* **Direct Drag-and-Drop on Widget:** Reorder metrics visually and resize freely from any edge or corner with automatic content scaling.
* **Per-Item Personalization:** Rename any metric, set individual label/value colors, toggle headers between **Text** and **Icon**, customize font family and weight, and adjust the **Elevation (3D Depth)** slider.
* **Click-Through Lock Mode:** Lock the overlay in place so mouse clicks pass right through while gaming or working.

### 5. Built-in Auto-Updater
* Automatically checks, downloads, and installs new versions directly from GitHub Releases while preserving all saved layouts and settings.

---

## Download & Installation

1. Go to the [**Releases (Latest Version)**](https://github.com/dangdinhnambhlc2709-alt/SysMonitor-Widget/releases/latest) page.
2. Download the `.exe` installer (e.g., `SysMonitor-Widget-Setup_v1.0.0.exe`).
3. Double-click the installer to set up and launch the app.

### First-Run Notes
* **Administrator Privileges:** Required to access hardware temperature/power sensors and capture frame telemetry via PresentMon.
* **Microsoft Defender SmartScreen Warning:** Because this is an independent open-source project without a commercial code-signing certificate, Windows may display a blue warning screen on first launch. Simply click **More info** → **Run anyway**.

---

## System Requirements & Tech Stack
* **OS:** Windows 10 / Windows 11 (64-bit). Self-contained—no manual .NET installation required.
* **Tech Stack:** C# (.NET 8), WPF, WPF-UI, LibreHardwareMonitorLib, Intel PresentMon, Inno Setup.
---

## Tính năng nổi bật

### 1. Theo dõi phần cứng toàn diện theo thời gian thực
* **CPU:** Mức tải (%), nhiệt độ (°C), xung nhịp (GHz), công suất tiêu thụ (W).
* **GPU (hỗ trợ cả card tích hợp và card rời tách biệt):** Mức tải, nhiệt độ, VRAM đang dùng, công suất tiêu thụ.
* **RAM & Mạng:** Dung lượng bộ nhớ đang dùng và tốc độ tải xuống / tải lên tức thời.
* **Ổ cứng:** Theo dõi tốc độ đọc / ghi (MB/s) theo từng ổ vật lý hoặc tổng hệ thống.

### 2. Đo FPS chuyên sâu & An toàn với hệ thống chống gian lận
* Tích hợp **Intel PresentMon** đọc sự kiện hệ thống trực tiếp (không chèn mã vào tiến trình game).
* Hiển thị tách bạch **FPS gốc**, **FPS FrameGen**, **FPS trung bình**, **1% Low**, **0.1% Low** và **Đồ thị FPS** mượt theo thời gian thực.

### 3. Nhận diện công nghệ đồ họa theo hãng GPU
* Tự động lọc và ẩn/làm tối công nghệ không có trên cấu hình máy:
  * **NVIDIA:** DLSS, DLAA, Smooth Motion, Ray Tracing, Path Tracing, NVIDIA Reflex.
  * **AMD:** AMD FSR, FSR Frame Generation, AFMF (Fluid Motion Frames), FSR Latency Reduction.
  * **Intel:** XeSS Super Resolution, XeSS Frame Generation / Multi-Frame Generation, Xe Low Latency (XeLL), Ray Tracing.
* Hỗ trợ bảng ghi đè thủ công và tự động ghi nhớ thiết lập theo từng tệp game.

### 4. Tùy biến giao diện hiện đại chuẩn Windows 11
* **Kéo thả trực tiếp trên Widget:** Sắp xếp thứ tự mục thông số trực quan, kéo góc để phóng to/thu nhỏ kèm tự động co giãn nội dung hài hòa.
* **Cá nhân hóa từng mục:** Tự đổi tên hiển thị, đổi màu chữ/số riêng biệt, chuyển đầu mục giữa dạng **Chữ** và **Biểu tượng**, chỉnh phông chữ, độ đậm nhạt và thanh trượt **Mức độ nổi khối**.
* **Khóa vị trí (Xuyên chuột):** Không lo bấm nhầm vào Widget khi đang chơi game hoặc làm việc.

### 5. Tự động cập nhật
* Tự động kiểm tra, tải về và cài đè phiên bản mới trực tiếp từ GitHub Releases mà vẫn giữ nguyên toàn bộ bố cục đã thiết lập.

---

## Hướng dẫn tải và cài đặt

1. Truy cập mục [**Releases (Bản phát hành mới nhất)**](https://github.com/dangdinhnambhlc2709-alt/SysMonitor-Widget/releases/latest).
2. Tải tệp cài đặt có đuôi `.exe` (ví dụ `SysMonitor-Widget_Setup_v1.0.0.exe`).
3. Bấm đúp để cài đặt và mở ứng dụng.

### Lưu ý khi chạy lần đầu
* **Quyền Quản trị viên (Administrator):** Ứng dụng cần quyền Quản trị để đọc cảm biến nhiệt độ, điện năng phần cứng và đo FPS qua PresentMon.
* **Cảnh báo Microsoft Defender SmartScreen:** Vì đây là phần mềm mã nguồn mở cá nhân chưa mua chữ ký số thương mại, Windows có thể hiện bảng cảnh báo xanh ở lần mở đầu tiên. Bạn chỉ cần bấm **More info (Thông tin thêm)** → chọn **Run anyway (Vẫn chạy)**.

---

## Yêu cầu hệ thống & Công nghệ sử dụng
* **Hệ điều hành:** Windows 10 / Windows 11 (64-bit). Không cần cài thêm .NET thủ công.
* **Công nghệ:** C# (.NET 8), WPF, WPF-UI, LibreHardwareMonitorLib, Intel PresentMon, Inno Setup.
