# Báo cáo dò link bộ cài QA98

- Lượt chạy: 2026-11-18 16:44 UTC — catalog revision 235, 1006 khoá (gói × phiên bản) cần dò.
- Đã dò lượt này: **100** — đạt 84 (tải cả file kiểm SHA-256: 14), lỗi tạm 1, link chết 10, sai file 4, bỏ qua (file quá giới hạn tải thử) 1.
- Đang tạm ẩn: **11** (mới ẩn lượt này 0, vừa hiện lại 0).
- Đề xuất (proposals.json, dữ liệu chưa tin cậy): healthy 84, needs_review 14, transient 1.

Ngưỡng: link chết / sai file ẩn sau 3 lượt lỗi, mỗi lượt cách lượt trước ≥ 6 giờ; lỗi tạm (timeout, 429, 5xx) không bao giờ ẩn; đang ẩn cần 2 lượt đạt (có kiểm SHA-256) mới hiện lại.

## Thùng rác — chờ duyệt

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| Ablaze.Floorp | latest | winget manifest missing | 2026-11-14 10:59 UTC | 13 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Ablaze/Floorp/latest/Ablaze.Floorp.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Ablaze/Floorp/latest/Ablaze.Floorp.installer.yaml |
| Anaconda.Miniconda3 | latest | winget manifest missing | 2026-11-14 10:59 UTC | 13 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/latest/Anaconda.Miniconda3.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Anaconda/Miniconda3/latest/Anaconda.Miniconda3.installer.yaml |
| DelugeTeam.Deluge | 2.2.0 | url is not https | 2026-11-10 11:35 UTC | 23 | http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/d/DelugeTeam/Deluge/2.2.0/DelugeTeam.Deluge.installer.yaml<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win32-setup.exe<br>http://download.deluge-torrent.org/windows/deluge-2.2.0-win64-setup.exe |
| IridiumIO.CompactGUI | latest | winget manifest missing | 2026-11-10 23:47 UTC | 22 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/i/IridiumIO/CompactGUI/latest/IridiumIO.CompactGUI.installer.yaml |
| Microsoft.Office | 16.0.20228.20124 | sha256 mismatch | 2026-11-11 18:46 UTC | 20 | https://officecdn.microsoft.com/pr/wsus/setup.exe expected f266bc16834fbfae5aa7aca7c27ed5f393cac0bb2acebc2d5bb2dd7813cf7497 got daea2a1c2beef16e88dded58916b9c4d816ff247e76ee5ebb41c3810fee16be1 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/Office/16.0.20228.20124/Microsoft.Office.installer.yaml<br>https://officecdn.microsoft.com/pr/wsus/setup.exe |
| PearAI.PearAI | 1.8.9 | HTTP 404 | 2026-11-17 08:34 UTC | 4 | https://pearai-app.nyc3.digitaloceanspaces.com/PearAI-latest/windows-x64/PearAISetup.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PearAI/PearAI/1.8.9/PearAI.PearAI.installer.yaml<br>https://pearai-app.nyc3.digitaloceanspaces.com/PearAI-latest/windows-x64/PearAISetup.exe |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | HTTP 403 (repeated) | 2026-11-12 22:41 UTC | 17 | https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml<br>https://dl.razerzone.com/drivers/Synapse3/win/RazerSynapseInstaller_V1.22.0.737.exe |
| TechPowerUp.GPU-Z | 2.71.0 | HTTP 404 | 2026-11-13 18:16 UTC | 15 | https://us1-dl.techpowerup.com/files/GPU-Z.2.71.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/GPU-Z/2.71.0/TechPowerUp.GPU-Z.installer.yaml<br>https://us1-dl.techpowerup.com/files/GPU-Z.2.71.0.exe |
| TechPowerUp.NVCleanstall | 1.19.0 | redirect to non-https | 2026-11-13 18:16 UTC | 15 | https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml<br>https://closest-dl.techpowerup.com/files/NVCleanstall_1.19.0.exe |
| TechPowerUp.ThrottleStop | 9.7 | HTTP 404 | 2026-11-13 18:16 UTC | 15 | https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml<br>https://uk2-dl.techpowerup.com/files/ThrottleStop_9.7.zip |
| Warp.Warp | latest | winget manifest missing | 2026-11-14 03:39 UTC | 14 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Warp/Warp/latest/Warp.Warp.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Warp/Warp/latest/Warp.Warp.installer.yaml |

Duyệt: sửa link / hash trong catalog rồi ký lại (bản mới => khoá mới, mục cũ tự hết tác dụng), hoặc chờ bot tự hiện lại khi link hết lỗi. Hiện lại bằng tay: xoá mục khỏi state.json (README).

## Vừa hiện lại

Không có.

## Đề xuất cần duyệt

| Gói | Bản | Loại | Bản đề xuất | URL đề xuất | SHA-256 đề xuất | Bằng chứng |
|---|---|---|---|---|---|---|
| Ablaze.Floorp | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| Anaconda.Miniconda3 | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| DelugeTeam.Deluge | 2.2.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/d/DelugeTeam/Deluge/2.2.0/DelugeTeam.Deluge.installer.yaml winget manifest exists but installer does not serve the expected file |
| Google.Chrome | 157.0.8103.79 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/g/Google/Chrome/157.0.8103.79/Google.Chrome.installer.yaml winget manifest exists but installer does not serve the expected file |
| IridiumIO.CompactGUI | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| Microsoft.Office | 16.0.20228.20124 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/m/Microsoft/Office/16.0.20228.20124/Microsoft.Office.installer.yaml winget manifest exists but installer does not serve the expected file |
| PearAI.PearAI | 1.8.9 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/p/PearAI/PearAI/1.8.9/PearAI.PearAI.installer.yaml winget manifest exists but installer does not serve the expected file |
| RazerInc.RazerInstaller.Synapse3 | 1.22.0.737 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/r/RazerInc/RazerInstaller/Synapse3/1.22.0.737/RazerInc.RazerInstaller.Synapse3.installer.yaml winget manifest exists but installer does not serve the expected file |
| SiberSystems.GoodSync | 12.11.12.2 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/s/SiberSystems/GoodSync/12.11.12.2/SiberSystems.GoodSync.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.GPU-Z | 2.71.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/GPU-Z/2.71.0/TechPowerUp.GPU-Z.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.NVCleanstall | 1.19.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/NVCleanstall/1.19.0/TechPowerUp.NVCleanstall.installer.yaml winget manifest exists but installer does not serve the expected file |
| TechPowerUp.ThrottleStop | 9.7 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/t/TechPowerUp/ThrottleStop/9.7/TechPowerUp.ThrottleStop.installer.yaml winget manifest exists but installer does not serve the expected file |
| Warp.Warp | latest | needs_review | — | — | — | approved version missing from winget-pkgs, no newer version |
| Ubisoft.Connect | 175.1.0.13416 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/u/Ubisoft/Connect/175.1.0.13416/Ubisoft.Connect.installer.yaml winget manifest exists but installer does not serve the expected file |

same_artifact = cùng SHA-256 đã tải kiểm, chỉ đổi URL; new_artifact = bản mới hơn trong winget-pkgs (chưa tải, phải kiểm file + chữ ký trên máy quản trị trước khi ký catalog); retire_candidate = cả thư mục gói mất khỏi winget-pkgs.

## Đang lỗi, chưa đủ ngưỡng ẩn

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| Google.Chrome | 157.0.8103.79 | sha256 mismatch | 2026-11-18 00:11 UTC | 2 | https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise_arm64.msi expected 67edcdaae7019a78e160cc653be39d73e508e33e22f1051f9b88457cabdb83f4 got ac5b9053c223dca21633fb4c4c9878a7ae5f3aebe13920d9523a432540db740c | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/g/Google/Chrome/157.0.8103.79/Google.Chrome.installer.yaml<br>https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise.msi<br>https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi<br>https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise_arm64.msi |
| SiberSystems.GoodSync | 12.11.12.2 | sha256 mismatch | 2026-11-18 00:11 UTC | 2 | https://www.goodsync.com/download/GoodSync-vsub-Setup.exe expected d853127575f08026bce068b39ea9eb8d063e9a461e55157ac2e6b75a576ae6cf got b087f8c462d624068bbad64387609e9b31f0106524f70a389cef900a8c9746f3 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/s/SiberSystems/GoodSync/12.11.12.2/SiberSystems.GoodSync.installer.yaml<br>https://www.goodsync.com/download/GoodSync-vsub-Setup.msi<br>https://www.goodsync.com/download/GoodSync-vsub-Setup.exe |
| Ubisoft.Connect | 175.1.0.13416 | sha256 mismatch | 2026-11-18 16:44 UTC | 1 | https://static3.cdn.ubi.com/orbit/launcher_installer/UbisoftConnectInstaller.exe expected e907f33e1fc74fddd61eaf548647201ebc4baac8c439c3fa8e7043c67dc11576 got 551ab83c2bd11490c7f3316325e6bf1a63cbeda66c6a7ba52a8bf4ed00b2abfe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/u/Ubisoft/Connect/175.1.0.13416/Ubisoft.Connect.installer.yaml<br>https://static3.cdn.ubi.com/orbit/launcher_installer/UbisoftConnectInstaller.exe |
