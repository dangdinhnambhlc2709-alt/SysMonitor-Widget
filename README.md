# SysMonitor-Widget
# [SYSMONITOR-WIDGET]

A modern **floating hardware monitoring & gaming overlay widget for Windows 10/11**. Designed with a sleek Windows 11 aesthetic, it replaces cluttered legacy configuration menus with intuitive **drag-and-drop** customization and complete visual freedom.
Ứng dụng theo dõi thông số phần cứng và hiệu năng chơi game dạng **Widget nổi hiện đại cho Windows 10/11**. Thiết kế tối giản chuẩn Windows 11, thay thế cách cấu hình rườm rà truyền thống bằng thao tác **kéo thả trực quan** và tùy biến tự do.

> <img width="427" height="435" alt="Screenshot 2026-11-24 142121" src="https://github.com/user-attachments/assets/479c4392-5692-4ad3-bb49-4b42a23a1a30" />
> <img width="1125" height="782" alt="Screenshot 2026-11-24 142142" src="https://github.com/user-attachments/assets/b72a0547-e2ab-4345-9444-061ecea5262a" />
> <img width="1121" height="778" alt="Screenshot 2026-11-24 142239" src="https://github.com/user-attachments/assets/0fb26b36-4a5c-42eb-9670-4252b52d0681" />
> <img width="1118" height="781" alt="Screenshot 2026-11-24 142254" src="https://github.com/user-attachments/assets/d8624f34-e59c-4be4-8d51-3bb0045cb535" />
> <img width="1121" height="782" alt="Screenshot 2026-11-24 142305" src="https://github.com/user-attachments/assets/64fd64f7-1ea8-43e9-a491-2327f1b3f21e" />


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
* **3 Background Effects:** No Blur (lightest for gaming), **Soft Glass** (translucent glass with glossy highlight borders), and **Frosted Glass** (Windows 11 Acrylic blur).
* **Direct Drag-and-Drop on Widget:** Reorder metrics visually and resize freely from any edge or corner with automatic content scaling.
* **Per-Item Personalization:** Rename any metric, set individual label/value colors, toggle headers between **Text** and **Icon**, customize font family and weight, and adjust the **Elevation (3D Depth)** slider.
* **Click-Through Lock Mode:** Lock the overlay in place so mouse clicks pass right through while gaming or working.

### 5. Built-in Auto-Updater
* Automatically checks, downloads, and installs new versions directly from GitHub Releases while preserving all saved layouts and settings.

---

## Download & Installation

1. Go to the [**Releases (Latest Version)**](https://github.com/dangdinhnambhlc2709-alt/SysMonitor-Widget/releases/latest) page.
2. Download the `.exe` installer (e.g., `YourAppName_Setup_v1.0.0.exe`).
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
* **3 chế độ hiệu ứng nền:** Tắt làm mờ (nhẹ nhất), **Kính mềm** (viền sáng bóng trong suốt) và **Kính mờ** (kính mờ đục Acrylic chuẩn Windows 11).
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
