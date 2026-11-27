# Báo cáo dò link bộ cài QA98

- Lượt chạy: 2026-11-27 23:35 UTC — catalog revision 353, 1015 khoá (gói × phiên bản) cần dò.
- Đã dò lượt này: **100** — đạt 84 (tải cả file kiểm SHA-256: 20), lỗi tạm 0, link chết 13, sai file 3, bỏ qua (file quá giới hạn tải thử) 0.
- Đang tạm ẩn: **16** (mới ẩn lượt này 1, vừa hiện lại 0).
- Đề xuất (proposals.json, dữ liệu chưa tin cậy): healthy 84, needs_review 16.

Ngưỡng: link chết / sai file ẩn sau 3 lượt lỗi, mỗi lượt cách lượt trước ≥ 6 giờ; lỗi tạm (timeout, 429, 5xx) không bao giờ ẩn; đang ẩn cần 2 lượt đạt (có kiểm SHA-256) mới hiện lại.

## Thùng rác — chờ duyệt

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| Ablaze.Floorp | latest | winget manifest missing | 2026-11-14 10:59 UTC | 35 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Ablaze/Floorp/latest/Ablaze.Floorp.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Ablaze/Floorp/latest/Ablaze.Floorp.installer.yaml |
| Anaconda.Miniconda3 | latest | winget manifest missing | 2026-11-14 10:59 UTC | 35 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/latest/Anaconda.Miniconda3.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/latest/Anaconda.Miniconda3.installer.yaml |
| Codeium.Windsurf | 2.3.15 | HTTP 404 | 2026-11-25 13:37 UTC | 6 | https://windsurf-stable.codeiumdata.com/win32-x64-user/stable/c46c49e94b4d3f41181204d59809d8f1b2c48d68/WindsurfUserSetup-x64-2.3.15.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/c/Codeium/Windsurf/2.3.15/Codeium.Windsurf.installer.yaml<br>https://windsurf-stable.codeiumdata.com/win32-x64-user/stable/c46c49e94b4d3f41181204d59809d8f1b2c48d68/WindsurfUserSetup-x64-2.3.15.exe<br>https://windsurf-stable.codeiumdata.com/win32-x64/stable/c46c49e94b4d3f41181204d59809d8f1b2c48d68/WindsurfSetup-x64-2.3.15.exe<br>https://windsurf-stable.codeiumdata.com/win32-arm64-user/stable/c46c49e94b4d3f41181204d59809d8f1b2c48d68/WindsurfUserSetup-arm64-2.3.15.exe<br>https://windsurf-stable.codeiumdata.com/win32-arm64/stable/c46c49e94b4d3f41181204d59809d8f1b2c48d68/WindsurfSetup-arm64-2.3.15.exe |
| DelugeTeam.Deluge | 2.2.0 | url is not https | 2026-11-10 11:35 UTC | 45 | http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/d/DelugeTeam/Deluge/2.2.0/DelugeTeam.Deluge.installer.yaml<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win64-setup.exe |
| Freeplane.Freeplane | 1.13.5 | sha256 mismatch | 2026-11-26 14:22 UTC | 4 | https://sourceforge.net/projects/freeplane/files/freeplane%20stable/Freeplane-Setup-1.13.5.exe/download expected 4261a47714ae3fcff1cf6ce33f4b186a10bc66baed605b60d167c598a0290816 got d4b65a2c9ad12dcfa7ec34e7a9cca6d1a08d6f2e5e023691d726365e24644959 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/f/Freeplane/Freeplane/1.13.5/Freeplane.Freeplane.installer.yaml<br>https://sourceforge.net/projects/freeplane/files/freeplane%20stable/Freeplane-Setup-1.13.5.exe/download |
| IridiumIO.CompactGUI | latest | winget manifest missing | 2026-11-10 23:47 UTC | 44 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml |
| KDE.Okular | 26.11.80 | HTTP 404 | 2026-11-27 05:09 UTC | 3 | https://cdn.kde.org/ci-builds/graphics/okular/release-26.12/windows/okular-release_26.12-8286-windows-cl-msvc2022-x86_64.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/k/KDE/Okular/26.11.80/KDE.Okular.installer.yaml<br>https://cdn.kde.org/ci-builds/graphics/okular/release-26.12/windows/okular-release_26.12-8286-windows-cl-msvc2022-x86_64.exe |
| Microsoft.Office | 16.0.20228.20124 | sha256 mismatch | 2026-11-11 18:46 UTC | 42 | https://officecdn.microsoft.com/pr/wsus/setup.exe expected f266bc16834fbfae5aa7aca7c27ed5f393cac0bb2acebc2d5bb2dd7813cf7497 got daea2a1c2beef16e88dded58916b9c4d816ff247e76ee5ebb41c3810fee16be1 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/Office/16.0.20228.20124/Microsoft.Office.installer.yaml<br>https://officecdn.microsoft.com/pr/wsus/setup.exe |
| PaulPacifico.ShutterEncoder | 20.5 | sha256 mismatch | 2026-11-23 02:54 UTC | 12 | https://www.shutterencoder.com/Shutter%20Encoder%2020.5%20Windows%2064bits.exe expected 18779ec1472e6ee4712ca7e74d1d03c19baee8add2315d675acc58673f9d8e0b got 888898bf804e80d6d6c320183cfb4d4ace727249ee641f5eb13b919326d12031 | https://www.shutterencoder.com/Shutter%20Encoder%2020.5%20Windows%2064bits.exe<br>https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PaulPacifico/ShutterEncoder/20.5/PaulPacifico.ShutterEncoder.installer.yaml |
| PearAI.PearAI | 1.8.9 | HTTP 404 | 2026-11-17 08:34 UTC | 26 | https://pearai-app.nyc3.digitaloceanspaces.com/PearAI-latest/windows-x64/PearAISetup.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PearAI/PearAI/1.8.9/PearAI.PearAI.installer.yaml<br>https://pearai-app.nyc3.digitaloceanspaces.com/PearAI-latest/windows-x64/PearAISetup.exe |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | HTTP 403 (repeated) | 2026-11-12 22:41 UTC | 39 | https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml<br>https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe |
| TechPowerUp.GPU-Z | 2.72.0 | HTTP 404 | 2026-11-24 00:04 UTC | 10 | https://us1-dl.techpowerup.com/files/GPU-Z.2.72.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/GPU-Z/2.72.0/TechPowerUp.GPU-Z.installer.yaml<br>https://us1-dl.techpowerup.com/files/GPU-Z.2.72.0.exe |
| TechPowerUp.NVCleanstall | 1.19.0 | redirect to non-https | 2026-11-13 18:16 UTC | 37 | https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml<br>https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe |
| TechPowerUp.ThrottleStop | 9.7 | HTTP 404 | 2026-11-13 18:16 UTC | 37 | https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml<br>https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip |
| Wampserver.Wampserver | 3.4.0 | HTTP 404 | 2026-11-24 09:45 UTC | 9 | https://wampserver.aviatechno.net/files/install/wampserver3.4.0_x64.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Wampserver/Wampserver/3.4.0/Wampserver.Wampserver.installer.yaml<br>https://wampserver.aviatechno.net/files/install/wampserver3.4.0_x64.exe |
| Warp.Warp | latest | winget manifest missing | 2026-11-14 03:39 UTC | 36 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Warp/Warp/latest/Warp.Warp.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Warp/Warp/latest/Warp.Warp.installer.yaml |

Duyệt: sửa link / hash trong catalog rồi ký lại (bản mới => khoá mới, mục cũ tự hết tác dụng), hoặc chờ bot tự hiện lại khi link hết lỗi. Hiện lại bằng tay: xoá mục khỏi state.json (README).

## Vừa hiện lại

Không có.

## Đề xuất cần duyệt

| Gói | Bản | Loại | Bản đề xuất | URL đề xuất | SHA-256 đề xuất | Bằng chứng |
|---|---|---|---|---|---|---|
| Ablaze.Floorp | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| Anaconda.Miniconda3 | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| Codeium.Windsurf | 2.3.15 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/c/Codeium/Windsurf/2.3.15/Codeium.Windsurf.installer.yaml winget manifest exists but installer does not serve the expected file |
| DelugeTeam.Deluge | 2.2.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/d/DelugeTeam/Deluge/2.2.0/DelugeTeam.Deluge.installer.yaml winget manifest exists but installer does not serve the expected file |
| Freeplane.Freeplane | 1.13.5 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/f/Freeplane/Freeplane/1.13.5/Freeplane.Freeplane.installer.yaml winget manifest exists but installer does not serve the expected file |
| IridiumIO.CompactGUI | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| KDE.Okular | 26.11.80 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/k/KDE/Okular/26.11.80/KDE.Okular.installer.yaml winget manifest exists but installer does not serve the expected file |
| Microsoft.Office | 16.0.20228.20124 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/Office/16.0.20228.20124/Microsoft.Office.installer.yaml winget manifest exists but installer does not serve the expected file |
| PaulPacifico.ShutterEncoder | 20.5 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PaulPacifico/ShutterEncoder/20.5/PaulPacifico.ShutterEncoder.installer.yaml |
| PearAI.PearAI | 1.8.9 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PearAI/PearAI/1.8.9/PearAI.PearAI.installer.yaml winget manifest exists but installer does not serve the expected file |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.GPU-Z | 2.72.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/GPU-Z/2.72.0/TechPowerUp.GPU-Z.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.NVCleanstall | 1.19.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.ThrottleStop | 9.7 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml winget manifest exists but installer does not serve the expected file |
| Wampserver.Wampserver | 3.4.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Wampserver/Wampserver/3.4.0/Wampserver.Wampserver.installer.yaml winget manifest exists but installer does not serve the expected file |
| Warp.Warp | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |

same_artifact = cùng SHA-256 đã tải kiểm, chỉ đổi URL; new_artifact = bản mới hơn trong winget-pkgs (chưa tải, phải kiểm file + chữ ký trên máy quản trị trước khi ký catalog); retire_candidate = cả thư mục gói mất khỏi winget-pkgs.

## Đang lỗi, chưa đủ ngưỡng ẩn

Không có.
