# Báo cáo dò link bộ cài QA98

- Lượt chạy: 2026-11-14 10:59 UTC — catalog revision 190, 1005 khoá (gói × phiên bản) cần dò.
- Đã dò lượt này: **100** — đạt 90 (tải cả file kiểm SHA-256: 26), lỗi tạm 0, link chết 9, sai file 1, bỏ qua (file quá giới hạn tải thử) 0.
- Đang tạm ẩn: **7** (mới ẩn lượt này 3, vừa hiện lại 0).
- Đề xuất (proposals.json, dữ liệu chưa tin cậy): healthy 90, needs_review 10.

Ngưỡng: link chết / sai file ẩn sau 3 lượt lỗi, mỗi lượt cách lượt trước ≥ 6 giờ; lỗi tạm (timeout, 429, 5xx) không bao giờ ẩn; đang ẩn cần 2 lượt đạt (có kiểm SHA-256) mới hiện lại.

## Thùng rác — chờ duyệt

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| DelugeTeam.Deluge | 2.2.0 | url is not https | 2026-11-10 11:35 UTC | 11 | http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/d/DelugeTeam/Deluge/2.2.0/DelugeTeam.Deluge.installer.yaml<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win64-setup.exe |
| IridiumIO.CompactGUI | latest | winget manifest missing | 2026-11-10 23:47 UTC | 10 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml |
| Microsoft.Office | 16.0.20228.20124 | sha256 mismatch | 2026-11-11 18:46 UTC | 8 | https://officecdn.microsoft.com/pr/wsus/setup.exe expected f266bc16834fbfae5aa7aca7c27ed5f393cac0bb2acebc2d5bb2dd7813cf7497 got c883ab32dffe4a371bb37411de83edc5d62ff31d315c5e3b59e830326bb4e322 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/Office/16.0.20228.20124/Microsoft.Office.installer.yaml<br>https://officecdn.microsoft.com/pr/wsus/setup.exe |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | HTTP 403 (repeated) | 2026-11-12 22:41 UTC | 5 | https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml<br>https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe |
| TechPowerUp.GPU-Z | 2.71.0 | HTTP 404 | 2026-11-13 18:16 UTC | 3 | https://us1-dl.techpowerup.com/files/GPU-Z.2.71.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/GPU-Z/2.71.0/TechPowerUp.GPU-Z.installer.yaml<br>https://us1-dl.techpowerup.com/files/GPU-Z.2.71.0.exe |
| TechPowerUp.NVCleanstall | 1.19.0 | redirect to non-https | 2026-11-13 18:16 UTC | 3 | https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml<br>https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe |
| TechPowerUp.ThrottleStop | 9.7 | HTTP 404 | 2026-11-13 18:16 UTC | 3 | https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml<br>https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip |

Duyệt: sửa link / hash trong catalog rồi ký lại (bản mới => khoá mới, mục cũ tự hết tác dụng), hoặc chờ bot tự hiện lại khi link hết lỗi. Hiện lại bằng tay: xoá mục khỏi state.json (README).

## Vừa hiện lại

Không có.

## Đề xuất cần duyệt

| Gói | Bản | Loại | Bản đề xuất | URL đề xuất | SHA-256 đề xuất | Bằng chứng |
|---|---|---|---|---|---|---|
| DelugeTeam.Deluge | 2.2.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/d/DelugeTeam/Deluge/2.2.0/DelugeTeam.Deluge.installer.yaml winget manifest exists but installer does not serve the expected file |
| IridiumIO.CompactGUI | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| Microsoft.Office | 16.0.20228.20124 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/Office/16.0.20228.20124/Microsoft.Office.installer.yaml winget manifest exists but installer does not serve the expected file |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.GPU-Z | 2.71.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/GPU-Z/2.71.0/TechPowerUp.GPU-Z.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.NVCleanstall | 1.19.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.ThrottleStop | 9.7 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml winget manifest exists but installer does not serve the expected file |
| Warp.Warp | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| Ablaze.Floorp | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| Anaconda.Miniconda3 | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |

same_artifact = cùng SHA-256 đã tải kiểm, chỉ đổi URL; new_artifact = bản mới hơn trong winget-pkgs (chưa tải, phải kiểm file + chữ ký trên máy quản trị trước khi ký catalog); retire_candidate = cả thư mục gói mất khỏi winget-pkgs.

## Đang lỗi, chưa đủ ngưỡng ẩn

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| Ablaze.Floorp | latest | winget manifest missing | 2026-11-14 10:59 UTC | 1 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Ablaze/Floorp/latest/Ablaze.Floorp.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Ablaze/Floorp/latest/Ablaze.Floorp.installer.yaml |
| Anaconda.Miniconda3 | latest | winget manifest missing | 2026-11-14 10:59 UTC | 1 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/latest/Anaconda.Miniconda3.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/latest/Anaconda.Miniconda3.installer.yaml |
| Warp.Warp | latest | winget manifest missing | 2026-11-14 03:39 UTC | 2 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Warp/Warp/latest/Warp.Warp.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Warp/Warp/latest/Warp.Warp.installer.yaml |
