# Báo cáo dò link bộ cài QA98

- Lượt chạy: 2026-11-24 20:23 UTC — catalog revision 306, 1007 khoá (gói × phiên bản) cần dò.
- Đã dò lượt này: **100** — đạt 85 (tải cả file kiểm SHA-256: 42), lỗi tạm 0, link chết 11, sai file 4, bỏ qua (file quá giới hạn tải thử) 0.
- Đang tạm ẩn: **13** (mới ẩn lượt này 2, vừa hiện lại 0).
- Đề xuất (proposals.json, dữ liệu chưa tin cậy): healthy 85, needs_review 15.

Ngưỡng: link chết / sai file ẩn sau 3 lượt lỗi, mỗi lượt cách lượt trước ≥ 6 giờ; lỗi tạm (timeout, 429, 5xx) không bao giờ ẩn; đang ẩn cần 2 lượt đạt (có kiểm SHA-256) mới hiện lại.

## Thùng rác — chờ duyệt

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| Ablaze.Floorp | latest | winget manifest missing | 2026-11-14 10:59 UTC | 28 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Ablaze/Floorp/latest/Ablaze.Floorp.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Ablaze/Floorp/latest/Ablaze.Floorp.installer.yaml |
| Anaconda.Miniconda3 | latest | winget manifest missing | 2026-11-14 10:59 UTC | 28 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/latest/Anaconda.Miniconda3.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/latest/Anaconda.Miniconda3.installer.yaml |
| DelugeTeam.Deluge | 2.2.0 | url is not https | 2026-11-10 11:35 UTC | 38 | http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/d/DelugeTeam/Deluge/2.2.0/DelugeTeam.Deluge.installer.yaml<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win64-setup.exe |
| IridiumIO.CompactGUI | latest | winget manifest missing | 2026-11-10 23:47 UTC | 37 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml |
| Microsoft.Office | 16.0.20228.20124 | sha256 mismatch | 2026-11-11 18:46 UTC | 35 | https://officecdn.microsoft.com/pr/wsus/setup.exe expected f266bc16834fbfae5aa7aca7c27ed5f393cac0bb2acebc2d5bb2dd7813cf7497 got daea2a1c2beef16e88dded58916b9c4d816ff247e76ee5ebb41c3810fee16be1 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/Office/16.0.20228.20124/Microsoft.Office.installer.yaml<br>https://officecdn.microsoft.com/pr/wsus/setup.exe |
| PaulPacifico.ShutterEncoder | 20.5 | sha256 mismatch | 2026-11-23 02:54 UTC | 5 | https://www.shutterencoder.com/Shutter%20Encoder%2020.5%20Windows%2064bits.exe expected 18779ec1472e6ee4712ca7e74d1d03c19baee8add2315d675acc58673f9d8e0b got 888898bf804e80d6d6c320183cfb4d4ace727249ee641f5eb13b919326d12031 | https://www.shutterencoder.com/Shutter%20Encoder%2020.5%20Windows%2064bits.exe<br>https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PaulPacifico/ShutterEncoder/20.5/PaulPacifico.ShutterEncoder.installer.yaml |
| PearAI.PearAI | 1.8.9 | HTTP 404 | 2026-11-17 08:34 UTC | 19 | https://pearai-app.nyc3.digitaloceanspaces.com/PearAI-latest/windows-x64/PearAISetup.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PearAI/PearAI/1.8.9/PearAI.PearAI.installer.yaml<br>https://pearai-app.nyc3.digitaloceanspaces.com/PearAI-latest/windows-x64/PearAISetup.exe |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | HTTP 403 (repeated) | 2026-11-12 22:41 UTC | 32 | https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml<br>https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe |
| StartIsBack.StartAllBack | 3.9.29 | sha256 mismatch | 2026-11-24 00:04 UTC | 3 | https://startisback.sfo3.cdn.digitaloceanspaces.com/StartAllBack_3.9.29_setup.exe expected 203c12b2029e3722c8220657b2a22bc3ff2997d3afdd1530cc16dfc27d0cd89d got f6ac577834e948c0145cb7bfe5744ae029106bb108d3df909097fda4f170e7b8 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/s/StartIsBack/StartAllBack/3.9.29/StartIsBack.StartAllBack.installer.yaml<br>https://startisback.sfo3.cdn.digitaloceanspaces.com/StartAllBack_3.9.29_setup.exe |
| TechPowerUp.GPU-Z | 2.72.0 | HTTP 404 | 2026-11-24 00:04 UTC | 3 | https://us1-dl.techpowerup.com/files/GPU-Z.2.72.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/GPU-Z/2.72.0/TechPowerUp.GPU-Z.installer.yaml<br>https://us1-dl.techpowerup.com/files/GPU-Z.2.72.0.exe |
| TechPowerUp.NVCleanstall | 1.19.0 | redirect to non-https | 2026-11-13 18:16 UTC | 30 | https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml<br>https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe |
| TechPowerUp.ThrottleStop | 9.7 | HTTP 404 | 2026-11-13 18:16 UTC | 30 | https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml<br>https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip |
| Warp.Warp | latest | winget manifest missing | 2026-11-14 03:39 UTC | 29 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Warp/Warp/latest/Warp.Warp.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Warp/Warp/latest/Warp.Warp.installer.yaml |

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
| PaulPacifico.ShutterEncoder | 20.5 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PaulPacifico/ShutterEncoder/20.5/PaulPacifico.ShutterEncoder.installer.yaml |
| PearAI.PearAI | 1.8.9 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PearAI/PearAI/1.8.9/PearAI.PearAI.installer.yaml winget manifest exists but installer does not serve the expected file |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml winget manifest exists but installer does not serve the expected file |
| StartIsBack.StartAllBack | 3.9.29 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/s/StartIsBack/StartAllBack/3.9.29/StartIsBack.StartAllBack.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.GPU-Z | 2.72.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/GPU-Z/2.72.0/TechPowerUp.GPU-Z.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.NVCleanstall | 1.19.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.ThrottleStop | 9.7 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml winget manifest exists but installer does not serve the expected file |
| Wampserver.Wampserver | 3.4.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Wampserver/Wampserver/3.4.0/Wampserver.Wampserver.installer.yaml winget manifest exists but installer does not serve the expected file |
| Warp.Warp | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| WiseCleaner.WiseDiskCleaner | 12.0.1 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/WiseCleaner/WiseDiskCleaner/12.0.1/WiseCleaner.WiseDiskCleaner.installer.yaml winget manifest exists but installer does not serve the expected file |

same_artifact = cùng SHA-256 đã tải kiểm, chỉ đổi URL; new_artifact = bản mới hơn trong winget-pkgs (chưa tải, phải kiểm file + chữ ký trên máy quản trị trước khi ký catalog); retire_candidate = cả thư mục gói mất khỏi winget-pkgs.

## Đang lỗi, chưa đủ ngưỡng ẩn

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| Wampserver.Wampserver | 3.4.0 | HTTP 404 | 2026-11-24 09:45 UTC | 2 | https://wampserver.aviatechno.net/files/install/wampserver3.4.0_x64.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Wampserver/Wampserver/3.4.0/Wampserver.Wampserver.installer.yaml<br>https://wampserver.aviatechno.net/files/install/wampserver3.4.0_x64.exe |
| WiseCleaner.WiseDiskCleaner | 12.0.1 | sha256 mismatch | 2026-11-24 09:45 UTC | 2 | https://downloads.wisecleaner.com/soft/WDCFree_12.0.1.963.exe expected cd317fa6f40eb91f6c6abdda878df25f3f35939f2b91e487d26ad1093c671977 got 84a36f245b6507d6f97b781f8d4e82f8130d8b9b115bae9f1aa002adfbead3d6 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/WiseCleaner/WiseDiskCleaner/12.0.1/WiseCleaner.WiseDiskCleaner.installer.yaml<br>https://downloads.wisecleaner.com/soft/WDCFree_12.0.1.963.exe |
