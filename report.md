# Báo cáo dò link bộ cài QA98

- Lượt chạy: 2026-11-23 12:17 UTC — catalog revision 290, 1009 khoá (gói × phiên bản) cần dò.
- Đã dò lượt này: **100** — đạt 86 (tải cả file kiểm SHA-256: 55), lỗi tạm 1, link chết 9, sai file 4, bỏ qua (file quá giới hạn tải thử) 0.
- Đang tạm ẩn: **11** (mới ẩn lượt này 0, vừa hiện lại 0).
- Đề xuất (proposals.json, dữ liệu chưa tin cậy): healthy 86, needs_review 12, suspect 1, transient 1.

Ngưỡng: link chết / sai file ẩn sau 3 lượt lỗi, mỗi lượt cách lượt trước ≥ 6 giờ; lỗi tạm (timeout, 429, 5xx) không bao giờ ẩn; đang ẩn cần 2 lượt đạt (có kiểm SHA-256) mới hiện lại.

## Thùng rác — chờ duyệt

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| Ablaze.Floorp | latest | winget manifest missing | 2026-11-14 10:59 UTC | 25 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Ablaze/Floorp/latest/Ablaze.Floorp.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Ablaze/Floorp/latest/Ablaze.Floorp.installer.yaml |
| Anaconda.Miniconda3 | latest | winget manifest missing | 2026-11-14 10:59 UTC | 25 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/latest/Anaconda.Miniconda3.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/latest/Anaconda.Miniconda3.installer.yaml |
| DelugeTeam.Deluge | 2.2.0 | url is not https | 2026-11-10 11:35 UTC | 35 | http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/d/DelugeTeam/Deluge/2.2.0/DelugeTeam.Deluge.installer.yaml<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win64-setup.exe |
| IridiumIO.CompactGUI | latest | winget manifest missing | 2026-11-10 23:47 UTC | 34 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml |
| Microsoft.Office | 16.0.20228.20124 | sha256 mismatch | 2026-11-11 18:46 UTC | 32 | https://officecdn.microsoft.com/pr/wsus/setup.exe expected f266bc16834fbfae5aa7aca7c27ed5f393cac0bb2acebc2d5bb2dd7813cf7497 got daea2a1c2beef16e88dded58916b9c4d816ff247e76ee5ebb41c3810fee16be1 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/Office/16.0.20228.20124/Microsoft.Office.installer.yaml<br>https://officecdn.microsoft.com/pr/wsus/setup.exe |
| PearAI.PearAI | 1.8.9 | HTTP 404 | 2026-11-17 08:34 UTC | 16 | https://pearai-app.nyc3.digitaloceanspaces.com/PearAI-latest/windows-x64/PearAISetup.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PearAI/PearAI/1.8.9/PearAI.PearAI.installer.yaml<br>https://pearai-app.nyc3.digitaloceanspaces.com/PearAI-latest/windows-x64/PearAISetup.exe |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | HTTP 403 (repeated) | 2026-11-12 22:41 UTC | 29 | https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml<br>https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe |
| TechPowerUp.NVCleanstall | 1.19.0 | redirect to non-https | 2026-11-13 18:16 UTC | 27 | https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml<br>https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe |
| TechPowerUp.ThrottleStop | 9.7 | HTTP 404 | 2026-11-13 18:16 UTC | 27 | https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml<br>https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip |
| VSDC.Editor | 11.3.3.761 | sha256 mismatch | 2026-11-19 04:16 UTC | 12 | https://downloads.videosoftdev.com/video_tools/video_editor_x64.exe expected 55068a2fe08b99575770430662cf9cce9155e47dc8f90ba8d56554c264cfb292 got 21d2b450d1c4f74e677a73682b8f73b03ba7066f3f5abfc6ef611b0c779140fd | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/v/VSDC/Editor/11.3.3.761/VSDC.Editor.installer.yaml<br>https://downloads.videosoftdev.com/video_tools/video_editor_x64.exe |
| Warp.Warp | latest | winget manifest missing | 2026-11-14 03:39 UTC | 26 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Warp/Warp/latest/Warp.Warp.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Warp/Warp/latest/Warp.Warp.installer.yaml |

Duyệt: sửa link / hash trong catalog rồi ký lại (bản mới => khoá mới, mục cũ tự hết tác dụng), hoặc chờ bot tự hiện lại khi link hết lỗi. Hiện lại bằng tay: xoá mục khỏi state.json (README).

## Vừa hiện lại

Không có.

## Đề xuất cần duyệt

| Gói | Bản | Loại | Bản đề xuất | URL đề xuất | SHA-256 đề xuất | Bằng chứng |
|---|---|---|---|---|---|---|
| Ablaze.Floorp | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| Anaconda.Miniconda3 | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| DelugeTeam.Deluge | 2.2.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/d/DelugeTeam/Deluge/2.2.0/DelugeTeam.Deluge.installer.yaml winget manifest exists but installer does not serve the expected file |
| IridiumIO.CompactGUI | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| Microsoft.Office | 16.0.20228.20124 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/Office/16.0.20228.20124/Microsoft.Office.installer.yaml winget manifest exists but installer does not serve the expected file |
| PearAI.PearAI | 1.8.9 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PearAI/PearAI/1.8.9/PearAI.PearAI.installer.yaml winget manifest exists but installer does not serve the expected file |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.NVCleanstall | 1.19.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.ThrottleStop | 9.7 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml winget manifest exists but installer does not serve the expected file |
| VSDC.Editor | 11.3.3.761 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/v/VSDC/Editor/11.3.3.761/VSDC.Editor.installer.yaml winget manifest exists but installer does not serve the expected file |
| Warp.Warp | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| lamquangminh.EVKey | 6.0.5 | needs_review | — | https://github.com/lamquangminh/EVKey/releases/download/Release/EVKey.zip | 836fb7d7498b8ee63ab97b01ef0f37ba008a736aefbbb33adfb427ce662c0a90 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/l/lamquangminh/EVKey/6.0.5/lamquangminh.EVKey.installer.yaml winget has same version with different sha256 |

same_artifact = cùng SHA-256 đã tải kiểm, chỉ đổi URL; new_artifact = bản mới hơn trong winget-pkgs (chưa tải, phải kiểm file + chữ ký trên máy quản trị trước khi ký catalog); retire_candidate = cả thư mục gói mất khỏi winget-pkgs.

## Đang lỗi, chưa đủ ngưỡng ẩn

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| lamquangminh.EVKey | 6.0.5 | size mismatch | 2026-11-23 12:17 UTC | 1 | https://github.com/lamquangminh/EVKey/releases/download/Release/EVKey.zip expected 1504714 got 956205 | https://github.com/lamquangminh/EVKey/releases/download/Release/EVKey.zip |
| PaulPacifico.ShutterEncoder | 20.5 | sha256 mismatch | 2026-11-23 02:54 UTC | 2 | https://www.shutterencoder.com/Shutter%20Encoder%2020.5%20Windows%2064bits.exe expected 18779ec1472e6ee4712ca7e74d1d03c19baee8add2315d675acc58673f9d8e0b got 888898bf804e80d6d6c320183cfb4d4ace727249ee641f5eb13b919326d12031 | https://www.shutterencoder.com/Shutter%20Encoder%2020.5%20Windows%2064bits.exe<br>https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PaulPacifico/ShutterEncoder/20.5/PaulPacifico.ShutterEncoder.installer.yaml |
