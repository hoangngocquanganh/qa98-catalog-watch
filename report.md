# Báo cáo dò link bộ cài QA98

- Lượt chạy: 2026-11-13 18:16 UTC — catalog revision 141, 954 khoá (gói × phiên bản) cần dò.
- Đã dò lượt này: **100** — đạt 91 (tải cả file kiểm SHA-256: 76), lỗi tạm 1, link chết 6, sai file 1, bỏ qua (file quá giới hạn tải thử) 1.
- Đang tạm ẩn: **5** (mới ẩn lượt này 1, vừa hiện lại 0).
- Đề xuất (proposals.json, dữ liệu chưa tin cậy): healthy 91, needs_review 7, transient 1.

Ngưỡng: link chết / sai file ẩn sau 3 lượt lỗi, mỗi lượt cách lượt trước ≥ 6 giờ; lỗi tạm (timeout, 429, 5xx) không bao giờ ẩn; đang ẩn cần 2 lượt đạt (có kiểm SHA-256) mới hiện lại.

## Thùng rác — chờ duyệt

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| Bandisoft.Bandizip | 7.47 | sha256 mismatch | — | 0 | https://bandisoft.app/bandizip/BANDIZIP-SETUP-STD-ALL.EXE expected f35590f4b2660d0698049a454167381efa462e5821b32af5e4562bc4ac929d44 got 20ef6974dd63e8f6e55b365551f8ad1fb72501c8af24c37967ef124675a44111 | https://bandisoft.app/bandizip/BANDIZIP-SETUP-STD-X64.EXE |
| DelugeTeam.Deluge | 2.2.0 | url is not https | 2026-11-10 11:35 UTC | 9 | http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/d/DelugeTeam/Deluge/2.2.0/DelugeTeam.Deluge.installer.yaml<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win64-setup.exe |
| IridiumIO.CompactGUI | latest | winget manifest missing | 2026-11-10 23:47 UTC | 8 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml |
| Microsoft.Office | 16.0.20228.20124 | sha256 mismatch | 2026-11-11 18:46 UTC | 6 | https://officecdn.microsoft.com/pr/wsus/setup.exe expected f266bc16834fbfae5aa7aca7c27ed5f393cac0bb2acebc2d5bb2dd7813cf7497 got c883ab32dffe4a371bb37411de83edc5d62ff31d315c5e3b59e830326bb4e322 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/Office/16.0.20228.20124/Microsoft.Office.installer.yaml<br>https://officecdn.microsoft.com/pr/wsus/setup.exe |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | HTTP 403 (repeated) | 2026-11-12 22:41 UTC | 3 | https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml<br>https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe |

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

same_artifact = cùng SHA-256 đã tải kiểm, chỉ đổi URL; new_artifact = bản mới hơn trong winget-pkgs (chưa tải, phải kiểm file + chữ ký trên máy quản trị trước khi ký catalog); retire_candidate = cả thư mục gói mất khỏi winget-pkgs.

## Đang lỗi, chưa đủ ngưỡng ẩn

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| TechPowerUp.GPU-Z | 2.71.0 | HTTP 404 | 2026-11-13 18:16 UTC | 1 | https://us1-dl.techpowerup.com/files/GPU-Z.2.71.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/GPU-Z/2.71.0/TechPowerUp.GPU-Z.installer.yaml<br>https://us1-dl.techpowerup.com/files/GPU-Z.2.71.0.exe |
| TechPowerUp.NVCleanstall | 1.19.0 | redirect to non-https | 2026-11-13 18:16 UTC | 1 | https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml<br>https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe |
| TechPowerUp.ThrottleStop | 9.7 | HTTP 404 | 2026-11-13 18:16 UTC | 1 | https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml<br>https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip |
