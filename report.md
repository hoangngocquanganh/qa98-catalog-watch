# Báo cáo dò link bộ cài QA98

- Lượt chạy: 2026-11-10 05:34 UTC — catalog revision 24, 953 khoá (gói × phiên bản) cần dò.
- Đã dò lượt này: **100** — đạt 92 (tải cả file kiểm SHA-256: 75), lỗi tạm 0, link chết 3, sai file 3, bỏ qua (file quá giới hạn tải thử) 2.
- Đang tạm ẩn: **0** (mới ẩn lượt này 0, vừa hiện lại 0).
- Đề xuất (proposals.json, dữ liệu chưa tin cậy): healthy 92, needs_review 5, new_artifact 1.

Ngưỡng: link chết / sai file ẩn sau 3 lượt lỗi, mỗi lượt cách lượt trước ≥ 6 giờ; lỗi tạm (timeout, 429, 5xx) không bao giờ ẩn; đang ẩn cần 2 lượt đạt (có kiểm SHA-256) mới hiện lại.

## Thùng rác — chờ duyệt

Không có gói nào đang bị ẩn.

## Vừa hiện lại

Không có.

## Đề xuất cần duyệt

| Gói | Bản | Loại | Bản đề xuất | URL đề xuất | SHA-256 đề xuất | Bằng chứng |
|---|---|---|---|---|---|---|
| AIMP.AIMP | 5.10.2418 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/AIMP/AIMP/5.10.2418/AIMP.AIMP.installer.yaml winget manifest exists but installer does not serve the expected file |
| Google.Chrome | 157.0.8103.39 | new_artifact | 157.0.8103.51 | https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise.msi | 0b76a091b8eaf495775abbef027d4d78814530cced68cd7a43033e1aaee0aa77 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/g/Google/Chrome/157.0.8103.51/Google.Chrome.installer.yaml manifests/g/Google/Chrome/157.0.8103.51@7899c1f5d686426eb6d161633dd1e110e9bea998 approved version missing from winget-pkgs; newer manifest found (needs review, not downloaded) |
| lamquangminh.EVKey | 6.0.5 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/l/lamquangminh/EVKey/6.0.5/lamquangminh.EVKey.installer.yaml winget manifest exists but installer does not serve the expected file |
| Autodesk.DWGTrueView.2027 | 1.0.0.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Autodesk/DWGTrueView/2027/1.0.0.0/Autodesk.DWGTrueView.2027.installer.yaml winget manifest exists but installer does not serve the expected file |
| Bandisoft.Bandizip | 7.47 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/b/Bandisoft/Bandizip/7.47/Bandisoft.Bandizip.installer.yaml winget manifest exists but installer does not serve the expected file |
| BiniSoft.WindowsFirewallControl | 6.37.0.0 | needs_review | — | — | — | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/b/BiniSoft/WindowsFirewallControl/6.37.0.0/BiniSoft.WindowsFirewallControl.installer.yaml winget manifest exists but installer does not serve the expected file |

same_artifact = cùng SHA-256 đã tải kiểm, chỉ đổi URL; new_artifact = bản mới hơn trong winget-pkgs (chưa tải, phải kiểm file + chữ ký trên máy quản trị trước khi ký catalog); retire_candidate = cả thư mục gói mất khỏi winget-pkgs.

## Đang lỗi, chưa đủ ngưỡng ẩn

| Gói | Bản | Lý do | Từ lúc | Số lần lỗi | Ghi chú | Link đã thử |
|---|---|---|---|---|---|---|
| AIMP.AIMP | 5.10.2418 | HTTP 404 | 2026-11-10 05:10 UTC | 1 | https://aimp.ru/files/windows/builds/aimp_5.10.2418_w64.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/AIMP/AIMP/5.10.2418/AIMP.AIMP.installer.yaml<br>https://aimp.ru/files/windows/builds/aimp_5.10.2418_w64.exe<br>https://aimp.ru/files/windows/builds/aimp_5.10.2418_w32.exe |
| Autodesk.DWGTrueView.2027 | 1.0.0.0 | HTTP 404 | 2026-11-10 05:34 UTC | 1 | https://upload2.delivery.autodesk.com/WebInstall3StubGUI-1781876152414.exe | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/a/Autodesk/DWGTrueView/2027/1.0.0.0/Autodesk.DWGTrueView.2027.installer.yaml<br>https://upload2.delivery.autodesk.com/WebInstall3StubGUI-1781876152414.exe |
| Bandisoft.Bandizip | 7.47 | sha256 mismatch | 2026-11-10 05:34 UTC | 1 | https://bandisoft.app/bandizip/BANDIZIP-SETUP-STD-ALL.EXE expected f35590f4b2660d0698049a454167381efa462e5821b32af5e4562bc4ac929d44 got 20ef6974dd63e8f6e55b365551f8ad1fb72501c8af24c37967ef124675a44111 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/b/Bandisoft/Bandizip/7.47/Bandisoft.Bandizip.installer.yaml<br>https://bandisoft.app/bandizip/BANDIZIP-SETUP-STD-ALL.EXE<br>https://bandisoft.app/bandizip/BANDIZIP-SETUP-STD-X64.EXE |
| BiniSoft.WindowsFirewallControl | 6.37.0.0 | sha256 mismatch | 2026-11-10 05:34 UTC | 1 | https://www.binisoft.org/download/wfc6setup.exe expected 26bd4b19fb8ab523e621e40ec5426b7289b467e88505d6764a87e7ad6df4d5c1 got f726c3c3a3701fa37ddf821385ea9cc81bf64c0445318856b517491d00d61d8c | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/b/BiniSoft/WindowsFirewallControl/6.37.0.0/BiniSoft.WindowsFirewallControl.installer.yaml<br>https://www.binisoft.org/download/wfc6setup.exe |
| Google.Chrome | 157.0.8103.39 | winget manifest missing | 2026-11-10 05:10 UTC | 1 | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/g/Google/Chrome/157.0.8103.39/Google.Chrome.installer.yaml | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/g/Google/Chrome/157.0.8103.39/Google.Chrome.installer.yaml |
| lamquangminh.EVKey | 6.0.5 | sha256 mismatch | 2026-11-10 05:10 UTC | 1 | https://github.com/lamquangminh/EVKey/releases/download/Release/EVKey.zip expected 836fb7d7498b8ee63ab97b01ef0f37ba008a736aefbbb33adfb427ce662c0a90 got 59ac1ceeb7fce4407f5565a9ecf885210b450a3b789e035f06d07728e26e78bc | https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/l/lamquangminh/EVKey/6.0.5/lamquangminh.EVKey.installer.yaml<br>https://github.com/lamquangminh/EVKey/releases/download/Release/EVKey.zip |
