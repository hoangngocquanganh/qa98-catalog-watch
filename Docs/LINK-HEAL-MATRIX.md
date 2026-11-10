# Ma trận gãy link bộ cài — phát hiện, tự chữa, điều kiện an toàn

Tài liệu cho bộ tự chữa **LinkHeal** (`tool/Heal.cs`). Bộ này chạy ngay sau bước ghim trong job `catalog-pin` (runner Windows, có `Get-AuthenticodeSignature`).

Nó chỉ **đề xuất** link thay thế đã tải về và kiểm. Bot không bao giờ sửa catalog. Server QA98 đọc `status.json` → `heals`, tự kiểm lại rồi mới đưa vào catalog ký.

## Luồng chạy

1. **Phát hiện** (job `catalog-watch`, Linux, mỗi 15 phút): `LinkProber` dò từng khoá rồi ghi `state.json`. Mỗi khoá có `fails`, `last_reason`, `last_note`; link gãy nằm ở đầu `last_note`.
2. **Tự chữa** (job `catalog-pin`, Windows, 2 lần/ngày): với mỗi khoá có `fails > 0` hoặc `hidden`, trừ lỗi tạm:
   1. Sinh ứng viên theo bảng dưới (`HealLogic.Strategies` cùng các hàm thuần, có tự kiểm `HealLogic.SelfTest`).
   2. Tải từng ứng viên, chỉ qua https, chặn IP nội bộ, tự kiểm từng redirect. Tệp tải về **không bao giờ được chạy**: chỉ băm, đọc chữ ký rồi xoá.
   3. Kiểm theo mục "Điều kiện an toàn". Ứng viên đầu tiên đạt được ghi vào `heals`. Không ứng viên nào đạt thì ghi vào `heal_blocked` kèm lý do.
3. **Thu thập chữ ký tham chiếu** (cùng lượt, khi còn hạn mức): bot tải installer của các gói **đang tốt** và chỉ nhận khi SHA-256 trùng manifest winget / catalog. Chữ ký của tệp đó được ghi vào `state/heals.json` → `anchors`. Nhờ vậy, lần sau link gãy mới kiểm được người ký có liên tục không.
4. **Công bố** (`catalog-watch`): bot chép `heals` / `heal_blocked` từ `state/heals.json` sang `status.json`, chỉ với khoá còn gãy.

## Ma trận

Cột "Tự sửa" là `kind` ghi trong `heals`. Mọi ứng viên đều phải qua mục "Điều kiện an toàn".

| # | Nguyên nhân gãy | Bot phát hiện thế nào | Bot tự sửa thế nào (`kind`) | Ghi chú an toàn |
|---|---|---|---|---|
| 1 | **Hash đổi tại chỗ**: nhà phát hành thay tệp ở cùng URL, winget-pkgs chưa cập nhật. VD Bandizip `BANDIZIP-SETUP-STD-ALL.EXE`, WFC `wfc6setup.exe`, Emsisoft | Tải cả file: `sha256 mismatch` / `size mismatch` | Lấy lại đúng URL cũ, ghi SHA-256 mới (`same_url_new_hash`); thêm installer manifest winget mới hơn (`winget_newer`) | Bắt buộc người ký trùng tham chiếu. Cùng URL mà **người ký đổi** = dấu hiệu bị tráo tệp ⇒ chặn cả gói, ghi "NGUY HIỂM" |
| 2 | **404 / 410 do lên bản mới**: tệp bản cũ bị xoá | `HTTP 404` / `HTTP 410` | Installer manifest winget mới hơn (`winget_newer`, tệp phải trùng SHA-256 winget); link cùng tên trên trang chủ (`publisher_page`) | `winget_newer` đổi phiên bản: có `candidate_version`, server phải coi là lên bản |
| 3 | **Bản cũ bị xoá nhưng có link "latest" cố định** | `HTTP 404` và tên tệp có số phiên bản | Bỏ số phiên bản khỏi tên tệp, VD `aimp_5.10.2418_w64.exe` ⇒ `aimp_w64.exe` (`versionless_name`); GitHub `releases/latest/download/<tệp>` (`github_latest`) | Link latest đổi nội dung theo thời gian ⇒ bắt buộc kiểm người ký + kích thước |
| 4 | **http → https**: manifest ghi `http://` (VD Deluge) hoặc redirect sang http | `url is not https` / `redirect to non-https` | Thử cùng đường dẫn bằng `https://` (`https_upgrade`); tìm trên trang chủ (`publisher_page`) | Không bao giờ tải qua http. Máy chủ không có TLS ⇒ không chữa |
| 5 | **Trang HTML** thay vì bộ cài: trang trung gian, chuyển hướng JS, meta refresh, nút tải. VD Crucial | `content-type text/html` hoặc byte đầu là `<html` (`HTML page`) | Tải lại với UA trình duyệt (`browser_headers`). Nếu vẫn ra HTML: đọc meta refresh, `location=` / `location.replace()`, `href` / `src` / `data-url` tới `.exe/.msi/.zip`. Link trùng tên tệp xếp trước, đi sâu 1 tầng (`html_link`) | Link lấy từ HTML là dữ liệu không tin cậy ⇒ phải qua đủ kiểm |
| 6 | **403 / 401 / 429**: chặn UA bot, cần Referer. VD Dolphin `dl-mirror` | `HTTP 403 (repeated)` (prober đã thử lại 1 lần) | UA Chrome + `Referer` = gốc host (`browser_headers`); thêm `publisher_page` | 429 là lỗi tạm: không chữa, chỉ thử lại. Heal `browser_headers` ghi rõ "cần gửi UA trình duyệt + Referer" |
| 7 | **Mirror ngẫu nhiên**: SourceForge `*.dl.sourceforge.net`, FossHub | Host cuối khác host đã duyệt; mirror chết ⇒ 404 / timeout | SourceForge: dùng link gốc ổn định `https://downloads.sourceforge.net/project/<dự án>/<đường dẫn>` (`sourceforge_stable`). FossHub: link có token ⇒ xem #9 / #5 | App phải chấp nhận cả nhóm host `*.dl.sourceforge.net`, không khớp host chính xác. `final_host` / `hosts` chỉ là mirror của lượt kiểm |
| 8 | **GitHub Releases đổi tên tệp / asset** | 404 trên `github.com/<o>/<r>/releases/download/<tag>/<tệp>` | GitHub API `releases/latest` tìm asset trùng tên; không có thì asset duy nhất khớp mẫu tên khi thay mọi cụm số phiên bản (`github_asset`) | Nhiều asset cùng mẫu (x86/x64/arm64) ⇒ không đoán |
| 9 | **CDN có token hết hạn** (`?token=`, `Expires=`, `Signature=`, `X-Amz-*`, `hdnts`…) | 403 / 404 / 410 trên URL có query token | Bỏ query (`strip_token`); tìm lại qua trang HTML / trang chủ (#5) | `PublicUrl` luôn bỏ query khi lưu bằng chứng |
| 10 | **Chứng chỉ / TLS lỗi** | `network error: The SSL connection could not be established` | Không chữa trực tiếp (lỗi mạng = lỗi tạm). Kéo dài nhiều ngày ⇒ chủ xem; `winget_newer` có thể ra domain mới | Không bao giờ tắt kiểm chứng chỉ |
| 11 | **DNS chết** (tên miền hết hạn) | `network error` (Name or service not known) lặp nhiều lượt | Như #10; khi đã thành `Broken` thì dùng `winget_newer` + `publisher_page` | Domain hết hạn có thể bị người khác mua ⇒ chỉ tin tệp có người ký trùng tham chiếu |
| 12 | **Nhà phát hành đổi domain** | 404 / DNS chết / redirect sang domain lạ | Đọc `PackageUrl` / `PublisherUrl` trong manifest locale winget, tìm link **cùng tên tệp** (`publisher_page`); `winget_newer` | Domain mới chỉ được nhận khi người ký trùng |
| 13 | **Tệp bị thay bằng bản khác**: signer đổi = **nguy hiểm** | Ứng viên Authenticode `Valid` nhưng subject + thumbprint khác tham chiếu | **Không chữa.** Cùng URL (`same_url_new_hash` / `https_upgrade` / `browser_headers` / `strip_token`) ⇒ chặn cả gói "NGUY HIỂM — NGƯỜI KÝ ĐỔI". Link khác ⇒ bỏ ứng viên đó | Đổi chủ hợp pháp (VD BiniSoft → Malwarebytes) vẫn phải để chủ duyệt tay |
| 14 | **Kích thước bất thường** | Lệch `size_bytes` đã duyệt (`size mismatch`), hoặc ứng viên lệch quá ±50 % tham chiếu | Ứng viên lệch quá ±50 % bị loại, trừ khi SHA-256 trùng manifest winget-pkgs (đã có người duyệt) | Tệp nhỏ bất thường hay gặp ở stub / trang lỗi; tệp to bất thường có thể là gói gộp adware |
| 15 | **winget-pkgs xoá manifest** của bản đã duyệt | `winget manifest missing` | Liệt kê thư mục gói (GitHub API) ⇒ bản mới hơn (`winget_newer`). Cả thư mục mất ⇒ chặn "winget-pkgs đã xoá cả thư mục gói" | Đề xuất gỡ gói là việc của chủ (`retire_candidate` trong `proposals.json`) |
| 16 | **Store ID đổi** (msstore / MSIX) | winget báo không thấy gói (phía app) | Không tự chữa: không có tệp để kiểm chữ ký | Chủ sửa ID trong catalog |
| 17 | **Timeout / 5xx / 408 / 429 tạm thời** | `timeout`, `network error`, `HTTP 5xx` | **Không sửa**, chỉ thử lại. Prober thử lại 1 lần và theo Retry-After; lỗi tạm không bao giờ ẩn gói | `HealLogic.IsTransientReason` loại ngay từ đầu |
| 18 | Bộ cài **zip / msix** (VD EVKey, Crucial) | Như các dòng trên | Không tự chữa: zip không có chữ ký Authenticode để kiểm người phát hành. Ghi SHA-256 mới vào `heal_blocked` để chủ duyệt tay | — |
| 19 | Tệp gốc **không ký** (Deluge, Dolphin 5.0…) | Tham chiếu có `subject` rỗng, hoặc ứng viên `NotSigned` | Không tự chữa | Không ký thì không có cách kiểm "cùng nhà phát hành" |

## Điều kiện an toàn (`HealLogic.Judge`)

Ứng viên chỉ được nhận khi qua **đủ** các điều kiện sau:

1. **Đúng loại tệp** theo byte đầu: exe (`MZ`), msi (OLE `D0CF11E0…`), zip (`PK`). Loại phải khớp `InstallerType` của manifest hoặc đuôi URL cũ. Ứng viên ra HTML thì bị loại.
2. **Link lấy từ manifest winget** (`winget_newer`): SHA-256 tải về phải trùng `InstallerSha256` của manifest đó.
3. **Authenticode `Valid`** (WinVerifyTrust, có kiểm thu hồi) và có người ký.
4. **Người ký liên tục**: ứng viên phải cùng người ký với **chữ ký tham chiếu** của gói. Chữ ký tham chiếu lấy theo thứ tự:
   - bản đã ghim trong `state/pins.json`;
   - `anchors` trong `state/heals.json` (thu thập lúc link còn tốt, SHA-256 trùng nguồn đã duyệt);
   - trong lượt chữa, tệp `winget_newer` trùng SHA-256 manifest và ký hợp lệ.

   Cách so: trùng **thumbprint** (cùng chứng chỉ), hoặc trùng **subject** sau khi chuẩn hoá khoảng trắng / hoa thường (chứng chỉ gia hạn hằng năm). `evidence_vi` ghi rõ trùng theo cách nào.
5. **Chưa có tham chiếu** ⇒ chỉ nhận tệp có SHA-256 trùng manifest winget-pkgs, tức là có người duyệt độc lập. Các trường hợp khác bị chặn với lý do "chưa có chữ ký tham chiếu", kèm người ký và SHA-256 của tệp mới để chủ duyệt nhanh.
6. **Kích thước** ±50 % tham chiếu, trừ tệp đã trùng SHA-256 winget-pkgs.
7. **Ưu tiên cùng phiên bản**: `winget_newer` chỉ là phương án cuối, khi không có link cùng phiên bản nào đạt.
8. **Trùng hẳn SHA-256 đã duyệt** (cùng tệp, chỉ đổi link) ⇒ nhận ngay, không cần kiểm thêm người ký.

## Định dạng đầu ra (`status.json`)

`heals` (tối đa 300 mục):

```json
{ "vkey": "w:bandisoft.bandizip|7.47", "slug": "bandisoft-bandizip", "winget_id": "Bandisoft.Bandizip", "version": "7.47",
  "kind": "same_url_new_hash", "url": "https://bandisoft.app/bandizip/BANDIZIP-SETUP-STD-ALL.EXE", "sha256": "…", "size": 11326184,
  "signer_subject": "CN=Bandisoft International Inc., …", "signer_thumbprint": "…", "final_host": "bandisoft.app", "hosts": ["bandisoft.app"],
  "evidence_vi": "nhà phát hành thay tệp tại chỗ …; cùng người ký với bản tham chiếu …",
  "from_url": "…link gãy…", "candidate_version": "(chỉ khi winget_newer)", "checked_at": "…" }
```

`heal_blocked` có dạng `{ "vkey", "slug", "version", "reason_vi" }`.

Các khoá cũ của `status.json` giữ nguyên. Thiếu khoá `heals` nghĩa là job ghim chưa có dữ liệu.

## Hạn mức và tham số (`catalog-pin.yml`)

| Tham số | Giá trị | Ý nghĩa |
|---|---|---|
| `--heal` | — | Bật bộ tự chữa |
| `--heal-state` | `state/heals.json` | Nơi ghi kết quả |
| `--heal-max` | 30 | Số tệp tải tối đa mỗi lượt |
| `--heal-budget-mb` | 4096 | Tổng dung lượng tải mỗi lượt |
| `--heal-max-file-mb` | 1024 | Dung lượng tối đa một tệp |
| `--harvest-max` | 15 | Số chữ ký tham chiếu thu thập mỗi lượt |
| `--harvest-max-file-mb` | 300 | Dung lượng tối đa một tệp khi thu thập |

Mỗi khoá thử tối đa 10 ứng viên. Kết quả chữa được giữ 36 giờ rồi kiểm lại. Khoá bị chặn được thử lại mỗi lượt.

Tự kiểm khi chạy: `dotnet out/CatalogWatch.dll --selftest`. Bước tự chữa cũng tự chạy tự kiểm; tự kiểm hỏng thì job đỏ và không ghi gì.
